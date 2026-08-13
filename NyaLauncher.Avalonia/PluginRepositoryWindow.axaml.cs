using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Plugins;

namespace NyaLauncher.Avalonia;

public partial class PluginRepositoryWindow : Window
{
    private static readonly IBrush SuccessBackground = new SolidColorBrush(Color.Parse("#243B35"));
    private static readonly IBrush SuccessForeground = new SolidColorBrush(Color.Parse("#91E0B3"));
    private static readonly IBrush InfoBackground = new SolidColorBrush(Color.Parse("#2A3150"));
    private static readonly IBrush InfoForeground = new SolidColorBrush(Color.Parse("#B8C2FF"));
    private static readonly IBrush WarningBackground = new SolidColorBrush(Color.Parse("#403522"));
    private static readonly IBrush WarningForeground = new SolidColorBrush(Color.Parse("#E8C882"));
    private static readonly IBrush ErrorBackground = new SolidColorBrush(Color.Parse("#412832"));
    private static readonly IBrush ErrorForeground = new SolidColorBrush(Color.Parse("#F1A7B6"));

    private PluginManager? _pluginManager;
    private PluginRepositoryClient? _repositoryClient;
    private PluginRepositoryIndex? _index;
    private IReadOnlyList<RepositoryListItem> _allItems = [];
    private string? _selectedPluginId;
    private bool _loading;
    private bool _installing;
    private bool _confirmingInstall;
    private bool _synchronizingSelection;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _installCancellation;

