using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia;

public partial class PersonalizationWindow : UserControl
{
    private static readonly IBrush Muted = Brush.Parse("#8F98B3");
    private static readonly string[] PresetGlyphs = ["◇", "▶", "◆", "✦", "⚙", "☰", "＋", "⌂", "▦", "◉"];
    private FeatureAreaRegistry _registry = null!;
    private readonly List<AreaEditorState> _editors = [];
    private readonly HashSet<string> _draftUserAreaIds = new(StringComparer.OrdinalIgnoreCase);
    private string _storageDirectory = WorkspaceProfileStore.PlatformDefaultDirectory;

    public event EventHandler<PersonalizationResult>? Saved;
    public event EventHandler? Cancelled;

    public PersonalizationWindow()
    {
        InitializeComponent();
    }

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

    private void BuildEditors(WorkspaceProfile profile)
    {
        AreaEditors.Children.Clear();
        _editors.Clear();

        var preferences = profile.Areas.ToDictionary(
            preference => preference.AreaId,
            StringComparer.OrdinalIgnoreCase);
        var allActions = _registry.AvailableActions;

        foreach (var sourceArea in _registry.SourceAreas)
        {
            preferences.TryGetValue(sourceArea.Id, out var preference);
            AddAreaEditor(sourceArea, preference, allActions);
        }
    }

