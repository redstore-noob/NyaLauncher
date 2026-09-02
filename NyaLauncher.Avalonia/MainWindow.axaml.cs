using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Dialogs;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Pages;
using NyaLauncher.Avalonia.Plugins;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Avalonia.Windows;
using NyaLauncher.Core;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Launch.Auth;
using NyaLauncher.Core.Logs;
using NyaLauncher.Core.Models;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Avalonia;

public partial class MainWindow : Window
{
    private readonly WorkspaceProfileStore _profileStore = new();
    private readonly MinecraftProfileService _minecraftProfileService = new();
    private readonly PluginManager _pluginManager;
    private readonly PluginRepositoryClient _pluginRepositoryClient;
    private readonly GameLaunchService _gameLaunchService;
    private readonly GameDownloadService _gameDownloadService;
    private readonly DownloadPage _downloadPage;
    private readonly VersionManagerPage _versionManagerPage;
    private readonly SettingsHubPage _settingsPage;
    private readonly PluginManagerPage _pluginManagerPage;
    private readonly AccountManagePage _accountManagePage;
    private readonly MusicPlayerPage _musicPlayerPage;
    private TaskDetailsWindow? _taskDetailsWindow;
    private bool _suppressWorkspaceSave;
    private bool _storageChangeInProgress;
    private bool _polygonShutdownInProgress;
    private bool _polygonShutdownComplete;
    private bool _themeReloading;
    private DispatcherTimer? _windowSizeSaveTimer;
    private bool _applyingSavedSize;
    private Core.Logs.LogsWrite _logSystem;

