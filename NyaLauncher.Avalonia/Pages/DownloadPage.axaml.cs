using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Pages;

public partial class DownloadPage : UserControl
{
    private const int PageSize = 50;
    private readonly GameDownloadService _downloadService;
    private CancellationTokenSource? _searchCts;

    private List<MinecraftVersion>? _allVersions;
    private List<ModrinthProject>? _allMods;
    private List<ModrinthProject>? _allModpacks;
    private List<ModrinthProject>? _allShaders;
    private List<ModrinthProject>? _allResourcepacks;

    private List<MinecraftVersion>? _versionFiltered;
    private List<ModrinthProject>? _modFiltered;
    private List<ModrinthProject>? _modpackFiltered;
    private List<ModrinthProject>? _shaderFiltered;
    private List<ModrinthProject>? _resourcepackFiltered;

    private int _versionPage = 1;
    private int _modPage = 1;
    private int _modpackPage = 1;
    private int _shaderPage = 1;
    private int _resourcepackPage = 1;

    private string? _activeLoadHeader;
    private bool _versionsLoaded;
    private bool _modsLoaded;
    private bool _modpacksLoaded;
    private bool _shadersLoaded;
    private bool _resourcepacksLoaded;
    private bool _javaLoaded;
    private bool _isRefreshing;
    private DateTime _lastProgressUiUpdate;
    private bool _isJavaDownloading;
    private bool _initializingJavaControls;
    private IReadOnlyList<JavaDownloadCandidate> _javaCandidates = [];
    private CancellationTokenSource? _javaQueryCts;

    /// <summary>当用户点击 Mod 安装按钮时触发，传递 ModrinthProject 给宿主。</summary>
    public event EventHandler<ModrinthProject>? ModInstallRequested;

    /// <summary>当用户点击整合包/资源包/光影包下载按钮时触发。</summary>
    public event EventHandler<(ModrinthProject Project, ContentDownloadKind Kind)>? ContentDownloadRequested;

    /// <summary>XAML 设计器 / Avalonia 反射需要无参构造；运行时由 MainWindow 使用 internal 构造。</summary>
    public DownloadPage()
        : this(new GameDownloadService())
    {
    }

    internal DownloadPage(GameDownloadService downloadService)
    {
        _downloadService = downloadService;
        InitializeComponent();
        _downloadService.Changed += OnDownloadChanged;
        LoadingOverlay.IsVisible = true;
        // 使用 AddHandler 确保 DataTemplate 内的 Tapped 事件也能被捕获
        //（整行点击 → 弹出对应下载遮罩层，不再需要列表里的按钮）
        ModList.AddHandler(TappedEvent, OnModListTapped, handledEventsToo: true);
        VersionList.AddHandler(TappedEvent, OnVersionListTapped, handledEventsToo: true);
        ModpackList.AddHandler(TappedEvent, OnContentListTapped, handledEventsToo: true);
        ShaderList.AddHandler(TappedEvent, OnContentListTapped, handledEventsToo: true);
        ResourcepackList.AddHandler(TappedEvent, OnContentListTapped, handledEventsToo: true);
        InitializeJavaControls();
        // 默认只加载首个可见标签；其余标签在切换时才加载（懒加载）
        LoadTabAsync(DownloadTabs.SelectedItem as TabItem);
    }

    /// <summary>
    /// 切换到下载页的 Java 标签页（供设置页跳转使用）。
    /// </summary>
    public void ActivateJavaTab() => ActivateTab("Java");

