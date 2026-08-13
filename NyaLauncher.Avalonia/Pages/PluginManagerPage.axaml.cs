using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Plugins;
using NyaLauncher.Plugin.Abstractions.Plugins;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>
/// Launcher-owned master/detail view over immutable plugin snapshots. Keeping
/// the list, diagnostics, and declarative settings editor in one control avoids
/// retaining plugin runtime objects or fragmenting a small workflow into many
/// page and view-model files.
/// </summary>
public partial class PluginManagerPage : UserControl
{
    private readonly Dictionary<string, SettingEditor> _settingEditors =
        new(StringComparer.OrdinalIgnoreCase);
    private PluginManager? _pluginManager;
    private PluginRepositoryClient? _repositoryClient;
    private PluginRepositoryWindow? _repositoryWindow;
    private IReadOnlyList<PluginListItem> _allItems = [];
    private string? _selectedPluginId;
    private bool _synchronizingSelection;
    private bool _refreshInProgress;
    private bool _isInitialized;

    public PluginManagerPage()
    {
        InitializeComponent();
        _isInitialized = true;
        ApplyCatalog(null);
    }

    internal PluginManagerPage(PluginManager pluginManager) : this()
    {
        Attach(pluginManager);
    }

    internal PluginManagerPage(
        PluginManager pluginManager,
        PluginRepositoryClient repositoryClient) : this()
    {
        Attach(pluginManager);
        _repositoryClient = repositoryClient ??
                            throw new ArgumentNullException(nameof(repositoryClient));
    }

    internal void Attach(PluginManager pluginManager)
    {
        ArgumentNullException.ThrowIfNull(pluginManager);
        if (ReferenceEquals(_pluginManager, pluginManager))
            return;
        if (_pluginManager is not null)
            _pluginManager.Changed -= OnCatalogChanged;

        _pluginManager = pluginManager;
        _pluginManager.Changed += OnCatalogChanged;
        ApplyCatalog(_pluginManager.Current);
    }

    public void Activate()
    {
        if (_pluginManager is null)
        {
            StatusText.Text = "插件宿主尚未连接。";
            return;
        }

        ApplyCatalog(_pluginManager.Current);
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_pluginManager is null || _refreshInProgress)
            return;

