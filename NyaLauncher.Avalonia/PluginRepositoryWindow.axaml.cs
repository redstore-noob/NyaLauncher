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
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia;

public partial class PluginRepositoryWindow : Window
{
    private static IBrush SuccessBackground => ThemeBrushes.BadgeBackground;
    private static IBrush SuccessForeground => ThemeBrushes.Success;
    private static IBrush InfoBackground => ThemeBrushes.BadgeBackground;
    private static IBrush InfoForeground => ThemeBrushes.Info;
    private static IBrush WarningBackground => ThemeBrushes.BadgeBackground;
    private static IBrush WarningForeground => ThemeBrushes.Warning;
    private static IBrush ErrorBackground => ThemeBrushes.ErrorDark;
    private static IBrush ErrorForeground => ThemeBrushes.Error;

    private PluginManager? _pluginManager;
    private PluginRepositoryClient? _repositoryClient;
    private PluginRepositoryIndex? _index;
    private IReadOnlyList<RepositoryListItem> _allItems = [];
    private string? _selectedPluginId;
    private string? _selectedReleaseVersion;
    private bool _loading;
    private bool _repositoryLoadFailed;
    private bool _installing;
    private bool _confirmingInstall;
    private bool _synchronizingSelection;
    private bool _synchronizingVersionSelection;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _installCancellation;

