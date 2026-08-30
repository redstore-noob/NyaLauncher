using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Minecraft;
using NyaLauncher.Plugin.Abstractions.Plugins;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Plugins;

/// <summary>
/// Owns one collectible load context and one plugin entry point. Registrations
/// remain private until StartAsync succeeds, which prevents half-started
/// plugins from leaking components or launch hooks into the launcher.
/// </summary>
internal sealed class PluginRuntimeHost : IAsyncDisposable
{
    private readonly PluginLoadContext _loadContext;
    private readonly PluginContext _context;
    private readonly PluginSettingsLease _settingsLease;
    private readonly object _lifecycleSync = new();
    private INyaLauncherPlugin? _plugin;
    private Task? _lifecycleTask;
    private TaskCompletionSource<bool>? _invocationsDrained;
    private Task? _disposeAttempt;
    private int _activeInvocations;
    private int _isStarted;
    private bool _stopping;
    private bool _quarantined;
    private bool _unsafeToUnload;
    private bool _unloaded;
    private bool _deferredDisposeScheduled;

    private PluginRuntimeHost(
        PluginPackage package,
        PluginSettingsStore settings,
        IReadOnlySet<string> grantedCapabilities,
        PluginLoadContext loadContext,
        INyaLauncherPlugin plugin)
    {
        Package = package;
        Settings = settings;
        _loadContext = loadContext;
        _plugin = plugin;
        // Finish every fallible path/storage snapshot before subscribing the
        // runtime-scoped settings lease. A constructor failure must not leave
        // the settings store holding a delegate into this collectible ALC.
        var manifest = SnapshotManifest(package.Manifest!);
        var storage = new PluginStorage(
            package.PackageDirectory,
            Path.GetDirectoryName(settings.FilePath)!);
        _settingsLease = new PluginSettingsLease(
            settings,
            EnterInvocation,
            grantedCapabilities.Contains);
        _context = new PluginContext(
            manifest,
            storage,
            _settingsLease,
            grantedCapabilities);
    }

    private static PluginManifest SnapshotManifest(PluginManifest source) => source with
    {
        Authors = Array.AsReadOnly(source.Authors.ToArray()),
        RequiredCapabilities = Array.AsReadOnly(source.RequiredCapabilities.ToArray()),
        OptionalCapabilities = Array.AsReadOnly(source.OptionalCapabilities.ToArray()),
        Settings = Array.AsReadOnly(source.Settings.Select(setting => setting with
        {
            Options = Array.AsReadOnly(setting.Options.Select(option => option with { }).ToArray()),
            FileExtensions = Array.AsReadOnly(setting.FileExtensions.ToArray())
        }).ToArray())
    };

    public PluginPackage Package { get; }

    public PluginManifest Manifest => Package.Manifest!;

    public PluginSettingsStore Settings { get; }

    public bool IsStarted => Volatile.Read(ref _isStarted) != 0;

    public bool IsUnloaded
    {
        get
        {
            lock (_lifecycleSync)
                return _unloaded;
        }
    }

    public IReadOnlyList<PluginComponentArea> ComponentAreas { get; private set; } = [];

    public IReadOnlyList<PluginInstanceExtensionRegistration> InstanceExtensions { get; private set; } = [];

    public IReadOnlyList<PluginLaunchContributorRegistration> LaunchContributors { get; private set; } = [];

