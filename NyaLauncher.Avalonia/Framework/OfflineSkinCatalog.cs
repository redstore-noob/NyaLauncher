using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>内置离线皮肤选项：离线账号可选的一件默认皮肤。</summary>
/// <param name="Id">皮肤标识（如 <c>steve</c>）。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Model">皮肤模型（<c>Classic</c> 宽手臂 / <c>Slim</c> 窄手臂）。</param>
/// <param name="FallbackText">图片缺失时显示的占位文字。</param>
internal sealed record OfflineSkinChoice(
    string Id,
    string DisplayName,
    MinecraftSkinModel Model,
    string FallbackText);

/// <summary>
/// 内置离线皮肤目录。纹理文件需要从游戏 jar 中解压，相关 IO 与 PNG 处理
/// 全部放到线程池执行，并按「存储目录 + 游戏目录」对缓存结果。
/// </summary>
internal static class OfflineSkinCatalog
{
    private static readonly StringComparer PathKeyComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly object CacheGate = new();
    private static readonly object ExtractionGate = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> TextureCatalogCache =
        new(PathKeyComparer);
    private static readonly Dictionary<string, SemaphoreSlim> TextureCatalogGates =
        new(PathKeyComparer);
    private static readonly Dictionary<string, string> GeneratedTextureCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<OfflineSkinChoice> Choices { get; } =
    [
        new("steve", "Steve", MinecraftSkinModel.Classic, "S"),
        new("alex", "Alex", MinecraftSkinModel.Slim, "A"),
        new("noor", "Noor", MinecraftSkinModel.Slim, "N"),
        new("sunny", "Sunny", MinecraftSkinModel.Classic, "S"),
        new("ari", "Ari", MinecraftSkinModel.Slim, "A"),
        new("zuri", "Zuri", MinecraftSkinModel.Slim, "Z"),
        new("makena", "Makena", MinecraftSkinModel.Classic, "M"),
        new("kai", "Kai", MinecraftSkinModel.Slim, "K"),
        new("efe", "Efe", MinecraftSkinModel.Slim, "E")
    ];

    /// <summary>
    /// 按 Id 取一个离线皮肤选项（忽略大小写）。
    /// 找不到对应项时回退到列表中的第一项，因此返回值永不为 <c>null</c>。
    /// </summary>
    /// <param name="id">皮肤标识，可为 <c>null</c>。</param>
    /// <returns>匹配的选项；没有匹配时返回 <c>Choices[0]</c>。</returns>
    public static OfflineSkinChoice Get(string? id) =>
        Choices.FirstOrDefault(choice => string.Equals(
            choice.Id,
            id,
            StringComparison.OrdinalIgnoreCase)) ?? Choices[0];

    /// <summary>
    /// 解析某个离线皮肤的纹理来源路径（必要时先解压）。
    /// 全部文件系统与 PNG 处理在线程池上完成；目录按「存储目录 + 游戏目录」
    /// 组合缓存一份，因此在两者之间反复切换不会重新扫描 versions 目录。
    /// </summary>
    /// <param name="id">皮肤标识；为空或未知时回退到第一项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可直接作为图片来源使用的本地路径。</returns>
    public static Task<string> ResolveTextureSourceAsync(
        string? id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var choice = Get(id);
        // 派发到后台前先给共享配置拍个快照：ConfigFileManager 持有可变的 JsonDocument，
        // 账号持久化也在 UI 线程上用它，后台线程不能与它并发读取。
        var context = CaptureContext();
        return Task.Run(
            () => ResolveTextureSourceCoreAsync(choice, context, cancellationToken),
            cancellationToken);
    }