    private void AddAreaEditor(
        FeatureAreaDefinition sourceArea,
        FeatureAreaPreference? preference,
        IReadOnlyList<FeatureAreaAction> allActions)
    {
        var selectedIds = (preference?.ActionIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nameBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(preference?.DisplayName)
                ? sourceArea.Title
                : preference.DisplayName,
            PlaceholderText = sourceArea.Title,
            FontSize = 14,
            Background = Brush.Parse("#202638"),
            BorderBrush = Brush.Parse("#343C52"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var descriptionBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(preference?.Description)
                ? sourceArea.Subtitle
                : preference.Description,
            PlaceholderText = sourceArea.Subtitle,
            FontSize = 14,
            Background = Brush.Parse("#202638"),
            BorderBrush = Brush.Parse("#343C52"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var iconState = new IconEditorState(
            string.IsNullOrWhiteSpace(preference?.IconGlyph) ? sourceArea.Glyph : preference.IconGlyph,
            string.IsNullOrWhiteSpace(preference?.IconPath) ? sourceArea.IconPath : preference.IconPath);
        var iconEditor = CreateIconEditor(iconState);
        var isUserArea = _draftUserAreaIds.Contains(sourceArea.Id);

        var actionChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var actionGrid = CreateActionGrid(allActions, selectedIds, actionChecks);

        var card = new Border
        {
            Background = Brush.Parse("#171B2B"),
            BorderBrush = Brush.Parse("#30374D"),
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
                        Height = 1,
                        Background = Brush.Parse("#2C3347")
                    },
                    new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "显示的功能按钮",
                                FontSize = 13,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = Brushes.White
                            },
                            new TextBlock
                            {
                                Text = "可跨区域重复选择；取消全部选择可创建纯自定义内容区。",
                                FontSize = 11,
                                Foreground = Muted
                            }
                        }
                    },
                    actionGrid
                }
            }
        };

        AreaEditors.Children.Add(card);
        _editors.Add(new AreaEditorState(
            sourceArea.Id,
            sourceArea.Title,
            sourceArea.Subtitle,
            isUserArea,
            card,
            nameBox,
            descriptionBox,
            iconState,
            actionChecks));
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
            Background = Brush.Parse("#303958"),
            ClipToBounds = true
        };
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
                    Text = $"功能区 · {area.Id}",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                },
                new TextBlock
                {
                    Text = area.Subtitle,
                    FontSize = 10,
                    Foreground = Muted
                }
            }
        };
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
                    FontSize = 10,
                    Foreground = Muted
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
                    FontSize = 10,
                    Foreground = Muted
                },
                descriptionBox
            }
        };
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
                Background = Brush.Parse("#39212B"),
                BorderBrush = Brush.Parse("#713547"),
                BorderThickness = new Thickness(1),
                Foreground = Brush.Parse("#FFB7C5"),
                CornerRadius = new CornerRadius(9),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Top
            };
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
            Background = Brush.Parse("#303958"),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 12, 0)
        };
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
                Content = glyph,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                FontSize = 15,
                Background = Brush.Parse("#252C40"),
                BorderBrush = Brush.Parse("#394159"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
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
                    FontSize = 10,
                    Foreground = Muted
                },
                presets
            }
        };
        Grid.SetColumn(center, 1);

        var pathLabel = new TextBlock
        {
            FontSize = 10,
            Foreground = Muted,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        state.RegisterPathLabel(pathLabel);

        var browseButton = new Button
        {
            Content = "浏览本地图片…",
            Padding = new Thickness(13, 7),
            Background = Brush.Parse("#252C40"),
            BorderBrush = Brush.Parse("#394159"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
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

    private static Grid CreateActionGrid(
        IReadOnlyList<FeatureAreaAction> actions,
        ISet<string> selectedIds,
        IDictionary<string, CheckBox> checks)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*")
        };

        var rowCount = Math.Max(1, (int)Math.Ceiling(actions.Count / 3d));
        for (var row = 0; row < rowCount; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var check = new CheckBox
            {
                IsChecked = selectedIds.Contains(action.Id),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new StackPanel
                {
                    Margin = new Thickness(6, 0, 0, 0),
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{action.Glyph}  {action.Title}",
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White
                        },
                        new TextBlock
                        {
                            Text = action.Description,
                            FontSize = 10,
                            Foreground = Muted,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                }
            };

            var item = new Border
            {
                Margin = new Thickness(0, 5, 8, 3),
                Padding = new Thickness(11, 9),
                Background = Brush.Parse("#202638"),
                BorderBrush = Brush.Parse("#31394F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = check
            };

            Grid.SetColumn(item, index % 3);
            Grid.SetRow(item, index / 3);
            grid.Children.Add(item);
            checks[action.Id] = check;
        }

        return grid;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var profile = new WorkspaceProfile
        {
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
                ActionIds = editor.ActionChecks
                    .Where(pair => pair.Value.IsChecked == true)
                    .Select(pair => pair.Key)
                    .ToList()
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

        Saved?.Invoke(this, new PersonalizationResult(profile, _storageDirectory));
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
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
            Glyph = "◇",
            IconPath = null,
            Actions = []
        };

        _draftUserAreaIds.Add(id);
        AddAreaEditor(definition, new FeatureAreaPreference
        {
            AreaId = id,
            DisplayName = title,
            Description = "用户创建的功能区",
            IconGlyph = "◇",
            IconPath = null,
            ActionIds = []
        }, _registry.AvailableActions);

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

    public void Reload(string storageDirectory)
    {
        SetStorageDirectory(storageDirectory);
        _draftUserAreaIds.Clear();
        _draftUserAreaIds.UnionWith(_registry.UserAreaIds);
        BuildEditors(_registry.CreateCurrentProfile());
    }

    private sealed record AreaEditorState(
        string AreaId,
        string DefaultName,
        string DefaultDescription,
        bool IsUserArea,
        Border Card,
        TextBox NameBox,
        TextBox DescriptionBox,
        IconEditorState Icon,
        IReadOnlyDictionary<string, CheckBox> ActionChecks);

    private sealed class IconEditorState(string glyph, string? iconPath)
    {
        private readonly List<Border> _previews = [];
        private readonly List<TextBlock> _pathLabels = [];

        public string Glyph { get; private set; } = string.IsNullOrWhiteSpace(glyph) ? "◇" : glyph;
        public string? IconPath { get; private set; } = iconPath;

        public void RegisterPreview(Border preview)
        {
            _previews.Add(preview);
            Refresh();
        }

        public void RegisterPathLabel(TextBlock label)
        {
            _pathLabels.Add(label);
            Refresh();
        }

        public void SelectPreset(string selectedGlyph)
        {
            Glyph = selectedGlyph;
            IconPath = null;
            Refresh();
        }

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

public sealed record PersonalizationResult(
    WorkspaceProfile Profile,
    string StorageDirectory);
