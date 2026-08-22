using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Dialogs;

public partial class ModDetailDialog : Window
{
    private static readonly string FabricApiProjectId = "P7dR8mSH";

    private readonly ModrinthProject _project;
    private readonly List<string> _installedVersions = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _downloadCts;
    private string? _selectedGameVersion;
    private List<ModrinthVersion> _currentVersions = [];
    private ModrinthVersion? _selectedModVersion;

    public ModDetailDialog(ModrinthProject project)
    {
        _project = project;
        InitializeComponent();
        ModTitle.Text = project.Title;
        ModDescription.Text = project.Description;
        ModDownloads.Text = project.DownloadsDisplay;
        ModFollows.Text = project.FollowsDisplay;
        ModIcon.SourceUrl = project.IconUrl;
        _ = LoadGameVersionsAsync();
    }

    // ------------------------------------------------------------------
    // 1) 加载已安装的 MC 版本
    // ------------------------------------------------------------------

    private async Task LoadGameVersionsAsync()
    {
        try
        {
            var snapshot = GameInstanceStore.Current;
            if (!string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
            {
                var ids = MinecraftDirectoryLocator
                    .GetInstalledVersionIds(snapshot.MinecraftDirectory);
                _installedVersions.AddRange(ids);
            }
        }
        catch { }

        if (_installedVersions.Count == 0)
        {
            try
            {
                var supported = await ModrinthVersionApi
                    .GetSupportedGameVersionsAsync(_project.ProjectId);
                _installedVersions.AddRange(supported.Take(20));
            }
            catch
            {
                StatusText.Text = "无法获取支持的 Minecraft 版本。";
                StatusText.IsVisible = true;
                return;
            }
        }

        GameVersionBox.ItemsSource = _installedVersions;
        if (_installedVersions.Count > 0)
            GameVersionBox.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------
    // 2) MC 版本变更 → 加载所有兼容的 Mod 版本
    // ------------------------------------------------------------------

    private async void OnGameVersionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GameVersionBox.SelectedItem is not string gv) return;
        _selectedGameVersion = gv;
        _selectedModVersion = null;
        _currentVersions = [];
        ModVersionList.ItemsSource = null;
        ModVersionHint.Text = "正在查询版本…";
        DependencyPanel.IsVisible = false;
        SelectedVersionPanel.IsVisible = false;
        DownloadButton.IsEnabled = false;

        // Fabric API 选项：仅当版本 ID 包含 "fabric" 时显示
        FabricApiPanel.IsVisible = gv.Contains("fabric", StringComparison.OrdinalIgnoreCase);

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            // 直接按 MC 版本查询，不过滤 loader
            var versions = await ModrinthVersionApi.GetVersionsAsync(
                _project.ProjectId,
                gameVersions: [gv],
                cancellationToken: ct);
            if (ct.IsCancellationRequested) return;

            if (versions.Count == 0)
            {
                ModVersionHint.Text = "该版本下无可用的 Mod 版本。";
                return;
            }

            _currentVersions = versions;

            var items = new List<string>();
            foreach (var v in versions)
            {
                try
                {
                    var parts = new List<string> { v.DisplayName ?? v.VersionNumber ?? "未知" };
                    if (!string.IsNullOrWhiteSpace(v.LoaderDisplay))
                        parts.Add(v.LoaderDisplay);
                    if (!string.IsNullOrWhiteSpace(v.DateDisplay))
                        parts.Add(v.DateDisplay);
                    if (v.PrimaryFile is { } f)
                        parts.Add(f.SizeDisplay);
                    items.Add(string.Join(" · ", parts));
                }
                catch
                {
                    items.Add(v.VersionNumber ?? "未知版本");
                }
            }

            ModVersionList.ItemsSource = items;
            ModVersionHint.Text = $"共 {versions.Count} 个版本可用。";
            ModVersionList.SelectedIndex = 0;
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            ModVersionHint.Text = $"查询失败：{ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // 3) Mod 版本选中 → 显示详情 + 依赖
    // ------------------------------------------------------------------

    private void OnModVersionSelected(object? sender, SelectionChangedEventArgs e)
    {
        var index = ModVersionList.SelectedIndex;
        if (index < 0 || index >= _currentVersions.Count) return;

        var mv = _currentVersions[index];
        _selectedModVersion = mv;
        var file = mv.PrimaryFile;
        DownloadButton.IsEnabled = file is not null;

        // 显示版本详情
        var info = $"{mv.DisplayName}";
        if (file is not null)
            info += $"\n文件：{file.Filename} ({file.SizeDisplay})";
        if (!string.IsNullOrWhiteSpace(mv.LoaderDisplay))
            info += $"\n加载器：{mv.LoaderDisplay}";
        if (!string.IsNullOrWhiteSpace(mv.DateDisplay))
            info += $"\n发布日期：{mv.DateDisplay}";
        if (mv.GameVersions.Count > 0)
            info += $"\n支持版本：{mv.GameVersionsDisplay}";

        SelectedVersionInfo.Text = info;
        ChangelogText.Text = string.IsNullOrWhiteSpace(mv.Changelog)
            ? "无更新日志。"
            : mv.Changelog!.Length > 200 ? mv.Changelog[..200] + "…" : mv.Changelog;
        SelectedVersionPanel.IsVisible = true;

        ModVersionHint.Text = file is not null
            ? $"{mv.DisplayName} · {file.SizeDisplay}"
            : $"{mv.DisplayName} · 无可下载文件";

        // 前置依赖
        var requiredDeps = mv.Dependencies.Where(d => d.IsRequired).ToList();
        if (requiredDeps.Count > 0)
        {
            var depTexts = requiredDeps.Select(d =>
            {
                if (!string.IsNullOrWhiteSpace(d.FileName))
                    return $"• {d.FileName}";
                if (!string.IsNullOrWhiteSpace(d.ProjectId))
                    return $"• 项目 {d.ProjectId}";
                if (!string.IsNullOrWhiteSpace(d.VersionId))
                    return $"• 版本 {d.VersionId}";
                return "• 未知依赖";
            });
            DependencyList.Text = string.Join("\n", depTexts);
            DependencyPanel.IsVisible = true;
        }
        else
        {
            DependencyPanel.IsVisible = false;
        }
    }

    // ------------------------------------------------------------------
    // 4) 下载
    // ------------------------------------------------------------------

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var file = _selectedModVersion?.PrimaryFile;
        if (file is null || string.IsNullOrWhiteSpace(file.Url))
        {
            StatusText.Text = "所选版本无可下载文件。";
            StatusText.IsVisible = true;
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var result = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 Mod 文件",
            SuggestedFileName = file.Filename,
            FileTypeChoices =
            [
                new FilePickerFileType("Mod 文件") { Patterns = ["*.jar", "*.zip"] },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
            ]
        });

