using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Markup.Xaml.MarkupExtensions;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Windows;

/// <summary>
/// 个性化窗口：让用户重命名功能区、自定义简介与图标、挑选每个区域显示哪些按钮，
/// 调整全局组件缩放，以及新建/删除自定义区域与切换配置目录。
/// <para>
/// 本控件以 <see cref="UserControl"/> 形式嵌入主窗口的覆盖层，不是独立 <see cref="Window"/>。
/// 编辑过程全部在内部的草稿状态里进行，<b>只有点击保存</b>才会把结果通过
/// <see cref="Saved"/> 交回宿主真正落盘。
/// </para>
/// <para>
/// 所有颜色一律经 <see cref="DynamicResourceExtension"/> 绑定主题资源键，
/// 主题切换时界面实时跟随（禁止快照画刷）。
/// </para>
/// </summary>
public partial class PersonalizationWindow : UserControl
{
    /// <summary>内置简约图标预设，供用户直接点选；本地图片失效时也会回退到这里。</summary>
    private static readonly string[] PresetGlyphs =
    [
        "material:Apps",
        "material:Play",
        "material:Diamond",
        "material:Star",
        "material:Cog",
        "material:Menu",
        "material:Add",
        "material:Home",
        "material:ViewDashboard",
        "material:Circle"
    ];
    private FeatureAreaRegistry _registry = null!;
    private readonly List<AreaEditorState> _editors = [];
    private readonly HashSet<string> _draftUserAreaIds = new(StringComparer.OrdinalIgnoreCase);
    private string _storageDirectory = WorkspaceProfileStore.PlatformDefaultDirectory;

    /// <summary>
    /// 用户点击「恢复默认」后置位：保存时连同工作区布局 / 侧边栏 / 组件摆放一起恢复出厂，
    /// 而不是把当前布局原样写回。取消或外部重载会清除该标志。
    /// </summary>
    private bool _resetLayoutPending;

    /// <summary>
    /// 用户点击「保存」时触发，携带完整的个性化结果。
    /// 宿主应据此调用注册表应用偏好并持久化。
    /// </summary>
    public event EventHandler<PersonalizationResult>? Saved;

    /// <summary>用户点击「取消」或关闭窗口时触发，不携带任何修改。</summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// 仅供 XAML 设计器使用的无参构造。
    /// 运行时请使用带参构造——不注入注册表会导致后续操作空引用。
    /// </summary>
    public PersonalizationWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 创建并初始化个性化窗口：绑定功能区注册表、记录配置目录、载入当前档案构建编辑区。
    /// </summary>
    /// <param name="registry">主窗口的功能区注册表，编辑结果将作用到它上面。</param>
    /// <param name="storageDirectory">当前配置目录，显示在界面上并可由用户修改。</param>
    public PersonalizationWindow(
        FeatureAreaRegistry registry,
        string storageDirectory) : this()
    {
        _registry = registry;
        SetStorageDirectory(storageDirectory);
        _draftUserAreaIds.UnionWith(_registry.UserAreaIds);
        BuildEditors(_registry.CreateCurrentProfile());
    }