    private static async Task<string> ResolveTextureSourceCoreAsync(
        OfflineSkinChoice choice,
        CatalogContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SemaphoreSlim catalogGate;
        lock (CacheGate)
        {
            if (TextureCatalogCache.TryGetValue(context.Key, out var cached))
                return cached[choice.Id];
            if (!TextureCatalogGates.TryGetValue(context.Key, out catalogGate!))
            {
                catalogGate = new SemaphoreSlim(1, 1);
                TextureCatalogGates[context.Key] = catalogGate;
            }
        }

        await catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (CacheGate)
            {
                if (TextureCatalogCache.TryGetValue(context.Key, out var cached))
                    return cached[choice.Id];
            }

            var result = BuildTextureCatalog(context, cancellationToken);
            if (result.CanCache)
            {
                lock (CacheGate)
                    TextureCatalogCache[context.Key] = result.Sources;
            }

            return result.Sources[choice.Id];
        }
        finally
        {
            catalogGate.Release();
        }
    }

    private static CatalogContext CaptureContext()
    {
        var storageDirectory = LauncherConfig.StorageDirectory;
        string? gameDirectory;
        try
        {
            gameDirectory = LauncherConfig.GameDirectory;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read the Minecraft directory setting: {exception}");
            gameDirectory = null;
        }

        // 默认 .minecraft 目录统一走 Core 的跨平台实现，避免重复硬编码
        var conventionalDirectory = MinecraftDirectoryLocator.GetDefaultDirectory();
        var key = $"{NormalizePath(storageDirectory)}\0{NormalizePath(gameDirectory)}\0{NormalizePath(conventionalDirectory)}";
        return new CatalogContext(
            key,
            storageDirectory,
            gameDirectory,
            conventionalDirectory);
    }

    /// <summary>
    /// Reuses default player textures already shipped in an installed client.
    /// No game archive is modified; PNGs are copied into the launcher cache.
    /// </summary>
    private static CatalogBuildResult BuildTextureCatalog(
        CatalogContext context,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Dictionary<string, OfflineSkinChoice>(StringComparer.OrdinalIgnoreCase);
        var canCache = true;

        try
        {
            var cacheDirectory = Path.Combine(
                context.StorageDirectory,
                "appearance-cache",
                "default-skins");
            foreach (var choice in Choices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cachedPath = Path.Combine(cacheDirectory, $"{choice.Id}.png");
                try
                {
                    if (File.Exists(cachedPath))
                    {
                        sources[choice.Id] = Path.GetFullPath(cachedPath);
                        continue;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    canCache = false;
                    System.Diagnostics.Debug.WriteLine($"Failed to read an offline skin cache entry: {exception}");
                }

                pending[choice.Id] = choice;
            }

            if (pending.Count > 0)
            {
                var jarSearch = FindClientJars(context, cancellationToken);
                canCache &= jarSearch.CanCache;
                foreach (var jarPath in jarSearch.Paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (pending.Count == 0)
                        break;

                    try
                    {
                        using var archive = ZipFile.OpenRead(jarPath);
                        foreach (var choice in pending.Values.ToArray())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var preferredModel = choice.Model == MinecraftSkinModel.Slim ? "slim" : "wide";
                            var entry = FindTextureEntry(archive, choice.Id, preferredModel) ??
                                        FindTextureEntry(
                                            archive,
                                            choice.Id,
                                            preferredModel == "slim" ? "wide" : "slim");
                            if (entry is null)
                                continue;

                            var cachedPath = Path.Combine(cacheDirectory, $"{choice.Id}.png");
                            lock (ExtractionGate)
                            {
                                if (!File.Exists(cachedPath))
                                {
                                    Directory.CreateDirectory(cacheDirectory);
                                    entry.ExtractToFile(cachedPath, overwrite: true);
                                }
                            }

                            sources[choice.Id] = Path.GetFullPath(cachedPath);
                            pending.Remove(choice.Id);
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        // A client archive can be partially written while the
                        // game is updating, so leave the catalog retryable.
                        canCache = false;
                        System.Diagnostics.Debug.WriteLine($"Failed to read a Minecraft client archive: {exception}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            canCache = false;
            System.Diagnostics.Debug.WriteLine($"Failed to build the offline skin catalog: {exception}");
        }

        foreach (var choice in Choices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.ContainsKey(choice.Id))
                sources[choice.Id] = GetOrCreateGeneratedTexture(choice);
        }

        return new CatalogBuildResult(sources, canCache);
    }

    private static string GetOrCreateGeneratedTexture(OfflineSkinChoice choice)
    {
        lock (CacheGate)
        {
            if (GeneratedTextureCache.TryGetValue(choice.Id, out var cached))
                return cached;
        }

        var generated = CreateGeneratedTextureDataUri(choice);
        lock (CacheGate)
        {
            if (GeneratedTextureCache.TryGetValue(choice.Id, out var cached))
                return cached;
            GeneratedTextureCache[choice.Id] = generated;
            return generated;
        }
    }

    private static ZipArchiveEntry? FindTextureEntry(
        ZipArchive archive,
        string skinId,
        string model) =>
        archive.GetEntry($"assets/minecraft/textures/entity/player/{model}/{skinId}.png");

    private static JarSearchResult FindClientJars(
        CatalogContext context,
        CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.GameDirectory))
            roots.Add(context.GameDirectory);
        if (!roots.Any(root => PathUtil.PathsEqual(root, context.ConventionalDirectory)))
            roots.Add(context.ConventionalDirectory);

        var files = new List<(string Path, DateTime LastWriteTimeUtc)>();
        var canCache = true;
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versionsDirectory = Path.Combine(root, "versions");
            try
            {
                if (!Directory.Exists(versionsDirectory))
                    continue;
                foreach (var path in Directory.EnumerateFiles(
                             versionsDirectory,
                             "*.jar",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        files.Add((path, File.GetLastWriteTimeUtc(path)));
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        canCache = false;
                        System.Diagnostics.Debug.WriteLine($"Failed to inspect a Minecraft client archive: {exception}");
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                canCache = false;
                System.Diagnostics.Debug.WriteLine($"Failed to scan the Minecraft versions directory: {exception}");
            }
        }

        return new JarSearchResult(
            files
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.Path)
                .ToArray(),
            canCache);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string CreateGeneratedTextureDataUri(OfflineSkinChoice choice)
    {
        var palette = choice.Id switch
        {
            "alex" => new AvatarPalette(0xFFD89B74, 0xFFB45B28, 0xFF4B792D, 0xFF9B4B2A),
            "noor" => new AvatarPalette(0xFF9A6547, 0xFF241B1A, 0xFF4E3828, 0xFF6F4034),
            "sunny" => new AvatarPalette(0xFFBD805B, 0xFF5A3425, 0xFF426D91, 0xFF8A4D3D),
            "ari" => new AvatarPalette(0xFFD2A078, 0xFF32241F, 0xFF75543A, 0xFF965F4A),
            "zuri" => new AvatarPalette(0xFF82513D, 0xFF201817, 0xFF6E4F31, 0xFF5C332D),
            "makena" => new AvatarPalette(0xFF74432F, 0xFF181414, 0xFF47321F, 0xFF4C2927),
            "kai" => new AvatarPalette(0xFFC18B64, 0xFF2A211E, 0xFF3F6E61, 0xFF814B3D),
            "efe" => new AvatarPalette(0xFF69402F, 0xFF171313, 0xFF684D2E, 0xFF482725),
            _ => new AvatarPalette(0xFFB98463, 0xFF39251B, 0xFF4656A6, 0xFF875044)
        };

        const int textureSize = 64;
        var pixels = new byte[textureSize * textureSize * 4];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
                SetPixel(pixels, textureSize, x + 8, y + 8, palette.Skin);
        }

        for (var x = 0; x < 8; x++)
        {
            SetPixel(pixels, textureSize, x + 8, 8, palette.Hair);
            SetPixel(pixels, textureSize, x + 8, 9, palette.Hair);
        }

        for (var y = 2; y < 5; y++)
        {
            SetPixel(pixels, textureSize, 8, y + 8, palette.Hair);
            SetPixel(pixels, textureSize, 15, y + 8, palette.Hair);
        }

        SetPixel(pixels, textureSize, 10, 11, 0xFFF4F5F8);
        SetPixel(pixels, textureSize, 11, 11, palette.Eye);
        SetPixel(pixels, textureSize, 12, 11, palette.Eye);
        SetPixel(pixels, textureSize, 13, 11, 0xFFF4F5F8);
        SetPixel(pixels, textureSize, 11, 13, palette.Shadow);
        SetPixel(pixels, textureSize, 12, 13, palette.Shadow);
        SetPixel(pixels, textureSize, 11, 14, palette.Shadow);
        SetPixel(pixels, textureSize, 12, 14, palette.Shadow);

        SetPixel(pixels, textureSize, 40, 8, palette.Hair);
        SetPixel(pixels, textureSize, 47, 8, palette.Hair);
        SetPixel(pixels, textureSize, 40, 9, palette.Hair);
        SetPixel(pixels, textureSize, 47, 9, palette.Hair);

        var png = EncodePng(textureSize, textureSize, pixels);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, uint color)
    {
        var index = (y * width + x) * 4;
        pixels[index] = (byte)(color >> 24);
        pixels[index + 1] = (byte)(color >> 16);
        pixels[index + 2] = (byte)(color >> 8);
        pixels[index + 3] = (byte)color;
    }

    private static byte[] EncodePng(int width, int height, byte[] rgba) =>
        NyaLauncher.Core.Tools.PngEncoder.Encode(width, height, rgba);

    private sealed record CatalogContext(
        string Key,
        string StorageDirectory,
        string? GameDirectory,
        string ConventionalDirectory);

    private sealed record CatalogBuildResult(
        IReadOnlyDictionary<string, string> Sources,
        bool CanCache);

    private sealed record JarSearchResult(
        IReadOnlyList<string> Paths,
        bool CanCache);

    private sealed record AvatarPalette(uint Skin, uint Hair, uint Eye, uint Shadow);
}