    /// <summary>
    /// 切换到指定头名称的标签页（供快捷组件 / 导航跳转使用），不存在时忽略。
    /// </summary>
    public void ActivateTab(string header)
    {
        if (DownloadTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Header is string h && h == header) is { } tab)
        {
            DownloadTabs.SelectedItem = tab;
            LoadTabAsync(tab);
        }
    }

    // ------------------------------------------------------------------
    // Java 运行时管理
    // ------------------------------------------------------------------

    /// <summary>
    /// 初始化 JDK 供应商下拉框并触发首次版本查询。
    /// </summary>
    private void InitializeJavaControls()
    {
        _initializingJavaControls = true;
        try
        {
            JavaVendorComboBox.Items.Clear();
            foreach (JavaVendor vendor in Enum.GetValues<JavaVendor>())
            {
                JavaVendorComboBox.Items.Add(InstalledJavaRuntime.VendorDisplayName(vendor));
            }
            JavaVendorComboBox.SelectedIndex = 0; // 默认 Zulu（不触发 OnJavaVendorChanged）
        }
        finally
        {
            _initializingJavaControls = false;
        }

        JavaPlatformText.Text = $"当前平台：{JavaRuntimeInstaller.GetPlatformDisplayName()} · 将按平台自动选择安装包";
        UpdateJavaVendorHint();
        // 构造时不联网查询 Java 版本，改为切到 Java 标签时才加载
    }

    private void OnJavaVendorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingJavaControls)
            return;
        var vendor = GetSelectedVendor();
        UpdateJavaVendorHint();
        _ = LoadJavaVersionsAsync(vendor);
    }

    /// <summary>
    /// 根据当前供应商更新提示文字。
    /// </summary>
    private void UpdateJavaVendorHint()
    {
        var vendor = GetSelectedVendor();
        JavaVendorHintText.Text = vendor switch
        {
            JavaVendor.Zulu => "Zulu JDK（Azul 官方构建）：免费商用、更新稳定，支持全部 8/11/17/21/25。",
            JavaVendor.Oracle => "Oracle JDK：官方商业构建，直接下载仅提供 Java 21 / 25。",
            JavaVendor.Temurin => "Temurin JDK（Adoptium）：社区开源构建，下载带 SHA-256 完整性校验。",
            _ => ""
        };
    }

    private JavaVendor GetSelectedVendor()
    {
        var vendors = Enum.GetValues<JavaVendor>();
        var index = JavaVendorComboBox.SelectedIndex;
        return index >= 0 && index < vendors.Length ? vendors[index] : JavaVendor.Zulu;
    }

    /// <summary>
    /// 实时查询当前供应商的所有可用版本并填充列表。
    /// </summary>
    private async Task LoadJavaVersionsAsync(JavaVendor vendor)
    {
        _javaQueryCts?.Cancel();
        _javaQueryCts = new CancellationTokenSource();
        var ct = _javaQueryCts.Token;

        try
        {
            JavaVersionList.ItemsSource = null;
            JavaSelectionInfo.Text = "正在查询可用版本…";
            JavaDownloadStatusText.Text = "";
            var candidates = await JavaRuntimeInstaller.QueryAvailableVersionsAsync(vendor, ct);

            if (ct.IsCancellationRequested)
                return;

            _javaCandidates = candidates;
            JavaVersionList.ItemsSource = candidates;
            if (candidates.Count > 0)
            {
                JavaVersionList.SelectedIndex = 0;
                JavaSelectionInfo.Text = $"找到 {candidates.Count} 个可用版本，请选择后下载。";
            }
            else
            {
                JavaSelectionInfo.Text = "该提供商当前平台下没有可用版本。";
            }
        }
        catch (OperationCanceledException)
        {
            // 切换供应商时取消旧查询，属正常流程
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                JavaSelectionInfo.Text = $"查询失败：{ex.Message}";
                JavaDownloadStatusText.Text = $"查询失败：{ex.Message}";
                JavaDownloadStatusText.Foreground = FindBrush("ErrorBrush");
            }
        }
    }

    private void OnJavaVersionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (JavaVersionList.SelectedItem is JavaDownloadCandidate candidate)
        {
            JavaSelectionInfo.Text = $"已选 {candidate.DisplayName} · {candidate.DetailText}";
        }
    }

    /// <summary>
    /// 刷新已安装的 Java 运行时列表（磁盘扫描在后台执行，避免 UI 卡顿）。
    /// </summary>
    private async System.Threading.Tasks.Task RefreshJavaRuntimeList()
    {
        try
        {
            var runtimes = await System.Threading.Tasks.Task.Run(JavaRuntimeInstaller.GetInstalledRuntimes);
            JavaRuntimeList.ItemsSource = runtimes;
            JavaRuntimeEmptyText.IsVisible = runtimes.Count == 0;
        }
        catch
        {
            JavaRuntimeList.ItemsSource = null;
            JavaRuntimeEmptyText.IsVisible = true;
        }
    }

    private void OnRefreshJavaRuntimeClick(object? sender, RoutedEventArgs e)
    {
        _ = RefreshJavaRuntimeList();
        JavaDownloadStatusText.Text = "已刷新 Java 运行时列表。";
        JavaDownloadStatusText.Foreground = FindBrush("AccentTextColor");
    }

    private async void OnDownloadJavaClick(object? sender, RoutedEventArgs e)
    {
        if (JavaVersionList.SelectedItem is not JavaDownloadCandidate candidate)
        {
            JavaDownloadStatusText.Text = "请先在右侧列表中选择要下载的 JDK 版本。";
            JavaDownloadStatusText.Foreground = FindBrush("AccentTextColor");
            return;
        }

        if (_isJavaDownloading)
        {
            JavaDownloadStatusText.Text = "已有 Java 下载任务进行中，请稍候。";
            return;
        }

        if (candidate.Vendor == JavaVendor.Oracle && candidate.MajorVersion < 21)
        {
            JavaDownloadStatusText.Text = JavaRuntimeInstaller.OracleUnsupportedMessage;
            JavaDownloadStatusText.Foreground = FindBrush("ErrorBrush");
            return;
        }

        var vendorDisplay = candidate.VendorName;
        _isJavaDownloading = true;
        try
        {
            JavaDownloadProgressBar.IsVisible = true;
            JavaDownloadProgressBar.Value = 0;
            JavaDownloadStatusText.Text = $"正在准备下载 {vendorDisplay} JDK {candidate.MajorVersion}…";
            JavaDownloadStatusText.Foreground = FindBrush("AccentTextColor");

            var installer = new JavaRuntimeInstaller();
            var progress = new Progress<JavaRuntimeInstallProgress>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var pct = (int)Math.Clamp(p.CompletedBytes * 100.0 / p.TotalBytes, 0, 100);
                    JavaDownloadProgressBar.Value = pct;
                }
                JavaDownloadStatusText.Text =
                    $"{p.Phase}… {FormatJavaProgress(p.CompletedBytes, p.TotalBytes, p.BytesPerSecond)}";
            });

            var installed = await installer.InstallCandidateAsync(candidate, progress);
            JavaDownloadStatusText.Text = $"{vendorDisplay} JDK {candidate.MajorVersion} 安装完成：{installed.JavaExecutablePath}";
            JavaDownloadStatusText.Foreground = FindBrush("SuccessBrush");
            await RefreshJavaRuntimeList();
        }
        catch (Exception ex)
        {
            JavaDownloadStatusText.Text = $"下载 {vendorDisplay} JDK {candidate.MajorVersion} 失败：{ex.Message}";
            JavaDownloadStatusText.Foreground = FindBrush("ErrorBrush");
        }
        finally
        {
            _isJavaDownloading = false;
            JavaDownloadProgressBar.IsVisible = false;
        }
    }

    private void OnUseJavaRuntimeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstalledJavaRuntime runtime })
            return;

        var version = runtime.MajorVersion?.ToString() ?? "unknown";
        if (LauncherConfig.AddJava(runtime.JavaExecutablePath, version) &&
            LauncherConfig.SetPrimaryJava(runtime.JavaExecutablePath))
        {
            // 清除全局 override，让 javaPath 列表首位成为唯一权威
            var current = GlobalLaunchSettingsStore.Load();
            _ = GlobalLaunchSettingsStore.Save(current with { JavaExecutable = "" });
            JavaDownloadStatusText.Text = $"已设为默认 Java：{runtime.JavaExecutablePath}";
            JavaDownloadStatusText.Foreground = FindBrush("SuccessBrush");
        }
        else
        {
            JavaDownloadStatusText.Text = "保存 Java 路径失败。";
            JavaDownloadStatusText.Foreground = FindBrush("ErrorBrush");
        }
    }

    private void OnDeleteJavaRuntimeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstalledJavaRuntime runtime })
            return;

        try
        {
            JavaRuntimeInstaller.DeleteRuntime(runtime.DirectoryPath);
            JavaDownloadStatusText.Text = $"已删除 {runtime.DisplayName}。";
            JavaDownloadStatusText.Foreground = FindBrush("SuccessBrush");
            _ = RefreshJavaRuntimeList();
        }
        catch (Exception ex)
        {
            JavaDownloadStatusText.Text = $"删除失败：{ex.Message}";
            JavaDownloadStatusText.Foreground = FindBrush("ErrorBrush");
        }
    }

    private static string FormatJavaProgress(long completedBytes, long totalBytes, double bytesPerSecond)
    {
        var size = totalBytes > 0
            ? $"{completedBytes / 1048576.0:0.0}/{totalBytes / 1048576.0:0.0} MiB"
            : $"{completedBytes / 1048576.0:0.0} MiB";
        var speed = bytesPerSecond > 0 ? $" · {bytesPerSecond / 1048576.0:0.0} MiB/s" : "";
        return $"{size}{speed}";
    }

    /// <summary>点击 Minecraft 版本列表项 → 弹出内嵌遮罩层：选择 Loader 类型、版本、自定义实例名。</summary>
    private async void OnVersionListTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Source is not Control control) return;
            var version = FindDataContext<MinecraftVersion>(control);
            if (version is null) return;

            if (_downloadService.Current.IsActive)
            {
                DownloadTaskStatusText.Text =
                    $"正在下载 Minecraft {_downloadService.Current.VersionId}，请等待当前任务结束。";
                return;
            }

            var view = new MinecraftDownloadOverlay();
            view.Setup(version);
            var options = await DownloadOverlay.ShowAsync<DownloadOptions>(view);
            if (options is null)
                return;
            await HandleDownloadAsync(options);
        }
        catch (Exception ex)
        {
            DownloadTaskStatusText.Text = $"操作失败：{ex.Message}";
            DownloadTaskStatusText.Foreground = FindBrush("ErrorBrush");
        }
    }

    /// <summary>执行遮罩层确认后的下载：原版直接安装，带加载器则先装加载器版本。</summary>
    private async System.Threading.Tasks.Task HandleDownloadAsync(DownloadOptions options)
    {
        try
        {
            if (options.LoaderType == ModLoaderType.Vanilla)
            {
                DownloadTaskStatusText.Text = $"正在创建 Minecraft {options.Version.Id} 下载任务…";
                await _downloadService.StartAsync(options.Version);
            }
            else
            {
                var loaderName = $"{options.LoaderType} {options.LoaderVersion?.LoaderVersion}";
                DownloadTaskStatusText.Text = $"正在创建 {loaderName} 下载任务…";
                await _downloadService.StartModLoaderAsync(
                    options.Version,
                    options.LoaderVersion!,
                    options.InstanceName,
                    options.SkipFabricApi);
            }
        }
        catch (Exception ex)
        {
            DownloadTaskStatusText.Text = $"操作失败：{ex.Message}";
            DownloadTaskStatusText.Foreground = FindBrush("ErrorBrush");
        }
    }

    private void OnDownloadChanged(GameDownloadSnapshot snapshot)
    {
        // 进度节流：下载进行中每 80ms 最多刷一次 UI，
        // 避免高频进度回调（每 128KB 一次）把 UI 线程排队拖死
        if (snapshot.Phase == GameDownloadPhase.Downloading)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastProgressUiUpdate).TotalMilliseconds < 80)
                return;
            _lastProgressUiUpdate = now;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnDownloadChanged(snapshot));
            return;
        }

        // 更新进度条与取消按钮
        var active = snapshot.Phase is GameDownloadPhase.Preparing or GameDownloadPhase.Downloading;
        CancelDownloadButton.IsVisible = active;
        if (snapshot.Phase == GameDownloadPhase.Downloading)
        {
            DownloadProgressBar.IsVisible = true;
            DownloadProgressBar.Value = snapshot.Percentage;
        }
        else if (snapshot.Phase == GameDownloadPhase.Completed)
        {
            DownloadProgressBar.IsVisible = false;
            DownloadProgressBar.Value = 100;
        }
        else if (snapshot.Phase == GameDownloadPhase.Failed ||
                 snapshot.Phase == GameDownloadPhase.Cancelled)
        {
            DownloadProgressBar.IsVisible = false;
            DownloadProgressBar.Value = 0;
        }

        DownloadTaskStatusText.Text = snapshot.Phase switch
        {
            GameDownloadPhase.Preparing => $"正在准备 Minecraft {snapshot.VersionId}",
            GameDownloadPhase.Downloading =>
                $"{snapshot.StageName} · {snapshot.Percentage:0.0}% · {FormatSpeed(snapshot.BytesPerSecond)}",
            GameDownloadPhase.Completed => $"Minecraft {snapshot.VersionId} 安装完成",
            GameDownloadPhase.Failed => $"下载失败：{snapshot.Detail}",
            GameDownloadPhase.Cancelled => "下载任务已取消",
            _ => "选择版本后开始下载"
        };
    }

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadService.CancelActive())
        {
            DownloadTaskStatusText.Text = "正在取消下载任务…";
            CancelDownloadButton.IsVisible = false;
        }
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 0)
            return "正在测速";
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):0.00} MiB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.0} KiB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    /// <summary>
    /// 按标签懒加载：仅当该标签首次被选中时才拉取内容；加载过则直接复用，不再重复请求。
    /// </summary>
    private void LoadTabAsync(TabItem? tab)
    {
        if (tab?.Header is not string header || _activeLoadHeader == header || IsTabLoaded(header))
            return;

        switch (header)
        {
            case "Minecraft 本体":
                _ = RunTabLoadAsync("Minecraft 本体", LoadVersionsAsync, () => _versionsLoaded = true);
                break;
            case "Mod":
                _ = RunTabLoadAsync("Mod",
                    () => LoadCategoryAsync("Mod", v => _allMods = v, ModList, ModCountText, ModrinthSearch.GetModsAsync),
                    () => _modsLoaded = true);
                break;
            case "整合包":
                _ = RunTabLoadAsync("整合包",
                    () => LoadCategoryAsync("整合包", v => _allModpacks = v, ModpackList, ModpackCountText, ModrinthSearch.GetModpacksAsync),
                    () => _modpacksLoaded = true);
                break;
            case "光影包":
                _ = RunTabLoadAsync("光影包",
                    () => LoadCategoryAsync("光影包", v => _allShaders = v, ShaderList, ShaderCountText, ModrinthSearch.GetShadersAsync),
                    () => _shadersLoaded = true);
                break;
            case "材质包":
                _ = RunTabLoadAsync("材质包",
                    () => LoadCategoryAsync("材质包", v => _allResourcepacks = v, ResourcepackList, ResourcepackCountText, ModrinthSearch.GetResourcePacksAsync),
                    () => _resourcepacksLoaded = true);
                break;
            case "Java":
                _ = RunTabLoadAsync("Java", LoadJavaAsync, () => _javaLoaded = true);
                break;
        }
    }

    /// <summary>该标签是否已加载过内容（用于避免切换回已加载标签时重复拉取）。</summary>
    private bool IsTabLoaded(string header) => header switch
    {
        "Minecraft 本体" => _versionsLoaded,
        "Mod" => _modsLoaded,
        "整合包" => _modpacksLoaded,
        "光影包" => _shadersLoaded,
        "材质包" => _resourcepacksLoaded,
        "Java" => _javaLoaded,
        _ => false
    };

    /// <summary>
    /// 包装单次标签加载：显示遮罩 → 执行加载 → 隐藏遮罩，并带 30s 超时兜底。
    /// 仅当本标签仍为"当前激活加载项"时才隐藏遮罩，避免快速切标签时遮罩被提前关闭。
    /// </summary>
    private async System.Threading.Tasks.Task RunTabLoadAsync(
        string header, Func<System.Threading.Tasks.Task> loadAction, Action? onLoaded = null)
    {
        _activeLoadHeader = header;
        DispatcherTimer? timeout = null;
        void OnTimeout(object? _, EventArgs __)
        {
            timeout?.Stop();
            if (_activeLoadHeader == header)
                _activeLoadHeader = null;
            LoadingOverlay.IsVisible = false;
            LoadingDetail.Text = "部分内容加载超时，可点击右上角刷新重试。";
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LoadingOverlay.IsVisible = true;
            LoadingDetail.Text = $"正在加载{header}…";
            timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += OnTimeout;
            timeout.Start();
        });

        try
        {
            await loadAction();
            onLoaded?.Invoke();
        }
        finally
        {
            timeout?.Stop();
            if (_activeLoadHeader == header)
            {
                _activeLoadHeader = null;
                await Dispatcher.UIThread.InvokeAsync(() => LoadingOverlay.IsVisible = false);
            }
        }
    }

    /// <summary>Java 标签的加载：刷新已安装运行时列表 + 联网查询可下载版本。</summary>
    private async System.Threading.Tasks.Task LoadJavaAsync()
    {
        await RefreshJavaRuntimeList();
        await LoadJavaVersionsAsync(GetSelectedVendor());
    }

    /// <summary>重置某标签的已加载标记，使刷新时能强制重新拉取。</summary>
    private void ResetTabLoaded(string header)
    {
        switch (header)
        {
            case "Minecraft 本体": _versionsLoaded = false; break;
            case "Mod": _modsLoaded = false; break;
            case "整合包": _modpacksLoaded = false; break;
            case "光影包": _shadersLoaded = false; break;
            case "材质包": _resourcepacksLoaded = false; break;
            case "Java": _javaLoaded = false; break;
        }
    }

    private void OnDownloadTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // XAML 在 EndInit 阶段会先触发一次默认选中的 SelectionChanged，
        // 此时 DownloadTabs 字段尚未赋值，直接忽略该初始化期事件，
        // 首屏加载由构造函数显式触发。
        if (!IsInitialized || DownloadTabs is null)
            return;

        LoadTabAsync(DownloadTabs.SelectedItem as TabItem);

        // Tab 内容切换动效（M3 shared-axis 风格：新内容自下方 24px 淡入上浮）。
        // 每个 TabItem 的 Content 是独立持久的元素，切换即对新的内容根播放入场；
        // SlideFadeInAsync 内部带 generation 防抖，快速连点标签不会打架。
        if (DownloadTabs.SelectedItem is TabItem { Content: Control content })
            _ = AnimationHelper.SlideFadeInAsync(content, MaterialMotion.MediumTransitionMs);
    }

    private async System.Threading.Tasks.Task LoadVersionsAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshButton.IsEnabled = false;
                RefreshButton.Content = "⟳ 刷新中…";
                LoadingDetail.Text = "正在加载 Minecraft 版本列表…";
            });

            var versions = await ManifestGet.GetVersionsAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allVersions = versions;
                _versionPage = 1;
                ApplyFilter();
                RefreshButton.IsEnabled = true;
                RefreshButton.Content = "刷新";
            });
            await PopulateModVersionFilterAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VersionCountText.Text = $"版本加载失败: {ex.Message}";
                RefreshButton.IsEnabled = true;
                RefreshButton.Content = "刷新";
            });
        }
    }

    private async System.Threading.Tasks.Task LoadCategoryAsync(
        string categoryName,
        Action<List<ModrinthProject>> storeFunc,
        ItemsControl listControl,
        TextBlock countText,
        Func<int, System.Threading.Tasks.Task<List<ModrinthProject>>> fetchFunc)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoadingDetail.Text = $"正在加载{categoryName}…";
                countText.Text = $"正在加载{categoryName}…";
            });

            var items = await fetchFunc(50);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                storeFunc(items);
                countText.Text = $"来自 Modrinth · 共 {items.Count} 个{categoryName}";
                listControl.ItemsSource = new ObservableCollection<ModrinthProject>(items);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                countText.Text = $"{categoryName}加载失败: {ex.Message}";
            });
        }
    }

    private static void UpdatePageUI(TextBlock pageText, Button prevBtn, Button nextBtn, int page, int totalPages)
    {
        pageText.Text = $"第 {page} 页 / 共 {totalPages} 页";
        prevBtn.IsEnabled = page > 1;
        nextBtn.IsEnabled = page < totalPages;
    }

    /// <summary>
    /// 从版本过滤 ComboBox 获取选中的 MC 版本。
    /// "所有版本" 以及非版本号的哨兵值返回 null。
    /// </summary>
    private string? ResolveSelectedGameVersion()
    {
        var selected = ModVersionFilter?.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected) || selected == "所有版本")
            return null;
        return selected;
    }

    /// <summary>填充版本过滤器下拉：已安装版本（后台扫描磁盘）+ 兜底最新清单。</summary>
    private async System.Threading.Tasks.Task PopulateModVersionFilterAsync()
    {
        if (ModVersionFilter is null) return;

        ModVersionFilter.Items.Clear();
        ModVersionFilter.Items.Add("所有版本");

        // 优先用已安装的版本（磁盘扫描在后台执行，避免 UI 卡顿）
        List<string> installed;
        try
        {
            installed = await System.Threading.Tasks.Task.Run(() =>
            {
                var snapshot = GameInstanceStore.Current;
                return string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory)
                    ? []
                    : MinecraftDirectoryLocator
                        .GetInstalledVersionIds(snapshot.MinecraftDirectory)
                        .ToList();
            });
        }
        catch
        {
            installed = [];
        }
        foreach (var id in installed)
            ModVersionFilter.Items.Add(id);

        // 如果没有已安装版本，用 Mojang 版本清单的最新几个
        if (ModVersionFilter.Items.Count <= 1 && _allVersions is not null)
        {
            foreach (var v in _allVersions.Take(20))
                ModVersionFilter.Items.Add(v.Id);
        }

        ModVersionFilter.SelectedIndex = 0;
    }

    private void ApplyFilter()
    {
        if (_allVersions is null) return;

        var filterKey = VersionTypeFilter.SelectedIndex switch
        {
            1 => "release",
            2 => "snapshot",
            3 => "old",
            _ => "all"
        };

        var searchText = VersionSearchBox?.Text ?? "";
        _versionFiltered = VersionFilter.Apply(_allVersions, filterKey)
            .Where(v => string.IsNullOrWhiteSpace(searchText) ||
                        v.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _versionPage = 1;
        ShowVersionPage();
    }

    private void ShowVersionPage()
    {
        if (_versionFiltered is null) return;
        var totalPages = Math.Max(1, (_versionFiltered.Count + PageSize - 1) / PageSize);
        _versionPage = Math.Clamp(_versionPage, 1, totalPages);

        VersionList.ItemsSource = new ObservableCollection<MinecraftVersion>(
            _versionFiltered.Skip((_versionPage - 1) * PageSize).Take(PageSize));
        VersionCountText.Text = $"共 {_versionFiltered.Count} 个版本 · 第 {_versionPage}/{totalPages} 页";
        UpdatePageUI(VersionPageText, VersionPrevBtn, VersionNextBtn, _versionPage, totalPages);
    }

    private void FilterAndPageModrinth(
        List<ModrinthProject>? allItems, string searchText,
        ref List<ModrinthProject>? filteredStore, ref int page,
        ItemsControl listControl, TextBlock countText,
        TextBlock pageText, Button prevBtn, Button nextBtn,
        string categoryName)
    {
        if (allItems is null) return;

        var query = allItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(p => p.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                     p.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        filteredStore = query.ToList();
        page = 1;

        var totalPages = Math.Max(1, (filteredStore.Count + PageSize - 1) / PageSize);
        listControl.ItemsSource = new ObservableCollection<ModrinthProject>(filteredStore.Take(PageSize));
        countText.Text = $"来自 Modrinth · 共 {filteredStore.Count} 个{categoryName} · 第 1/{totalPages} 页";
        UpdatePageUI(pageText, prevBtn, nextBtn, 1, totalPages);
    }

    private async System.Threading.Tasks.Task<List<ModrinthProject>?> SearchModrinthAsync(
        string searchText, string projectType, string categoryName,
        TextBlock countText, string? gameVersion = null)
    {
        if (string.IsNullOrWhiteSpace(searchText) && string.IsNullOrWhiteSpace(gameVersion))
            return null;

        // 取消上一次搜索
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        try
        {
            countText.Text = $"正在搜索{categoryName}…";
            return await ModrinthSearch.SearchAsync(projectType, searchText, gameVersion, 50, ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            countText.Text = $"搜索失败: {ex.Message}";
            return null;
        }
    }

    private void ShowModrinthPage(
        List<ModrinthProject>? filteredStore, ref int page,
        ItemsControl listControl, TextBlock countText,
        TextBlock pageText, Button prevBtn, Button nextBtn,
        string categoryName)
    {
        if (filteredStore is null) return;

        var totalPages = Math.Max(1, (filteredStore.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, totalPages);

        listControl.ItemsSource = new ObservableCollection<ModrinthProject>(
            filteredStore.Skip((page - 1) * PageSize).Take(PageSize));
        countText.Text = $"来自 Modrinth · 共 {filteredStore.Count} 个{categoryName} · 第 {page}/{totalPages} 页";
        UpdatePageUI(pageText, prevBtn, nextBtn, page, totalPages);
    }

    private void OnVersionSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnModSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ModSearchBox?.Text ?? "";
            var gameVersion = ResolveSelectedGameVersion();
            if (string.IsNullOrWhiteSpace(searchText) && string.IsNullOrWhiteSpace(gameVersion))
            {
                FilterAndPageModrinth(_allMods, "", ref _modFiltered, ref _modPage,
                    ModList, ModCountText, ModPageText, ModPrevBtn, ModNextBtn, "Mod");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "mod", "Mod", ModCountText, gameVersion);
            if (results is not null)
            {
                _modFiltered = results;
                _modPage = 1;
                ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                    ModPageText, ModPrevBtn, ModNextBtn, "Mod");
            }
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnModVersionFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            var searchText = ModSearchBox?.Text ?? "";
            var gameVersion = ResolveSelectedGameVersion();

            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                // 选择了"所有版本"，回退到完整列表
                FilterAndPageModrinth(_allMods, searchText, ref _modFiltered, ref _modPage,
                    ModList, ModCountText, ModPageText, ModPrevBtn, ModNextBtn, "Mod");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "mod", "Mod", ModCountText, gameVersion);
            if (results is not null)
            {
                _modFiltered = results;
                _modPage = 1;
                ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                    ModPageText, ModPrevBtn, ModNextBtn, "Mod");
            }
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"筛选异常: {ex.Message}";
        }
    }

    private async void OnModpackSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ModpackSearchBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilterAndPageModrinth(_allModpacks, "", ref _modpackFiltered, ref _modpackPage,
                    ModpackList, ModpackCountText, ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "modpack", "整合包", ModpackCountText);
            if (results is not null)
            {
                _modpackFiltered = results;
                _modpackPage = 1;
                ShowModrinthPage(_modpackFiltered, ref _modpackPage, ModpackList, ModpackCountText,
                    ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
            }
        }
        catch (Exception ex)
        {
            ModpackCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnShaderSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ShaderSearchBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilterAndPageModrinth(_allShaders, "", ref _shaderFiltered, ref _shaderPage,
                    ShaderList, ShaderCountText, ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "shader", "光影包", ShaderCountText);
            if (results is not null)
            {
                _shaderFiltered = results;
                _shaderPage = 1;
                ShowModrinthPage(_shaderFiltered, ref _shaderPage, ShaderList, ShaderCountText,
                    ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
            }
        }
        catch (Exception ex)
        {
            ShaderCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnResourcepackSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ResourcepackSearchBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilterAndPageModrinth(_allResourcepacks, "", ref _resourcepackFiltered, ref _resourcepackPage,
                    ResourcepackList, ResourcepackCountText, ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "resourcepack", "材质包", ResourcepackCountText);
            if (results is not null)
            {
                _resourcepackFiltered = results;
                _resourcepackPage = 1;
                ShowModrinthPage(_resourcepackFiltered, ref _resourcepackPage, ResourcepackList, ResourcepackCountText,
                    ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
            }
        }
        catch (Exception ex)
        {
            ResourcepackCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private void OnVersionPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_versionFiltered is not null && _versionPage > 1)
        {
            _versionPage--;
            ShowVersionPage();
        }
    }

    private void OnVersionNextClick(object? sender, RoutedEventArgs e)
    {
        if (_versionFiltered is null) return;
        var total = Math.Max(1, (_versionFiltered.Count + PageSize - 1) / PageSize);
        if (_versionPage < total)
        {
            _versionPage++;
            ShowVersionPage();
        }
    }

    private void OnModListTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Source is not Control control) return;
            var project = FindDataContext<ModrinthProject>(control);
            if (project is null) return;
            ModInstallRequested?.Invoke(this, project);
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"操作失败：{ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // 整合包 / 光影包 / 材质包 列表项点击 → 弹出内容下载遮罩层
    // ------------------------------------------------------------------

    private void OnContentListTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Source is not Control control) return;
            var project = FindDataContext<ModrinthProject>(control);
            if (project is null) return;

            var kind = ReferenceEquals(sender, ModpackList) ? ContentDownloadKind.Modpack
                : ReferenceEquals(sender, ShaderList) ? ContentDownloadKind.Shaderpack
                : ContentDownloadKind.Resourcepack;

            ContentDownloadRequested?.Invoke(this, (project, kind));
        }
        catch (Exception ex)
        {
            var countText = ReferenceEquals(sender, ModpackList) ? ModpackCountText
                : ReferenceEquals(sender, ShaderList) ? ShaderCountText
                : ResourcepackCountText;
            countText.Text = $"操作失败：{ex.Message}";
        }
    }

    private static T? FindDataContext<T>(Control control) where T : class
    {
        var current = control;
        while (current is not null)
        {
            if (current.DataContext is T typed)
                return typed;
            current = current.Parent as Control;
        }
        return null;
    }

    private void OnModPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_modFiltered is not null && _modPage > 1)
        {
            _modPage--;
            ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                ModPageText, ModPrevBtn, ModNextBtn, "Mod");
        }
    }

    private void OnModNextClick(object? sender, RoutedEventArgs e)
    {
        if (_modFiltered is null) return;
        var total = Math.Max(1, (_modFiltered.Count + PageSize - 1) / PageSize);
        if (_modPage < total)
        {
            _modPage++;
            ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                ModPageText, ModPrevBtn, ModNextBtn, "Mod");
        }
    }

    private void OnModpackPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_modpackFiltered is not null && _modpackPage > 1)
        {
            _modpackPage--;
            ShowModrinthPage(_modpackFiltered, ref _modpackPage, ModpackList, ModpackCountText,
                ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
        }
    }

    private void OnModpackNextClick(object? sender, RoutedEventArgs e)
    {
        if (_modpackFiltered is null) return;
        var total = Math.Max(1, (_modpackFiltered.Count + PageSize - 1) / PageSize);
        if (_modpackPage < total)
        {
            _modpackPage++;
            ShowModrinthPage(_modpackFiltered, ref _modpackPage, ModpackList, ModpackCountText,
                ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
        }
    }

    private void OnShaderPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_shaderFiltered is not null && _shaderPage > 1)
        {
            _shaderPage--;
            ShowModrinthPage(_shaderFiltered, ref _shaderPage, ShaderList, ShaderCountText,
                ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
        }
    }

    private void OnShaderNextClick(object? sender, RoutedEventArgs e)
    {
        if (_shaderFiltered is null) return;
        var total = Math.Max(1, (_shaderFiltered.Count + PageSize - 1) / PageSize);
        if (_shaderPage < total)
        {
            _shaderPage++;
            ShowModrinthPage(_shaderFiltered, ref _shaderPage, ShaderList, ShaderCountText,
                ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
        }
    }

    private void OnResourcepackPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_resourcepackFiltered is not null && _resourcepackPage > 1)
        {
            _resourcepackPage--;
            ShowModrinthPage(_resourcepackFiltered, ref _resourcepackPage, ResourcepackList, ResourcepackCountText,
                ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
        }
    }

    private void OnResourcepackNextClick(object? sender, RoutedEventArgs e)
    {
        if (_resourcepackFiltered is null) return;
        var total = Math.Max(1, (_resourcepackFiltered.Count + PageSize - 1) / PageSize);
        if (_resourcepackPage < total)
        {
            _resourcepackPage++;
            ShowModrinthPage(_resourcepackFiltered, ref _resourcepackPage, ResourcepackList, ResourcepackCountText,
                ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
        }
    }

    private void OnVersionFilterChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        if (DownloadTabs.SelectedItem is not TabItem tab || tab.Header is not string header)
            return;

        _isRefreshing = true;
        try
        {
            // 仅刷新当前标签：重置已加载标记后重新拉取
            ResetTabLoaded(header);
            LoadTabAsync(tab);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private IBrush FindBrush(string key)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, null, out var value) == true)
        {
            if (value is IBrush brush)
                return brush;
            // 部分主题键是 Color 而非 SolidColorBrush，代码侧需包装成画刷
            if (value is Color color)
                return new SolidColorBrush(color);
        }
        return Brushes.Gray;
    }
}
