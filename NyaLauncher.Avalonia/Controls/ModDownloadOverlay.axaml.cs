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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Material.Icons;
using Material.Icons.Avalonia;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 左侧 MC 版本列表条目：版本号 + 该版本下可用的加载器数量。
/// </summary>
public sealed record GameVersionEntry(string Version, int LoaderCount);

/// <summary>
/// Mod 下载遮罩层：嵌入主窗口，左边 MC 版本列表，右边按 Loader 分组的版本选择。
/// </summary>
public partial class ModDownloadOverlay : UserControl, IModalHostAware
{
    private ModrinthProject? _project;
    private List<GameVersionEntry> _allGameVersions = [];
    private CancellationTokenSource? _loadCts;
    private string? _loaderFilter;

    /// <summary>承载本视图的宿主（由 ModalOverlayHost.Show 自动注入）。</summary>
    public ModalOverlayHost? Host { get; set; }

    /// <summary>所有 MC 版本 → 该版本下有哪些 loader。</summary>
    private readonly Dictionary<string, List<string>> _versionLoaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"mcVersion+loader" → 对应的 Mod 版本列表。</summary>
    private readonly Dictionary<string, List<ModrinthVersion>> _comboVersions = new(StringComparer.OrdinalIgnoreCase);

    public ModDownloadOverlay()
    {
        InitializeComponent();
        LoaderFilterComboBox.Items.Add("全部加载器");
        LoaderFilterComboBox.Items.Add("Fabric");
        LoaderFilterComboBox.Items.Add("Forge");
        LoaderFilterComboBox.Items.Add("NeoForge");
        LoaderFilterComboBox.Items.Add("Quilt");
        LoaderFilterComboBox.SelectedIndex = 0;
        DownloadStatus.IdleText = "选择版本后点击下载";
    }

    /// <summary>
    /// 宿主展示前调用：加载指定 Mod 的版本信息。
    /// </summary>
    public async void Setup(ModrinthProject project)
    {
        _project = project;
        _versionLoaders.Clear();
        _comboVersions.Clear();
        LoaderListPanel.Children.Clear();
        GameVersionList.ItemsSource = null;
        StatusText.Text = "正在加载版本信息…";
        StatusText.Foreground = OverlayHelpers.FindBrush("HintTextBrush");
        Header.Title = project.Title;
        Header.Subtitle = project.Description;
        Header.ExtraText = $"{project.DownloadsDisplay} · {project.FollowsDisplay}";
        Header.IconUrl = project.IconUrl;
        DownloadStatus.Reset();
        TargetPicker.Setup(DownloadTargetKind.Mod);

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            // 获取该 Mod 的所有版本（不过滤）
            var allVersions = await ModrinthVersionApi.GetVersionsAsync(
                project.ProjectId, cancellationToken: ct);
            if (ct.IsCancellationRequested) return;

            if (allVersions.Count == 0)
            {
                StatusText.Text = "该 Mod 没有可用版本。";
                return;
            }

            // 按 MC 版本 + Loader 分组 + 排序：热门 Mod 可能上千版本，
            // 嵌套循环在后台线程执行，避免 UI 线程被占满导致界面卡死
            var (versionLoaders, comboVersions, gameVersions) = await System.Threading.Tasks.Task.Run(
                () =>
                {
                    var localLoaders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var localCombos = new Dictionary<string, List<ModrinthVersion>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var v in allVersions)
                    {
                        foreach (var gv in v.GameVersions)
                        {
                            foreach (var loader in v.Loaders)
                            {
                                var key = $"{gv}+{loader}";
                                if (!localCombos.ContainsKey(key))
                                    localCombos[key] = [];
                                localCombos[key].Add(v);

                                if (!localLoaders.ContainsKey(gv))
                                    localLoaders[gv] = [];
                                if (!localLoaders[gv].Contains(loader, StringComparer.OrdinalIgnoreCase))
                                    localLoaders[gv].Add(loader);
                            }
                        }
                    }

                    // 左侧：MC 版本列表（按版本号降序，避免 1.9 排在 1.10 前）
                    var sortedVersions = localLoaders
                        .Select(kv => new GameVersionEntry(kv.Key, kv.Value.Count))
                        .OrderByDescending(e => e.Version, new VersionStringComparer())
                        .ToList();
                    return (localLoaders, localCombos, sortedVersions);
                },
                ct);

            _versionLoaders.Clear();
            foreach (var pair in versionLoaders)
                _versionLoaders[pair.Key] = pair.Value;
            _comboVersions.Clear();
            foreach (var pair in comboVersions)
                _comboVersions[pair.Key] = pair.Value;
            _allGameVersions = gameVersions;

            VersionFilterBox.Text = "";
            GameVersionList.ItemsSource = _allGameVersions;
            VersionCountText.Text = $"{_allGameVersions.Count} 个版本可用";
            if (_allGameVersions.Count > 0)
                GameVersionList.SelectedIndex = 0;

