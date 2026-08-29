using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Avalonia.Windows;

/// <summary>
/// 任务详情窗口：集中展示下载进度与游戏启动状态（含实时 Java 输出日志）。
/// <para>
/// 窗口内以 250ms 的定时器轮询服务状态并刷新界面，
/// 同时在 <see cref="Window.Opened"/> 时把自身注册到任务栏进度等其他宿主逻辑。
/// 若打开时指定的视图已经无任务可展示（例如指定「下载」但当前没有下载），
/// 窗口会自行关闭而不是留一个空界面。
/// </para>
/// </summary>
internal partial class TaskDetailsWindow : Window
{
    private readonly GameDownloadService _downloadService;
    private readonly GameLaunchService _launchService;
    private readonly DispatcherTimer _refreshTimer;
    private TaskDetailView _selectedView;
    private string _lastLaunchLog = string.Empty;
    private bool _autoCloseScheduled;

    /// <summary>
    /// 创建任务详情窗口并订阅下载/启动服务的变更事件。
    /// </summary>
    /// <param name="downloadService">游戏下载服务，提供当前下载任务状态。</param>
    /// <param name="launchService">游戏启动服务，提供启动状态与 Java 输出日志。</param>
    /// <param name="initialView">
    /// 打开时要展示的视图。
    /// 传入 <see cref="TaskDetailView.Download"/> 但当前没有进行中的下载时，窗口会立即自行关闭。
    /// </param>
    public TaskDetailsWindow(
        GameDownloadService downloadService,
        GameLaunchService launchService,
        TaskDetailView initialView)
    {
        _downloadService = downloadService;
        _launchService = launchService;
        _selectedView = initialView;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _downloadService.Changed += OnDownloadChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    /// <summary>
    /// 切换到「当前最值得看」的视图：有下载任务在进行就显示下载，
    /// 否则显示启动（含 Java 日志）。供宿主在打开窗口后调用。
    /// </summary>
    public void ShowPreferredView()
    {
        SelectView(_downloadService.Current.IsActive
            ? TaskDetailView.Download
            : TaskDetailView.Launch);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_selectedView == TaskDetailView.Download &&
            !_downloadService.Current.IsActive)
        {
            Close();
            return;
        }

        // 窗口存续期间跟随主题热重载（代码后置取色的画刷需要手动刷新）
        ThemeManager.ThemeChanged += OnThemeChanged;
        SelectView(_selectedView);
        RefreshView();
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _downloadService.Changed -= OnDownloadChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        Opened -= OnOpened;
        Closed -= OnClosed;
    }

    private void OnThemeChanged() => SelectView(_selectedView);

    private void OnRefreshTimerTick(object? sender, EventArgs e) => RefreshView();

