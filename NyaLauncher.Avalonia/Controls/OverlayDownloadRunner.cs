using System;
using System.Threading.Tasks;
using NyaLauncher.Core.Download;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>下载流程的终态。</summary>
public enum DownloadRunStatus
{
    Success,
    Cancelled,
    Failed
}

/// <summary>一次下载的终态与面向用户的提示文案。</summary>
public sealed record DownloadRunResult(DownloadRunStatus Status, string Message);

/// <summary>
/// 遮罩层内的标准下载流程封装：绑定 <see cref="DownloadStatusPanel"/>，
/// 统一 Begin → Progress → Success / Cancelled / Failure 的生命周期，并返回终态结果，
/// 供各下载内容视图复用，避免重复实现下载调度与终态处理。
/// </summary>
public static class OverlayDownloadRunner
{
    /// <summary>执行一次下载：更新进度面板，返回终态（面板内已完成 Success/Cancelled/Failure 展示）。</summary>
    public static async Task<DownloadRunResult> RunAsync(
        DownloadStatusPanel status, string fileName, string url, string targetPath, long fileSize)
    {
        var cts = status.Begin(fileName, targetPath, fileSize);
        try
        {
            var progress = new Progress<(long downloaded, long total)>(
                p => status.Update(p.downloaded, p.total));
            await ModDownloadService.DownloadAsync(url, targetPath, progress, cts.Token);
            status.Success($"已保存：{targetPath}");
            return new DownloadRunResult(DownloadRunStatus.Success, $"已保存：{targetPath}");
        }
        catch (OperationCanceledException)
        {
            status.Cancelled();
            return new DownloadRunResult(DownloadRunStatus.Cancelled, "下载已取消。");
        }
        catch (Exception ex)
        {
            status.Failure(ex.Message);
            return new DownloadRunResult(DownloadRunStatus.Failed, $"下载失败：{ex.Message}");
        }
    }
}
