using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Avalonia.Windows;

internal partial class TaskDetailsWindow : Window
{
    private readonly GameDownloadService _downloadService;
    private readonly GameLaunchService _launchService;
    private readonly DispatcherTimer _refreshTimer;
    private TaskDetailView _selectedView;
    private string _lastLaunchLog = string.Empty;
    private bool _autoCloseScheduled;

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

        SelectView(_selectedView);
        RefreshView();
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _downloadService.Changed -= OnDownloadChanged;
        Opened -= OnOpened;
        Closed -= OnClosed;
    }

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
        DownloadStagesList.ItemsSource = GameDownloadService.StageNames
            .Select((name, index) =>
            {
                var number = index + 1;
                var marker = snapshot.IsTerminal && snapshot.Phase == GameDownloadPhase.Completed
                    ? "✓"
                    : number < snapshot.StageIndex
                        ? "✓"
                        : number == snapshot.StageIndex
                            ? snapshot.Phase == GameDownloadPhase.Failed
                                ? "✕"
                                : snapshot.Phase == GameDownloadPhase.Cancelled
                                    ? "—"
                                    : "▶"
                            : "○";
                var suffix = number == snapshot.StageIndex
                    ? $"  {snapshot.Detail}"
                    : string.Empty;
                return $"{marker}  {number}. {name}{suffix}";
            })
            .ToArray();
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

internal enum TaskDetailView
{
    Download,
    Launch
}
