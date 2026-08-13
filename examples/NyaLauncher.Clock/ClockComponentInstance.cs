using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Clock;

internal sealed class ClockComponentInstance : IPolygonComponentInstance
{
    internal const string FormatSettingKey = "time.format";
    internal const string ShowTimeZoneSettingKey = "display.timezone";
    internal const string ShowSecondsSettingKey = "display.seconds";

    private static readonly TimeSpan MaximumRefreshDelay = TimeSpan.FromSeconds(1);

    private readonly IPluginSettings _settings;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _settingsChanged = new(0, 1);
    private readonly object _stateGate = new();
    private readonly Task _refreshTask;
    private ComponentStateSnapshot _currentState = ComponentStateSnapshot.Empty;
    private ClockOptions _options;
    private ClockDisplay? _lastDisplay;
    private long _revision;
    private int _disposed;

    public ClockComponentInstance(IPluginSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _options = ReadOptions();
        Publish(DateTimeOffset.Now, force: true);
        _settings.Changed += OnSettingChanged;
        _refreshTask = RunRefreshLoopAsync(_disposeCancellation.Token);
    }

    public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

    public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

    public ValueTask<ComponentActionResult> InvokeAsync(
        ComponentActionInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ComponentActionResult.Failed("电子时钟没有可调用动作。"));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _settings.Changed -= OnSettingChanged;
        _disposeCancellation.Cancel();
        try
        {
            await _refreshTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _settingsChanged.Dispose();
            _disposeCancellation.Dispose();
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.Now;
            Publish(now);

            var delay = DelayUntilNextVisibleChange(now, Volatile.Read(ref _options));
            await _settingsChanged.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnSettingChanged(object? sender, PluginSettingChangedEventArgs args)
    {
        if (args.Scope != PluginSettingScope.Global || !IsClockSetting(args.Key) ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Volatile.Write(ref _options, ReadOptions());
        Publish(DateTimeOffset.Now, force: true);
        try
        {
            _settingsChanged.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pending signal already makes the loop re-evaluate its delay.
        }
    }

    private void Publish(DateTimeOffset now, bool force = false)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var display = ClockDisplayFormatter.Create(now, Volatile.Read(ref _options), TimeZoneInfo.Local);
        EventHandler<ComponentStateChangedEventArgs>? handler;
        ComponentStateSnapshot snapshot;

        lock (_stateGate)
        {
            if (!force && display == _lastDisplay)
                return;

            _lastDisplay = display;
            snapshot = CreateSnapshot(Interlocked.Increment(ref _revision), display);
            Volatile.Write(ref _currentState, snapshot);
            handler = StateChanged;
        }

        handler?.Invoke(this, new ComponentStateChangedEventArgs(snapshot));
    }

    private ClockOptions ReadOptions() => new(
        string.Equals(
            _settings.Get(FormatSettingKey, "24"),
            "24",
            StringComparison.Ordinal),
        _settings.Get(ShowTimeZoneSettingKey, true),
        _settings.Get(ShowSecondsSettingKey, true));

    private static bool IsClockSetting(string key) =>
        string.Equals(key, FormatSettingKey, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, ShowTimeZoneSettingKey, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, ShowSecondsSettingKey, StringComparison.OrdinalIgnoreCase);

    private static TimeSpan DelayUntilNextVisibleChange(DateTimeOffset now, ClockOptions options)
    {
        var intervalMilliseconds = options.ShowSeconds ? 1_000L : 60_000L;
        var elapsed = Math.Abs(now.ToUnixTimeMilliseconds() % intervalMilliseconds);
        var untilBoundary = TimeSpan.FromMilliseconds(intervalMilliseconds - elapsed + 20);
        return untilBoundary < MaximumRefreshDelay ? untilBoundary : MaximumRefreshDelay;
    }

    private static ComponentStateSnapshot CreateSnapshot(long revision, ClockDisplay display) => new()
    {
        Revision = revision,
        Elements = new Dictionary<string, ComponentElementState>(StringComparer.OrdinalIgnoreCase)
        {
            [ClockComponentDefinition.TimeElementId] = new() { Text = display.Time },
            [ClockComponentDefinition.TimeZoneElementId] = new()
            {
                Text = display.TimeZone,
                IsVisible = display.ShowTimeZone
            },
            [ClockComponentDefinition.PeriodElementId] = new()
            {
                Text = display.Period,
                IsVisible = display.ShowPeriod
            },
            [ClockComponentDefinition.SecondsElementId] = new()
            {
                Text = display.Seconds,
                IsVisible = display.ShowSeconds
            }
        }
    };
}
