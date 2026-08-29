using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Material.Icons;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>内容下载类型。</summary>
public enum ContentDownloadKind
{
    Modpack,
    Resourcepack,
    Shaderpack
}

/// <summary>
/// 内容下载遮罩层：整合包 / 资源包 / 光影包通用。
/// 支持选择版本 + 下载到已安装实例（自动放入对应目录）或自定义保存路径。
/// </summary>
public partial class ContentDownloadOverlay : UserControl, IModalHostAware
{
    private ModrinthProject? _project;
    private ContentDownloadKind _kind;
    private List<ModrinthVersion> _versions = [];
    private CancellationTokenSource? _loadCts;

    /// <summary>承载本视图的宿主（由 ModalOverlayHost.Show 自动注入）。</summary>
    public ModalOverlayHost? Host { get; set; }

    /// <summary>版本安装服务，用于按整合包要求自动安装缺失的 Minecraft 版本 + 加载器。</summary>
    public GameDownloadService? DownloadService { get; set; }

    public ContentDownloadOverlay()
    {
        InitializeComponent();
        DownloadStatus.IdleText = "选择版本后点击下载";
    }

    /// <summary>宿主展示前调用：加载项目版本与下载目标选项。</summary>
    public async void Setup(ModrinthProject project, ContentDownloadKind kind)
    {
        // 可能被后台线程（如内容下载请求事件）触发，
        // 控件属于 UI 线程，必须回到 UI 线程再访问，否则抛 InvalidOperationException。
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Setup(project, kind));
            return;
        }

        _project = project;
        _kind = kind;

        Header.Title = project.Title;
        Header.Subtitle = project.Description;
        Header.IconUrl = project.IconUrl;
        Header.Glyph = kind switch
        {
            ContentDownloadKind.Modpack => MaterialIconKind.PackageVariantClosed,
            ContentDownloadKind.Resourcepack => MaterialIconKind.Palette,
            _ => MaterialIconKind.ImageFilterVintage
        };

        DownloadStatus.Reset();
        TargetPicker.Setup(kind switch
        {
            ContentDownloadKind.Modpack => DownloadTargetKind.Modpack,
            ContentDownloadKind.Resourcepack => DownloadTargetKind.Resourcepack,
            _ => DownloadTargetKind.Shaderpack
        });

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        VersionComboBox.ItemsSource = null;
        VersionComboBox.PlaceholderText = "正在加载版本…";
        VersionComboBox.IsEnabled = false;
        try
        {
            var allVersions = await ModrinthVersionApi.GetVersionsAsync(
                    project.ProjectId, cancellationToken: ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested)
                return;

            // 按发布日期排序：版本多时在后台执行，避免 UI 线程卡顿
            var sorted = await System.Threading.Tasks.Task.Run(
                () => allVersions
                    .OrderByDescending(v => v.DatePublishedRaw, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ct);
            _versions = sorted;
            VersionComboBox.ItemsSource = sorted.Select(v => v.DisplayName).ToList();
            VersionComboBox.IsEnabled = true;
            VersionComboBox.PlaceholderText = "选择版本";
            if (sorted.Count > 0)
                VersionComboBox.SelectedIndex = 0;
            else
                VersionComboBox.PlaceholderText = "该内容暂无可下载版本";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            VersionComboBox.PlaceholderText = "加载失败";
            StatusText.Text = $"加载版本失败：{ex.Message}";
            StatusText.IsVisible = true;
        }
    }

    // ------------------------------------------------------------------
    // 下载
    // ------------------------------------------------------------------

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_project is null)
            return;
        if (VersionComboBox.SelectedIndex < 0 || VersionComboBox.SelectedIndex >= _versions.Count)
        {
            StatusText.Text = "请先选择版本。";
            StatusText.IsVisible = true;
            return;
        }

        var selected = _versions[VersionComboBox.SelectedIndex];
        var file = selected.PrimaryFile;
        if (file is null || string.IsNullOrWhiteSpace(file.Url))
        {
            StatusText.Text = "所选版本无可下载文件。";
            StatusText.IsVisible = true;
            return;
        }

        if (DownloadStatus.IsDownloading)
            return;

        var selection = TargetPicker.SelectedTarget;
        if (string.IsNullOrEmpty(selection))
        {
            StatusText.Text = "请选择下载目标。";
            StatusText.IsVisible = true;
            return;
        }

        try
        {
            StatusText.IsVisible = false; // 下载开始后隐藏此前状态提示
            if (TargetPicker.IsCustomPath)
            {
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage is null)
                    return;
                var extension = _kind == ContentDownloadKind.Modpack ? ".mrpack" : Path.GetExtension(file.Filename);
                var result = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "保存文件",
                    SuggestedFileName = file.Filename,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("内容文件") { Patterns = [extension] },
                        new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
                    ]
                });
                if (result is null)
                    return;
                var savePath = result.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(savePath))
                    return;
                await OverlayDownloadRunner.RunAsync(DownloadStatus, file.Filename, file.Url, savePath, file.Size);
            }
            else
            {
                if (_kind == ContentDownloadKind.Modpack)
                {
                    // 整合包只能安装为独立实例：用户自定义名字作为版本 id
                    var name = TargetPicker.InstanceName;
                    if (!OverlayHelpers.IsValidInstanceName(name, out var nameErr))
                    {
                        StatusText.Text = nameErr;
                        StatusText.IsVisible = true;
                        return;
                    }
                    await RunModpackInstallAsync(file.Filename, file.Url, file.Size, forcedVersionId: name);
                }
                else
                {
                    var contentDir = TargetPicker.ResolveContentDir();
                    if (string.IsNullOrWhiteSpace(contentDir))
                    {
                        StatusText.Text = "无法定位实例内容目录。";
                        StatusText.IsVisible = true;
                        return;
                    }
                    var subDir = _kind == ContentDownloadKind.Resourcepack
                        ? "resourcepacks"
                        : "shaderpacks";
                    var targetPath = Path.Combine(contentDir, subDir, Path.GetFileName(file.Filename));
                    await OverlayDownloadRunner.RunAsync(DownloadStatus, file.Filename, file.Url, targetPath, file.Size);
                }
            }
        }
        catch (Exception ex)
        {
            OverlayHelpers.SetStatus(StatusText, $"操作失败：{ex.Message}", isError: true);
        }
    }

    /// <summary>整合包安装：下载 mrpack → 解析所需版本 → 确保目标版本（新建实例或自动派生）已装并选中 → 解压 + 依赖下载。</summary>
    private async Task RunModpackInstallAsync(
        string fileName, string url, long fileSize, string? forcedVersionId = null)
    {
        var tempMrpack = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mrpack");
        var cts = DownloadStatus.Begin(fileName, tempMrpack, fileSize);
        try
        {
            var progress = new Progress<(long downloaded, long total)>(p => DownloadStatus.Update(p.downloaded, p.total));
            await ModDownloadService.DownloadAsync(url, tempMrpack, progress, cts.Token);

            // 读取整合包声明的游戏版本 + 加载器，确保目标版本已安装并选中
            DownloadStatus.SetDetail("正在解析整合包所需的游戏版本…");
            var requirements = await ContentInstallService.ReadRequirementsAsync(
                tempMrpack, cts.Token);

            var (targetVersionId, contentDir, warnings) =
                await ResolveModpackTargetVersionAsync(requirements, forcedVersionId, cts.Token);

            if (string.IsNullOrWhiteSpace(contentDir))
            {
                var reason = warnings.Count > 0
                    ? string.Join("；", warnings)
                    : "无法确定整合包的安装目标目录。";
                DownloadStatus.Failure(reason);
                return;
            }

            DownloadStatus.SetDetail("正在解压整合包并安装依赖…");
            var (installed, mods, errors) = await ContentInstallService.InstallModpackAsync(
                tempMrpack, contentDir, null, cts.Token);

            GameInstanceStore.Select(targetVersionId);

            var reqText = requirements is null
                ? "未识别到版本要求"
                : $"目标版本：{targetVersionId}（MC {requirements.MinecraftVersion}" +
                  (requirements.RawLoaderKey is null
                      ? "，原版"
                      : requirements.LoaderSupported
                          ? $" + {requirements.LoaderType} {requirements.LoaderVersion}"
                          : $"，加载器 {requirements.RawLoaderKey} 暂不支持") + "）";

            var summary = $"已解压 {installed} 个文件";
            if (mods > 0)
                summary += $"、下载依赖 {mods} 个";
            summary += $"\n{reqText}";
            if (errors.Count > 0)
                summary += $"，{errors.Count} 项失败";
            DownloadStatus.Success(summary);

            var notes = warnings.Concat(errors.Take(3));
            if (notes.Any())
            {
                StatusText.Text = string.Join("；", notes);
                StatusText.IsVisible = true;
            }
        }
        catch (OperationCanceledException)
        {
            DownloadStatus.Cancelled();
        }
        catch (Exception ex)
        {
            DownloadStatus.Failure(ex.Message);
        }
        finally
        {
            try { File.Delete(tempMrpack); } catch { }
        }
    }

    /// <summary>
    /// 根据整合包要求解析/安装目标版本，返回应安装 mod 的版本 id 与内容目录。
    /// 若要求版本尚未安装则自动安装（复用 <see cref="GameDownloadService"/>）；
    /// <paramref name="forcedVersionId"/> 非空时（用户自定义的新实例名），
    /// 加载器整合包直接以该名字作为版本 id 安装；
    /// 要求无法解析（未知 MC 版本 / 不支持的加载器 / 安装失败）时，
    /// 新建实例模式下直接失败并给出原因，否则回退到用户所选版本并告警。
    /// </summary>
    private async Task<(string versionId, string contentDir, List<string> warnings)>
        ResolveModpackTargetVersionAsync(
            ContentInstallService.ModpackRequirements? requirements,
            string? forcedVersionId,
            CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var snapshot = GameInstanceStore.Current;
        var fallbackId = TargetPicker.SelectedInstanceId;
        var isNewInstance = forcedVersionId is not null;

        string FallbackDir() => ContentInstallService.ResolveContentDirectory(
            snapshot.MinecraftDirectory, snapshot.SourcePath, fallbackId);

        // 新建实例模式下没有可回退的已有版本，要求解析失败 = 直接失败
        string FallbackFailure(string reason)
        {
            warnings.Add(isNewInstance
                ? reason
                : $"{reason}，已安装到你选择的版本（可能与整合包不兼容）。");
            return isNewInstance ? string.Empty : FallbackDir();
        }

        if (requirements is null || string.IsNullOrWhiteSpace(requirements.MinecraftVersion))
        {
            return (fallbackId, FallbackFailure("未识别到整合包的版本要求，无法安装为独立实例"), warnings);
        }

        // 在版本清单中查找要求的 Minecraft 版本
        MinecraftVersion? mc = null;
        try
        {
            var versions = await ManifestGet.GetVersionsAsync().ConfigureAwait(true);
            mc = versions.FirstOrDefault(v =>
                string.Equals(v.Id, requirements.MinecraftVersion, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            mc = null;
        }

        if (mc is null)
        {
            return (fallbackId, FallbackFailure($"整合包要求 Minecraft {requirements.MinecraftVersion}，但版本清单中找不到该版本，无法安装为独立实例"), warnings);
        }

        var isVanilla = requirements.RawLoaderKey is null;
        if (!isVanilla && !requirements.LoaderSupported)
        {
            return (fallbackId, FallbackFailure($"本启动器暂不支持整合包使用的加载器（{requirements.RawLoaderKey}），无法安装为独立实例"), warnings);
        }

        string targetVersionId;
        if (isVanilla)
        {
            // 原版整合包的版本 id 必须等于 Minecraft 版本号，无法自定义名字
            if (forcedVersionId is not null)
                warnings.Add("原版整合包不能自定义实例名，已使用 Minecraft 版本号命名。");
            targetVersionId = mc.Id;
        }
        else
        {
            targetVersionId = forcedVersionId
                ?? ModLoaderInstaller.CreateDefaultInstanceName(
                    requirements.LoaderType, requirements.LoaderVersion ?? string.Empty, mc.Id);
        }

        if (snapshot.VersionIds.Contains(targetVersionId, StringComparer.OrdinalIgnoreCase))
        {
            GameInstanceStore.Select(targetVersionId);
            return (targetVersionId, ContentInstallService.ResolveContentDirectory(
                snapshot.MinecraftDirectory, snapshot.SourcePath, targetVersionId), warnings);
        }

        // 版本尚未安装，自动安装
        if (DownloadService is null)
        {
            if (isNewInstance)
            {
                warnings.Add("缺少下载服务，无法自动安装整合包所需版本，安装中止。");
                return (targetVersionId, string.Empty, warnings);
            }
            warnings.Add("缺少下载服务，无法自动安装整合包所需版本，已回退到你选择的版本。");
            return (fallbackId, FallbackDir(), warnings);
        }

        DownloadStatus.SetDetail($"正在安装整合包所需版本 {targetVersionId}…");
        bool installed;
        if (isVanilla)
        {
            installed = await DownloadService.StartAsync(mc).ConfigureAwait(true);
        }
        else
        {
            // 从 Loader 元数据服务解析真实安装描述（MetadataUrl / RequiresInstallerExtraction）：
            // 仅凭 mrpack dependencies 中的类型 + 版本号无法完成安装
            //（Fabric 需要 MetadataUrl 拉版本 JSON；NeoForge/Forge 需要
            //  RequiresInstallerExtraction 走安装器路径）。
            DownloadStatus.SetDetail(
                $"正在获取 {requirements.LoaderType} {requirements.LoaderVersion} 的安装元数据…");
            ModLoaderVersion? loader = null;
            try
            {
                var loaderVersions = await ModLoaderMetadata.GetVersionsAsync(
                        requirements.LoaderType, mc.Id, cancellationToken)
                    .ConfigureAwait(true);
                var wanted = requirements.LoaderVersion ?? string.Empty;
                // 精确匹配：Fabric/NeoForge 直接对版本号；Forge 的 LoaderVersion 形如 "1.20.1-47.1.12"
                loader = loaderVersions.FirstOrDefault(v =>
                    string.Equals(v.LoaderVersion, wanted, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(wanted) &&
                     v.LoaderVersion.EndsWith("-" + wanted, StringComparison.OrdinalIgnoreCase)));
                if (loader is null && loaderVersions.Count > 0)
                {
                    // 精确版本不在元数据源中：回退到最新稳定版，保证整合包仍可安装
                    loader = loaderVersions.FirstOrDefault(v => v.IsStable) ?? loaderVersions[0];
                    warnings.Add(
                        $"元数据源中未找到 {requirements.LoaderType} {wanted}，" +
                        $"已回退到可用版本 {loader.LoaderVersion}（可能与整合包要求不完全一致）。");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"获取 {requirements.LoaderType} 安装元数据失败：{ex.Message}");
            }

            if (loader is null)
            {
                warnings.Add($"无法获取 {requirements.LoaderType} 的安装元数据，安装中止。");
                return (targetVersionId, string.Empty, warnings);
            }

            DownloadStatus.SetDetail($"正在安装整合包所需版本 {targetVersionId}…");
            installed = await DownloadService.StartModLoaderAsync(
                mc, loader, targetVersionId, skipFabricApi: false, cancellationToken)
                .ConfigureAwait(true);
        }

        snapshot = GameInstanceStore.Current;
        if (!installed || !snapshot.VersionIds.Contains(targetVersionId, StringComparer.OrdinalIgnoreCase))
        {
            if (isNewInstance)
            {
                warnings.Add($"整合包所需版本 {targetVersionId} 安装失败，安装中止。");
                return (targetVersionId, string.Empty, warnings);
            }
            warnings.Add($"整合包所需版本 {targetVersionId} 安装失败，已回退到你选择的版本。");
            return (fallbackId, FallbackDir(), warnings);
        }

        GameInstanceStore.Select(targetVersionId);
        return (targetVersionId, ContentInstallService.ResolveContentDirectory(
            snapshot.MinecraftDirectory, snapshot.SourcePath, targetVersionId), warnings);
    }

    // ------------------------------------------------------------------
    // 事件
    // ------------------------------------------------------------------

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VersionComboBox.SelectedIndex < 0 ||
            VersionComboBox.SelectedIndex >= _versions.Count)
            return;
        var selected = _versions[VersionComboBox.SelectedIndex];
        DownloadStatus.SetIdleText(selected.PrimaryFile is { } file
            ? $"{selected.DisplayName} · {file.SizeDisplay}"
            : selected.DisplayName);
    }

    private void OnCloseClick(object? sender, EventArgs e)
    {
        _loadCts?.Cancel();
        DownloadStatus.Reset();
        Host?.Close();
    }
}