    public PluginRepositoryWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
        ApplyFilter();
    }

    internal PluginRepositoryWindow(
        PluginManager pluginManager,
        PluginRepositoryClient repositoryClient) : this()
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _repositoryClient = repositoryClient ??
                            throw new ArgumentNullException(nameof(repositoryClient));
        _pluginManager.Changed += OnInstalledCatalogChanged;
    }

    private async void OnOpened(object? sender, EventArgs e) => await LoadRepositoryAsync();

    private void OnClosed(object? sender, EventArgs e)
    {
        _installCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        if (_pluginManager is not null)
            _pluginManager.Changed -= OnInstalledCatalogChanged;
    }

    private async Task LoadRepositoryAsync()
    {
        if (_loading || _repositoryClient is null)
            return;

        _loading = true;
        RepositoryLoadingOverlay.IsVisible = true;
        RefreshRepositoryButton.IsEnabled = false;
        RepositoryStatusText.Text = "正在读取 NyaLauncher-Plugins 远程索引…";
        try
        {
            _index = await _repositoryClient.LoadIndexAsync(_lifetimeCancellation.Token);
            RebuildItems();
            RepositoryStatusText.Text =
                $"已从在线仓库读取 {_index.Plugins.Count} 个插件条目。";
        }
        catch (OperationCanceledException)
        {
            RepositoryStatusText.Text = "在线仓库请求已取消。";
        }
        catch (Exception exception)
        {
            RepositoryStatusText.Text = $"在线仓库读取失败：{exception.Message}";
            EmptyRepositoryTitle.Text = "无法读取在线仓库";
            EmptyRepositoryHint.Text = "请检查网络连接，或稍后点击“刷新索引”重试";
        }
        finally
        {
            _loading = false;
            RepositoryLoadingOverlay.IsVisible = false;
            RefreshRepositoryButton.IsEnabled = true;
            ApplyFilter();
        }
    }

    private void RebuildItems()
    {
        if (_index is null || _repositoryClient is null)
        {
            _allItems = [];
        }
        else
        {
            var installed = _pluginManager?.Current.Plugins ?? [];
            _allItems = _index.Plugins
                .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(plugin => new RepositoryListItem(
                    plugin,
                    _repositoryClient.GetLatestCompatibleRelease(plugin),
                    installed.FirstOrDefault(local => string.Equals(
                        local.Id,
                        plugin.Id,
                        StringComparison.OrdinalIgnoreCase))))
                .ToArray();
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (RepositoryPluginList is null)
            return;
        var query = RepositorySearchBox.Text?.Trim();
        var filtered = _allItems.Where(item =>
            string.IsNullOrWhiteSpace(query) || item.Contains(query)).ToArray();

        _synchronizingSelection = true;
        try
        {
            RepositoryPluginList.ItemsSource = filtered;
            RepositoryPluginList.SelectedItem = filtered.FirstOrDefault(item =>
                string.Equals(
                    item.Plugin.Id,
                    _selectedPluginId,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _synchronizingSelection = false;
        }

        RepositoryCountText.Text = $"{filtered.Length} / {_allItems.Count}";
        EmptyRepositoryView.IsVisible = filtered.Length == 0 && !_loading;
        if (_allItems.Count == 0 && _index is not null)
        {
            EmptyRepositoryTitle.Text = "仓库暂时没有插件";
            EmptyRepositoryHint.Text = "插件作者可 Fork NyaLauncher-Plugins 并提交 PR 收录";
        }
        else if (_allItems.Count > 0 && filtered.Length == 0)
        {
            EmptyRepositoryTitle.Text = "没有匹配的插件";
            EmptyRepositoryHint.Text = "尝试清空搜索内容";
        }

        if (RepositoryPluginList.SelectedItem is RepositoryListItem selected)
            ShowDetails(selected);
        else
            ShowEmptyDetails();
    }

    private void OnInstalledCatalogChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnInstalledCatalogChanged(sender, e));
            return;
        }

        if (!_installing)
            RebuildItems();
    }

    private void OnRepositorySearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnRepositorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection)
            return;
        if (RepositoryPluginList.SelectedItem is not RepositoryListItem item)
        {
            _selectedPluginId = null;
            ShowEmptyDetails();
            return;
        }

        _selectedPluginId = item.Plugin.Id;
        ShowDetails(item);
    }

    private void ShowDetails(RepositoryListItem item)
    {
        EmptyRepositoryDetails.IsVisible = false;
        RepositoryDetails.IsVisible = true;
        RepositoryDetailsInitial.Text = item.Initial;
        RepositoryDetailsName.Text = item.Name;
        RepositoryDetailsSummary.Text = item.Metadata;
        RepositoryDetailsDescription.Text = string.IsNullOrWhiteSpace(item.Plugin.Description)
            ? "插件作者未提供说明。"
            : item.Plugin.Description;
        RepositoryDetailsId.Text = item.Plugin.Id;
        RepositoryDetailsAuthors.Text = item.Plugin.Authors.Count == 0
            ? "未提供"
            : string.Join("、", item.Plugin.Authors);
        RepositoryDetailsLicense.Text =
            $"{item.Plugin.License} · {string.Join("、", item.Plugin.Categories)}";
        RepositoryDetailsVersion.Text = item.Release?.Version ?? "没有兼容的稳定版本";
        RepositoryDetailsReview.Text = item.Release is null
            ? "—"
            : CreateReviewDetailsText(item.Release);
        RepositoryDetailsReview.Foreground = item.Release is null
            ? InfoForeground
            : RepositoryReviewPolicy.RequiresInstallConfirmation(item.Release)
                ? WarningForeground
                : SuccessForeground;
        RepositoryDetailsCapabilities.Text = item.Release is null
            ? "—"
            : CreateCapabilitiesText(item.Release);
        RepositoryDetailsHash.Text = item.Release is null
            ? "—"
            : $"SHA-256  {item.Release.Download.Sha256}\n" +
              $"大小  {FormatBytes(item.Release.Download.Size)}";
        OpenReleaseNotesButton.IsEnabled = item.Release is not null;
        InstallPluginButton.Content = item.ActionText;
        InstallPluginButton.IsEnabled = item.CanInstall && !_installing && !_confirmingInstall;
        InstallHintText.Text = item.ActionHint;
    }

    private void ShowEmptyDetails()
    {
        EmptyRepositoryDetails.IsVisible = true;
        RepositoryDetails.IsVisible = false;
    }

    private async void OnRefreshRepositoryClick(object? sender, RoutedEventArgs e) =>
        await LoadRepositoryAsync();

    private async void OnInstallPluginClick(object? sender, RoutedEventArgs e)
    {
        if (_installing ||
            _confirmingInstall ||
            _pluginManager is null ||
            _repositoryClient is null ||
            RepositoryPluginList.SelectedItem is not RepositoryListItem
            {
                CanInstall: true,
                Release: not null
            } item)
        {
            return;
        }

        if (RepositoryReviewPolicy.RequiresInstallConfirmation(item.Release))
        {
            _confirmingInstall = true;
            InstallPluginButton.IsEnabled = false;
            bool confirmed;
            try
            {
                confirmed = await ConfirmUnreviewedInstallAsync(item);
            }
            finally
            {
                _confirmingInstall = false;
                InstallPluginButton.IsEnabled = item.CanInstall && !_installing;
            }

            if (!confirmed)
            {
                RepositoryStatusText.Text =
                    $"已取消安装 {item.Plugin.Name}；未开始下载插件包。";
                return;
            }
        }

        _installing = true;
        _installCancellation = new CancellationTokenSource();
        CancelInstallButton.IsEnabled = true;
        InstallPluginButton.IsEnabled = false;
        CancelInstallButton.IsVisible = true;
        InstallProgressBar.IsVisible = true;
        InstallProgressBar.Value = 0;
        RefreshRepositoryButton.IsEnabled = false;
        RepositoryStatusText.Text = $"正在下载 {item.Plugin.Name} {item.Release.Version}…";
        var progress = new Progress<RepositoryDownloadProgress>(value =>
        {
            InstallProgressBar.Value = value.TotalBytes == 0
                ? 0
                : Math.Clamp((double)value.BytesReceived / value.TotalBytes, 0, 1);
            RepositoryStatusText.Text =
                $"正在下载 {item.Plugin.Name}：{FormatBytes(value.BytesReceived)} / " +
                FormatBytes(value.TotalBytes);
        });
        try
        {
            var result = await _pluginManager.InstallFromRepositoryAsync(
                _repositoryClient,
                item.Plugin,
                item.Release,
                progress,
                _installCancellation.Token);
            RepositoryStatusText.Text = result.Message;
        }
        catch (Exception exception)
        {
            RepositoryStatusText.Text = $"插件安装失败：{exception.Message}";
        }
        finally
        {
            _installCancellation.Dispose();
            _installCancellation = null;
            _installing = false;
            CancelInstallButton.IsVisible = false;
            InstallProgressBar.IsVisible = false;
            RefreshRepositoryButton.IsEnabled = true;
            RebuildItems();
        }
    }

    private async Task<bool> ConfirmUnreviewedInstallAsync(RepositoryListItem item)
    {
        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 88,
            Padding = new Thickness(14, 7)
        };
        var installButton = new Button
        {
            Content = "仍要安装",
            MinWidth = 104,
            Padding = new Thickness(14, 7),
            Background = ErrorBackground,
            Foreground = ErrorForeground
        };
        var dialog = new Window
        {
            Title = "安装未经审核的插件",
            Width = 610,
            MinWidth = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#171B29")),
            Content = new Border
            {
                Padding = new Thickness(22),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "此版本未经仓库管理员审核",
                            FontSize = 18,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = ErrorForeground
                        },
                        new TextBlock
                        {
                            Text =
                                $"{item.Plugin.Name}  {item.Release!.Version}\n" +
                                $"插件 ID：{item.Plugin.Id}",
                            Foreground = new SolidColorBrush(Color.Parse("#E7EAF7"))
                        },
                        new Border
                        {
                            Background = WarningBackground,
                            CornerRadius = new CornerRadius(9),
                            Padding = new Thickness(13),
                            Child = new TextBlock
                            {
                                Text =
                                    "该版本尚未经过 NyaLauncher-Plugins 管理员审核。" +
                                    "SHA-256 只能证明下载内容与索引一致，不能证明插件代码安全。" +
                                    "插件会在启动器进程中运行，并可能获得你之后授予的能力。",
                                Foreground = WarningForeground,
                                TextWrapping = TextWrapping.Wrap
                            }
                        },
                        new TextBlock
                        {
                            Text =
                                "请先核对插件源仓库、Release 与发布者；只有点击“仍要安装”后才会开始下载。\n\n" +
                                $"来源：{item.Plugin.RepositoryUrl}\n" +
                                $"SHA-256：{item.Release.Download.Sha256}",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#AEB7D0")),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancelButton, installButton }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        installButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool?>(this) == true;
    }

    private void OnCancelInstallClick(object? sender, RoutedEventArgs e)
    {
        _installCancellation?.Cancel();
        CancelInstallButton.IsEnabled = false;
        RepositoryStatusText.Text = "正在取消插件下载…";
    }

    private void OnOpenRepositoryClick(object? sender, RoutedEventArgs e) =>
        OpenUrl(PluginRepositoryClient.RepositoryUrl);

    private void OnOpenPluginSourceClick(object? sender, RoutedEventArgs e)
    {
        if (RepositoryPluginList.SelectedItem is RepositoryListItem item)
            OpenUrl(item.Plugin.RepositoryUrl);
    }

    private void OnOpenReleaseNotesClick(object? sender, RoutedEventArgs e)
    {
        if (RepositoryPluginList.SelectedItem is RepositoryListItem { Release: not null } item)
            OpenUrl(item.Release.ReleaseNotesUrl);
    }

    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            RepositoryStatusText.Text = "拒绝打开无效的外部地址。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            RepositoryStatusText.Text = $"无法打开外部地址：{exception.Message}";
        }
    }

    private static string CreateCapabilitiesText(RepositoryRelease release)
    {
        var capabilities = release.RequiredCapabilities
            .Select(capability => $"必要 · {capability}")
            .Concat(release.OptionalCapabilities.Select(capability => $"可选 · {capability}"))
            .ToArray();
        return capabilities.Length == 0 ? "未声明额外能力" : string.Join("；", capabilities);
    }

    private static string CreateReviewDetailsText(RepositoryRelease release)
    {
        if (RepositoryReviewPolicy.RequiresInstallConfirmation(release))
        {
            return "未经仓库管理员审核。安装前请自行核对插件源仓库、Release 与发布者。";
        }

        var review = release.Review!;
        var notes = string.IsNullOrWhiteSpace(review.Notes)
            ? "未提供补充说明"
            : review.Notes.Trim();
        return
            $"管理员已审核\n审核人：{review.ReviewedBy}\n" +
            $"审核时间：{review.ReviewedAt}\n说明：{notes}";
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private sealed class RepositoryListItem
    {
        public RepositoryListItem(
            RepositoryPlugin plugin,
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            Plugin = plugin;
            Release = release;
            Installed = installed;
            Initial = string.IsNullOrWhiteSpace(plugin.Name)
                ? "P"
                : plugin.Name[..1].ToUpperInvariant();
            Name = plugin.Name;
            Metadata = release is null
                ? plugin.Id
                : $"{release.Version} · {plugin.Authors.FirstOrDefault() ?? plugin.Id}";
            (StatusText, StatusBackground, StatusForeground, ActionText, ActionHint, CanInstall) =
                ResolveState(release, installed);
            var reviewed = release is not null &&
                           !RepositoryReviewPolicy.RequiresInstallConfirmation(release);
            ReviewText = reviewed ? "管理员已审核" : "未经审核";
            ReviewBackground = reviewed ? SuccessBackground : WarningBackground;
            ReviewForeground = reviewed ? SuccessForeground : WarningForeground;
            if (release is not null && CanInstall &&
                RepositoryReviewPolicy.RequiresInstallConfirmation(release))
            {
                ActionHint += " 此版本未经仓库管理员审核，安装前会再次确认风险。";
            }
        }

        public RepositoryPlugin Plugin { get; }

        public RepositoryRelease? Release { get; }

        public PluginSnapshot? Installed { get; }

        public string Initial { get; }

        public string Name { get; }

        public string Metadata { get; }

        public string StatusText { get; }

        public IBrush StatusBackground { get; }

        public IBrush StatusForeground { get; }

        public string ReviewText { get; }

        public IBrush ReviewBackground { get; }

        public IBrush ReviewForeground { get; }

        public string ActionText { get; }

        public string ActionHint { get; }

        public bool CanInstall { get; }

        public bool Contains(string query) =>
            Plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Plugin.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Authors.Any(author => author.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));

        private static (string, IBrush, IBrush, string, string, bool) ResolveState(
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            if (release is null)
            {
                return (
                    "当前版本不兼容",
                    ErrorBackground,
                    ErrorForeground,
                    "不可安装",
                    "仓库中没有与当前 NyaLauncher 兼容的稳定版本。",
                    false);
            }
            if (installed is null)
            {
                return (
                    "可安装",
                    InfoBackground,
                    InfoForeground,
                    $"安装 {release.Version}",
                    "安装后默认保持禁用；启用时仍需确认必要能力。",
                    true);
            }
            if (string.Equals(installed.Version, release.Version, StringComparison.Ordinal))
            {
                return (
                    "已安装",
                    SuccessBackground,
                    SuccessForeground,
                    "已安装",
                    $"本地已安装相同版本 {installed.Version}。",
                    false);
            }
            if (SemanticVersion.TryParse(installed.Version, out var local) &&
                SemanticVersion.TryParse(release.Version, out var remote) &&
                local.CompareTo(remote) > 0)
            {
                return (
                    "本地版本更高",
                    WarningBackground,
                    WarningForeground,
                    "不会降级",
                    $"本地版本 {installed.Version} 高于仓库稳定版 {release.Version}。",
                    false);
            }
            if (installed.IsEnabled || installed.Status == PluginStatus.RestartRequired)
            {
                return (
                    "更新需先禁用",
                    WarningBackground,
                    WarningForeground,
                    "先禁用插件",
                    "正在运行的插件不会被覆盖。请在插件列表禁用；如提示重启，请重启后更新。",
                    false);
            }

            return (
                "有更新",
                InfoBackground,
                InfoForeground,
                $"更新到 {release.Version}",
                $"将整体替换本地包 {installed.Version}，插件私有数据与授权状态会保留。",
                true);
        }
    }
}