        if (result is null) return;
        var savePath = result.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(savePath)) return;

        DownloadButton.IsEnabled = false;
        StatusText.Text = $"正在下载 {file.Filename}…";
        StatusText.Foreground = ThemeBrushes.Muted;
        StatusText.IsVisible = true;

        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        try
        {
            var progressBar = new Progress<(long downloaded, long total)>(p =>
            {
                if (p.total > 0)
                {
                    var pct = (int)Math.Clamp(p.downloaded * 100L / p.total, 0, 100);
                    StatusText.Text = $"正在下载 {file.Filename}… {pct}%";
                }
            });
            await ModDownloadService.DownloadAsync(file.Url, savePath, progressBar, ct);
            StatusText.Text = $"下载完成：{savePath}";
            StatusText.Foreground = ThemeBrushes.Success;

            // Fabric API 自动安装
            if (FabricApiPanel.IsVisible &&
                FabricApiCheckBox.IsChecked == true &&
                _selectedGameVersion is not null)
            {
                StatusText.Text += "\n正在下载 Fabric API…";
                await DownloadFabricApiAsync(savePath, ct);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "下载已取消。";
            StatusText.Foreground = ThemeBrushes.Muted;
            DownloadButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败：{ex.Message}";
            StatusText.Foreground = ThemeBrushes.Error;
            DownloadButton.IsEnabled = true;
        }
    }

    private async Task DownloadFabricApiAsync(string modFilePath, CancellationToken ct)
    {
        try
        {
            var saveDir = Path.GetDirectoryName(modFilePath);
            if (string.IsNullOrWhiteSpace(saveDir)) return;

            var versions = await ModrinthVersionApi.GetVersionsAsync(
                FabricApiProjectId,
                gameVersions: [_selectedGameVersion!],
                loaders: ["fabric"],
                cancellationToken: ct);

            var latest = versions.FirstOrDefault(v => v.PrimaryFile is not null);
            if (latest?.PrimaryFile is null) return;

            var fabricApiPath = Path.Combine(saveDir, latest.PrimaryFile.Filename);
            await ModDownloadService.DownloadAsync(latest.PrimaryFile.Url, fabricApiPath, cancellationToken: ct);
            StatusText.Text += $"\nFabric API 已下载：{latest.PrimaryFile.Filename}";
        }
        catch
        {
            StatusText.Text += "\nFabric API 下载失败，请手动安装。";
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
    }
}