    private void OnDownloadChanged(GameDownloadSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnDownloadChanged(snapshot));
            return;
        }

        RefreshDownloadView(snapshot);
        if (_selectedView != TaskDetailView.Download ||
            !snapshot.IsTerminal ||
            _autoCloseScheduled)
        {
            return;
        }

        _autoCloseScheduled = true;
        var taskId = snapshot.TaskId;
        DispatcherTimer.RunOnce(() =>
        {
            _autoCloseScheduled = false;
            if (_selectedView == TaskDetailView.Download &&
                _downloadService.Current.TaskId == taskId &&
                _downloadService.Current.IsTerminal)
            {
                Close();
            }
        }, TimeSpan.FromMilliseconds(700));
    }

    private void RefreshView()
    {
        DownloadViewButton.IsEnabled = _downloadService.Current.IsActive;
        LaunchViewButton.IsEnabled = _launchService.Current.Phase != GameLaunchPhase.Idle;
        RefreshDownloadView(_downloadService.Current);
        RefreshLaunchView(_launchService.Current);
    }

    private void RefreshDownloadView(GameDownloadSnapshot snapshot)
    {
        DownloadTitleText.Text = string.IsNullOrWhiteSpace(snapshot.VersionId)
            ? "Minecraft 下载进度"
            : $"Minecraft {snapshot.VersionId}";
        DownloadDetailText.Text = snapshot.Detail;
        DownloadPhaseText.Text = snapshot.Phase switch
        {
            GameDownloadPhase.Preparing => "准备中",
            GameDownloadPhase.Downloading => "下载中",
            GameDownloadPhase.Completed => "已完成",
            GameDownloadPhase.Failed => "失败",
            GameDownloadPhase.Cancelled => "已取消",
            _ => "空闲"
        };
        DownloadProgressBar.Value = snapshot.Percentage;
        DownloadProgressBar.IsIndeterminate = snapshot.IsActive && snapshot.TotalBytes <= 0;
        DownloadPercentageText.Text = $"{snapshot.Percentage:0.0}%";
        DownloadSpeedText.Text = FormatSpeed(snapshot.BytesPerSecond);
        DownloadBytesText.Text = $"{FormatBytes(snapshot.CompletedBytes)} / {FormatBytes(snapshot.TotalBytes)}";
        DownloadFilesText.Text = snapshot.TotalFiles <= 0
            ? $"{snapshot.CompletedFiles} / 计算中"
            : $"{snapshot.CompletedFiles} / {snapshot.TotalFiles}";
        CancelDownloadButton.IsVisible = snapshot.IsActive;
        // 阶段行：状态标记（✓/✕/▶/○）经 FeatureIconFactory 渲染为 Material 图标，其余回退文字
        DownloadStagesList.ItemsSource = BuildStageRows(snapshot);
    }

    /// <summary>
    /// 构建下载阶段行：状态标记映射为 Material 图标字形
    /// （✓→Check、✕→Close、▶→Play、○→CircleOutline，"—" 回退文字）。
    /// </summary>
    private IReadOnlyList<Control> BuildStageRows(GameDownloadSnapshot snapshot)
    {
        // 前景沿用原 BodyTextBrush 主题资源
        var foreground = Application.Current?.TryGetResource("BodyTextBrush", null, out var resource) == true &&
                         resource is IBrush brush
            ? brush
            : Brushes.Gray;

        var stageNames = GameDownloadService.StageNames.ToList();
        var rows = new List<Control>(stageNames.Count);
        for (var index = 0; index < stageNames.Count; index++)
        {
            var number = index + 1;
            var marker = snapshot.IsTerminal && snapshot.Phase == GameDownloadPhase.Completed
                ? "material:Check"
                : number < snapshot.StageIndex
                    ? "material:Check"
                    : number == snapshot.StageIndex
                        ? snapshot.Phase == GameDownloadPhase.Failed
                            ? "material:Close"
                            : snapshot.Phase == GameDownloadPhase.Cancelled
                                ? "—"
                                : "material:Play"
                        : "material:CircleOutline";
            var suffix = number == snapshot.StageIndex
                ? $"  {snapshot.Detail}"
                : string.Empty;

            rows.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(2, 4),
                Children =
                {
                    FeatureIconFactory.CreateGlyph(marker, 12, foreground),
                    new TextBlock
                    {
                        Text = $"{number}. {stageNames[index]}{suffix}",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        Foreground = foreground,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            });
        }

        return rows;
    }

    private void RefreshLaunchView(GameLaunchSnapshot snapshot)
    {
        LaunchLogStatusText.Text = $"{snapshot.Title} · {snapshot.Message}";
        LaunchLogPhaseText.Text = snapshot.Phase switch
        {
            GameLaunchPhase.Preparing => "启动中",
            GameLaunchPhase.Running => "运行中",
            GameLaunchPhase.Failed => "失败",
            GameLaunchPhase.Exited => "已退出",
            _ => "空闲"
        };

        var logText = _launchService.GetLogText();
        if (string.IsNullOrWhiteSpace(logText))
            logText = "等待启动任务…";
        if (string.Equals(logText, _lastLaunchLog, StringComparison.Ordinal))
            return;

        _lastLaunchLog = logText;
        LaunchLogTextBox.Text = logText;
        LaunchLogTextBox.CaretIndex = logText.Length;
    }

    private void SelectView(TaskDetailView view)
    {
        if (view == TaskDetailView.Download && !_downloadService.Current.IsActive)
            view = TaskDetailView.Launch;
        if (view == TaskDetailView.Launch && _launchService.Current.Phase == GameLaunchPhase.Idle &&
            _downloadService.Current.IsActive)
        {
            view = TaskDetailView.Download;
        }

        _selectedView = view;
        DownloadView.IsVisible = view == TaskDetailView.Download;
        LaunchView.IsVisible = view == TaskDetailView.Launch;
        DownloadViewButton.Background = view == TaskDetailView.Download
            ? ThemeBrushes.ButtonBg : ThemeBrushes.SurfaceBg;
        LaunchViewButton.Background = view == TaskDetailView.Launch
            ? ThemeBrushes.ButtonBg : ThemeBrushes.SurfaceBg;
        RefreshView();
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond <= 0 || !double.IsFinite(bytesPerSecond)
            ? "正在测速"
            : $"{FormatBytes(bytesPerSecond)}/s";

    private static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes <= 0)
            return "0 B";
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return $"{bytes:0.##} {units[unit]}";
    }

    private void OnDownloadViewClick(object? sender, RoutedEventArgs e) =>
        SelectView(TaskDetailView.Download);

    private void OnLaunchViewClick(object? sender, RoutedEventArgs e) =>
        SelectView(TaskDetailView.Launch);

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e) =>
        _downloadService.CancelActive();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

/// <summary>任务详情窗口左侧可切换的视图。</summary>
internal enum TaskDetailView
{
    /// <summary>下载进度视图：展示当前下载任务的分阶段状态。</summary>
    Download,

    /// <summary>启动状态视图：展示启动进度与实时的 Java 输出日志。</summary>
    Launch
}