            StatusText.Text = string.Empty;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败：{ex.Message}";
            StatusText.Foreground = OverlayHelpers.FindBrush("ErrorBrush");
        }
    }

    // ------------------------------------------------------------------
    // 左侧 MC 版本选中 → 右侧加载 Loader 列表
    // ------------------------------------------------------------------

    private void OnGameVersionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (GameVersionList.SelectedItem is not GameVersionEntry entry) return;
        BuildLoaderList(entry.Version);
    }

    private void OnVersionFilterChanged(object? sender, TextChangedEventArgs e)
    {
        var keyword = VersionFilterBox?.Text?.Trim() ?? "";
        List<GameVersionEntry> filtered;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            filtered = _allGameVersions;
        }
        else
        {
            filtered = _allGameVersions
                .Where(entry => entry.Version.Contains(keyword, StringComparison.OrdinalIgnoreCase))
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

        // 应用加载器过滤器
        IEnumerable<string> visibleLoaders = loaders;
        if (!string.IsNullOrWhiteSpace(_loaderFilter))
        {
            visibleLoaders = loaders.Where(l =>
                string.Equals(l, _loaderFilter, StringComparison.OrdinalIgnoreCase));
        }

        var visibleList = visibleLoaders.ToList();
        if (visibleList.Count == 0)
        {
            StatusText.Text = $"该版本下没有 {_loaderFilter} 的版本。";
            return;
        }

        foreach (var loader in visibleList)
        {
            var key = $"{gameVersion}+{loader}";
            if (!_comboVersions.TryGetValue(key, out var versions) || versions.Count == 0)
                continue;

            // 按发布日期降序排列
            var sorted = versions
                .OrderByDescending(v => v.DatePublishedRaw, StringComparer.OrdinalIgnoreCase)
                .ToList();

            LoaderListPanel.Children.Add(BuildLoaderCard(loader, sorted));
        }

        StatusText.Text = _loaderFilter is null
            ? $"{gameVersion} · {visibleList.Count} 个加载器可用，点击下载按钮开始"
            : $"{gameVersion} · {_loaderFilter} 下 {visibleList.Count} 个版本可用";
    }

    /// <summary>
    /// 构建单个 Loader 的下载卡片：名称徽标 + 版本数 + 版本选择 + 下载按钮。
    /// </summary>
    private Border BuildLoaderCard(string loader, List<ModrinthVersion> sorted)
    {
        var card = new Border
        {
            Background = OverlayHelpers.FindBrush("ControlBgBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12)
        };

        var grid = new Grid
        {
            RowSpacing = 8,
            ColumnSpacing = 10
        };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        // Loader 名称徽标（彩色）
        var badge = new Border
        {
            Background = LoaderColor(loader),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = FormatLoaderName(loader),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White
        };
        grid.Children.Add(badge);

        // 版本数量
        var countText = new TextBlock
        {
            Text = $"{sorted.Count} 个版本",
            FontSize = 11,
            Foreground = OverlayHelpers.FindBrush("HintTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 1);
        grid.Children.Add(countText);

        // 版本 ComboBox
        var combo = new ComboBox
        {
            Background = OverlayHelpers.FindBrush("ButtonBgBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = sorted,
            IsEnabled = !DownloadStatus.IsDownloading
        };
        foreach (var v in sorted)
        {
            var file = v.PrimaryFile;
            var sizeStr = file is not null ? $" · {file.SizeDisplay}" : "";
            combo.Items.Add($"{v.DisplayName}{sizeStr}");
        }
        if (sorted.Count > 0)
            combo.SelectedIndex = 0;
        Grid.SetRow(combo, 1);
        Grid.SetColumnSpan(combo, 2);
        grid.Children.Add(combo);

        // 下载按钮（Material 下载箭头图标 + 文字）
        var downloadContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        downloadContent.Children.Add(new MaterialIcon
        {
            Kind = MaterialIconKind.ArrowDown,
            Width = 14,
            Height = 14,
            Foreground = OverlayHelpers.FindBrush("WhiteBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        downloadContent.Children.Add(new TextBlock
        {
            Text = "下载",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = OverlayHelpers.FindBrush("WhiteBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var downloadBtn = new Button
        {
            Content = downloadContent,
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(8),
            Background = OverlayHelpers.FindBrush("SystemAccentColor"),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = combo,
            IsEnabled = !DownloadStatus.IsDownloading
        };
        downloadBtn.Click += OnDownloadComboClick;
        Grid.SetRow(downloadBtn, 1);
        Grid.SetColumn(downloadBtn, 2);
        grid.Children.Add(downloadBtn);

        card.Child = grid;
        return card;
    }

    /// <summary>
    /// 加载器过滤器变更时重建右侧列表。
    /// </summary>
    private void OnLoaderFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LoaderFilterComboBox.SelectedItem is not string filter)
            return;
        _loaderFilter = filter == "全部加载器" ? null : filter;
        if (GameVersionList.SelectedItem is GameVersionEntry entry)
            BuildLoaderList(entry.Version);
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
            StatusText.Foreground = OverlayHelpers.FindBrush("ErrorBrush");
            return;
        }

        // 防重入：下载期间再次点击直接忽略
        if (DownloadStatus.IsDownloading)
        {
            StatusText.Text = "已有下载任务进行中，请等待完成。";
            return;
        }

        try
        {
            // 目标实例：直接下载到实例 mods 目录
            var selection = TargetPicker.SelectedTarget;
            var instanceId = TargetPicker.SelectedInstanceId;
            if (!TargetPicker.IsCustomPath && !string.IsNullOrWhiteSpace(instanceId))
            {
                var contentDir = TargetPicker.ResolveContentDir();
                if (string.IsNullOrWhiteSpace(contentDir))
                {
                    StatusText.Text = "无法定位实例内容目录。";
                    StatusText.Foreground = OverlayHelpers.FindBrush("ErrorBrush");
                    return;
                }

                var targetPath = Path.Combine(contentDir, "mods", Path.GetFileName(file.Filename));
                BeginDownload(file.Filename, file.Url, targetPath, file.Size);
                return;
            }

            // 自定义保存路径：弹出文件保存对话框
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

            BeginDownload(file.Filename, file.Url, savePath, file.Size);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"选择保存位置失败：{ex.Message}";
            StatusText.Foreground = OverlayHelpers.FindBrush("ErrorBrush");
        }
    }

    private async void BeginDownload(string fileName, string url, string savePath, long fileSize)
    {
        SetDownloadButtonsEnabled(false);
        StatusText.Text = "正在下载…";
        StatusText.Foreground = OverlayHelpers.FindBrush("HintTextBrush");

        var result = await OverlayDownloadRunner.RunAsync(
            DownloadStatus, fileName, url, savePath, fileSize);
        StatusText.Text = result.Message;
        StatusText.Foreground = result.Status switch
        {
            DownloadRunStatus.Success => OverlayHelpers.FindBrush("SuccessBrush"),
            DownloadRunStatus.Cancelled => OverlayHelpers.FindBrush("HintTextBrush"),
            _ => OverlayHelpers.FindBrush("ErrorBrush")
        };

        SetDownloadButtonsEnabled(true);
    }

    private void SetDownloadButtonsEnabled(bool enabled)
    {
        foreach (var child in LoaderListPanel.Children)
        {
            if (child is Border { Child: Grid grid })
            {
                foreach (var element in grid.Children)
                {
                    if (element is ComboBox combo)
                        combo.IsEnabled = enabled;
                    if (element is Button button)
                        button.IsEnabled = enabled;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // 关闭
    // ------------------------------------------------------------------

    private void OnCloseClick(object? sender, EventArgs e)
    {
        _loadCts?.Cancel();
        DownloadStatus.Reset();
        LoaderListPanel.Children.Clear();
        GameVersionList.ItemsSource = null;
        _allGameVersions = [];
        _versionLoaders.Clear();
        _comboVersions.Clear();
        Host?.Close();
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

    /// <summary>Loader 徽标配色。</summary>
    private static IBrush LoaderColor(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => new SolidColorBrush(Color.Parse("#B08D6A")),
        "forge" => new SolidColorBrush(Color.Parse("#D9A84C")),
        "neoforge" => new SolidColorBrush(Color.Parse("#3E9BD9")),
        "quilt" => new SolidColorBrush(Color.Parse("#A86FD9")),
        _ => new SolidColorBrush(Color.Parse("#7A7A7A"))
    };

    /// <summary>
    /// 按分段数值比较 MC 版本号（1.10.2 &gt; 1.9.4），替代会产生错误顺序的字符串比较。
    /// </summary>
    private sealed class VersionStringComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            if (a is null) return b is null ? 0 : -1;
            if (b is null) return 1;
            var aParts = a.Split('.', '-', '_');
            var bParts = b.Split('.', '-', '_');
            var length = Math.Max(aParts.Length, bParts.Length);
            for (var i = 0; i < length; i++)
            {
                var aPart = i < aParts.Length ? aParts[i] : "0";
                var bPart = i < bParts.Length ? bParts[i] : "0";
                if (int.TryParse(aPart, out var aNum) && int.TryParse(bPart, out var bNum))
                {
                    var diff = aNum.CompareTo(bNum);
                    if (diff != 0) return diff;
                }
                else
                {
                    var diff = string.CompareOrdinal(aPart, bPart);
                    if (diff != 0) return diff;
                }
            }
            return 0;
        }
    }
}
