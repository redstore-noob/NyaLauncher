using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Mod 下载遮罩层：嵌入主窗口，左边 MC 版本列表，右边按 Loader 分组的版本选择。
/// </summary>
public partial class ModDownloadOverlay : UserControl
{
    private ModrinthProject? _project;
    private List<ModrinthVersion> _allVersions = [];
    private List<string> _allGameVersions = [];
    private CancellationTokenSource? _loadCts;

    /// <summary>所有 MC 版本 → 该版本下有哪些 loader。</summary>
    private readonly Dictionary<string, List<string>> _versionLoaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"mcVersion+loader" → 对应的 Mod 版本列表。</summary>
    private readonly Dictionary<string, List<ModrinthVersion>> _comboVersions = new(StringComparer.OrdinalIgnoreCase);

    public ModDownloadOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开遮罩层并加载指定 Mod 的版本信息。
    /// </summary>
    public async void ShowFor(ModrinthProject project)
    {
        _project = project;
        _allVersions = [];
        _versionLoaders.Clear();
        _comboVersions.Clear();
        LoaderListPanel.Children.Clear();
        GameVersionList.ItemsSource = null;
        StatusText.Text = "正在加载版本信息…";
        ModTitleText.Text = project.Title;
        ModDescText.Text = project.Description;
        IsVisible = true;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            // 获取该 Mod 的所有版本（不过滤）
            _allVersions = await ModrinthVersionApi.GetVersionsAsync(
                project.ProjectId, cancellationToken: ct);
            if (ct.IsCancellationRequested) return;

            if (_allVersions.Count == 0)
            {
                StatusText.Text = "该 Mod 没有可用版本。";
                return;
            }

            // 按 MC 版本 + Loader 分组
            foreach (var v in _allVersions)
            {
                foreach (var gv in v.GameVersions)
                {
                    foreach (var loader in v.Loaders)
                    {
                        var key = $"{gv}+{loader}";
                        if (!_comboVersions.ContainsKey(key))
                            _comboVersions[key] = [];
                        _comboVersions[key].Add(v);

                        if (!_versionLoaders.ContainsKey(gv))
                            _versionLoaders[gv] = [];
                        if (!_versionLoaders[gv].Contains(loader, StringComparer.OrdinalIgnoreCase))
                            _versionLoaders[gv].Add(loader);
                    }
                }
            }

            // 左侧：MC 版本列表（按版本降序）
            _allGameVersions = _versionLoaders.Keys
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
            VersionFilterBox.Text = "";
            GameVersionList.ItemsSource = _allGameVersions;
            VersionCountText.Text = $"{_allGameVersions.Count} 个版本可用";
            if (_allGameVersions.Count > 0)
                GameVersionList.SelectedIndex = 0;

            StatusText.Text = "";
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败：{ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // 左侧 MC 版本选中 → 右侧加载 Loader 列表
    // ------------------------------------------------------------------

    private void OnGameVersionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (GameVersionList.SelectedItem is not string gv) return;
        BuildLoaderList(gv);
    }

    private void OnVersionFilterChanged(object? sender, TextChangedEventArgs e)
    {
        var keyword = VersionFilterBox?.Text?.Trim() ?? "";
        List<string> filtered;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            filtered = _allGameVersions;
        }
        else
        {
            filtered = _allGameVersions
                .Where(v => v.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        GameVersionList.ItemsSource = filtered;
        VersionCountText.Text = filtered.Count == _allGameVersions.Count
            ? $"{filtered.Count} 个版本可用"
            : $"匹配 {filtered.Count} / {_allGameVersions.Count} 个版本";

        if (filtered.Count > 0)
            GameVersionList.SelectedIndex = 0;
        else
            LoaderListPanel.Children.Clear();
    }

    private void BuildLoaderList(string gameVersion)
    {
        LoaderListPanel.Children.Clear();

        if (!_versionLoaders.TryGetValue(gameVersion, out var loaders) || loaders.Count == 0)
        {
            StatusText.Text = "该版本下无可用的加载器。";
            return;
        }

        foreach (var loader in loaders)
        {
            var key = $"{gameVersion}+{loader}";
            if (!_comboVersions.TryGetValue(key, out var versions) || versions.Count == 0)
                continue;

            // 按发布日期降序排列
            var sorted = versions
                .OrderByDescending(v => v.DatePublishedRaw, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 每个 Loader 一行：标签 + ComboBox + 下载按钮
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.Margin = new Thickness(0, 0, 0, 0);

            // Loader 标签
            var label = new TextBlock
            {
                Text = FormatLoaderName(loader),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindBrush("SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 70
            };
            row.Children.Add(label);

            // 版本 ComboBox
            var combo = new ComboBox
            {
                Background = FindBrush("ControlBgBrush"),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = sorted
            };
            foreach (var v in sorted)
            {
                var file = v.PrimaryFile;
                var sizeStr = file is not null ? $" ({file.SizeDisplay})" : "";
                combo.Items.Add($"{v.DisplayName}{sizeStr}");
            }
            if (sorted.Count > 0)
                combo.SelectedIndex = 0;
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);

            // 下载按钮
            var downloadBtn = new Button
            {
                Content = "下载",
                Padding = new Thickness(14, 6),
                CornerRadius = new CornerRadius(8),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Background = FindBrush("SystemAccentColor"),
                Foreground = FindBrush("WhiteBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = combo
            };
            downloadBtn.Click += OnDownloadComboClick;
            Grid.SetColumn(downloadBtn, 2);
            row.Children.Add(downloadBtn);

            LoaderListPanel.Children.Add(row);
        }

        StatusText.Text = $"{gameVersion} 下有 {loaders.Count} 个加载器可用。";
    }

    // ------------------------------------------------------------------
    // 下载
    // ------------------------------------------------------------------

    private async void OnDownloadComboClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ComboBox combo }) return;
        if (combo.Tag is not List<ModrinthVersion> versions) return;
        if (combo.SelectedIndex < 0 || combo.SelectedIndex >= versions.Count) return;

        var selected = versions[combo.SelectedIndex];
        var file = selected.PrimaryFile;
        if (file is null || string.IsNullOrWhiteSpace(file.Url))
        {
            StatusText.Text = "所选版本无可下载文件。";
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

        StatusText.Text = $"正在下载 {file.Filename}…";
        try
        {
            var progress = new Progress<(long downloaded, long total)>(p =>
            {
                if (p.total > 0)
                {
                    var pct = (int)Math.Clamp(p.downloaded * 100L / p.total, 0, 100);
                    StatusText.Text = $"正在下载 {file.Filename}… {pct}%";
                }
            });
            await ModDownloadService.DownloadAsync(file.Url, savePath, progress);
            StatusText.Text = $"下载完成：{savePath}";
            StatusText.Foreground = FindBrush("SuccessBrush");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败：{ex.Message}";
            StatusText.Foreground = FindBrush("ErrorBrush");
        }
    }

    // ------------------------------------------------------------------
    // 关闭
    // ------------------------------------------------------------------

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        IsVisible = false;
        LoaderListPanel.Children.Clear();
        GameVersionList.ItemsSource = null;
        _allVersions = [];
        _versionLoaders.Clear();
        _comboVersions.Clear();
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static string FormatLoaderName(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => loader
    };

    private IBrush FindBrush(string key)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, null, out var value) == true
            && value is IBrush brush)
            return brush;
        return Brushes.Gray;
    }
}