    public static PluginRuntimeHost Create(
        PluginPackage package,
        PluginSettingsStore settings,
        IReadOnlySet<string> grantedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(package.Manifest);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(grantedCapabilities);

        if (!PluginCatalog.TryResolvePackagePath(
                package.PackageDirectory,
                package.Manifest.EntryAssembly,
                out var assemblyPath))
        {
            throw new InvalidDataException("插件入口程序集路径越过了包目录。");
        }

        var loadContext = new PluginLoadContext(assemblyPath, package.PackageDirectory);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var entryType = assembly.GetType(
                package.Manifest.EntryType,
                throwOnError: true,
                ignoreCase: false)!;
            if (entryType.IsAbstract ||
                !typeof(INyaLauncherPlugin).IsAssignableFrom(entryType))
            {
                throw new InvalidDataException(
                    $"入口类型 {entryType.FullName} 没有实现 INyaLauncherPlugin。");
            }

            var plugin = Activator.CreateInstance(entryType) as INyaLauncherPlugin ??
                throw new InvalidDataException(
                    $"无法用无参数构造函数创建入口类型 {entryType.FullName}。");
            return new PluginRuntimeHost(
                package,
                settings,
                grantedCapabilities,
                loadContext,
                plugin);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleSync)
        {
            if (_unloaded)
                throw new ObjectDisposedException(nameof(PluginRuntimeHost));
            if (_quarantined)
                throw new InvalidOperationException("插件运行时已经隔离，必须重启后再加载。");
            if (IsStarted)
                return Task.CompletedTask;
            if (_lifecycleTask is { IsCompleted: false })
                return _lifecycleTask;

            var plugin = _plugin ?? throw new ObjectDisposedException(nameof(PluginRuntimeHost));
            EnsureRequiredCapabilitiesGranted();
            var registrar = new PluginRegistrar(Manifest.Id, _context.IsCapabilityGranted);
            _context.OpenRegistration(registrar);
            var task = RunStartAsync(plugin, registrar, cancellationToken);
            _lifecycleTask = task;
            ClearLifecycleWhenCompleted(task);
            return task;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleSync)
        {
            if (_unloaded || _plugin is null)
                return Task.CompletedTask;
            if (_lifecycleTask is { IsCompleted: false } active)
            {
                _stopping = true;
                var queuedStop = StopAfterAsync(active, cancellationToken);
                _lifecycleTask = queuedStop;
                ClearLifecycleWhenCompleted(queuedStop);
                return queuedStop;
            }
            if (!IsStarted)
                return Task.CompletedTask;

            // Refuse new host callbacks before waiting for existing leases.
            // This keeps StopAsync from racing settings observers or a hook
            // that was already admitted by the host.
            _stopping = true;
            var task = StopAfterInvocationsAsync(_plugin, cancellationToken);
            _lifecycleTask = task;
            ClearLifecycleWhenCompleted(task);
            return task;
        }
    }

    public void Quarantine()
    {
        lock (_lifecycleSync)
        {
            _quarantined = true;
            _context.CloseRegistration();
            _settingsLease.Suspend();
        }
    }

    /// <summary>
    /// Pins this runtime while host-invoked plugin code is executing. Timed-out
    /// contributors and instance actions may outlive their caller; the lease
    /// prevents their collectible load context from being unloaded underneath
    /// that still-running code.
    /// </summary>
    public IDisposable EnterInvocation()
    {
        lock (_lifecycleSync)
        {
            if (_unloaded || _plugin is null)
                throw new ObjectDisposedException(nameof(PluginRuntimeHost));
            if (_quarantined || _stopping || !IsStarted)
                throw new InvalidOperationException("插件运行时当前不接受新调用。");
            if (_activeInvocations++ == 0)
            {
                _invocationsDrained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return new InvocationLease(this);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            if (_unloaded || _deferredDisposeScheduled)
                return ValueTask.CompletedTask;
            if (_disposeAttempt is { IsCompleted: false } activeAttempt)
                return new ValueTask(activeAttempt);

            _disposeAttempt = DisposeCoreAsync();
            return new ValueTask(_disposeAttempt);
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Stop accepting host callbacks immediately, but keep the settings
        // view readable until StopAsync finishes. Plugins commonly need their
        // own configuration while releasing resources during a normal stop.
        BeginDisposal();
        Task? active;
        lock (_lifecycleSync)
        {
            if (_unloaded)
                return;
            active = _lifecycleTask;
        }

        if (active is { IsCompleted: false })
        {
            try
            {
                await active.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                ScheduleDeferredDispose(active);
                return;
            }
            catch (Exception)
            {
                // The invocation returned; unloading can continue below.
            }
        }

        var invocations = GetInvocationDrainTask();
        if (!invocations.IsCompleted)
        {
            try
            {
                await invocations.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                ScheduleDeferredDispose(invocations);
                return;
            }
        }

        if (IsStarted)
        {
            CancellationTokenSource? stopCancellation = null;
            try
            {
                stopCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var stopTask = StopAsync(stopCancellation.Token);
                DisposeCancellationWhenCompleted(stopTask, stopCancellation);
                stopCancellation = null;
                await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                lock (_lifecycleSync)
                    active = _lifecycleTask;
                if (active is not null)
                    ScheduleDeferredDispose(active);
                return;
            }
            catch (Exception)
            {
                // Stop returned with an error; no plugin call remains active.
            }
            finally
            {
                stopCancellation?.Dispose();
            }
        }

        _settingsLease.Suspend();
        Unload();
    }

    private void BeginDisposal()
    {
        lock (_lifecycleSync)
        {
            _quarantined = true;
            _context.CloseRegistration();
        }
    }

    private async Task RunStartAsync(
        INyaLauncherPlugin plugin,
        PluginRegistrar registrar,
        CancellationToken cancellationToken)
    {
        try
        {
            // Work before the plugin's first await runs off the UI thread, so
            // the manager can still enforce its lifecycle timeout.
            await Task.Run(
                async () => await plugin
                    .StartAsync(_context, cancellationToken)
                    .ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);

            lock (_lifecycleSync)
            {
                if (_quarantined)
                    throw new OperationCanceledException("插件在启动完成前被隔离。");
                var contributions = registrar.SealAndSnapshot();
                ComponentAreas = contributions.ComponentAreas;
                InstanceExtensions = contributions.InstanceExtensions;
                LaunchContributors = contributions.LaunchContributors;
                Volatile.Write(ref _isStarted, 1);
            }
        }
        catch
        {
            registrar.CloseWithoutPublishing();
            await TryStopAfterFailedStartAsync(plugin).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _context.CloseRegistration();
        }
    }

    private async Task RunStopAsync(
        INyaLauncherPlugin plugin,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(
                async () => await plugin
                    .StopAsync(cancellationToken)
                    .ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            lock (_lifecycleSync)
                _unsafeToUnload = true;
            throw;
        }
        finally
        {
            Volatile.Write(ref _isStarted, 0);
            ComponentAreas = [];
            InstanceExtensions = [];
            LaunchContributors = [];
        }
    }

    private async Task StopAfterAsync(Task active, CancellationToken cancellationToken)
    {
        try
        {
            await active.ConfigureAwait(false);
        }
        catch
        {
            // A failed start already attempted cleanup.
        }

        if (IsStarted && _plugin is { } plugin)
            await StopAfterInvocationsAsync(plugin, cancellationToken).ConfigureAwait(false);
    }

    private async Task StopAfterInvocationsAsync(
        INyaLauncherPlugin plugin,
        CancellationToken cancellationToken)
    {
        await GetInvocationDrainTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await RunStopAsync(plugin, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryStopAfterFailedStartAsync(INyaLauncherPlugin plugin)
    {
        try
        {
            // Keep the lifecycle task incomplete until cleanup really exits.
            // The manager applies its own timeout and quarantines this host;
            // returning early here would allow an ALC unload to race StopAsync.
            await Task.Run(
                async () => await plugin.StopAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // In-process third-party code cannot be force-stopped safely.
            lock (_lifecycleSync)
                _unsafeToUnload = true;
        }
    }

    private void ClearLifecycleWhenCompleted(Task task)
    {
        _ = task.ContinueWith(
            _ =>
            {
                lock (_lifecycleSync)
                {
                    if (ReferenceEquals(_lifecycleTask, task))
                        _lifecycleTask = null;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void DisposeCancellationWhenCompleted(
        Task task,
        CancellationTokenSource cancellation)
    {
        _ = task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ScheduleDeferredDispose(Task active)
    {
        lock (_lifecycleSync)
        {
            if (_deferredDisposeScheduled)
                return;
            _deferredDisposeScheduled = true;
        }

        _ = active.ContinueWith(
            _ => RetryDeferredDisposeAsync(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
    }

    private async Task RetryDeferredDisposeAsync()
    {
        lock (_lifecycleSync)
        {
            _deferredDisposeScheduled = false;
            _disposeAttempt = null;
        }

        try
        {
            await DisposeAsync();
        }
        catch (Exception)
        {
            // The runtime stays quarantined. A later process exit remains the
            // only safe way to end uncooperative in-process plugin code.
        }
    }

    private void Unload()
    {
        lock (_lifecycleSync)
        {
            if (_unloaded)
                return;
            if (_unsafeToUnload)
                return;
            if (_lifecycleTask is { IsCompleted: false })
                throw new InvalidOperationException("插件代码仍在执行，不能卸载程序集。");

            _unloaded = true;
            _plugin = null;
            ComponentAreas = [];
            InstanceExtensions = [];
            LaunchContributors = [];
        }

        _loadContext.Unload();
    }

    private Task GetInvocationDrainTask()
    {
        lock (_lifecycleSync)
            return _activeInvocations == 0
                ? Task.CompletedTask
                : _invocationsDrained!.Task;
    }

    private void ExitInvocation()
    {
        TaskCompletionSource<bool>? completion = null;
        lock (_lifecycleSync)
        {
            if (_activeInvocations <= 0)
                return;
            if (--_activeInvocations == 0)
            {
                completion = _invocationsDrained;
                _invocationsDrained = null;
            }
        }

        completion?.TrySetResult(true);
    }

    private void EnsureRequiredCapabilitiesGranted()
    {
        var denied = Manifest.RequiredCapabilities
            .Where(capability => !_context.IsCapabilityGranted(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (denied.Length > 0)
        {
            throw new UnauthorizedAccessException(
                $"插件缺少必要能力授权：{string.Join(", ", denied)}");
        }
    }

    private sealed class InvocationLease(PluginRuntimeHost owner) : IDisposable
    {
        private PluginRuntimeHost? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitInvocation();
    }

    private sealed class PluginLoadContext(
        string entryAssemblyPath,
        string packageDirectory) : AssemblyLoadContext(isCollectible: true)
    {
        private static readonly string ContractAssemblyName =
            typeof(INyaLauncherPlugin).Assembly.GetName().Name!;
        private readonly AssemblyDependencyResolver _resolver = new(entryAssemblyPath);
        private readonly string _packageDirectory = packageDirectory;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Returning null shares the SDK from the default context. Loading a
            // plugin-bundled SDK copy would create incompatible interface types.
            if (string.Equals(
                    assemblyName.Name,
                    ContractAssemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(ValidateResolvedPath(path));
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(ValidateResolvedPath(path));
        }

        private string ValidateResolvedPath(string path)
        {
            var relative = Path.GetRelativePath(_packageDirectory, path);
            if (!PluginCatalog.TryResolvePackagePath(
                    _packageDirectory,
                    relative,
                    out var resolved))
            {
                throw new FileLoadException("插件依赖路径越过包目录或经过符号链接。", path);
            }

            return resolved;
        }
    }
}

/// <summary>
/// Runtime-scoped settings view. It detaches every plugin event handler when
/// the runtime is quarantined, preventing a forgotten subscription from
/// pinning the collectible AssemblyLoadContext after disable or update.
/// </summary>
internal sealed class PluginSettingsLease : IPluginSettings
{
    private readonly PluginSettingsStore _store;
    private readonly Func<IDisposable> _enterInvocation;
    private readonly Func<string, bool> _isCapabilityGranted;
    private readonly object _gate = new();
    private EventHandler<PluginSettingChangedEventArgs>? _changed;
    private bool _suspended;

    public PluginSettingsLease(
        PluginSettingsStore store,
        Func<IDisposable> enterInvocation,
        Func<string, bool> isCapabilityGranted)
    {
        _store = store;
        _enterInvocation = enterInvocation;
        _isCapabilityGranted = isCapabilityGranted;
        _store.Changed += OnStoreChanged;
    }

    public event EventHandler<PluginSettingChangedEventArgs>? Changed
    {
        add
        {
            lock (_gate)
            {
                if (_suspended)
                    throw new ObjectDisposedException(nameof(PluginSettingsLease));
                _changed += value;
            }
        }
        remove
        {
            lock (_gate)
                _changed -= value;
        }
    }

    public bool TryGet<T>(string key, out T? value, string? instanceId = null)
    {
        ThrowIfSuspended();
        EnsureSettingCapability(key);
        return _store.TryGet(key, out value, instanceId);
    }

    public T Get<T>(string key, T fallback, string? instanceId = null)
    {
        ThrowIfSuspended();
        EnsureSettingCapability(key);
        return _store.Get(key, fallback, instanceId);
    }

    public ValueTask SetAsync<T>(
        string key,
        T value,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfSuspended();
        EnsureSettingCapability(key);
        return _store.SetAsync(key, value, instanceId, cancellationToken);
    }

    public ValueTask ResetAsync(
        string key,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfSuspended();
        EnsureSettingCapability(key);
        return _store.ResetAsync(key, instanceId, cancellationToken);
    }

    public void Suspend()
    {
        lock (_gate)
        {
            if (_suspended)
                return;
            _suspended = true;
            _changed = null;
            _store.Changed -= OnStoreChanged;
        }
    }

    private void OnStoreChanged(object? sender, PluginSettingChangedEventArgs args)
    {
        EventHandler<PluginSettingChangedEventArgs>[] handlers;
        lock (_gate)
        {
            if (_suspended || _changed is null)
                return;
            handlers = _changed.GetInvocationList()
                .OfType<EventHandler<PluginSettingChangedEventArgs>>()
                .ToArray();
        }

        foreach (var handler in handlers)
        {
            IDisposable invocation;
            try
            {
                invocation = _enterInvocation();
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            try
            {
                _ = Task.Run(() =>
                {
                    using (invocation)
                    {
                        try
                        {
                            handler(this, args);
                        }
                        catch (Exception)
                        {
                            // Third-party observers cannot break the writer.
                        }
                    }
                });
            }
            catch
            {
                invocation.Dispose();
                // A scheduler failure must not roll back an already persisted
                // setting solely because an observer could not be dispatched.
            }
        }
    }

    private void ThrowIfSuspended()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_suspended, this);
    }

    private void EnsureSettingCapability(string key)
    {
        if (_store.RequiresUserFileRead(key) &&
            !_isCapabilityGranted(PluginCapabilities.UserFilesRead))
        {
            throw new UnauthorizedAccessException(
                $"设置 {key} 需要 {PluginCapabilities.UserFilesRead} 能力授权。");
        }
    }
}

internal sealed class PluginStorage : IPluginStorage
{
    public PluginStorage(string packageDirectory, string dataDirectory)
    {
        PackageDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(packageDirectory));
        DataDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(dataDirectory, "data")));
        CacheDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(dataDirectory, "cache")));
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }

    public string PackageDirectory { get; }

    public string DataDirectory { get; }

    public string CacheDirectory { get; }

    public string GetDataPath(string relativePath) => Resolve(DataDirectory, relativePath);

    public string GetCachePath(string relativePath) => Resolve(CacheDirectory, relativePath);

    private static string Resolve(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!PluginCatalog.TryResolveContainedPath(root, relativePath, out var candidate))
            throw new ArgumentException(
                "插件私有路径不能越过数据目录或经过符号链接。",
                nameof(relativePath));
        return candidate;
    }
}

internal sealed class PluginContext(
    PluginManifest manifest,
    IPluginStorage storage,
    IPluginSettings settings,
    IReadOnlySet<string> grantedCapabilities) : IPluginContext
{
    private IPluginRegistrar? _registrar;

    public PluginManifest Manifest { get; } = manifest;

    public IPluginStorage Storage { get; } = storage;

    public IPluginSettings Settings { get; } = settings;

    public IPluginRegistrar Registrar => Volatile.Read(ref _registrar) ??
        throw new InvalidOperationException("插件只能在 StartAsync 期间注册贡献。");

    public bool IsCapabilityGranted(string capability) =>
        !string.IsNullOrWhiteSpace(capability) && grantedCapabilities.Contains(capability);

    public TService? GetService<TService>() where TService : class
    {
        if (typeof(TService) == typeof(IPluginNotifications))
        {
            // 通知 UI 由启动器渲染（NyaAlert/NyaPrompt），归入 ui.native 能力；
            // 未授权时按契约返回 null，而不是抛异常或暴露宿主内部。
            return IsCapabilityGranted(PluginCapabilities.NativeUi)
                ? (TService)(object)PluginNotifications.Instance
                : null;
        }
        return null;
    }

    public void OpenRegistration(IPluginRegistrar registrar) =>
        Volatile.Write(ref _registrar, registrar);

    public void CloseRegistration() => Volatile.Write(ref _registrar, null);
}

/// <summary>
/// <see cref="IPluginNotifications"/> 的宿主实现：把插件侧请求桥接到
/// NyaAlert / NyaPrompt 门面。门面自身完成 UI 线程封送，可在任意线程调用。
/// </summary>
internal sealed class PluginNotifications : IPluginNotifications
{
    public static readonly PluginNotifications Instance = new();

    private PluginNotifications()
    {
    }

    public void Alert(PluginNoticeSeverity severity, string message, TimeSpan? duration = null) =>
        NyaAlert.Show(message, Map(severity), duration);

    public Task<string?> PromptAsync(
        string title,
        string message = "",
        PluginNoticeSeverity severity = PluginNoticeSeverity.Info,
        params PluginPromptButton[] buttons) =>
        NyaPrompt.ShowAsync(title, message, Map(severity), Convert(buttons));

    public Task<bool> ConfirmAsync(
        string title,
        string message = "",
        PluginNoticeSeverity severity = PluginNoticeSeverity.Warning) =>
        NyaPrompt.ConfirmAsync(title, message, severity: Map(severity));

    private static NyaNoticeSeverity Map(PluginNoticeSeverity severity) => severity switch
    {
        PluginNoticeSeverity.Success => NyaNoticeSeverity.Success,
        PluginNoticeSeverity.Warning => NyaNoticeSeverity.Warning,
        PluginNoticeSeverity.Error => NyaNoticeSeverity.Error,
        _ => NyaNoticeSeverity.Info,
    };

    private static NyaPromptButton[] Convert(PluginPromptButton[] buttons) =>
        buttons is { Length: > 0 }
            ? Array.ConvertAll(
                buttons,
                button => new NyaPromptButton(button.Label, button.Id, button.IsDefault))
            : [];
}

internal sealed class PluginRegistrar(
    string pluginId,
    Func<string, bool> isCapabilityGranted) : IPluginRegistrar
{
    private const int MaximumAreaCount = 32;
    private const int MaximumComponentsPerArea = 128;
    private const int MaximumTotalComponents = 512;
    private const int MaximumExtensionCount = 32;
    private const int MaximumActionsPerExtension = 128;
    private const int MaximumTotalInstanceActions = 256;
    private const int MaximumContributorCount = 32;
    private readonly List<PluginComponentArea> _componentAreas = [];
    private readonly List<PluginInstanceExtensionRegistration> _instanceExtensions = [];
    private readonly List<PluginLaunchContributorRegistration> _launchContributors = [];
    private int _componentCount;
    private int _instanceActionCount;
    private bool _isOpen = true;

    public void AddComponentArea(PluginComponentArea contribution)
    {
        EnsureOpen();
        RequireCapability(PluginCapabilities.Components);
        ArgumentNullException.ThrowIfNull(contribution);
        if (_componentAreas.Count >= MaximumAreaCount)
            throw new ArgumentException($"单个插件最多注册 {MaximumAreaCount} 个组件功能区。", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.Id) || contribution.Id.Length > 128 ||
            string.IsNullOrWhiteSpace(contribution.Title) || contribution.Title.Length > 256 ||
            contribution.Subtitle?.Length > 1024 || contribution.Glyph?.Length > 32 ||
            contribution.Icon?.Length > 4096)
        {
            throw new ArgumentException("组件功能区 ID、标题或说明无效。", nameof(contribution));
        }

        var components = new List<PolygonComponentRegistration>();
        var registrations = (contribution.Components ?? [])
            .Take(MaximumComponentsPerArea + 1)
            .ToArray();
        if (registrations.Length > MaximumComponentsPerArea)
            throw new ArgumentException(
                $"单个功能区最多注册 {MaximumComponentsPerArea} 个组件。",
                nameof(contribution));
        if (_componentCount + registrations.Length > MaximumTotalComponents)
            throw new ArgumentException(
                $"单个插件最多注册 {MaximumTotalComponents} 个组件。",
                nameof(contribution));
        foreach (var registration in registrations)
        {
            if (registration?.Definition is null)
                throw new ArgumentException("组件注册及其定义不能为空。", nameof(contribution));
            EnsureOwnedId(registration.Definition.Id, "组件");
            var definition = PolygonComponentValidator.ValidateAndSnapshot(
                registration.Definition);
            components.Add(new PolygonComponentRegistration
            {
                Definition = definition,
                Factory = registration.Factory
            });
        }

        _componentAreas.Add(contribution with
        {
            Subtitle = contribution.Subtitle ?? string.Empty,
            Glyph = string.IsNullOrWhiteSpace(contribution.Glyph) ? "◇" : contribution.Glyph,
            Components = components.AsReadOnly()
        });
        _componentCount += components.Count;
    }

    public void AddMinecraftInstanceExtension(IMinecraftInstanceExtension extension)
    {
        EnsureOpen();
        RequireCapability(PluginCapabilities.MinecraftInstanceModify);
        ArgumentNullException.ThrowIfNull(extension);
        if (_instanceExtensions.Count >= MaximumExtensionCount)
            throw new ArgumentException($"单个插件最多注册 {MaximumExtensionCount} 个实例扩展。", nameof(extension));
        var extensionId = extension.Id;
        EnsureOwnedId(extensionId, "实例扩展");
        if (_instanceExtensions.Any(item => string.Equals(
                item.Id,
                extensionId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"实例扩展 ID {extensionId} 重复。");
        }

        var actions = (extension.Actions ??
                throw new ArgumentException("实例扩展 Actions 不能为 null。", nameof(extension)))
            .Take(MaximumActionsPerExtension + 1)
            .Select(action => action is null
                ? throw new ArgumentException("实例操作不能为 null。", nameof(extension))
                : action with
                {
                    Description = action.Description ?? string.Empty,
                    Glyph = string.IsNullOrWhiteSpace(action.Glyph) ? "◇" : action.Glyph
                })
            .ToArray();
        if (actions.Length > MaximumActionsPerExtension)
            throw new ArgumentException(
                $"单个实例扩展最多声明 {MaximumActionsPerExtension} 个操作。",
                nameof(extension));
        if (_instanceActionCount + actions.Length > MaximumTotalInstanceActions)
            throw new ArgumentException(
                $"单个插件最多声明 {MaximumTotalInstanceActions} 个实例操作。",
                nameof(extension));
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id) ||
                string.IsNullOrWhiteSpace(action.Title) ||
                action.Id.Length > 128 ||
                action.Title.Length > 256 ||
                action.Description.Length > 4096 ||
                action.Glyph.Length > 32 ||
                action.ConfirmationMessage?.Length > 4096)
            {
                throw new ArgumentException("实例操作的 ID、标题或说明无效。", nameof(extension));
            }
        }

        var duplicateAction = actions
            .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAction is not null)
            throw new ArgumentException($"实例操作 ID {duplicateAction.Key} 重复。", nameof(extension));

        _instanceExtensions.Add(new PluginInstanceExtensionRegistration(
            extensionId,
            extension,
            actions));
        _instanceActionCount += actions.Length;
    }

    public void AddMinecraftLaunchContributor(IMinecraftLaunchContributor contributor)
    {
        EnsureOpen();
        RequireCapability(PluginCapabilities.MinecraftLaunchModify);
        ArgumentNullException.ThrowIfNull(contributor);
        if (_launchContributors.Count >= MaximumContributorCount)
            throw new ArgumentException($"单个插件最多注册 {MaximumContributorCount} 个启动贡献。", nameof(contributor));
        // Snapshot metadata while StartAsync is already isolated and bounded.
        // Reading plugin-defined getters later while assembling a launch plan
        // would otherwise let synchronous code bypass the hook timeout.
        var contributorId = contributor.Id;
        var order = contributor.Order;
        EnsureOwnedId(contributorId, "启动贡献");
        if (_launchContributors.Any(item => string.Equals(
                item.Id,
                contributorId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"启动贡献 ID {contributorId} 重复。");
        }

        _launchContributors.Add(new PluginLaunchContributorRegistration(
            contributorId,
            order,
            isCapabilityGranted(PluginCapabilities.MinecraftInstanceRead),
            contributor));
    }

    public PluginContributions SealAndSnapshot()
    {
        EnsureOpen();
        _isOpen = false;
        var duplicateArea = _componentAreas
            .GroupBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateArea is not null)
            throw new InvalidOperationException($"组件功能区 {duplicateArea.Key} 重复。");

        return new PluginContributions(
            _componentAreas.ToArray(),
            _instanceExtensions.ToArray(),
            _launchContributors.ToArray());
    }

    public void CloseWithoutPublishing() => _isOpen = false;

    private void EnsureOpen()
    {
        if (!_isOpen)
            throw new InvalidOperationException("插件注册窗口已经关闭。");
    }

    private void RequireCapability(string capability)
    {
        if (!isCapabilityGranted(capability))
            throw new UnauthorizedAccessException($"当前插件未获能力 {capability}。" );
    }

    private void EnsureOwnedId(string id, string kind)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id.Length > 256 ||
            !id.StartsWith(pluginId + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{kind} ID 必须以 {pluginId}/ 开头。",
                nameof(id));
        }
    }
}

internal sealed record PluginContributions(
    IReadOnlyList<PluginComponentArea> ComponentAreas,
    IReadOnlyList<PluginInstanceExtensionRegistration> InstanceExtensions,
    IReadOnlyList<PluginLaunchContributorRegistration> LaunchContributors);

internal sealed record PluginInstanceExtensionRegistration(
    string Id,
    IMinecraftInstanceExtension Extension,
    IReadOnlyList<MinecraftInstanceActionDefinition> Actions);

internal sealed record PluginLaunchContributorRegistration(
    string Id,
    int Order,
    bool CanReadInstanceFiles,
    IMinecraftLaunchContributor Contributor);
