using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Avalonia.Animations.Helpers;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 可复用的下载状态面板：文件名 + 百分比 + 进度条 + 明细/速度 + 取消 / 打开文件夹。
/// 供 ModDownloadOverlay / ContentDownloadOverlay 等共享，避免重复实现下载进度 UI 与逻辑。
/// 面板自持取消令牌与状态，调用方通过 <see cref="Begin"/> 拿到令牌用于实际下载。
/// </summary>
public partial class DownloadStatusPanel : UserControl
{
    private DateTime _lastProgressTime;
    private long _lastProgressBytes;
    private double _bytesPerSecond;

    public DownloadStatusPanel()
    {
        InitializeComponent();
    }

    /// <summary>当前是否有下载任务进行中。</summary>
    public bool IsDownloading { get; private set; }

    /// <summary>当前下载任务的取消令牌（Begin 时创建，完成/取消/失败/Reset 时释放）。</summary>
    public CancellationTokenSource? DownloadCts { get; private set; }

    /// <summary>最近一次下载目标路径（用于"打开文件夹"）。</summary>
    public string? LastDownloadPath { get; private set; }

    /// <summary>空闲时文件区的提示文字。</summary>
    public string IdleText { get; set; } = "准备就绪";

    /// <summary>开始一次下载：重置 UI、记录目标路径、创建并返回新的取消令牌。</summary>
    public CancellationTokenSource Begin(string fileName, string targetPath, long fileSize)
    {
        ResetInternal();
        IsDownloading = true;
        LastDownloadPath = targetPath;
        DownloadCts = new CancellationTokenSource();

        RingProgress.IsVisible = true;
        RingProgress.Value = 0;
        PercentText.Text = "0%";
        FileText.Text = fileName;
        DetailText.Text = fileSize > 0
            ? $"共 {fileSize / 1048576.0:0.1} MiB · 准备中…"
            : "准备中…";
        DetailText.Foreground = OverlayTheme.FindBrush("HintTextBrush");
        CancelButton.IsVisible = true;
        CancelButton.IsEnabled = true;
        OpenFolderButton.IsVisible = false;
        return DownloadCts;
    }

    /// <summary>更新进度与速度显示。</summary>
    public void Update(long downloaded, long total)
    {
        var now = DateTime.UtcNow;
        if (_lastProgressTime == default)
        {
            _lastProgressTime = now;
            _lastProgressBytes = downloaded;
        }
        else
        {
            var elapsed = (now - _lastProgressTime).TotalSeconds;
            if (elapsed >= 0.4)
            {
                _bytesPerSecond = (downloaded - _lastProgressBytes) / elapsed;
                _lastProgressTime = now;
                _lastProgressBytes = downloaded;
            }
        }

        var pct = total > 0
            ? (int)Math.Clamp(downloaded * 100L / total, 0, 100)
            : 0;
        RingProgress.Value = pct;
        PercentText.Text = $"{pct}%";

        var speed = _bytesPerSecond > 0
            ? $"{_bytesPerSecond / 1048576.0:0.0} MiB/s"
            : "…";
        var size = total > 0
            ? $"{downloaded / 1048576.0:0.1} / {total / 1048576.0:0.1} MiB"
            : $"{downloaded / 1048576.0:0.1} MiB";
        DetailText.Text = $"{speed} · {size}";
        DetailText.Foreground = OverlayTheme.FindBrush("HintTextBrush");
    }

    /// <summary>覆盖明细文字（不改变整体状态，用于安装中提示）。</summary>
    public void SetDetail(string text)
    {
        DetailText.Text = text;
        DetailText.Foreground = OverlayTheme.FindBrush("HintTextBrush");
        PercentText.Text = "";
    }

    /// <summary>下载成功。</summary>
    public void Success(string detail)
    {
        RingProgress.Value = 100;
        PercentText.Text = "完成";
        DetailText.Text = detail;
        DetailText.Foreground = OverlayTheme.FindBrush("SuccessBrush");
        CancelButton.IsVisible = false;
        OpenFolderButton.IsVisible = true;
        Finish();
    }

    /// <summary>下载已取消。</summary>
    public void Cancelled()
    {
        DetailText.Text = "下载已取消";
        DetailText.Foreground = OverlayTheme.FindBrush("HintTextBrush");
        CancelButton.IsVisible = false;
        Finish();
    }

    /// <summary>下载失败。</summary>
    public void Failure(string message)
    {
        DetailText.Text = $"下载失败：{message}";
        DetailText.Foreground = OverlayTheme.FindBrush("ErrorBrush");
        CancelButton.IsVisible = false;
        // 失败提示：面板左右抖动几下（动画逻辑在 Animations 模块 Shake）
        Shake.Trigger(this);
        Finish();
    }

    /// <summary>重置到空闲状态（关闭弹窗 / 切换目标时调用）。</summary>
    public void Reset()
    {
        ResetInternal();
        FileText.Text = IdleText;
    }

    /// <summary>空闲时更新文件名区的提示（如"已选择 xx 版本"）。</summary>
    public void SetIdleText(string text) => FileText.Text = text;

    private void Finish()
    {
        IsDownloading = false;
        DownloadCts?.Dispose();
        DownloadCts = null;
    }

    private void ResetInternal()
    {
        DownloadCts?.Cancel();
        DownloadCts?.Dispose();
        DownloadCts = null;
        IsDownloading = false;
        LastDownloadPath = null;

        RingProgress.IsVisible = false;
        RingProgress.Value = 0;
        PercentText.Text = "";
        DetailText.Text = "";
        CancelButton.IsVisible = false;
        OpenFolderButton.IsVisible = false;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        DownloadCts?.Cancel();
        CancelButton.IsEnabled = false;
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LastDownloadPath))
            return;
        try
        {
            var directory = Path.GetDirectoryName(LastDownloadPath);
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe",
                    $"/select,\"{LastDownloadPath}\"")
                {
                    UseShellExecute = true
                });
            }
            else if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch
        {
            // 忽略打开失败
        }
    }
}