    /// <summary>
    /// Shared extension point for built-in modules and future plugins.
    /// A plugin can register an area at runtime and the workspace updates itself.
    /// </summary>
    public FeatureAreaRegistry FeatureAreas { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        // 让 config.json 与 workspace.json 存放在同一目录（含自定义存储目录）。
        LauncherConfig.SetStorageDirectory(_profileStore.StorageDirectory);
        try { DownloadSettings.ApplySavedSettings(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"下载设置恢复失败，使用默认值：{ex.Message}"); }
        // 启动即应用「彩虹背景」开关（同理）
        AmbientGradient.AmbientGradientEnabled = Pages.ThemeSettings.LoadAmbientGradient();
        // 启动即应用「星尘特效」开关（同理）
        SparkleTrail.SparkleTrailEnabled = Pages.ThemeSettings.LoadSparkleTrail();
        _pluginManager = new PluginManager(
            _profileStore.StorageDirectory,
            FeatureAreas,
            Workspace.DrainPluginComponentsAsync);
        _pluginRepositoryClient = new PluginRepositoryClient();
        // 插件系统：启动链注入 BuildLaunchTransformAsync，允许插件贡献 JVM/游戏参数与环境变量
        _gameLaunchService = new GameLaunchService(_pluginManager.BuildLaunchTransformAsync);
        _gameLaunchService.Changed += OnGameLaunchChanged;
        _gameDownloadService = new GameDownloadService();
        _gameDownloadService.Changed += OnGameDownloadChanged;
        // 日志系统在构造函数初始化：ShowStatus 可能早于窗口 Loaded 被快照事件触发
        _logSystem = new LogsWrite();

        FeatureAreas.Register(new BuiltInFeatureAreaProvider(
            NavigateFromAction,
            _minecraftProfileService,
            _gameLaunchService,
            OpenServerJoinDialog,
            _pluginManager));
        var profile = _profileStore.Load();
        FeatureAreas.SetGlobalComponentScale(profile.GlobalComponentScale);
        FeatureAreas.SynchronizeUserAreas(profile.CustomAreas);
        FeatureAreas.ApplyPersonalization(profile.Areas);

        Workspace.UseRegistry(FeatureAreas);
        Workspace.ImportLayout(
            profile.Layout,
            profile.Sidebars,
            profile.ComponentPlacements,
            profile.GlobalComponentScale);
        Workspace.LayoutChanged += (_, _) =>
        {
            if (!_suppressWorkspaceSave && !_storageChangeInProgress)
                SaveWorkspaceProfile();
        };
        Workspace.ComponentDropRequested += OnComponentDropRequested;
        Workspace.ComponentDiscardRequested += OnComponentRemovalRequested;
        Workspace.ComponentFeedback += (_, message) => ShowStatus(message);

        _downloadPage = new DownloadPage(_gameDownloadService);
        _downloadPage.ModInstallRequested += (_, project) =>
            ModalHost.Show(BuildModView(project));
        _downloadPage.ContentDownloadRequested += (_, args) =>
            ModalHost.Show(BuildContentView(args.Project, args.Kind));
        _versionManagerPage = new VersionManagerPage(_pluginManager);
        _settingsPage = new SettingsHubPage(
            FeatureAreas,
            _profileStore.StorageDirectory);
        _pluginManagerPage = new PluginManagerPage(
            _pluginManager,
            _pluginRepositoryClient);
        _settingsPage.PersonalizationSaved += OnPersonalizationSaved;
        _settingsPage.AccountManageRequested += OnAccountManageRequested;

        // ------------------------------------------------------------
        // 右侧「组件库」抽屉初始化
        // ------------------------------------------------------------
        ComponentLibraryView.AttachRegistry(FeatureAreas);
        // 拖动任一组件卡：立即缩回抽屉（回调发生在 DoDragDropAsync 之前，动画由渲染线程播完）
        ComponentLibraryView.DragStarting += (_, _) => CloseComponentLibraryDrawer();
        // 点击遮罩：关闭抽屉
        ComponentLibraryScrim.PointerPressed += (_, _) => CloseComponentLibraryDrawer();
        QuickNavScrim.PointerPressed += (_, _) => CloseQuickNavDrawer();

        // 主题热重载：切换主题后重挂载根元素刷新 StaticResource
        ThemeManager.ThemeChanged += OnThemeHotReload;
        _settingsPage.InstanceManageRequested += (_, _) =>
        {
            _versionManagerPage.Activate();
            ShowPage(_versionManagerPage, "版本管理");
        };
        _settingsPage.JavaRuntimeManageRequested += (_, _) =>
        {
            _downloadPage.ActivateJavaTab();
            ShowPage(_downloadPage, "资源下载");
        };
        _accountManagePage = new AccountManagePage();
        _musicPlayerPage = new MusicPlayerPage();

        AddHandler(
            KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                UpdateWindowStateIcons();
                // 从任务栏恢复（最小化→普通/最大化）：播放放大淡入，承接最小化的收缩动画
                if (args.OldValue is WindowState.Minimized && args.NewValue is not WindowState.Minimized)
                    WindowEffects.Restore(this, fromMinimized: true);
            }
        };
        UpdateWindowStateIcons();
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        // 整合包拖拽安装：Avalonia 12 移除了 Window 的 XAML 拖放属性（AllowDrop/DragOver/Drop），
        // 必须改用 DragDrop 静态类代码附加
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnWindowDragOver);
        DragDrop.AddDropHandler(this, OnWindowDrop);
        // 窗口尺寸变更（防抖 300ms）后轻量保存，避免拖动时反复整文件回写
        SizeChanged += (_, _) => OnWindowSizeChanged();
        _windowSizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _windowSizeSaveTimer.Tick += (_, _) =>
        {
            _windowSizeSaveTimer.Stop();
            SaveCurrentWindowSize();
        };
        // 主窗口创建时「飞出来」：入场动效逻辑在 Animations 模块的 WindowEffects.Enter
        Opened += (_, _) =>
        {
            WindowEffects.Enter(this);
            // 兜底强制启用（class 正常时幂等）：背景流光 + 星尘跟随鼠标
            AmbientGradient.Enable(WindowRoot);
            SparkleTrail.Enable(WindowRoot);
        };
    }

    /// <summary>构建 Mod 下载内容视图（每次新建实例，避免状态残留与事件重复订阅）。</summary>
    private ModDownloadOverlay BuildModView(ModrinthProject project)
    {
        var view = new ModDownloadOverlay();
        view.Setup(project);
        return view;
    }

    /// <summary>构建整合包/资源包/光影包下载内容视图。</summary>
    private ContentDownloadOverlay BuildContentView(ModrinthProject project, ContentDownloadKind kind)
    {
        var view = new ContentDownloadOverlay { DownloadService = _gameDownloadService };
        view.Setup(project, kind);
        return view;
    }

    /// <summary>
    /// 「服务器快连」组件请求进服：弹出遮罩层选择版本，
    /// 确认后切换选中实例并携带 --server/--port 参数后台启动。
    /// </summary>
    private void OpenServerJoinDialog(ServerJoinRequest request)
    {
        // 组件动作由 PolygonComponentInstanceHost 经 Task.Run 在后台线程执行；
        // 创建遮罩层等 UI 操作必须封送回 UI 线程，否则抛跨线程异常。
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var instance = GameInstanceStore.Current;
                var view = new ServerJoinOverlay(
                    request,
                    instance.VersionIds,
                    instance.SelectedVersionId);
                view.VersionLaunchRequested += (_, versionId) =>
                {
                    if (!GameInstanceStore.Select(versionId))
                        return;

                    ModalHost.Close();
                    // 启动进度由「启动游戏」组件卡片显示，无需再跳转旧启动页
                    var host = request.Host;
                    var port = request.Port;
                    _ = Task.Run(() => _gameLaunchService.LaunchSelectedAsync(
                        CancellationToken.None, host, port));
                };
                ModalHost.Show(view);
            }
            catch (Exception exception)
            {
                ShowStatus($"无法打开进服选择器：{exception.Message}");
            }
        });
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            await _pluginManager.InitializeAsync();
        }
        catch (Exception exception)
        {
            ShowStatus($"插件系统初始化失败：{exception.Message}");
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_storageChangeInProgress)
        {
            e.Cancel = true;
            ShowStatus("配置与插件目录正在迁移，请等待完成后再关闭启动器。");
            return;
        }
        if (_polygonShutdownComplete)
            return;

        // 关闭前兜底保存当前窗口尺寸（已内部判断非普通状态则跳过）
        SaveCurrentWindowSize();

        e.Cancel = true;
        if (_polygonShutdownInProgress)
            return;

        _polygonShutdownInProgress = true;
        SaveWorkspaceProfile();
        try
        {
            await Workspace.ShutdownPolygonComponentsAsync();
            await _pluginManager.DisposeAsync();
        }
        finally
        {
            _pluginRepositoryClient.Dispose();
            _polygonShutdownComplete = true;
            _polygonShutdownInProgress = false;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // 先播「飞走」退场动效（Animations 模块 WindowEffects.Exit），播完再真正关闭；
                    // 此时 _polygonShutdownComplete 已为 true，再次 OnClosing 会直接放行，不会递归。
                    WindowEffects.Exit(this, () => Close());
                }
                catch (InvalidOperationException)
                {
                    // The platform may have completed an operating-system shutdown
                    // while asynchronous plugin cleanup was in progress.
                }
            });
        }
    }

    private void NavigateFromAction(string actionId)
    {
        switch (actionId)
        {
            case "account":
                ShowPage(_accountManagePage, "账户管理");
                break;

            case "instances":
            case "version-manager":
                _versionManagerPage.Activate();
                ShowPage(_versionManagerPage, "版本管理");
                break;

            case "account-login":
                // 只打开账户管理页面即可，登录遮罩由页面内的"＋ 添加账户"按钮唤起
                ShowPage(_accountManagePage, "账户管理");
                break;

            case "downloads":
            case "tasks":
                ShowPage(_downloadPage, "资源下载");
                break;

            case "settings":
            case "runtime":
                ShowSettings(SettingsSection.Launcher);
                break;

            case "music":
            case "music-player":
                ShowPage(_musicPlayerPage, "音乐播放器");
                break;

            case "plugins":
                _pluginManagerPage.Activate();
                ShowPage(_pluginManagerPage, "插件列表");
                break;

            default:
                ShowStatus($"尚未注册页面：{actionId}");
                break;
        }
    }

    /// <summary>页面切换代数：新切换会使进行中的退出动画序作废，防止快速连续点击时序错乱。</summary>
    private int _pageTransitionGeneration;

    private async void ShowPage(Control page, string title)
    {
        var generation = ++_pageTransitionGeneration;
        var oldPage = PageHost.Content as Control;
        if (oldPage is { } && !ReferenceEquals(oldPage, page) && PageSurface.IsVisible)
        {
            await AnimationHelper.SlideFadeOutAsync(oldPage);
            if (generation != _pageTransitionGeneration)
            {
                // 已被更新的一次切换取代；旧页的隐藏与复位由新的那次切换负责
                return;
            }
            // 旧页已脱离（即将被替换）：复位缓存页面，供下次复用（动画总开关关闭时无残留）
            oldPage.Opacity = 1;
            oldPage.RenderTransform = null;
            oldPage.IsHitTestVisible = true;
        }

        PageHost.Content = page;
        CurrentPageTitle.Text = title;
        Workspace.IsVisible = false;
        PageSurface.IsVisible = true;
        HeaderStatusText.Text = title;
        ShowStatus($"已进入：{title}");

        // 页面切换动效：淡入 + 轻微上浮（滑入）
        _ = AnimationHelper.SlideFadeInAsync(page);
    }

    private void ShowSettings(SettingsSection section)
    {
        _settingsPage.SelectSection(section);
        ShowPage(_settingsPage, "设置");
    }

    /// <summary>设置页中的账户管理入口：切换到新的账户管理页面。</summary>
    private void OnAccountManageRequested(object? sender, EventArgs e)
    {
        ShowPage(_accountManagePage, "账户管理");
    }

    private async void ShowWorkspace()
    {
        var generation = ++_pageTransitionGeneration;
        var oldPage = PageHost.Content as Control;
        if (oldPage is { } && PageSurface.IsVisible)
        {
            await AnimationHelper.SlideFadeOutAsync(oldPage);
            if (generation != _pageTransitionGeneration)
            {
                // 已被更新的一次切换取代；旧页的隐藏与复位由新的那次切换负责
                return;
            }
        }

        PageSurface.IsVisible = false;
        PageHost.Content = null;
        if (oldPage is { })
        {
            // 页面已脱离视觉树：复位缓存页面（透明度/位移/命中），供下次复用
            oldPage.Opacity = 1;
            oldPage.RenderTransform = null;
            oldPage.IsHitTestVisible = true;
        }
        Workspace.IsVisible = true;
        CurrentPageTitle.Text = string.Empty;
        HeaderStatusText.Text = "工作区已就绪";
    }

    // ------------------------------------------------------------------
    // 整合包拖拽安装：.zip 拖入窗口 → 解压到当前选中实例的内容目录
    // ------------------------------------------------------------------

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        var hasZip = e.DataTransfer.TryGetFiles()?.Any(f =>
            f.TryGetLocalPath()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true) == true;
        e.DragEffects = hasZip ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        var zip = e.DataTransfer.TryGetFiles()?
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => p?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
        if (zip is null)
        {
            ShowStatus("请拖入 .zip 整合包文件");
            return;
        }

        var instance = GameInstanceStore.Current;
        if (string.IsNullOrWhiteSpace(instance.SelectedVersionId) ||
            (string.IsNullOrWhiteSpace(instance.SourcePath) &&
             string.IsNullOrWhiteSpace(instance.MinecraftDirectory)))
        {
            NyaAlert.Info("请先在「启动游戏」页面选择一个游戏实例，再拖入整合包");
            return;
        }

        var contentDir = ContentInstallService.ResolveContentDirectory(
            instance.MinecraftDirectory, instance.SourcePath, instance.SelectedVersionId);
        if (string.IsNullOrWhiteSpace(contentDir))
        {
            NyaAlert.Error("无法解析实例内容目录，安装已取消");
            return;
        }

        ShowStatus($"正在安装整合包：{Path.GetFileName(zip)} …");
        try
        {
            (int installed, int downloaded, List<string> errors) =
                await ContentInstallService.InstallModpackAsync(zip, contentDir);
            ShowStatus(errors.Count == 0
                ? $"整合包安装完成：解压 {installed} 个文件"
                : $"整合包安装完成（解压 {installed} 个，{errors.Count} 个问题）：{string.Join("；", errors.Take(2))}");
        }
        catch (Exception ex)
        {
            NyaAlert.Error($"整合包安装失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 由于不敢动之前的代码,该函数暂时与Log系统与NyaAlert()同时调用.
    /// </summary>
    /// <param name="message">日志系统中出现的日志,状态栏Text出现的文字,信息条出现的文字</param>
    private void ShowStatus(string message)
    {
        if (!NyaLauncherInfo.IsUnstable)
        {
            _logSystem.AddLogs(message, null);
            return;
        }
        StatusText.Text = message;
        NyaAlert.Info(message);
        _logSystem.AddLogs(message, null);
    }

    private void OnGameLaunchChanged(GameLaunchSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameLaunchChanged(snapshot));
            return;
        }

        UpdateTaskActivityIndicator();

        if (snapshot.Phase is GameLaunchPhase.Preparing or GameLaunchPhase.Running)
            HeaderStatusText.Text = snapshot.Title;
        ShowStatus($"{snapshot.Title}：{snapshot.Message}");
    }

    private void OnGameDownloadChanged(GameDownloadSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameDownloadChanged(snapshot));
            return;
        }

        UpdateTaskActivityIndicator();
        ShowStatus($"{snapshot.StageName}：{snapshot.Detail}");
    }

    private void UpdateTaskActivityIndicator()
    {
        var download = _gameDownloadService.Current;
        if (download.IsActive)
        {
            TaskActivityButton.IsVisible = true;
            TaskActivityButton.Background = ThemePolygonHelper.TaskDownloadingBg;
            TaskActivityButton.BorderBrush = ThemePolygonHelper.TaskDownloadingBorder;
            // 下载中图标："material:Kind" 渲染为 Material 图标，其余回退文字
            TaskActivityIcon.Content = CreateTaskActivityGlyph("material:ArrowDownward");
            TaskActivityProgress.IsVisible = true;
            TaskActivityProgress.IsIndeterminate = download.TotalBytes <= 0;
            TaskActivityProgress.Value = download.Percentage;
            ToolTip.SetTip(
                TaskActivityButton,
                $"正在下载 Minecraft {download.VersionId}\n" +
                $"{download.StageName} · {download.Percentage:0.0}%\n点击查看下载详情");
            return;
        }

        var launch = _gameLaunchService.Current;
        TaskActivityButton.IsVisible = launch.ShouldShowIndicator;
        if (!launch.ShouldShowIndicator)
            return;

        TaskActivityButton.Background = ThemePolygonHelper.TaskLaunchingBg;
        TaskActivityButton.BorderBrush = ThemePolygonHelper.TaskLaunchingBorder;
        TaskActivityProgress.IsVisible = launch.Phase == GameLaunchPhase.Preparing;
        TaskActivityProgress.IsIndeterminate = launch.Phase == GameLaunchPhase.Preparing;
        // 启动状态图标："material:Kind" 渲染为 Material 图标，其余（… / !）回退文字
        TaskActivityIcon.Content = launch.Phase switch
        {
            GameLaunchPhase.Preparing => CreateTaskActivityGlyph("…"),
            GameLaunchPhase.Running => CreateTaskActivityGlyph("material:Play"),
            GameLaunchPhase.Failed => CreateTaskActivityGlyph("!"),
            GameLaunchPhase.Exited => CreateTaskActivityGlyph("material:Check"),
            _ => CreateTaskActivityGlyph("material:Play")
        };
        ToolTip.SetTip(
            TaskActivityButton,
            $"{launch.Title}\n{launch.Message}\n点击查看启动日志");
    }

    /// <summary>
    /// 创建任务按钮图标："material:Kind" 渲染为 Material 图标，其余回退文字；
    /// 前景色沿用原 WhiteBrush 主题资源。
    /// </summary>
    private Control CreateTaskActivityGlyph(string glyph)
    {
        var foreground = Application.Current?.TryGetResource("WhiteBrush", null, out var resource) == true &&
                         resource is IBrush brush
            ? brush
            : Brushes.White;
        return FeatureIconFactory.CreateGlyph(glyph, 22, foreground);
    }

    private void OnTaskActivityClick(object? sender, RoutedEventArgs e)
    {
        var preferredView = _gameDownloadService.Current.IsActive
            ? TaskDetailView.Download
            : TaskDetailView.Launch;
        if (!_gameDownloadService.Current.IsActive &&
            !_gameLaunchService.Current.ShouldShowIndicator)
        {
            return;
        }

        if (_taskDetailsWindow is not null)
        {
            _taskDetailsWindow.ShowPreferredView();
            _taskDetailsWindow.Activate();
            return;
        }

        try
        {
            _taskDetailsWindow = new TaskDetailsWindow(
                _gameDownloadService,
                _gameLaunchService,
                preferredView);
            _taskDetailsWindow.Closed += (_, _) => _taskDetailsWindow = null;
            _taskDetailsWindow.Show(this);
        }
        catch (Exception exception)
        {
            _taskDetailsWindow = null;
            ShowStatus($"任务详情窗口打开失败：{exception.Message}");
        }
    }

    private void OnBackToWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        ShowWorkspace();
    }

    /// <summary>状态栏右下角齿轮按钮：快速进入设置页（与 Ctrl+, 一致）。</summary>
    private void OnSettingsQuickClick(object? sender, RoutedEventArgs e)
    {
        ShowSettings(SettingsSection.Launcher);
    }

    private bool _componentLibraryDrawerOpen;
    private int _drawerAnimationGeneration;
    private const double ComponentLibraryDrawerWidth = 400;

    /// <summary>点击状态栏「组件库」按钮：切换右侧抽屉开合。</summary>
    private void OnComponentLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (_componentLibraryDrawerOpen)
            CloseComponentLibraryDrawer();
        else
            OpenComponentLibraryDrawer();
    }

    /// <summary>
    /// 展开右侧组件库抽屉：容器先滑出（M3 emphasized 300ms），
    /// 走到 40% 时再播卡片错峰入场，符合 M3「先容器后内容」。
    /// </summary>
    private async void OpenComponentLibraryDrawer()
    {
        if (_componentLibraryDrawerOpen)
            return;
        var generation = ++_drawerAnimationGeneration;
        _componentLibraryDrawerOpen = true;
        CloseQuickNavDrawer();

        ComponentLibraryScrim.IsVisible = true;
        ComponentLibraryDrawer.IsVisible = true;
        // Transitions 需要观察到属性变化才生效；先确保基线为 0 再在下一帧赋展开宽度
        ComponentLibraryDrawer.Width = 0;
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (generation == _drawerAnimationGeneration)
                    ComponentLibraryDrawer.Width = ComponentLibraryDrawerWidth;
            },
            DispatcherPriority.Loaded);

        // 300ms * 40% = 120ms 后开始内容入场
        await Task.Delay((int)(MaterialMotion.MediumTransitionMs * MaterialMotion.FadeEndFraction));
        if (generation == _drawerAnimationGeneration)
            ComponentLibraryView.PlayStagger();
    }

    /// <summary>
    /// 缩回右侧组件库抽屉：宽度动画回 0；拖拽场景下 DoDragDropAsync 会阻塞
    /// UI 线程，但 Transitions 在渲染线程继续播放，不受影响。
    /// </summary>
    private async void CloseComponentLibraryDrawer()
    {
        if (!_componentLibraryDrawerOpen)
            return;
        var generation = ++_drawerAnimationGeneration;
        _componentLibraryDrawerOpen = false;

        ComponentLibraryScrim.IsVisible = false;
        ComponentLibraryDrawer.Width = 0;

        // 动画播完再隐藏，避免截断（生成号变化说明期间又重新打开，则跳过）
        await Task.Delay(MaterialMotion.MediumTransitionMs + 20);
        if (generation == _drawerAnimationGeneration)
            ComponentLibraryDrawer.IsVisible = false;
    }

    // ------------------------------------------------------------
    // 左侧「快捷入口」抽屉：机制与组件库抽屉一致（宽度 Transitions
    // + 生成号防错乱），两个抽屉互斥展开。
    // ------------------------------------------------------------
    private bool _quickNavDrawerOpen;
    private int _quickNavAnimationGeneration;
    private const double QuickNavDrawerWidth = 260;

    /// <summary>点击状态栏左下角「快捷入口」按钮：切换左侧抽屉开合。</summary>
    private void OnQuickNavClick(object? sender, RoutedEventArgs e)
    {
        if (_quickNavDrawerOpen)
            CloseQuickNavDrawer();
        else
            OpenQuickNavDrawer();
    }

    private async void OpenQuickNavDrawer()
    {
        if (_quickNavDrawerOpen)
            return;
        var generation = ++_quickNavAnimationGeneration;
        _quickNavDrawerOpen = true;
        CloseComponentLibraryDrawer();

        QuickNavScrim.IsVisible = true;
        QuickNavDrawer.IsVisible = true;
        QuickNavDrawer.Width = 0;
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (generation == _quickNavAnimationGeneration)
                    QuickNavDrawer.Width = QuickNavDrawerWidth;
            },
            DispatcherPriority.Loaded);
    }

    private async void CloseQuickNavDrawer()
    {
        if (!_quickNavDrawerOpen)
            return;
        var generation = ++_quickNavAnimationGeneration;
        _quickNavDrawerOpen = false;

        QuickNavScrim.IsVisible = false;
        QuickNavDrawer.Width = 0;

        await Task.Delay(MaterialMotion.MediumTransitionMs + 20);
        if (generation == _quickNavAnimationGeneration)
            QuickNavDrawer.IsVisible = false;
    }

    private void OnQuickNavDownloadsClick(object? sender, RoutedEventArgs e)
    {
        CloseQuickNavDrawer();
        NavigateFromAction("downloads");
    }

    private void OnQuickNavInstancesClick(object? sender, RoutedEventArgs e)
    {
        CloseQuickNavDrawer();
        NavigateFromAction("instances");
    }

    private void OnQuickNavAccountClick(object? sender, RoutedEventArgs e)
    {
        CloseQuickNavDrawer();
        NavigateFromAction("account");
    }

    private void OnQuickNavPluginsClick(object? sender, RoutedEventArgs e)
    {
        CloseQuickNavDrawer();
        NavigateFromAction("plugins");
    }

    private void OnQuickNavSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseQuickNavDrawer();
        NavigateFromAction("settings");
    }

    private void OnComponentRemovalRequested(
        object? sender,
        ComponentRemovalRequestedEventArgs e)
    {
        if (!FeatureAreas.RemoveComponent(e.ComponentId, e.SourceAreaId))
            return;

        Workspace.RemoveComponentPlacement(e.ComponentId, e.SourceAreaId);
        SaveWorkspaceProfile();
        var component = FeatureAreas.AvailableActions.FirstOrDefault(action =>
            string.Equals(action.Id, e.ComponentId, StringComparison.OrdinalIgnoreCase));
        ShowStatus($"组件“{component?.Title ?? e.ComponentId}”已从功能区移除并保存");
    }

    private void OnComponentDropRequested(
        object? sender,
        Controls.ComponentDropRequestedEventArgs e)
    {
        var membershipChanged = FeatureAreas.PlaceComponent(
            e.ComponentId,
            e.TargetAreaId,
            e.SourceAreaId);
        if (!membershipChanged && string.IsNullOrWhiteSpace(e.SourceAreaId))
        {
            return;
        }

        var placementChanged = Workspace.SetComponentPlacement(
            e.ComponentId,
            e.TargetAreaId,
            e.SourceAreaId,
            e.RelativeX,
            e.RelativeY);
        if (!membershipChanged && !placementChanged)
            return;

        SaveWorkspaceProfile();
        var component = FeatureAreas.AvailableActions.FirstOrDefault(action =>
            string.Equals(action.Id, e.ComponentId, StringComparison.OrdinalIgnoreCase));
        var target = FeatureAreas.Areas.FirstOrDefault(area =>
            string.Equals(area.Id, e.TargetAreaId, StringComparison.OrdinalIgnoreCase));
        var operation = string.IsNullOrWhiteSpace(e.SourceAreaId)
            ? "添加"
            : string.Equals(e.SourceAreaId, e.TargetAreaId, StringComparison.OrdinalIgnoreCase)
                ? "重新摆放"
                : "移动";
        ShowStatus($"组件“{component?.Title ?? e.ComponentId}”已{operation}至“{target?.Title ?? e.TargetAreaId}”并保存");
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // 设置页正在录制新快捷键：按键优先喂给捕获流程
        if (AppHotkeys.IsCapturing)
        {
            e.Handled = true;
            AppHotkeys.FeedKey(e);
            return;
        }

        // 「打开设置」快捷键（可在设置中自定义，默认 Ctrl+,）
        if (AppHotkeys.OpenSettingsGesture.Matches(e))
        {
            ShowSettings(SettingsSection.Launcher);
            e.Handled = true;
            return;
        }

        // 「快捷启动」快捷键（默认未设置，需在设置中录制）
        if (AppHotkeys.QuickLaunchGesture is { } launchGesture && launchGesture.Matches(e))
        {
            // 等价于旧启动页 TriggerLaunch 的 CanLaunch 条件，进度由「启动游戏」组件卡片显示
            var launch = _gameLaunchService.Current;
            if (GameInstanceStore.Current.VersionIds.Count > 0 &&
                !launch.IsBusy &&
                !launch.IsGameRunning)
            {
                _ = _gameLaunchService.LaunchSelectedAsync(CancellationToken.None);
            }
            e.Handled = true;
            return;
        }

        // Esc：从最上层逐层退出 —— 先关模态遮罩，再关抽屉，最后从页面返回工作区
        if (e.Key != Key.Escape)
            return;

        if (ModalHost.IsVisible)
        {
            ModalHost.Close();
            e.Handled = true;
            return;
        }

        // 工作区上打开的抽屉（右侧组件库 / 左侧快捷入口）优先于页面层级关闭
        if (_componentLibraryDrawerOpen || _quickNavDrawerOpen)
        {
            CloseComponentLibraryDrawer();
            CloseQuickNavDrawer();
            e.Handled = true;
            return;
        }

        if (PageSurface.IsVisible)
        {
            ShowWorkspace();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 主题热重载：把窗口根元素脱离再挂载，强制全部控件重新应用样式与资源。
    /// 使用 Dispatcher 合并连续切换（同一帧内只重挂载一次）。
    /// </summary>
    private async void OnThemeHotReload()
    {
        if (_themeReloading)
            return;
        _themeReloading = true;

        try
        {
            // 氛围流光层在启用时一次性取色，需在资源已更新后强制重建，
            // 否则主窗口底层光效停留在旧主题配色（重挂载 detach 会先移除该层）
            NyaLauncher.Avalonia.Animations.Helpers.AmbientGradient.RecreateAll();

            // 保存当前页面状态，重挂载后自动恢复
            var pageVisible = PageSurface.IsVisible;
            var lastPage = PageHost.Content as Control;
            var lastTitle = CurrentPageTitle.Text ?? string.Empty;

            await ThemeManager.RemountRootAsync(
                this,
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(200));

            // 恢复页面状态（如果之前正处于页面视图）
            if (pageVisible && lastPage is not null)
            {
                PageHost.Content = lastPage;
                CurrentPageTitle.Text = lastTitle;
                PageSurface.IsVisible = true;
                Workspace.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHotReload] Failed: {ex}");
            // 降级：无动画同步重挂载（根元素脱离后立即挂回，强制重新应用样式）
            if (Content is Control root)
            {
                Content = null;
                Content = root;
            }
        }
        finally
        {
            _themeReloading = false;
        }
    }

    private async void OnPersonalizationSaved(
        object? sender,
        PersonalizationResult result)
    {
        if (_storageChangeInProgress)
            return;
        _storageChangeInProgress = true;
        var profile = result.Profile;
        if (result.ResetLayout)
        {
            // 「恢复默认」：布局 / 侧边栏 / 组件摆放一律回出厂，而不是把当前布局写回。
            var defaultProfile = WorkspaceDefaultProfile.Create();
            profile.Layout = defaultProfile.Layout;
            profile.Sidebars = defaultProfile.Sidebars;
            profile.ComponentPlacements = defaultProfile.ComponentPlacements;
        }
        else
        {
            profile.Layout = Workspace.ExportLayout();
            profile.Sidebars = [.. Workspace.ExportSidebars()];
            profile.ComponentPlacements = [.. Workspace.ExportComponentPlacements()];
        }

        try
        {
            var directoryChanged = !PathUtil.PathsEqual(
                _profileStore.StorageDirectory,
                result.StorageDirectory);
            StorageDirectoryChangeTransaction? storageChange = null;
            if (directoryChanged)
            {
                var inspection = _profileStore.InspectStorageDirectory(
                    result.StorageDirectory);
                var action = ExistingConfigurationAction.None;

                if (inspection.HasConfiguration)
                {
                    var dialog = new ConfigurationConflictDialog(
                        _profileStore.StorageDirectory,
                        inspection);
                    var choice = await dialog.ShowDialog<ConfigurationConflictChoice>(this);
                    if (choice == ConfigurationConflictChoice.Cancel)
                    {
                        _settingsPage.ReloadPersonalization(
                            _profileStore.StorageDirectory);
                        ShowStatus("已取消配置目录切换。");
                        return;
                    }

                    action = choice == ConfigurationConflictChoice.DeletePrevious
                        ? ExistingConfigurationAction.DeletePrevious
                        : ExistingConfigurationAction.BackupPrevious;
                }

                await _pluginManager.PrepareStorageDirectoryChangeAsync();
                try
                {
                    // Plugin packages and private data may be large; all plugin
                    // runtimes are stopped so this copy is a stable snapshot.
                    storageChange = await Task.Run(() =>
                        _profileStore.PrepareStorageDirectoryChange(
                            result.StorageDirectory,
                            profile,
                            action));

                    // The old locator and source files remain intact until the
                    // plugin manager has successfully scanned and bound target.
                    await _pluginManager.ChangeStorageDirectoryAsync(
                        storageChange.TargetDirectory);
                    LauncherConfig.SetStorageDirectory(storageChange.TargetDirectory);
                    storageChange.Complete();
                }
                catch (Exception migrationException)
                {
                    // Preparation never changes the locator. Restore every
                    // consumer to that same old root, then remove only artifacts
                    // created in an otherwise-empty target by this attempt.
                    try
                    {
                        LauncherConfig.SetStorageDirectory(_profileStore.StorageDirectory);
                        if (storageChange is null)
                        {
                            await _pluginManager.AbortStorageDirectoryChangeAsync();
                        }
                        else
                        {
                            await _pluginManager.ChangeStorageDirectoryAsync(
                                _profileStore.StorageDirectory);
                        }
                    }
                    catch (Exception recoveryException)
                    {
                        // Do not remove the prepared target while a failed
                        // manager recovery may still be using it.
                        throw new AggregateException(
                            "存储目录切换失败，且插件管理器未能恢复旧目录。",
                            migrationException,
                            recoveryException);
                    }

                    if (storageChange is not null)
                    {
                        var rollbackFailures = await Task.Run(storageChange.Rollback);
                        if (rollbackFailures.Count > 0)
                        {
                            throw new AggregateException(
                                $"存储目录切换失败，且本次目标半成品未完全清理：" +
                                string.Join("；", rollbackFailures),
                                migrationException);
                        }
                    }

                    throw;
                }
                AccountStore.Reload();
                _pluginManagerPage.ReloadRepositorySourceSettings();
                profile = storageChange.AppliedProfile;
            }
            else
            {
                _profileStore.Save(profile);
            }

            ApplyWorkspaceProfile(
                profile,
                importStoredLayout: result.ResetLayout ||
                    storageChange?.AppliedExistingConfiguration == true);
            SaveWorkspaceProfile(force: true);

            _settingsPage.ReloadPersonalization(_profileStore.StorageDirectory);
            var status = CreateStorageChangeStatus(directoryChanged, storageChange);
            ShowStatus(result.ResetLayout ? $"已恢复默认布局。{status}" : status);
        }
        catch (Exception exception)
        {
            ShowStatus($"配置保存或目录切换失败：{exception.Message}");
        }
        finally
        {
            _storageChangeInProgress = false;
        }
    }

    private void ApplyWorkspaceProfile(
        WorkspaceProfile profile,
        bool importStoredLayout)
    {
        _suppressWorkspaceSave = true;
        try
        {
            FeatureAreas.SetGlobalComponentScale(profile.GlobalComponentScale);
            FeatureAreas.SynchronizeUserAreas(profile.CustomAreas);
            FeatureAreas.ApplyPersonalization(profile.Areas);
            Workspace.SetGlobalComponentScale(profile.GlobalComponentScale);

            if (importStoredLayout)
            {
                Workspace.ImportLayout(
                    profile.Layout,
                    profile.Sidebars,
                    profile.ComponentPlacements,
                    profile.GlobalComponentScale);
            }
        }
        finally
        {
            _suppressWorkspaceSave = false;
        }
    }

    private static string CreateStorageChangeStatus(
        bool directoryChanged,
        StorageDirectoryChangeTransaction? storageChange)
    {
        if (!directoryChanged || storageChange is null)
            return "个性化配置已保存并应用";

        var message = storageChange.AppliedExistingConfiguration
            ? storageChange.BackupDirectory is null
                ? "已应用目标目录配置，旧配置已删除。"
                : $"已应用目标目录配置，旧配置已备份至：{storageChange.BackupDirectory}"
            : "配置已迁移至新的存储目录。";

        if (storageChange.CleanupFailures.Count > 0)
        {
            message += $" 但部分旧文件未能删除：{string.Join("；", storageChange.CleanupFailures)}";
        }

        return message;
    }

    private void SaveWorkspaceProfile(bool force = false)
    {
        // Component removal, drag/drop and layout events all converge here.
        // None may overwrite either side while a directory transaction is open.
        if (_storageChangeInProgress && !force)
            return;

        try
        {
            var profile = FeatureAreas.CreateCurrentProfile();
            profile.GlobalComponentScale = Workspace.GlobalComponentScale;
            profile.Layout = Workspace.ExportLayout();
            profile.Sidebars = [.. Workspace.ExportSidebars()];
            profile.ComponentPlacements = [.. Workspace.ExportComponentPlacements()];
            _profileStore.Save(profile);
        }
        catch (Exception exception)
        {
            ShowStatus($"工作区配置保存失败：{exception.Message}");
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        // 先播放"收向任务栏"动画，播完再真正最小化（最小化后窗口不可见，动画必须提前播）
        WindowEffects.Minimize(this, () => WindowState = WindowState.Minimized);
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximized();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState != WindowState.Normal ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void OnResizeWestPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.West, e);

    private void OnResizeEastPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.East, e);

    private void OnResizeNorthPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.North, e);

    private void OnResizeSouthPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.South, e);

    private void OnResizeNorthWestPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.NorthWest, e);

    private void OnResizeNorthEastPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.NorthEast, e);

    private void OnResizeSouthWestPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.SouthWest, e);

    private void OnResizeSouthEastPressed(object? sender, PointerPressedEventArgs e) =>
        BeginWindowResize(WindowEdge.SouthEast, e);

    private void ToggleMaximized()
    {
        var wasMaximized = WindowState == WindowState.Maximized;
        WindowState = wasMaximized ? WindowState.Normal : WindowState.Maximized;
        // 状态切换后播放确认动画：最大化"弹开"，还原"收正"
        if (wasMaximized)
            WindowEffects.Restore(this, fromMinimized: false);
        else
            WindowEffects.Maximize(this);
    }

    private void UpdateWindowStateIcons()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WorkspaceMaximizeIcon.IsVisible = !isMaximized;
        WorkspaceRestoreIcon.IsVisible = isMaximized;
        PageMaximizeIcon.IsVisible = !isMaximized;
        PageRestoreIcon.IsVisible = isMaximized;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplySavedWindowSize();
        // 恢复特效开关与自定义背景（配置里关掉的特效在启动时静默生效）
        ClickRing.ClickRingEnabled = ThemeSettings.LoadClickRing();
        CustomBackgroundImage.SetOpacity(ThemeSettings.LoadCustomBackgroundOpacity());
        CustomBackgroundImage.SetBlur(ThemeSettings.LoadCustomBackgroundBlur());
        CustomBackgroundImage.SetImage(ThemeSettings.LoadCustomBackground());
        // 测试版警告水印独立控制；版本号由 XAML 绑定 NyaLauncherInfo 常驻显示，
        // 不再用运行时文本覆盖（避免破坏绑定、稳定版误藏版本号）
        UnstableWatermarkText.IsVisible = NyaLauncherInfo.IsUnstable;
    }

    /// <summary>
    /// 还原上次记忆的窗口尺寸，并 clamp 到主屏可用区域，避免记忆了外接屏导致窗口跑到屏外。
    /// </summary>
    private void ApplySavedWindowSize()
    {
        var settings = GlobalLaunchSettingsStore.Load();
        var w = settings.WindowWidth;
        var h = settings.WindowHeight;
        if (w < 320 || h < 240)
            return;

        _applyingSavedSize = true;
        try
        {
            var screen = Screens.Primary;
            if (screen is not null)
            {
                var scale = RenderScaling;
                var maxW = screen.WorkingArea.Width / scale;
                var maxH = screen.WorkingArea.Height / scale;
                Width = Math.Clamp((double)w, 320.0, maxW);
                Height = Math.Clamp((double)h, 240.0, maxH);
            }
            else
            {
                Width = w;
                Height = h;
            }
        }
        finally
        {
            _applyingSavedSize = false;
        }
    }

    /// <summary>
    /// 仅在窗口处于普通状态（未最大化/最小化）时记忆尺寸，避免存下一个"铺满屏"的无效值。
    /// </summary>
    private void SaveCurrentWindowSize()
    {
        if (WindowState != WindowState.Normal)
            return;
        GlobalLaunchSettingsStore.SaveWindowSize((int)Math.Round(Width), (int)Math.Round(Height));
    }

    private void OnWindowSizeChanged()
    {
        if (_applyingSavedSize || WindowState != WindowState.Normal)
            return;
        _windowSizeSaveTimer?.Stop();
        _windowSizeSaveTimer?.Start();
    }
}
