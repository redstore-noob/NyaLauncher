using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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
    private bool _synchronizingVersionSelection;
    private readonly Dictionary<ComboBoxItem, RepositoryVersionChoice> _versionChoices = [];
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
                $"已从在线仓库读取 {_index.Plugins.Count} 个插件条目，" +
                $"当前显示 {_allItems.Count} 个；已隐藏的撤回插件仅对已安装用户显示。";
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
                .Select(plugin => new
                {
                    Plugin = plugin,
                    Installed = installed.FirstOrDefault(local => string.Equals(
                        local.Id,
                        plugin.Id,
                        StringComparison.OrdinalIgnoreCase))
                })
                .Where(item => RepositoryCatalogPolicy.ShouldDisplay(
                    item.Plugin,
                    item.Installed))
                .OrderBy(item => item.Plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new RepositoryListItem(
                    item.Plugin,
                    SelectDisplayRelease(
                        item.Plugin,
                        item.Installed,
                        _repositoryClient.GetLatestCompatibleRelease(item.Plugin)),
                    item.Installed))
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
            EmptyRepositoryTitle.Text = _index.Plugins.Count == 0
                ? "仓库暂时没有插件"
                : "当前没有公开可安装插件";
            EmptyRepositoryHint.Text = _index.Plugins.Count == 0
                ? "插件作者可在自己的仓库发布固定 Release ZIP，再创建收录 Issue"
                : "全部撤回或隐藏的插件不会占用商店列表；已安装用户仍会看到对应风险提示";
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
        RepositoryIdentityWarningBorder.IsVisible = item.WarningText is not null;
        RepositoryIdentityWarningText.Text = item.WarningText ?? string.Empty;
        RepositoryDetailsId.Text = item.Plugin.Id;
        RepositoryDetailsIdentity.Text = item.IdentityText;
        RepositoryDetailsAuthors.Text = item.Plugin.Authors.Count == 0
            ? "未提供"
            : string.Join("、", item.Plugin.Authors);
        RepositoryDetailsLicense.Text =
            $"{item.Plugin.License} · {string.Join("、", item.Plugin.Categories)}";
        PopulateVersionSelector(item);
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

    private static RepositoryRelease? SelectDisplayRelease(
        RepositoryPlugin plugin,
        PluginSnapshot? installed,
        RepositoryRelease? latestInstallable)
    {
        if (latestInstallable is not null || installed is null)
            return latestInstallable;

        var installedGeneration = installed.InstallOrigin?.Generation;
        return plugin.Releases.FirstOrDefault(release =>
                   release.Generation == installedGeneration &&
                   string.Equals(release.Version, installed.Version, StringComparison.Ordinal)) ??
               plugin.Releases
                   .Select(release => new
                   {
                       Release = release,
                       Version = SemanticVersion.TryParse(release.Version, out var version)
                           ? version
                           : default
                   })
                   .OrderByDescending(item => item.Release.Generation)
                   .ThenByDescending(item => item.Version)
                   .Select(item => item.Release)
                   .FirstOrDefault();
    }

    private void PopulateVersionSelector(RepositoryListItem item)
    {
        _synchronizingVersionSelection = true;
        try
        {
            _versionChoices.Clear();
            var controls = item.VersionChoices.Select(choice =>
            {
                var control = new ComboBoxItem
                {
                    Content = choice.DisplayText,
                    IsEnabled = choice.IsSelectable
                };
                ToolTip.SetTip(control, choice.Hint);
                _versionChoices.Add(control, choice);
                return control;
            }).ToArray();
            RepositoryVersionComboBox.ItemsSource = controls;
            RepositoryVersionComboBox.SelectedItem = controls.FirstOrDefault(control =>
                ReferenceEquals(_versionChoices[control].Release, item.Release));
            RepositoryVersionComboBox.IsEnabled = !_installing &&
                                                   !_confirmingInstall &&
                                                   item.VersionChoices.Count > 0;
            var available = item.VersionChoices.Count(choice => choice.IsSelectable);
            RepositoryDetailsVersionHint.Text = item.Release is null
                ? $"共 {item.VersionChoices.Count} 个历史版本，但当前没有可安装版本。"
                : $"已选择 {item.Release.Version}（{RepositoryVersionChoice.GetChannelName(item.Release)}）；" +
                  $"共 {item.VersionChoices.Count} 个历史版本，{available} 个与当前启动器兼容且未撤回。";
        }
        finally
        {
            _synchronizingVersionSelection = false;
        }
    }

    private void ShowEmptyDetails()
    {
        EmptyRepositoryDetails.IsVisible = true;
        RepositoryDetails.IsVisible = false;
    }

    private void OnRepositoryVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingVersionSelection ||
            RepositoryPluginList.SelectedItem is not RepositoryListItem item ||
            RepositoryVersionComboBox.SelectedItem is not ComboBoxItem control ||
            !_versionChoices.TryGetValue(control, out var choice) ||
            !choice.IsSelectable)
        {
            return;
        }

        item.SelectRelease(choice.Release);
        ShowDetails(item);
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

        if (item.IsDowngrade)
        {
            _confirmingInstall = true;
            InstallPluginButton.IsEnabled = false;
            RepositoryVersionComboBox.IsEnabled = false;
            bool confirmed;
            try
            {
                confirmed = await ConfirmDowngradeAsync(item);
            }
            finally
            {
                _confirmingInstall = false;
                InstallPluginButton.IsEnabled = item.CanInstall && !_installing;
                RepositoryVersionComboBox.IsEnabled = !_installing;
            }

            if (!confirmed)
            {
                RepositoryStatusText.Text =
                    $"已取消将 {item.Plugin.Name} 降级到 {item.Release.Version}；未开始下载插件包。";
                return;
            }
        }

        if (RepositoryReviewPolicy.RequiresInstallConfirmation(item.Release))
        {
            _confirmingInstall = true;
            InstallPluginButton.IsEnabled = false;
            RepositoryVersionComboBox.IsEnabled = false;
            bool confirmed;
            try
            {
                confirmed = await ConfirmUnreviewedInstallAsync(item);
            }
            finally
            {
                _confirmingInstall = false;
                InstallPluginButton.IsEnabled = item.CanInstall && !_installing;
                RepositoryVersionComboBox.IsEnabled = !_installing;
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
        RepositoryVersionComboBox.IsEnabled = false;
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
                _installCancellation.Token,
                confirmedDowngradeFromVersion: item.IsDowngrade
                    ? item.Installed?.Version
                    : null);
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

    private async Task<bool> ConfirmDowngradeAsync(RepositoryListItem item)
    {
        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 88,
            Padding = new Thickness(14, 7)
        };
        var downgradeButton = new Button
        {
            Content = "确认降级",
            MinWidth = 104,
            Padding = new Thickness(14, 7),
            Background = WarningBackground,
            Foreground = WarningForeground
        };
        var dialog = new Window
        {
            Title = "确认插件降级",
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
                            Text = "你正在选择一个历史版本",
                            FontSize = 18,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = WarningForeground
                        },
                        new TextBlock
                        {
                            Text =
                                $"{item.Plugin.Name}\n" +
                                $"当前版本：{item.Installed!.Version}\n" +
                                $"目标版本：{item.Release!.Version}",
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
                                    "降级会整体替换当前插件包。旧版本可能无法读取新版本已经写入的插件私有数据，" +
                                    "也可能重新引入已修复的问题。插件必须保持禁用才能继续。",
                                Foreground = WarningForeground,
                                TextWrapping = TextWrapping.Wrap
                            }
                        },
                        new TextBlock
                        {
                            Text = "确认后仍会执行固定 Release ZIP、大小与 SHA-256 校验。",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#AEB7D0")),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { cancelButton, downgradeButton }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        downgradeButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool?>(this) == true;
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

    private sealed class RepositoryVersionChoice
    {
        public RepositoryVersionChoice(RepositoryPlugin plugin, RepositoryRelease release)
        {
            Release = release;
            IsSelectable = release.Generation == plugin.Generation &&
                           RepositoryCatalogPolicy.IsCurrentGenerationInstallable(plugin) &&
                           !release.Yanked &&
                           PluginRepositoryClient.IsCompatible(release);
            var review = RepositoryReviewPolicy.RequiresInstallConfirmation(release)
                ? "未经审核"
                : "管理员已审核";
            var availability = release.Generation != plugin.Generation
                ? $" · 历史第 {release.Generation} 代"
                : release.Yanked
                ? " · 已撤回"
                : IsSelectable
                    ? string.Empty
                    : " · 与当前启动器不兼容";
            DisplayText = $"第 {release.Generation} 代 · {release.Version} · " +
                          $"{GetChannelName(release)} · {review}{availability}";
            Hint = release.Generation != plugin.Generation
                ? "这是不同发布者代际的只读历史，不会覆盖当前安装。"
                : release.Yanked
                ? $"此版本已撤回：{release.YankReason ?? "未提供原因"}"
                : IsSelectable
                    ? $"发布于 {release.PublishedAt}"
                    : "此版本与当前 NyaLauncher 或插件 API 不兼容。";
        }

        public RepositoryRelease Release { get; }

        public bool IsSelectable { get; }

        public string DisplayText { get; }

        public string Hint { get; }

        public static string GetChannelName(RepositoryRelease release) =>
            string.Equals(release.Channel, "preview", StringComparison.Ordinal)
                ? "预览版"
                : "稳定版";
    }

    private sealed class RepositoryListItem : INotifyPropertyChanged
    {
        public RepositoryListItem(
            RepositoryPlugin plugin,
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            Plugin = plugin;
            Installed = installed;
            VersionChoices = plugin.Releases
                .Select(candidate => new
                {
                    Choice = new RepositoryVersionChoice(plugin, candidate),
                    Version = SemanticVersion.TryParse(candidate.Version, out var version)
                        ? version
                        : default
                })
                .OrderByDescending(candidate => candidate.Version)
                .Select(candidate => candidate.Choice)
                .ToArray();
            Initial = string.IsNullOrWhiteSpace(plugin.Name)
                ? "P"
                : plugin.Name[..1].ToUpperInvariant();
            Name = plugin.Name;
            IdentityText = plugin.Publisher is null
                ? "旧版 v1 索引 · 未绑定 GitHub 数字发布者"
                : $"{plugin.LineageId}\n第 {plugin.Generation} 代 · " +
                  $"repository #{plugin.Publisher.RepositoryId} · owner #{plugin.Publisher.OwnerId}";
            SelectRelease(release, notify: false);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public RepositoryPlugin Plugin { get; }

        public RepositoryRelease? Release { get; private set; }

        public PluginSnapshot? Installed { get; }

        public IReadOnlyList<RepositoryVersionChoice> VersionChoices { get; }

        public string Initial { get; }

        public string Name { get; }

        public string IdentityText { get; }

        public string? WarningText { get; private set; }

        public string Metadata { get; private set; } = string.Empty;

        public string StatusText { get; private set; } = string.Empty;

        public IBrush StatusBackground { get; private set; } = InfoBackground;

        public IBrush StatusForeground { get; private set; } = InfoForeground;

        public string ReviewText { get; private set; } = string.Empty;

        public IBrush ReviewBackground { get; private set; } = InfoBackground;

        public IBrush ReviewForeground { get; private set; } = InfoForeground;

        public string ActionText { get; private set; } = string.Empty;

        public string ActionHint { get; private set; } = string.Empty;

        public bool CanInstall { get; private set; }

        public bool IsDowngrade { get; private set; }

        public void SelectRelease(RepositoryRelease? release, bool notify = true)
        {
            Release = release;
            Metadata = release is null
                ? $"第 {Plugin.Generation} 代 · {Plugin.Id}"
                : $"第 {release.Generation} 代 · {release.Version} · " +
                  (Plugin.Authors.FirstOrDefault() ?? Plugin.Id);
            (StatusText, StatusBackground, StatusForeground, ActionText, ActionHint, CanInstall) =
                ResolveState(Plugin, release, Installed);
            WarningText = ResolveWarning(Plugin, release, Installed);
            IsDowngrade = CanInstall && IsVersionDowngrade(release, Installed);
            if (release is null)
            {
                ReviewText = "无可用版本";
                ReviewBackground = ErrorBackground;
                ReviewForeground = ErrorForeground;
            }
            else
            {
                var reviewed = !RepositoryReviewPolicy.RequiresInstallConfirmation(release);
                ReviewText = reviewed ? "管理员已审核" : "未经审核";
                ReviewBackground = reviewed ? SuccessBackground : WarningBackground;
                ReviewForeground = reviewed ? SuccessForeground : WarningForeground;
                if (CanInstall && !reviewed)
                    ActionHint += " 此版本未经仓库管理员审核，安装前会再次确认风险。";
                if (CanInstall && string.Equals(release.Channel, "preview", StringComparison.Ordinal))
                    ActionHint += " 这是预览版本，稳定性可能低于正式版。";
            }

            if (!notify)
                return;
            foreach (var propertyName in new[]
                     {
                         nameof(Release), nameof(Metadata), nameof(StatusText),
                         nameof(StatusBackground), nameof(StatusForeground), nameof(ReviewText),
                          nameof(ReviewBackground), nameof(ReviewForeground), nameof(ActionText),
                          nameof(ActionHint), nameof(CanInstall), nameof(IsDowngrade),
                          nameof(WarningText)
                     })
            {
                OnPropertyChanged(propertyName);
            }
        }

        public bool Contains(string query) =>
            Plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Plugin.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Authors.Any(author => author.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));

        private static (string, IBrush, IBrush, string, string, bool) ResolveState(
            RepositoryPlugin plugin,
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            if (installed is not null && release is not null)
            {
                var identity = RepositoryIdentityPolicy.Compare(
                    plugin,
                    release,
                    installed.InstallOrigin);
                if (!RepositoryIdentityPolicy.IsSafeUpdate(identity) &&
                    release.Generation == plugin.Generation)
                {
                    return (
                        "发布身份已变更",
                        ErrorBackground,
                        ErrorForeground,
                        "必须先卸载旧插件",
                        IdentityMismatchHint(identity),
                        false);
                }
            }
            if (!RepositoryCatalogPolicy.IsCurrentGenerationInstallable(plugin))
            {
                var transferred = string.Equals(
                    plugin.LifecycleStatus,
                    "transferred",
                    StringComparison.Ordinal);
                return (
                    transferred ? "ID 转让中" : "已撤回 / 隐藏",
                    ErrorBackground,
                    ErrorForeground,
                    installed is null ? "不可安装" : "保留已安装副本",
                    transferred
                        ? "此 ID 正在转让，新一代尚未形成可安装发布；旧代不会被自动替换。"
                        : "当前代没有可安装版本。已安装副本仍保留用于显示风险，但不会获得自动更新。",
                    false);
            }
            if (release is null)
            {
                return (
                    "当前版本不兼容",
                    ErrorBackground,
                    ErrorForeground,
                    "不可安装",
                    "仓库中没有与当前 NyaLauncher 兼容且未撤回的可安装版本。",
                    false);
            }
            if (release.Generation != plugin.Generation || release.Yanked)
            {
                return (
                    release.Yanked ? "版本已撤回" : "历史发布代",
                    ErrorBackground,
                    ErrorForeground,
                    "不可安装",
                    release.Yanked
                        ? $"此版本已由仓库撤回：{release.YankReason ?? "未提供原因"}"
                        : "不同发布代际只保留审计历史，不能覆盖当前插件。",
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
            if (installed.IsEnabled || installed.Status == PluginStatus.RestartRequired)
            {
                var operation = IsVersionDowngrade(release, installed) ? "降级" : "更新";
                return (
                    $"{operation}需先禁用",
                    WarningBackground,
                    WarningForeground,
                    "先禁用插件",
                    "正在运行的插件不会被覆盖。请在插件列表禁用；如提示重启，请重启后更新。",
                    false);
            }

            if (IsVersionDowngrade(release, installed))
            {
                return (
                    "可选择历史版",
                    WarningBackground,
                    WarningForeground,
                    $"降级到 {release.Version}",
                    $"将用历史版本替换本地包 {installed.Version}；继续前会要求明确确认降级风险。",
                    true);
            }

            return (
                "有更新",
                InfoBackground,
                InfoForeground,
                $"更新到 {release.Version}",
                $"将整体替换本地包 {installed.Version}，插件私有数据与授权状态会保留。",
                true);
        }

        private static string? ResolveWarning(
            RepositoryPlugin plugin,
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            if (installed is null)
                return null;
            var warnings = new List<string>();
            if (release is not null)
            {
                var identity = RepositoryIdentityPolicy.Compare(
                    plugin,
                    release,
                    installed.InstallOrigin);
                if (!RepositoryIdentityPolicy.IsSafeUpdate(identity) &&
                    release.Generation == plugin.Generation)
                    warnings.Add("安全警告：" + IdentityMismatchHint(identity));
            }
            if (!RepositoryCatalogPolicy.HasCurrentNonYankedRelease(plugin) ||
                plugin.LifecycleStatus is "retired" or "transferred" ||
                string.Equals(plugin.Visibility, "hidden", StringComparison.Ordinal))
            {
                warnings.Add(
                    "撤回警告：该插件当前没有公开、未撤回的可安装版本。" +
                    "已安装副本仍可能在启动器进程中运行；请根据撤回原因决定是否立即禁用或卸载。");
            }
            if (!string.IsNullOrWhiteSpace(installed.InstallOriginWarning))
                warnings.Add(installed.InstallOriginWarning);
            return warnings.Count == 0 ? null : string.Join("\n\n", warnings);
        }

        private static string IdentityMismatchHint(RepositoryIdentityMatch match) => match switch
        {
            RepositoryIdentityMatch.MissingInstalledOrigin =>
                "已安装包没有可信来源快照，不能仅凭相同插件 ID 自动更新。请到插件列表点击“卸载插件”，再重新安装。",
            RepositoryIdentityMatch.LegacyV1NeedsReinstall =>
                "已安装包来自未绑定数字发布者的 v1 索引；v2 身份不能自动认领它。请到插件列表卸载后重装。",
            RepositoryIdentityMatch.DifferentGeneration =>
                "此 ID 已进入新的发布代际；这不是旧插件的正常更新。请卸载旧代后单独确认新代。",
            RepositoryIdentityMatch.DifferentLineage =>
                "此 ID 已分配给新的插件谱系。为防止供应链劫持，必须卸载旧插件后重新确认。",
            RepositoryIdentityMatch.DifferentPublisher =>
                "GitHub 数字仓库或发布者身份与已安装来源不同，已阻止自动替换。",
            RepositoryIdentityMatch.InvalidRepositoryHistory =>
                "仓库改名历史没有连续包含已安装来源，已阻止自动替换。请核对中心仓库记录。",
            _ => "插件发布身份不一致，已阻止自动替换。"
        };

        private static bool IsVersionDowngrade(
            RepositoryRelease? release,
            PluginSnapshot? installed) =>
            release is not null &&
            installed is not null &&
            SemanticVersion.TryParse(installed.Version, out var local) &&
            SemanticVersion.TryParse(release.Version, out var remote) &&
            local.CompareTo(remote) > 0;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