    private async void OnBrowseStorageDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择个性化配置目录",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            SetStorageDirectory(path);
    }

    private void OnUseDefaultStorageDirectoryClick(object? sender, RoutedEventArgs e)
    {
        SetStorageDirectory(WorkspaceProfileStore.PlatformDefaultDirectory);
    }

    private void SetStorageDirectory(string directory)
    {
        _storageDirectory = System.IO.Path.GetFullPath(directory);
        StorageDirectoryText.Text = _storageDirectory;
        ToolTip.SetTip(StorageDirectoryText, _storageDirectory);
    }

    /// <summary>
    /// 以 DynamicResource 方式绑定画刷属性：主题切换时自动跟随，不做任何快照。
    /// </summary>
    private static void BindBrush(
        AvaloniaObject control,
        AvaloniaProperty property,
        string resourceKey)
        => control[!property] = new DynamicResourceExtension(resourceKey);

    private void BuildEditors(WorkspaceProfile profile)
    {
        ComponentScaleSlider.Value = Math.Clamp(
            profile.GlobalComponentScale,
            FeatureAreaRegistry.MinimumComponentScale,
            FeatureAreaRegistry.MaximumComponentScale);
        UpdateComponentScaleText();
        AreaEditors.Children.Clear();
        _editors.Clear();

        var preferences = profile.Areas.ToDictionary(
            preference => preference.AreaId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var sourceArea in _registry.SourceAreas)
        {
            preferences.TryGetValue(sourceArea.Id, out var preference);
            AddAreaEditor(sourceArea, preference);
        }
    }

    private void OnComponentScaleChanged(
        object? sender,
        RangeBaseValueChangedEventArgs e)
    {
        UpdateComponentScaleText();
    }

    private void UpdateComponentScaleText()
    {
        ComponentScaleText.Text = $"{ComponentScaleSlider.Value * 100:0}%";
    }

    private void AddAreaEditor(
        FeatureAreaDefinition sourceArea,
        FeatureAreaPreference? preference)
    {
        var nameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(preference?.DisplayName)
                ? sourceArea.Title
                : preference.DisplayName,
            PlaceholderText = sourceArea.Title,
            FontSize = 14,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        BindBrush(nameBox, TextBox.BackgroundProperty, "SurfaceBgBrush");
        BindBrush(nameBox, TextBox.BorderBrushProperty, "MediumBorderBrush");

        var descriptionBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(preference?.Description)
                ? sourceArea.Subtitle
                : preference.Description,
            PlaceholderText = sourceArea.Subtitle,
            FontSize = 14,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        BindBrush(descriptionBox, TextBox.BackgroundProperty, "SurfaceBgBrush");
        BindBrush(descriptionBox, TextBox.BorderBrushProperty, "MediumBorderBrush");

        var iconState = new IconEditorState(
            string.IsNullOrWhiteSpace(preference?.IconGlyph) ? sourceArea.Glyph : preference.IconGlyph,
            string.IsNullOrWhiteSpace(preference?.IconPath) ? sourceArea.IconPath : preference.IconPath);
        var iconEditor = CreateIconEditor(iconState);
        var isUserArea = _draftUserAreaIds.Contains(sourceArea.Id);

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    CreateAreaHeader(
                        sourceArea,
                        nameBox,
                        descriptionBox,
                        iconState,
                        isUserArea ? () => RemoveAreaEditor(sourceArea.Id) : null),
                    iconEditor,
                    new Border
                    {
                        Padding = new Thickness(12, 10),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Child = new TextBlock
                        {
                            Text = "组件通过工作区底栏的“组件库”管理，也可直接在功能区之间拖动。",
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        };
        BindBrush(card, Border.BackgroundProperty, "CardBg2Brush");
        BindBrush(card, Border.BorderBrushProperty, "CardBorderBrush");
        if (card.Child is StackPanel panel && panel.Children.Count > 1 &&
            panel.Children[1] is Border hintBorder &&
            hintBorder.Child is TextBlock hintText)
        {
            BindBrush(hintBorder, Border.BackgroundProperty, "SurfaceBgBrush");
            BindBrush(hintBorder, Border.BorderBrushProperty, "CardBorderBrush");
            BindBrush(hintText, TextBlock.ForegroundProperty, "MutedTextBrush");
        }

        AreaEditors.Children.Add(card);
        _editors.Add(new AreaEditorState(
            sourceArea.Id,
            sourceArea.Title,
            sourceArea.Subtitle,
            isUserArea,
            card,
            nameBox,
            descriptionBox,
            iconState));
    }

    private static Control CreateAreaHeader(
        FeatureAreaDefinition area,
        TextBox nameBox,
        TextBox descriptionBox,
        IconEditorState iconState,
        Action? deleteArea)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,180,*,Auto")
        };

        var glyph = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(13),
            ClipToBounds = true
        };
        BindBrush(glyph, Border.BackgroundProperty, "IconBoxBgBrush");
        iconState.RegisterPreview(glyph);

        var identity = new StackPanel
        {
            Margin = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = deleteArea is not null
                        ? $"自定义功能区 · {area.Id}"
                        : $"内置功能区 · {area.Id}",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = area.Subtitle,
                    FontSize = 10
                }
            }
        };
        BindBrush(identity.Children[0], TextBlock.ForegroundProperty, "MutedTextBrush");
        BindBrush(identity.Children[1], TextBlock.ForegroundProperty, "MutedTextBrush");
        Grid.SetColumn(identity, 1);

        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*")
        };

        var namePanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 6, 0),
            Spacing = 5,
            Children =
            {
                new TextBlock
                {
                    Text = "显示名称",
                    FontSize = 10
                },
                nameBox
            }
        };

        var descriptionPanel = new StackPanel
        {
            Margin = new Thickness(6, 0, 0, 0),
            Spacing = 5,
            Children =
            {
                new TextBlock
                {
                    Text = "功能区简介",
                    FontSize = 10
                },
                descriptionBox
            }
        };
        BindBrush(namePanel.Children[0], TextBlock.ForegroundProperty, "MutedTextBrush");
        BindBrush(descriptionPanel.Children[0], TextBlock.ForegroundProperty, "MutedTextBrush");
        Grid.SetColumn(descriptionPanel, 1);
        fields.Children.Add(namePanel);
        fields.Children.Add(descriptionPanel);
        Grid.SetColumn(fields, 2);

        grid.Children.Add(glyph);
        grid.Children.Add(identity);
        grid.Children.Add(fields);

        if (deleteArea is not null)
        {
            var deleteButton = new Button
            {
                Content = "删除功能区",
                Margin = new Thickness(12, 17, 0, 0),
                Padding = new Thickness(13, 8),
                CornerRadius = new CornerRadius(9),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Top
            };
            BindBrush(deleteButton, Button.BackgroundProperty, "ErrorDarkBrush");
            BindBrush(deleteButton, Button.BorderBrushProperty, "ErrorBrush");
            BindBrush(deleteButton, Button.ForegroundProperty, "WhiteBrush");
            ToolTip.SetTip(deleteButton, "保存配置后永久删除此自定义功能区");
            deleteButton.Click += (_, _) => deleteArea();
            Grid.SetColumn(deleteButton, 3);
            grid.Children.Add(deleteButton);
        }

        return grid;
    }

    private void RemoveAreaEditor(string areaId)
    {
        var editor = _editors.FirstOrDefault(candidate =>
            string.Equals(candidate.AreaId, areaId, StringComparison.OrdinalIgnoreCase));
        if (editor is null || !editor.IsUserArea)
            return;

        AreaEditors.Children.Remove(editor.Card);
        _editors.Remove(editor);
        _draftUserAreaIds.Remove(areaId);
    }

    private Control CreateIconEditor(IconEditorState state)
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var preview = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 12, 0)
        };
        BindBrush(preview, Border.BackgroundProperty, "IconBoxBgBrush");
        state.RegisterPreview(preview);

        var presets = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var glyph in PresetGlyphs)
        {
            var presetButton = new Button
            {
                // 预设按钮内容用 FeatureIconFactory 渲染，material: 字形显示为 MaterialIcon
                Content = FeatureIconFactory.CreateGlyph(glyph, 15),
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                FontSize = 15,
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            BindBrush(presetButton, Button.BackgroundProperty, "ComponentBgBrush");
            BindBrush(presetButton, Button.BorderBrushProperty, "DefaultBorderBrush");
            presetButton.Click += (_, _) => state.SelectPreset(glyph);
            presets.Children.Add(presetButton);
        }

        var center = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "区域图标 · 选择简约预设，或浏览本地图片",
                    FontSize = 10
                },
                presets
            }
        };
        BindBrush(center.Children[0], TextBlock.ForegroundProperty, "MutedTextBrush");
        Grid.SetColumn(center, 1);

        var pathLabel = new TextBlock
        {
            FontSize = 10,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        BindBrush(pathLabel, TextBlock.ForegroundProperty, "MutedTextBrush");
        state.RegisterPathLabel(pathLabel);

        var browseButton = new Button
        {
            Content = "浏览本地图片…",
            Padding = new Thickness(13, 7),
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        BindBrush(browseButton, Button.BackgroundProperty, "ComponentBgBrush");
        BindBrush(browseButton, Button.BorderBrushProperty, "DefaultBorderBrush");
        browseButton.Click += async (_, _) =>
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider is null)
                return;

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择功能区图标",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"]
                    }
                ]
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                state.SelectLocalImage(path);
        };

        var browsePanel = new StackPanel
        {
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { browseButton, pathLabel }
        };
        Grid.SetColumn(browsePanel, 2);

        root.Children.Add(preview);
        root.Children.Add(center);
        root.Children.Add(browsePanel);
        return root;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var liveComponents = _registry.CreateCurrentProfile().Areas.ToDictionary(
            area => area.AreaId,
            area => area.ActionIds,
            StringComparer.OrdinalIgnoreCase);

        var profile = new WorkspaceProfile
        {
            Version = WorkspaceProfile.CurrentVersion,
            GlobalComponentScale = ComponentScaleSlider.Value,
            Areas = _editors.Select(editor => new FeatureAreaPreference
            {
                AreaId = editor.AreaId,
                DisplayName = string.IsNullOrWhiteSpace(editor.NameBox.Text)
                    ? editor.DefaultName
                    : editor.NameBox.Text.Trim(),
                Description = string.IsNullOrWhiteSpace(editor.DescriptionBox.Text)
                    ? editor.DefaultDescription
                    : editor.DescriptionBox.Text.Trim(),
                IconGlyph = editor.Icon.Glyph,
                IconPath = editor.Icon.IconPath,
                ActionIds = liveComponents.TryGetValue(editor.AreaId, out var componentIds)
                    ? [.. componentIds]
                    : []
            }).ToList(),
            CustomAreas = _editors
                .Where(editor => editor.IsUserArea)
                .Select(editor => new UserFeatureAreaProfile
                {
                    Id = editor.AreaId,
                    Title = string.IsNullOrWhiteSpace(editor.NameBox.Text)
                        ? editor.DefaultName
                        : editor.NameBox.Text.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(editor.DescriptionBox.Text)
                        ? editor.DefaultDescription
                        : editor.DescriptionBox.Text.Trim(),
                    Glyph = editor.Icon.Glyph,
                    IconPath = editor.Icon.IconPath
                })
                .ToList()
        };

        Saved?.Invoke(this, new PersonalizationResult(profile, _storageDirectory, _resetLayoutPending));
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _resetLayoutPending = true;
        _draftUserAreaIds.Clear();
        _draftUserAreaIds.UnionWith(_registry.UserAreaIds);
        BuildEditors(WorkspaceDefaultProfile.Create());
    }

    private void OnCreateAreaClick(object? sender, RoutedEventArgs e)
    {
        var nextNumber = _editors
            .Select(editor => ParseAreaNumber(editor.AreaId))
            .DefaultIfEmpty(0)
            .Max() + 1;
        var id = $"area-{nextNumber:000}";
        var title = $"新功能区 {nextNumber}";

        var definition = new FeatureAreaDefinition
        {
            Id = id,
            Title = title,
            Subtitle = "用户创建的功能区",
            Glyph = "material:Apps",
            IconPath = null,
            Actions = []
        };

        _draftUserAreaIds.Add(id);
        AddAreaEditor(definition, new FeatureAreaPreference
        {
            AreaId = id,
            DisplayName = title,
            Description = "用户创建的功能区",
            IconGlyph = "material:Apps",
            IconPath = null,
            ActionIds = []
        });

        AreaEditors.Children[^1].BringIntoView();
    }

    private static int ParseAreaNumber(string id)
    {
        const string prefix = "area-";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(id[prefix.Length..], out var number)
            ? number
            : 0;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Reload(_storageDirectory);
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 丢弃所有未保存的草稿改动，按注册表的当前状态重建整个编辑界面。
    /// 点「取消」时会自动调用；宿主在外部改动了注册表后也可主动调用以同步界面。
    /// </summary>
    /// <param name="storageDirectory">要显示并使用的配置目录。</param>
    public void Reload(string storageDirectory)
    {
        _resetLayoutPending = false;
        SetStorageDirectory(storageDirectory);
        _draftUserAreaIds.Clear();
        _draftUserAreaIds.UnionWith(_registry.UserAreaIds);
        BuildEditors(_registry.CreateCurrentProfile());
    }

    /// <summary>一个功能区在编辑界面中的全部状态与对应控件引用。</summary>
    /// <param name="AreaId">区域 Id。</param>
    /// <param name="DefaultName">区域定义的原始名称，用于「恢复默认」。</param>
    /// <param name="DefaultDescription">区域定义的原始简介。</param>
    /// <param name="IsUserArea">是否为用户自建区域（决定能否删除）。</param>
    /// <param name="Card">该区域对应的卡片控件。</param>
    /// <param name="NameBox">名称输入框。</param>
    /// <param name="DescriptionBox">简介输入框。</param>
    /// <param name="Icon">图标编辑器状态。</param>
    private sealed record AreaEditorState(
        string AreaId,
        string DefaultName,
        string DefaultDescription,
        bool IsUserArea,
        Border Card,
        TextBox NameBox,
        TextBox DescriptionBox,
        IconEditorState Icon);

    /// <summary>
    /// 单个区域的图标选择状态：在「内置预设字符」与「本地图片路径」之间二选一。
    /// 改动会立刻同步到所有已注册的预览控件与路径标签。
    /// </summary>
    /// <param name="glyph">初始图标字符；为空白时回退到 <c>material:Apps</c>。</param>
    /// <param name="iconPath">初始本地图片路径；非空时优先于 <paramref name="glyph"/>。</param>
    private sealed class IconEditorState(string glyph, string? iconPath)
    {
        private readonly List<Border> _previews = [];
        private readonly List<TextBlock> _pathLabels = [];

        /// <summary>当前图标字符，支持 Material 前缀与 Emoji。</summary>
        public string Glyph { get; private set; } = string.IsNullOrWhiteSpace(glyph) ? "material:Apps" : glyph;

        /// <summary>当前本地图片路径；非空时优先于 <see cref="Glyph"/> 显示。</summary>
        public string? IconPath { get; private set; } = iconPath;

        /// <summary>登记一个图标预览控件，并立即用当前选择刷新它。</summary>
        /// <param name="preview">预览容器。</param>
        public void RegisterPreview(Border preview)
        {
            _previews.Add(preview);
            Refresh();
        }

        /// <summary>登记一个显示当前图标来源的文字标签，并立即刷新。</summary>
        /// <param name="label">路径标签控件。</param>
        public void RegisterPathLabel(TextBlock label)
        {
            _pathLabels.Add(label);
            Refresh();
        }

        /// <summary>选择内置预设图标；会清空已选的本地图片路径。</summary>
        /// <param name="selectedGlyph">预设图标字符。</param>
        public void SelectPreset(string selectedGlyph)
        {
            Glyph = selectedGlyph;
            IconPath = null;
            Refresh();
        }

        /// <summary>选择本地图片；<see cref="Glyph"/> 保留作为图片失效时的回退。</summary>
        /// <param name="path">本地图片绝对路径。</param>
        public void SelectLocalImage(string path)
        {
            IconPath = path;
            Refresh();
        }

        private void Refresh()
        {
            foreach (var preview in _previews)
                preview.Child = FeatureIconFactory.Create(Glyph, IconPath);

            var labelText = string.IsNullOrWhiteSpace(IconPath)
                ? $"当前预设：{Glyph}"
                : IconPath;
            foreach (var label in _pathLabels)
                label.Text = labelText;
        }
    }
}

/// <summary>个性化窗口的保存结果，经 <see cref="PersonalizationWindow.Saved"/> 交给宿主。</summary>
/// <param name="Profile">用户编辑后的工作区档案，宿主可直接持久化。</param>
/// <param name="StorageDirectory">用户最终选择的配置目录。</param>
/// <param name="ResetLayout">用户是否点了「恢复默认」：宿主应把布局 / 侧边栏 / 组件摆放一并恢复出厂。</param>
public sealed record PersonalizationResult(
    WorkspaceProfile Profile,
    string StorageDirectory,
    bool ResetLayout = false);
