namespace NyaLauncher.Core.Download;

/// <summary>
/// 使用官方版本清单和安装器现有的 SHA-1 校验流程补全缺失文件。
/// </summary>
public sealed class GameFileVerifier
{
    private readonly MinecraftVersionInstaller _installer = new();

    public async Task<int> VerifyAndRepairAsync(
        string minecraftDirectory,
        string versionId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        progress?.Report("正在获取版本清单…");
        var version = (await ManifestGet.GetVersionsAsync().ConfigureAwait(false))
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                versionId,
                StringComparison.OrdinalIgnoreCase));
        if (version is null || string.IsNullOrWhiteSpace(version.Url))
        {
            progress?.Report("该实例不是官方版本，已跳过自动补全。");
            return 0;
        }

        var downloaded = 0;
        var installProgress = new InlineProgress(update =>
        {
            progress?.Report(update.Detail);
            if (update.BytesPerSecond > 0)
                Interlocked.Exchange(ref downloaded, 1);
        });
        await _installer.InstallAsync(
                version.Id,
                version.Url,
                minecraftDirectory,
                installProgress,
                cancellationToken)
            .ConfigureAwait(false);
        return Volatile.Read(ref downloaded);
    }

    private sealed class InlineProgress(Action<MinecraftInstallProgress> report)
        : IProgress<MinecraftInstallProgress>
    {
        public void Report(MinecraftInstallProgress value) => report(value);
    }
}