    public PluginRepositoryWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
        StyleAlter.ThemeChanged += OnThemeChanged;
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
        StyleAlter.ThemeChanged -= OnThemeChanged;
        if (_pluginManager is not null)
            _pluginManager.Changed -= OnInstalledCatalogChanged;
    }

    private void OnThemeChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnThemeChanged);
            return;
        }

        if (!_installing)
            RebuildItems();
    }

    private async Task LoadRepositoryAsync()
    {
        if (_loading || _repositoryClient is null)
            return;

        _loading = true;
        var initialLoad = _index is null && _allItems.Count == 0;
        RepositoryLoadingOverlay.IsVisible = initialLoad;
        RepositoryLoadingText.Text = initialLoad ? "正在读取远程索引…" : "正在刷新远程索引…";
        RefreshRepositoryButton.IsEnabled = false;
        RefreshRepositoryButton.Content = "刷新中…";
        EmptyRepositoryActionButton.IsVisible = false;
        RepositoryStatusText.Text = "正在读取 NyaLauncher-Plugins 远程索引…";
        try
        {
            _index = await _repositoryClient.LoadIndexAsync(_lifetimeCancellation.Token);
            _repositoryLoadFailed = false;
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
            _repositoryLoadFailed = true;
            RepositoryStatusText.Text = $"在线仓库读取失败：{exception.Message}";
            EmptyRepositoryTitle.Text = "无法读取在线仓库";
            EmptyRepositoryHint.Text = "请检查网络连接，然后重试；已读取的列表不会被清空。";
        }
        finally
        {
            _loading = false;
            RepositoryLoadingOverlay.IsVisible = false;
            RefreshRepositoryButton.IsEnabled = true;
            RefreshRepositoryButton.Content = "刷新索引";
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
            var rebuilt = _index.Plugins
                .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(plugin => new RepositoryListItem(
                    plugin,
                    _repositoryClient.GetLatestCompatibleRelease(plugin),
                    installed.FirstOrDefault(local => string.Equals(
                        local.Id,
                        plugin.Id,
                        StringComparison.OrdinalIgnoreCase))))
                .ToArray();
            var restored = rebuilt.FirstOrDefault(item => string.Equals(
                item.Plugin.Id,
                _selectedPluginId,
                StringComparison.OrdinalIgnoreCase));
            if (restored is not null && !string.IsNullOrWhiteSpace(_selectedReleaseVersion))
            {
                var release = restored.VersionChoices.FirstOrDefault(choice => string.Equals(
                    choice.Release.Version,
                    _selectedReleaseVersion,
                    StringComparison.Ordinal))?.Release;
                if (release is not null)
                    restored.SelectRelease(release);
            }
            _allItems = rebuilt;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (RepositoryPluginList is null)
            return;
        var query = RepositorySearchBox.Text?.Trim();
        var filterIndex = RepositoryStateFilter?.SelectedIndex ?? 0;
        var filtered = _allItems.Where(item =>
        {
            if (!string.IsNullOrWhiteSpace(query) && !item.Contains(query))
                return false;
            return filterIndex switch
            {
                1 => item.HasInstallAction,
                2 => item.Installed is not null,
                _ => true
            };
        }).ToArray();

        RepositoryClearSearchButton.IsVisible = !string.IsNullOrWhiteSpace(query);

        _synchronizingSelection = true;
        try
        {
            RepositoryPluginList.ItemsSource = filtered;
            var chosen = filtered.FirstOrDefault(item =>
                string.Equals(
                    item.Plugin.Id,
                    _selectedPluginId,
                    StringComparison.OrdinalIgnoreCase)) ?? filtered.FirstOrDefault();
            RepositoryPluginList.SelectedItem = chosen;
            _selectedPluginId = chosen?.Plugin.Id;
            if (chosen is not null)
                _selectedReleaseVersion = chosen.Release?.Version;
        }
        finally
        {
            _synchronizingSelection = false;
        }

        RepositoryCountText.Text = $"{filtered.Length} / {_allItems.Count}";
        EmptyRepositoryView.IsVisible = filtered.Length == 0 && !_loading;
        EmptyRepositoryActionButton.IsVisible = false;
        if (_repositoryLoadFailed && _allItems.Count == 0)
        {
            EmptyRepositoryTitle.Text = "无法读取在线仓库";
            EmptyRepositoryHint.Text = "请检查网络连接，然后重试。";
            EmptyRepositoryActionButton.Content = "重新读取";
            EmptyRepositoryActionButton.IsVisible = true;
        }
        else if (_allItems.Count == 0 && _index is not null)
        {
            EmptyRepositoryTitle.Text = "仓库暂时没有插件";
            EmptyRepositoryHint.Text = "插件作者可在自己的仓库发布固定 Release ZIP，再创建收录 Issue";
        }
        else if (_allItems.Count > 0 && filtered.Length == 0)
        {
            EmptyRepositoryTitle.Text = "没有匹配的插件";
            EmptyRepositoryHint.Text = "尝试清空搜索内容或切换筛选条件";
            EmptyRepositoryActionButton.Content = "清空筛选";
            EmptyRepositoryActionButton.IsVisible = true;
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

    private void OnRepositoryStateFilterChanged(object? sender, SelectionChangedEventArgs e) =>
        ApplyFilter();

    private void OnClearRepositorySearchClick(object? sender, RoutedEventArgs e)
    {
        RepositorySearchBox.Text = string.Empty;
        RepositorySearchBox.Focus();
    }

    private async void OnEmptyRepositoryActionClick(object? sender, RoutedEventArgs e)
    {
        if (_repositoryLoadFailed && _allItems.Count == 0)
        {
            await LoadRepositoryAsync();
            return;
        }

        RepositorySearchBox.Text = string.Empty;
        RepositoryStateFilter.SelectedIndex = 0;
        ApplyFilter();
    }

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
        _selectedReleaseVersion = item.Release?.Version;
        ShowDetails(item);
    }

    private void ShowDetails(RepositoryListItem item)
    {
        _selectedReleaseVersion = item.Release?.Version;
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
        PopulateVersionSelector(item);
        var release = item.Release;
        if (release is null)
        {
            RepositoryDetailsVersionTitle.Text = "仓库中没有可查看的版本";
            RepositoryDetailsChannelText.Text = "无版本";
            RepositoryDetailsReviewBadgeText.Text = "无审核记录";
            RepositoryDetailsAvailabilityText.Text = "不可安装";
            RepositoryDetailsReviewTitle.Text = "没有版本可供审核";
            RepositoryDetailsReview.Text = "—";
            RepositoryDetailsCapabilities.Text = "—";
            RepositoryDetailsHash.Text = "—";
            ApplyTone(RepositoryDetailsChannelText, null, InfoForeground);
            ApplyTone(
                RepositoryDetailsReviewBadgeText,
                RepositoryDetailsReviewBadge,
                WarningForeground);
            ApplyTone(
                RepositoryDetailsAvailabilityText,
                RepositoryDetailsAvailabilityBadge,
                ErrorForeground);
            ApplyTone(RepositoryDetailsReviewTitle, RepositoryReviewCard, WarningForeground);
        }
        else
        {
            var choice = item.VersionChoices.First(candidate => ReferenceEquals(
                candidate.Release,
                release));
            var reviewed = !RepositoryReviewPolicy.RequiresInstallConfirmation(release);
            var reviewTone = reviewed ? SuccessForeground : WarningForeground;
            var availabilityTone = release.Yanked || !choice.IsCompatible
                ? ErrorForeground
                : item.IsDowngrade || !item.CanInstall && !IsInstalledVersion(item)
                    ? WarningForeground
                    : item.CanInstall
                        ? InfoForeground
                        : SuccessForeground;

            RepositoryDetailsVersionTitle.Text =
                $"{release.Version} · 发布于 {release.PublishedAt}";
            RepositoryDetailsChannelText.Text = choice.ChannelName;
            RepositoryDetailsReviewBadgeText.Text = choice.ReviewLabel;
            RepositoryDetailsAvailabilityText.Text = release.Yanked
                ? "已撤回"
                : !choice.IsCompatible
                    ? "不兼容"
                    : item.ActionText;
            RepositoryDetailsReviewTitle.Text = reviewed
                ? "此版本有匹配的管理员审核记录"
                : "此版本没有有效的管理员审核记录";
            RepositoryDetailsReview.Text = CreateReviewDetailsText(release);
            RepositoryDetailsCapabilities.Text = CreateCapabilitiesText(release);
            RepositoryDetailsHash.Text =
                $"SHA-256  {release.Download.Sha256}\n" +
                $"大小  {FormatBytes(release.Download.Size)}";
            ApplyTone(RepositoryDetailsChannelText, null, choice.ChannelForeground);
            ApplyTone(
                RepositoryDetailsReviewBadgeText,
                RepositoryDetailsReviewBadge,
                reviewTone);
            ApplyTone(
                RepositoryDetailsAvailabilityText,
                RepositoryDetailsAvailabilityBadge,
                availabilityTone);
            ApplyTone(RepositoryDetailsReviewTitle, RepositoryReviewCard, reviewTone);
        }

        OpenReleaseNotesButton.IsEnabled = release is not null;
        InstallPluginButton.Content = item.ActionText;
        InstallPluginButton.IsEnabled = item.CanInstall && !_installing && !_confirmingInstall;
        InstallSelectionText.Text = release is null
            ? $"{item.Plugin.Name} · 未选择版本"
            : $"{item.Plugin.Name} · {release.Version}";
        InstallHintText.Text = item.ActionHint;
    }

    private static bool IsInstalledVersion(RepositoryListItem item) =>
        item.Release is not null &&
        item.Installed is not null &&
        string.Equals(item.Release.Version, item.Installed.Version, StringComparison.Ordinal);

    private static void ApplyTone(TextBlock text, Border? border, IBrush tone)
    {
        text.Foreground = tone;
        if (border is not null)
            border.BorderBrush = tone;
    }

    private void PopulateVersionSelector(RepositoryListItem item)
    {
        _synchronizingVersionSelection = true;
        try
        {
            RepositoryVersionComboBox.ItemsSource = item.VersionChoices;
            RepositoryVersionComboBox.SelectedItem = item.VersionChoices.FirstOrDefault(choice =>
                ReferenceEquals(choice.Release, item.Release));
            RepositoryVersionComboBox.IsEnabled = !_installing &&
                                                   !_confirmingInstall &&
                                                   item.VersionChoices.Count > 0;
            var available = item.VersionChoices.Count(choice => choice.CanInstall);
            var selectedChoice = item.VersionChoices.FirstOrDefault(choice => ReferenceEquals(
                choice.Release,
                item.Release));
            RepositoryDetailsVersionHint.Text = item.Release is null
                ? $"共 {item.VersionChoices.Count} 个历史版本，但当前没有版本可供查看。"
                : $"{selectedChoice?.Hint} 共 {item.VersionChoices.Count} 个版本，" +
                  $"其中 {available} 个与当前启动器兼容且未撤回。";
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
            RepositoryVersionComboBox.SelectedItem is not RepositoryVersionChoice choice)
        {
            return;
        }

        item.SelectRelease(choice.Release);
        _selectedReleaseVersion = choice.Release.Version;
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
                RepositoryVersionComboBox.IsEnabled = !_installing &&
                                                       item.VersionChoices.Count > 0;
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
                RepositoryVersionComboBox.IsEnabled = !_installing &&
                                                       item.VersionChoices.Count > 0;
            }

            if (!confirmed)
            {
                RepositoryStatusText.Text =
                    $"已取消安装 {item.Plugin.Name}；未开始下载插件包。";
                return;
            }
        }

        _installing = true;
        _selectedPluginId = item.Plugin.Id;
        _selectedReleaseVersion = item.Release.Version;
        _installCancellation = new CancellationTokenSource();
        CancelInstallButton.IsEnabled = true;
        InstallPluginButton.IsEnabled = false;
        RepositoryVersionComboBox.IsEnabled = false;
        CancelInstallButton.IsVisible = true;
        InstallProgressBar.IsVisible = true;
        InstallProgressBar.Value = 0;
        RefreshRepositoryButton.IsEnabled = false;
        SetBrowsingEnabled(false);
        InstallSelectionText.Text = $"正在安装 {item.Plugin.Name} · {item.Release.Version}";
        InstallHintText.Text = "正在下载并校验固定 Release 包，请不要关闭窗口。";
        RepositoryStatusText.Text = $"正在下载 {item.Plugin.Name} {item.Release.Version}…";
        var progress = new Progress<RepositoryDownloadProgress>(value =>
        {
            InstallProgressBar.Value = value.TotalBytes == 0
                ? 0
                : Math.Clamp((double)value.BytesReceived / value.TotalBytes, 0, 1);
            RepositoryStatusText.Text =
                $"正在下载 {item.Plugin.Name}：{FormatBytes(value.BytesReceived)} / " +
                FormatBytes(value.TotalBytes);
            InstallHintText.Text =
                $"已下载 {FormatBytes(value.BytesReceived)} / {FormatBytes(value.TotalBytes)}";
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
        catch (OperationCanceledException)
        {
            RepositoryStatusText.Text = $"已取消下载 {item.Plugin.Name} {item.Release.Version}。";
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
            SetBrowsingEnabled(true);
            RebuildItems();
        }
    }

    private void SetBrowsingEnabled(bool enabled)
    {
        RepositoryPluginList.IsEnabled = enabled;
        RepositorySearchBox.IsEnabled = enabled;
        RepositoryClearSearchButton.IsEnabled = enabled;
        RepositoryStateFilter.IsEnabled = enabled;
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
            Background = ThemeBrushes.DialogBackground,
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
                            Foreground = ThemeBrushes.SecondaryText
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
                            Foreground = ThemeBrushes.TertiaryText,
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
            Background = ThemeBrushes.DialogBackground,
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
                            Foreground = ThemeBrushes.SecondaryText
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
                            Foreground = ThemeBrushes.TertiaryText,
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
            return
                "未找到与此版本 SHA-256 匹配的有效管理员审核记录。" +
                "安装前请自行核对插件源仓库、Release 与发布者；下载校验只能证明内容与索引一致。";
        }

        var review = release.Review!;
        var notes = string.IsNullOrWhiteSpace(review.Notes)
            ? "未提供补充说明"
            : review.Notes.Trim();
        return
            $"审核记录与此版本 SHA-256 匹配。\n审核人：{review.ReviewedBy}\n" +
            $"审核时间：{review.ReviewedAt}\n说明：{notes}\n" +
            "审核记录不代表对插件安全性的保证。";
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
        public RepositoryVersionChoice(RepositoryRelease release, string? installedVersion)
        {
            Release = release;
            IsCompatible = PluginRepositoryClient.IsCompatible(release);
            CanInstall = !release.Yanked && IsCompatible;
            IsPreview = string.Equals(release.Channel, "preview", StringComparison.Ordinal);
            IsReviewed = !RepositoryReviewPolicy.RequiresInstallConfirmation(release);
            IsInstalled = string.Equals(
                release.Version,
                installedVersion,
                StringComparison.Ordinal);
            Hint = release.Yanked
                ? $"此版本已撤回：{release.YankReason ?? "未提供原因"}。仍可查看版本信息，但不能安装。"
                : !IsCompatible
                    ? "此版本与当前 NyaLauncher 或插件 API 不兼容。仍可查看版本信息，但不能安装。"
                    : "此版本与当前启动器兼容且未撤回。";
        }

        public RepositoryRelease Release { get; }

        public bool CanInstall { get; }

        public bool IsCompatible { get; }

        public bool IsPreview { get; }

        public bool IsReviewed { get; }

        public bool IsInstalled { get; }

        public string VersionLabel => Release.Version;

        public string ChannelName => GetChannelName(Release);

        public string PublishedLabel => $"发布于 {Release.PublishedAt}";

        public string ReviewLabel => IsReviewed ? "管理员已审核" : "未经审核";

        public string AvailabilityLabel => Release.Yanked
            ? "已撤回"
            : IsCompatible
                ? string.Empty
                : "不兼容";

        public bool HasAvailabilityWarning => Release.Yanked || !IsCompatible;

        public IBrush ChannelForeground => IsPreview ? WarningForeground : InfoForeground;

        public IBrush ReviewForeground => IsReviewed ? SuccessForeground : WarningForeground;

        public string Hint { get; }

        public static string GetChannelName(RepositoryRelease release) =>
            string.Equals(release.Channel, "preview", StringComparison.Ordinal)
                ? "预览版"
                : "稳定版";
    }

    private sealed class RepositoryListItem
    {
        public RepositoryListItem(
            RepositoryPlugin plugin,
            RepositoryRelease? cardRelease,
            PluginSnapshot? installed)
        {
            Plugin = plugin;
            CardRelease = cardRelease;
            Installed = installed;
            VersionChoices = plugin.Releases
                .Select(candidate => new
                {
                    Choice = new RepositoryVersionChoice(candidate, installed?.Version),
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
            var author = plugin.Authors.FirstOrDefault() ?? plugin.Id;
            Metadata = $"{VersionChoices.Count} 个版本 · {author}";
            (StatusText, StatusBackground, StatusForeground, HasInstallAction) =
                ResolveCardState(cardRelease, installed);
            SelectRelease(cardRelease ?? VersionChoices.FirstOrDefault()?.Release);
        }

        public RepositoryPlugin Plugin { get; }

        public RepositoryRelease? CardRelease { get; }

        public RepositoryRelease? Release { get; private set; }

        public PluginSnapshot? Installed { get; }

        public IReadOnlyList<RepositoryVersionChoice> VersionChoices { get; }

        public string Initial { get; }

        public string Name { get; }

        public string Metadata { get; }

        public string StatusText { get; }

        public IBrush StatusBackground { get; }

        public IBrush StatusForeground { get; }

        public bool HasInstallAction { get; }

        public string ActionText { get; private set; } = string.Empty;

        public string ActionHint { get; private set; } = string.Empty;

        public bool CanInstall { get; private set; }

        public bool IsDowngrade { get; private set; }

        public void SelectRelease(RepositoryRelease? release)
        {
            Release = release;
            (_, _, _, ActionText, ActionHint, CanInstall) =
                ResolveState(release, Installed);
            IsDowngrade = IsVersionDowngrade(release, Installed);
            if (release is not null)
            {
                var reviewed = !RepositoryReviewPolicy.RequiresInstallConfirmation(release);
                if (CanInstall && !reviewed)
                    ActionHint += " 此版本未经仓库管理员审核，安装前会再次确认风险。";
                if (CanInstall && string.Equals(release.Channel, "preview", StringComparison.Ordinal))
                    ActionHint += " 这是预览版本，稳定性可能低于正式版。";
            }
        }

        public bool Contains(string query) =>
            Plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Plugin.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Authors.Any(author => author.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));

        private static (string, IBrush, IBrush, bool) ResolveCardState(
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            if (release is null && installed is not null)
            {
                return (
                    "已安装 · 无可用更新",
                    SuccessBackground,
                    SuccessForeground,
                    false);
            }

            var state = ResolveState(release, installed);
            return (state.Item1, state.Item2, state.Item3, HasInstallOrUpdate(release, installed));
        }

        private static bool HasInstallOrUpdate(
            RepositoryRelease? release,
            PluginSnapshot? installed)
        {
            if (release is null)
                return false;
            if (installed is null)
                return true;
            if (SemanticVersion.TryParse(installed.Version, out var local) &&
                SemanticVersion.TryParse(release.Version, out var remote))
            {
                return remote.CompareTo(local) > 0;
            }

            return !string.Equals(installed.Version, release.Version, StringComparison.Ordinal);
        }

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
                    "仓库中没有与当前 NyaLauncher 兼容且未撤回的可安装版本。",
                    false);
            }
            if (release.Yanked)
            {
                return (
                    "版本已撤回",
                    ErrorBackground,
                    ErrorForeground,
                    "版本已撤回",
                    $"仓库已撤回此版本：{release.YankReason ?? "未提供原因"}。可以查看详情，但不能安装。",
                    false);
            }
            if (!PluginRepositoryClient.IsCompatible(release))
            {
                return (
                    "版本不兼容",
                    ErrorBackground,
                    ErrorForeground,
                    "版本不兼容",
                    "此版本与当前 NyaLauncher 或插件 API 不兼容。可以查看详情，但不能安装。",
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

        private static bool IsVersionDowngrade(
            RepositoryRelease? release,
            PluginSnapshot? installed) =>
            release is not null &&
            installed is not null &&
            SemanticVersion.TryParse(installed.Version, out var local) &&
            SemanticVersion.TryParse(release.Version, out var remote) &&
            local.CompareTo(remote) > 0;

    }
}