        _refreshInProgress = true;
        RefreshButton.IsEnabled = false;
        try
        {
            await _pluginManager.RefreshAsync();
            StatusText.Text = "插件目录已重新扫描。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"插件扫描失败：{exception.Message}";
        }
        finally
        {
            _refreshInProgress = false;
            RefreshButton.IsEnabled = !(_pluginManager?.Current.IsScanning ?? false);
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnCatalogChanged(sender, e));
            return;
        }

        ApplyCatalog(_pluginManager?.Current);
    }

    private void ApplyCatalog(PluginCatalogSnapshot? snapshot)
    {
        var directory = snapshot?.PackagesDirectory;
        PackagesDirectoryText.Text = string.IsNullOrWhiteSpace(directory)
            ? "插件宿主尚未连接"
            : directory;
        CatalogErrorBanner.IsVisible = !string.IsNullOrWhiteSpace(snapshot?.Error);
        CatalogErrorText.Text = snapshot?.Error ?? string.Empty;
        ScanningOverlay.IsVisible = snapshot?.IsScanning == true;
        RefreshButton.IsEnabled = snapshot?.IsScanning != true && !_refreshInProgress;

        _allItems = snapshot?.Plugins
            .Select(plugin => new PluginListItem(plugin))
            .ToArray() ?? [];
        ApplyFilter();

        if (snapshot is null)
            StatusText.Text = "插件宿主尚未连接。";
        else if (snapshot.IsScanning)
            StatusText.Text = "正在扫描插件目录…";
        else if (!string.IsNullOrWhiteSpace(snapshot.Error))
            StatusText.Text = $"插件目录读取失败：{snapshot.Error}";
        else
            StatusText.Text = $"已发现 {_allItems.Count} 个插件。";
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim();
        var filterIndex = StatusFilter.SelectedIndex;
        var filtered = _allItems.Where(item =>
        {
            if (!string.IsNullOrWhiteSpace(query) && !item.Contains(query))
                return false;
            return filterIndex switch
            {
                1 => item.IsEnabled,
                2 => !item.IsEnabled,
                3 => item.NeedsAttention,
                _ => true
            };
        }).ToArray();

        _synchronizingSelection = true;
        try
        {
            PluginList.ItemsSource = filtered;
            PluginList.SelectedItem = filtered.FirstOrDefault(item => string.Equals(
                item.Plugin.Id,
                _selectedPluginId,
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _synchronizingSelection = false;
        }

        PluginCountText.Text = $"{filtered.Length} / {_allItems.Count}";
        EmptyPluginListView.IsVisible = filtered.Length == 0 && !ScanningOverlay.IsVisible;
        EmptyPluginListTitle.Text = _allItems.Count == 0
            ? "插件目录为空"
            : "没有符合筛选条件的插件";
        EmptyPluginListHint.Text = _allItems.Count == 0
            ? "把完整插件文件夹放入上方目录后重新扫描"
            : "尝试清空搜索内容或切换状态筛选";

        if (PluginList.SelectedItem is PluginListItem selected)
            ShowDetails(selected);
        else
            ShowEmptyDetails();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInitialized)
            ApplyFilter();
    }

    private void OnStatusFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitialized)
            ApplyFilter();
    }

    private void OnPluginSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection)
            return;
        if (PluginList.SelectedItem is not PluginListItem item)
        {
            _selectedPluginId = null;
            ShowEmptyDetails();
            return;
        }

        _selectedPluginId = item.Plugin.Id;
        ShowDetails(item);
    }

    private async void OnPluginToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_pluginManager is null ||
            sender is not ToggleSwitch toggle ||
            toggle.DataContext is not PluginListItem item ||
            toggle.IsChecked is not { } requested ||
            requested == item.IsEnabled)
        {
            return;
        }

        toggle.IsEnabled = false;
        _selectedPluginId = item.Plugin.Id;
        StatusText.Text = requested
            ? $"正在启用 {item.Plugin.Name}…"
            : $"正在禁用 {item.Plugin.Name}…";
        try
        {
            var result = await _pluginManager.SetEnabledAsync(item.Plugin.Id, requested);
            if (requested &&
                result.RequiresApproval &&
                result.PendingCapabilities is { Count: > 0 } pendingCapabilities)
            {
                var approved = await ConfirmCapabilitiesAsync(item, pendingCapabilities);
                if (!approved)
                {
                    StatusText.Text = $"已取消启用 {item.Plugin.Name}；授权和插件状态均未更改。";
                    ApplyCatalog(_pluginManager.Current);
                    return;
                }

                StatusText.Text = $"授权已确认，正在启用 {item.Plugin.Name}…";
                result = await _pluginManager.SetEnabledAsync(
                    item.Plugin.Id,
                    enabled: true,
                    approvedCapabilities: pendingCapabilities);
            }

            StatusText.Text = result.Message;
            if (!result.Success)
                ApplyCatalog(_pluginManager.Current);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"插件状态修改失败：{exception.Message}";
            ApplyCatalog(_pluginManager.Current);
        }
    }

    private async Task<bool> ConfirmCapabilitiesAsync(
        PluginListItem item,
        IReadOnlyList<string> capabilities)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        var capabilityList = new StackPanel { Spacing = 8 };
        foreach (var capability in capabilities)
        {
            var (title, risk, description) = DescribeCapability(capability);
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#EEF1FF")
            });
            header.Children.Add(new TextBlock
            {
                Text = risk,
                FontSize = 10,
                Foreground = Brush.Parse("#FFB4A9"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(header);
            content.Children.Add(new TextBlock
            {
                Text = capability,
                FontFamily = "Consolas",
                FontSize = 10,
                Foreground = Brush.Parse("#AEB6FF")
            });
            content.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = Brush.Parse("#AAB2C9"),
                TextWrapping = TextWrapping.Wrap
            });
            capabilityList.Children.Add(new Border
            {
                Padding = new Thickness(12),
                Background = Brush.Parse("#1B2132"),
                BorderBrush = Brush.Parse("#30384F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = content
            });
        }

        var cancelButton = new Button { Content = "取消", Padding = new Thickness(16, 8) };
        var approveButton = new Button
        {
            Content = "同意并启用",
            Padding = new Thickness(16, 8),
            Background = Brush.Parse("#A5525E"),
            Foreground = Brushes.White
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(approveButton);

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = $"启用 {item.Plugin.Name} 前需要授权",
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#F6F7FF")
        });
        body.Children.Add(new TextBlock
        {
            Text = "下面是插件运行必需的能力。授权会被记录，取消则不会修改任何状态。",
            FontSize = 12,
            Foreground = Brush.Parse("#B7BED3"),
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new Border
        {
            Padding = new Thickness(12),
            Background = Brush.Parse("#3B282B"),
            BorderBrush = Brush.Parse("#75434B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = "重要：第三方插件代码会在启动器进程内执行。能力授权用于限制启动器提供的服务并记录你的同意，不是操作系统级安全沙箱。只启用你信任来源的插件。",
                FontSize = 11,
                Foreground = Brush.Parse("#FFD2CE"),
                TextWrapping = TextWrapping.Wrap
            }
        });
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 360,
            VerticalScrollBarVisibility =
                global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = capabilityList
        });
        body.Children.Add(actions);

        var dialog = new Window
        {
            Title = "插件能力授权",
            Width = 620,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 720,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brush.Parse("#111522"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body
        };
        cancelButton.Click += (_, _) => dialog.Close(false);
        approveButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool?>(owner) == true;
    }

    private static (string Title, string Risk, string Description) DescribeCapability(
        string capability) => capability switch
        {
            PluginCapabilities.Components =>
                ("自定义启动器组件", "界面扩展", "可向多边形工作区注册自定义组件及交互功能。"),
            PluginCapabilities.NativeUi =>
                ("原生界面访问", "预留能力", "v1 尚未提供原生控件宿主服务；声明此能力不会获得 Avalonia Control 注入接口。"),
            PluginCapabilities.NetworkHttp =>
                ("网络访问", "隐私风险", "可连接互联网服务并发送数据，请确认插件的服务和隐私说明。"),
            PluginCapabilities.SystemInformationRead =>
                ("读取系统信息", "隐私风险", "可读取启动器提供的设备或系统信息。"),
            PluginCapabilities.UserFilesRead =>
                ("读取用户文件", "高风险", "可通过启动器服务读取插件目录之外的用户文件。"),
            PluginCapabilities.UserFilesWrite =>
                ("修改用户文件", "高风险", "可通过启动器服务创建或修改插件目录之外的用户文件。"),
            PluginCapabilities.ProcessStart =>
                ("启动外部程序", "高风险", "可启动本机程序或命令，可能对系统和文件产生额外影响。"),
            PluginCapabilities.MinecraftInstanceRead =>
                ("读取 Minecraft 实例", "实例访问", "可读取所选 Minecraft 实例的目录、版本和文件内容。"),
            PluginCapabilities.MinecraftInstanceModify =>
                ("修改 Minecraft 实例", "高风险", "可通过事务接口写入或删除实例文件，例如资源和加载页文件。"),
            PluginCapabilities.MinecraftLaunchModify =>
                ("修改 Minecraft 启动过程", "高风险", "可改写类路径、主类、参数、环境变量和工作目录，用于实现独立的加载协议。"),
            _ => ("插件扩展能力", "需确认", "这是插件声明的必要能力；请仅在理解插件用途并信任其来源时授权。")
        };

    private async void OnManageCapabilitiesClick(object? sender, RoutedEventArgs e)
    {
        if (_pluginManager is null || string.IsNullOrWhiteSpace(_selectedPluginId))
            return;
        var item = _allItems.FirstOrDefault(candidate => string.Equals(
            candidate.Plugin.Id,
            _selectedPluginId,
            StringComparison.OrdinalIgnoreCase));
        if (item is null || item.Plugin.OptionalCapabilities.Count == 0)
            return;

        var selected = await SelectOptionalCapabilitiesAsync(item);
        if (selected is null)
            return;

        ManageCapabilitiesButton.IsEnabled = false;
        try
        {
            var result = await _pluginManager.SetOptionalCapabilitiesAsync(
                item.Plugin.Id,
                selected);
            StatusText.Text = result.Message;
            ApplyCatalog(_pluginManager.Current);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"可选授权修改失败：{exception.Message}";
        }
        finally
        {
            // ApplyCatalog may have selected a busy or restart-required snapshot.
            // Re-evaluate the current item instead of blindly enabling the button.
            var current = _allItems.FirstOrDefault(candidate => string.Equals(
                candidate.Plugin.Id,
                _selectedPluginId,
                StringComparison.OrdinalIgnoreCase));
            ManageCapabilitiesButton.IsEnabled = current is not null &&
                                                 !current.Plugin.IsBusy &&
                                                 current.Plugin.Status != PluginStatus.RestartRequired;
        }
    }

    private async Task<IReadOnlyList<string>?> SelectOptionalCapabilitiesAsync(
        PluginListItem item)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return null;

        var choices = new List<(string Capability, CheckBox CheckBox)>();
        var list = new StackPanel { Spacing = 8 };
        foreach (var capability in item.Plugin.OptionalCapabilities)
        {
            var (title, risk, description) = DescribeCapability(capability);
            var checkBox = new CheckBox
            {
                Content = $"{title} · {risk}",
                IsChecked = item.Plugin.GrantedCapabilities.Contains(
                    capability,
                    StringComparer.OrdinalIgnoreCase),
                Foreground = Brush.Parse("#EEF1FF")
            };
            choices.Add((capability, checkBox));
            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(checkBox);
            content.Children.Add(new TextBlock
            {
                Text = capability,
                FontFamily = "Consolas",
                FontSize = 10,
                Foreground = Brush.Parse("#AEB6FF")
            });
            content.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = Brush.Parse("#AAB2C9"),
                TextWrapping = TextWrapping.Wrap
            });
            list.Children.Add(new Border
            {
                Padding = new Thickness(12),
                Background = Brush.Parse("#1B2132"),
                BorderBrush = Brush.Parse("#30384F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = content
            });
        }

        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 8) };
        var save = new Button
        {
            Content = item.Plugin.IsEnabled ? "保存并重启插件" : "保存授权",
            Padding = new Thickness(16, 8),
            Background = Brush.Parse("#566DDE"),
            Foreground = Brushes.White
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = $"管理 {item.Plugin.Name} 的可选能力",
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#F6F7FF")
        });
        body.Children.Add(new TextBlock
        {
            Text = "未勾选的可选能力会被拒绝。已启用插件会先安全停止，再使用新授权重新启动。能力仅约束宿主 API，不是操作系统沙箱。",
            FontSize = 12,
            Foreground = Brush.Parse("#B7BED3"),
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 380,
            VerticalScrollBarVisibility =
                global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = list
        });
        body.Children.Add(actions);

        var dialog = new Window
        {
            Title = "插件可选能力",
            Width = 620,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 760,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brush.Parse("#111522"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body
        };
        cancel.Click += (_, _) => dialog.Close(null);
        save.Click += (_, _) => dialog.Close(choices
            .Where(choice => choice.CheckBox.IsChecked == true)
            .Select(choice => choice.Capability)
            .ToArray());
        return await dialog.ShowDialog<string[]?>(owner);
    }

    private void ShowDetails(PluginListItem item)
    {
        var plugin = item.Plugin;
        EmptyDetailsView.IsVisible = false;
        DetailsView.IsVisible = true;
        DetailsInitialText.Text = item.Initial;
        DetailsNameText.Text = plugin.Name;
        DetailsSummaryText.Text = item.Metadata;
        DetailsDescriptionText.Text = string.IsNullOrWhiteSpace(plugin.Description)
            ? "该插件没有提供说明。"
            : plugin.Description;
        DetailsIdText.Text = plugin.Id;
        DetailsVersionText.Text = plugin.Version;
        DetailsAuthorsText.Text = plugin.Authors.Count == 0
            ? "未提供"
            : string.Join("、", plugin.Authors);
        DetailsStatusText.Text = item.StatusText;
        DetailsCapabilitiesText.Text = plugin.Capabilities.Count == 0
            ? "未声明额外能力"
            : string.Join("、", plugin.Capabilities);
        ManageCapabilitiesButton.IsVisible = plugin.OptionalCapabilities.Count > 0;
        ManageCapabilitiesButton.IsEnabled = !plugin.IsBusy &&
            plugin.Status is not (PluginStatus.Invalid or PluginStatus.Incompatible or
                PluginStatus.RestartRequired);
        DetailsDirectoryText.Text = plugin.PackageDirectory;
        PluginErrorPanel.IsVisible = !string.IsNullOrWhiteSpace(plugin.Error);
        PluginErrorText.Text = plugin.Error ?? string.Empty;
        BuildSettingsEditor(plugin);
    }

    private void ShowEmptyDetails()
    {
        EmptyDetailsView.IsVisible = true;
        DetailsView.IsVisible = false;
        _settingEditors.Clear();
        SettingsEditorPanel.Children.Clear();
        SaveSettingsButton.IsEnabled = false;
        ManageCapabilitiesButton.IsVisible = false;
    }

    private void BuildSettingsEditor(PluginSnapshot plugin)
    {
        _settingEditors.Clear();
        SettingsEditorPanel.Children.Clear();

        var globalDefinitions = plugin.SettingDefinitions
            .Where(definition => definition.Scope == PluginSettingScope.Global)
            .ToArray();
        var canReadUserDirectories = plugin.GrantedCapabilities.Contains(
            PluginCapabilities.UserFilesRead,
            StringComparer.OrdinalIgnoreCase);
        var instanceSettingCount = plugin.SettingDefinitions.Count - globalDefinitions.Length;
        if (globalDefinitions.Length == 0)
        {
            SettingsEditorPanel.Children.Add(new TextBlock
            {
                Text = instanceSettingCount > 0
                    ? "此插件只有 Minecraft 实例级设置；当前版本尚未自动生成实例设置界面。"
                    : "此插件没有可配置项。",
                Foreground = Brush.Parse("#8F99B3"),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            SettingsHintText.Text = "没有需要在此页面保存的全局设置。";
            SaveSettingsButton.IsEnabled = false;
            return;
        }

        foreach (var definition in globalDefinitions)
        {
            plugin.Settings.TryGetValue(definition.Key, out var value);
            var editor = CreateSettingEditor(definition, value, canReadUserDirectories);
            _settingEditors[definition.Key] = editor;
            SettingsEditorPanel.Children.Add(editor.Container);
        }

        SettingsHintText.Text = instanceSettingCount > 0
            ? $"另有 {instanceSettingCount} 项实例设置，请在对应实例页面中配置。"
            : "设置由启动器验证并存储；禁用插件不会删除这些值。";
        SaveSettingsButton.IsEnabled = !plugin.IsBusy;
    }

    private SettingEditor CreateSettingEditor(
        PluginSettingDefinition definition,
        string? currentValue,
        bool canReadUserDirectories)
    {
        var title = new TextBlock
        {
            Text = definition.Required ? $"{definition.Title} *" : definition.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#DDE2F4")
        };
        var descriptionText = CreateSettingDescription(definition, canReadUserDirectories);
        var description = new TextBlock
        {
            Text = descriptionText,
            FontSize = 10,
            Foreground = definition.Kind == PluginSettingKind.Directory
                ? Brush.Parse("#D9B978")
                : Brush.Parse("#7E88A4"),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = !string.IsNullOrWhiteSpace(descriptionText)
        };

        Control valueControl;
        Control editorContent;
        switch (definition.Kind)
        {
            case PluginSettingKind.Boolean:
                valueControl = new ToggleSwitch
                {
                    IsChecked = bool.TryParse(currentValue, out var enabled) && enabled
                };
                editorContent = valueControl;
                break;

            case PluginSettingKind.Choice:
                var comboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var option in definition.Options)
                {
                    var item = new ComboBoxItem
                    {
                        Content = option.Label,
                        Tag = option.Value
                    };
                    if (!string.IsNullOrWhiteSpace(option.Description))
                        ToolTip.SetTip(item, option.Description);
                    comboBox.Items.Add(item);
                    if (string.Equals(option.Value, currentValue, StringComparison.Ordinal))
                        comboBox.SelectedItem = item;
                }
                valueControl = comboBox;
                editorContent = valueControl;
                break;

            case PluginSettingKind.Integer when HasNumericSliderRange(definition):
            case PluginSettingKind.Number when HasNumericSliderRange(definition):
                var minimum = definition.Minimum.GetValueOrDefault();
                var maximum = definition.Maximum.GetValueOrDefault();
                var fallback = minimum;
                var numericValue = double.TryParse(
                    currentValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedNumeric)
                    ? parsedNumeric
                    : fallback;
                var numericSlider = new Slider
                {
                    Minimum = minimum,
                    Maximum = maximum,
                    Value = Math.Clamp(numericValue, minimum, maximum),
                    TickFrequency = definition.Step is > 0 ? definition.Step.Value : 1,
                    IsSnapToTickEnabled = definition.Step is > 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var numericValueText = new TextBlock
                {
                    Text = FormatNumericSettingValue(numericSlider.Value, definition.Kind),
                    Foreground = Brush.Parse("#B8C3E5"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    MinWidth = 54
                };
                numericSlider.ValueChanged += (_, _) =>
                    numericValueText.Text = FormatNumericSettingValue(
                        numericSlider.Value,
                        definition.Kind);
                var numericEditor = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Children = { numericSlider, numericValueText }
                };
                Grid.SetColumn(numericValueText, 1);
                valueControl = numericSlider;
                editorContent = numericEditor;
                break;

            case PluginSettingKind.File:
            case PluginSettingKind.Directory:
                var pathBox = new TextBox
                {
                    Text = currentValue,
                    PlaceholderText = definition.Placeholder ??
                        (definition.Kind == PluginSettingKind.File
                            ? "请选择要导入的文件"
                            : "请选择授权给插件读取的目录"),
                    IsReadOnly = true
                };
                var browseButton = new Button
                {
                    Content = definition.Kind == PluginSettingKind.File ? "选择文件…" : "选择目录…",
                    Padding = new Thickness(12, 7),
                    Margin = new Thickness(8, 0, 0, 0),
                    IsEnabled = definition.Kind != PluginSettingKind.Directory ||
                                canReadUserDirectories
                };
                browseButton.Click += async (_, _) =>
                    await BrowseSettingPathAsync(definition, pathBox);

                var pathEditor = new Grid
                {
                    ColumnDefinitions = definition.Required
                        ? new ColumnDefinitions("*,Auto")
                        : new ColumnDefinitions("*,Auto,Auto")
                };
                pathEditor.Children.Add(pathBox);
                Grid.SetColumn(browseButton, 1);
                pathEditor.Children.Add(browseButton);
                if (!definition.Required)
                {
                    var clearButton = new Button
                    {
                        Content = "清除",
                        Padding = new Thickness(10, 7),
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    clearButton.Click += (_, _) => pathBox.Text = string.Empty;
                    Grid.SetColumn(clearButton, 2);
                    pathEditor.Children.Add(clearButton);
                }

                valueControl = pathBox;
                editorContent = pathEditor;
                break;

            default:
                var textBox = new TextBox
                {
                    Text = currentValue,
                    PlaceholderText = definition.Placeholder,
                    AcceptsReturn = definition.Kind == PluginSettingKind.MultilineText,
                    MinHeight = definition.Kind == PluginSettingKind.MultilineText ? 88 : 0,
                    MaxLength = definition.MaximumLength is > 0
                        ? definition.MaximumLength.Value
                        : 0
                };
                if (definition.Kind == PluginSettingKind.Secret)
                    textBox.PasswordChar = '●';
                valueControl = textBox;
                editorContent = valueControl;
                break;
        }

        var container = new StackPanel { Spacing = 6 };
        container.Children.Add(title);
        container.Children.Add(editorContent);
        container.Children.Add(description);
        return new SettingEditor(definition, valueControl, container);
    }

    private static string CreateSettingDescription(
        PluginSettingDefinition definition,
        bool canReadUserDirectories)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(definition.Description))
            parts.Add(definition.Description);
        if (definition.Minimum is not null || definition.Maximum is not null)
        {
            parts.Add($"允许范围：{definition.Minimum?.ToString() ?? "不限"} ～ " +
                      $"{definition.Maximum?.ToString() ?? "不限"}");
        }
        if (definition.Kind is PluginSettingKind.File && definition.FileExtensions.Count > 0)
            parts.Add($"允许类型：{string.Join("、", definition.FileExtensions)}");
        if (definition.Kind == PluginSettingKind.File)
        {
            parts.Add("保存时由启动器复制到插件私有数据目录（最大 512 MiB），插件获得的是可由 Context.Storage.GetDataPath 解析的相对路径");
        }
        if (definition.Kind == PluginSettingKind.Directory)
        {
            parts.Add(canReadUserDirectories
                ? "目录不会被复制；保存绝对路径表示你明确授权此插件持续读取该目录"
                : $"插件未声明 {PluginCapabilities.UserFilesRead} 能力，目录选择已禁用");
        }
        if (definition.Kind == PluginSettingKind.Secret)
            parts.Add("当前版本仅在界面中遮蔽显示，值仍保存在插件私有 settings.json 中，不是系统密钥库");
        return string.Join(" · ", parts);
    }

    private async Task BrowseSettingPathAsync(
        PluginSettingDefinition definition,
        TextBox target)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            SettingsHintText.Text = "当前平台不支持本地路径选择。";
            return;
        }

        try
        {
            string? selectedPath;
            if (definition.Kind == PluginSettingKind.File)
            {
                var options = new FilePickerOpenOptions
                {
                    Title = $"选择“{definition.Title}”文件",
                    AllowMultiple = false
                };
                if (definition.FileExtensions.Count > 0)
                {
                    options.FileTypeFilter =
                    [
                        new FilePickerFileType($"{definition.Title} 文件")
                        {
                            Patterns = definition.FileExtensions
                                .Select(extension => $"*{extension}")
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                        }
                    ];
                }

                var files = await storageProvider.OpenFilePickerAsync(options);
                selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
            }
            else
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = $"选择授权给“{definition.Title}”的目录",
                    AllowMultiple = false
                });
                selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            target.Text = selectedPath;
            SettingsHintText.Text = definition.Kind == PluginSettingKind.File
                ? "文件将在保存时复制进插件私有数据目录，原文件不会被修改。"
                : "该目录不会被复制；保存即表示允许插件持续读取此绝对路径。";
        }
        catch (Exception exception)
        {
            SettingsHintText.Text = $"路径选择失败：{exception.Message}";
            StatusText.Text = SettingsHintText.Text;
        }
    }

    private async void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_pluginManager is null || string.IsNullOrWhiteSpace(_selectedPluginId))
            return;

        SaveSettingsButton.IsEnabled = false;
        var pluginId = _selectedPluginId;
        var values = _settingEditors.ToDictionary(
            pair => pair.Key,
            pair => ReadSettingValue(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        try
        {
            // Large files are streamed and validated by the settings store; keep
            // that disk work off the UI thread.
            var result = await Task.Run(() =>
                _pluginManager.SaveSettingsAsync(pluginId, values));
            if (string.Equals(_selectedPluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                SettingsHintText.Text = result.Message;
            StatusText.Text = result.Message;
        }
        catch (Exception exception)
        {
            var message = $"设置保存失败：{exception.Message}";
            if (string.Equals(_selectedPluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                SettingsHintText.Text = message;
            StatusText.Text = message;
        }
        finally
        {
            if (string.Equals(_selectedPluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                SaveSettingsButton.IsEnabled = true;
        }
    }

    private static string? ReadSettingValue(SettingEditor editor) => editor.Control switch
    {
        ToggleSwitch toggle => toggle.IsChecked == true ? "true" : "false",
        ComboBox comboBox when comboBox.SelectedItem is ComboBoxItem item => item.Tag?.ToString(),
        Slider slider when editor.Definition.Kind == PluginSettingKind.Integer =>
            Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture),
        Slider slider => slider.Value.ToString("0.################", CultureInfo.InvariantCulture),
        TextBox textBox => textBox.Text,
        _ => null
    };

    private static string FormatNumericSettingValue(
        double value,
        PluginSettingKind kind) =>
        kind == PluginSettingKind.Integer
            ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool HasNumericSliderRange(PluginSettingDefinition definition) =>
        definition.Minimum is not null &&
        definition.Maximum is not null &&
        double.IsFinite(definition.Minimum.Value) &&
        double.IsFinite(definition.Maximum.Value) &&
        definition.Maximum.Value > definition.Minimum.Value;

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnOpenRepositoryClick(object? sender, RoutedEventArgs e)
    {
        if (_pluginManager is null || _repositoryClient is null)
        {
            StatusText.Text = "在线插件仓库尚未连接。";
            return;
        }

        if (_repositoryWindow is not null)
        {
            _repositoryWindow.Activate();
            return;
        }

        _repositoryWindow = new PluginRepositoryWindow(_pluginManager, _repositoryClient);
        _repositoryWindow.Closed += (_, _) => _repositoryWindow = null;
        if (TopLevel.GetTopLevel(this) is Window owner)
            _repositoryWindow.Show(owner);
        else
            _repositoryWindow.Show();
    }

    private void OnOpenPackagesDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var path = _pluginManager?.Current.PackagesDirectory;
        OpenDirectory(path, create: true);
    }

    private void OnOpenSelectedPluginDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var selected = _allItems.FirstOrDefault(item => string.Equals(
            item.Plugin.Id,
            _selectedPluginId,
            StringComparison.OrdinalIgnoreCase));
        OpenDirectory(selected?.Plugin.PackageDirectory, create: false);
    }

    private void OpenDirectory(string? path, bool create)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "插件目录尚不可用。";
            return;
        }

        try
        {
            if (create)
                Directory.CreateDirectory(path);
            if (!Directory.Exists(path))
            {
                StatusText.Text = $"目录不存在：{path}";
                return;
            }

            var startInfo = OperatingSystem.IsWindows()
                ? CreateShellStartInfo("explorer.exe", path)
                : OperatingSystem.IsMacOS()
                    ? CreateShellStartInfo("open", path)
                    : CreateShellStartInfo("xdg-open", path);
            Process.Start(startInfo);
            StatusText.Text = $"已打开：{path}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"打开插件目录失败：{exception.Message}";
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string fileName, string argument)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
        startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private sealed record SettingEditor(
        PluginSettingDefinition Definition,
        Control Control,
        StackPanel Container);

    private sealed class PluginListItem
    {
        private static readonly IBrush SuccessBackground = Brush.Parse("#264437");
        private static readonly IBrush SuccessForeground = Brush.Parse("#8BE0B5");
        private static readonly IBrush WarningBackground = Brush.Parse("#443B25");
        private static readonly IBrush WarningForeground = Brush.Parse("#E8D59A");
        private static readonly IBrush ErrorBackground = Brush.Parse("#4A2731");
        private static readonly IBrush ErrorForeground = Brush.Parse("#F0A8B6");
        private static readonly IBrush MutedBackground = Brush.Parse("#30364A");
        private static readonly IBrush MutedForeground = Brush.Parse("#AAB2C9");

        public PluginListItem(PluginSnapshot plugin)
        {
            Plugin = plugin;
            Initial = string.IsNullOrWhiteSpace(plugin.Name)
                ? "?"
                : plugin.Name[..1].ToUpperInvariant();
            Name = string.IsNullOrWhiteSpace(plugin.Name) ? plugin.Id : plugin.Name;
            var author = plugin.Authors.FirstOrDefault();
            Metadata = string.IsNullOrWhiteSpace(author)
                ? $"{plugin.Version} · {plugin.Id}"
                : $"{plugin.Version} · {author}";
            StatusText = GetStatusText(plugin.Status);
            (StatusBackground, StatusForeground) = GetStatusBrushes(plugin.Status);
        }

        public PluginSnapshot Plugin { get; }

        public string Initial { get; }

        public string Name { get; }

        public string Metadata { get; }

        public string StatusText { get; }

        public IBrush StatusBackground { get; }

        public IBrush StatusForeground { get; }

        public bool IsEnabled => Plugin.IsEnabled;

        public bool CanToggle => !Plugin.IsBusy && Plugin.Status is not (
            PluginStatus.Invalid or
            PluginStatus.Incompatible or
            PluginStatus.RestartRequired);

        public bool NeedsAttention => !string.IsNullOrWhiteSpace(Plugin.Error) ||
                                      Plugin.Status is PluginStatus.Invalid or
                                          PluginStatus.Incompatible or
                                          PluginStatus.Failed or
                                          PluginStatus.RestartRequired;

        public bool Contains(string query) =>
            Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Plugin.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Plugin.Authors.Any(author => author.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));

        private static string GetStatusText(PluginStatus status) => status switch
        {
            PluginStatus.Disabled => "已禁用",
            PluginStatus.Enabling => "正在启用",
            PluginStatus.Enabled => "运行中",
            PluginStatus.Disabling => "正在禁用",
            PluginStatus.Invalid => "清单无效",
            PluginStatus.Incompatible => "不兼容",
            PluginStatus.Failed => "加载失败",
            PluginStatus.RestartRequired => "需要重启",
            _ => status.ToString()
        };

        private static (IBrush Background, IBrush Foreground) GetStatusBrushes(
            PluginStatus status) => status switch
            {
                PluginStatus.Enabled => (SuccessBackground, SuccessForeground),
                PluginStatus.Enabling or PluginStatus.Disabling or PluginStatus.RestartRequired =>
                    (WarningBackground, WarningForeground),
                PluginStatus.Invalid or PluginStatus.Incompatible or PluginStatus.Failed =>
                    (ErrorBackground, ErrorForeground),
                _ => (MutedBackground, MutedForeground)
            };
    }
}
