using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;

using NyaLauncher.Avalonia.Animations.Helpers;

namespace NyaLauncher.Avalonia.Dialogs;

/// <summary>
/// 离线默认皮肤选择窗口：展示 <see cref="OfflineSkinCatalog.Choices"/> 中的全部皮肤，
/// 点击后通过 <see cref="Close(NyaLauncher.Avalonia.Framework.OfflineSkinChoice?)"/> 返回所选皮肤。
/// </summary>
public partial class OfflineSkinPickerDialog : Window
{
    /// <summary>当前账号正在使用的皮肤 Id，用于在列表中标出"当前使用"。</summary>
    private readonly string _currentSkinId = "steve";

    public OfflineSkinPickerDialog()
    {
        InitializeComponent();
    }

    public OfflineSkinPickerDialog(string? currentSkinId) : this()
    {
        _currentSkinId = currentSkinId ?? "steve";
        BuildSkinGrid();
    }

    private void BuildSkinGrid()
    {
        foreach (var choice in OfflineSkinCatalog.Choices)
        {
            var isCurrent = string.Equals(
                choice.Id,
                _currentSkinId,
                StringComparison.OrdinalIgnoreCase);

            // 每一张皮肤卡片：缩略图 + 名称 + 模型说明 + "当前使用"标记
            var button = new Button
            {
                Tag = choice,
                Width = 168,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 14),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = isCurrent ? ThemePolygonHelper.SkinButtonBgCurrent : ThemePolygonHelper.SkinButtonBg,
                BorderBrush = isCurrent ? ThemePolygonHelper.SkinButtonBorderCurrent : ThemePolygonHelper.SkinButtonBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

            // 缩略图容器：未加载出贴图时显示字母占位，加载后由 AsyncImage 盖住
            var avatar = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(8),
                Background = ThemePolygonHelper.SkinAvatarBg,
                ClipToBounds = true,
                Child = CreateAvatarContent(choice)
            };

            var titleLine = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleLine.Children.Add(new TextBlock
            {
                Text = choice.DisplayName,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemePolygonHelper.Muted
            });
            if (isCurrent)
            {
                titleLine.Children.Add(new TextBlock
                {
                    Text = "当前使用",
                    FontSize = 10,
                    Foreground = ThemePolygonHelper.AccentGlyph,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            content.Children.Add(avatar);
            content.Children.Add(titleLine);
            content.Children.Add(new TextBlock
            {
                Text = choice.Model == MinecraftSkinModel.Slim
                    ? "纤细模型 · Slim"
                    : "经典模型 · Classic",
                FontSize = 10,
                Foreground = ThemePolygonHelper.DisabledText,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            button.Content = content;
            button.Click += OnSkinClick;
            SkinPanel.Children.Add(button);

            // 后台解析贴图源（文件路径或 data URI），不阻塞 UI
            _ = ResolvePreviewAsync(choice, avatar);
        }
    }

    private static Grid CreateAvatarContent(OfflineSkinChoice choice)
    {
        var grid = new Grid();
        // 皮肤回退字形："material:Kind" 渲染为 Material 图标，其余回退文字
        grid.Children.Add(
            FeatureIconFactory.CreateGlyph(choice.FallbackText, 26, ThemePolygonHelper.AccentGlyph));
        grid.Children.Add(new AsyncImage
        {
            Width = 64,
            Height = 64,
            Stretch = Stretch.Uniform,
            // 只显示 8×8 的头部贴图，放大后即经典的 MC 头像
            CropRect = new PixelRect(8, 8, 8, 8)
        });
        RenderOptions.SetBitmapInterpolationMode(
            (AsyncImage)grid.Children[^1],
            BitmapInterpolationMode.None);
        return grid;
    }

    private async Task ResolvePreviewAsync(OfflineSkinChoice choice, Border avatar)
    {
        try
        {
            var source = await OfflineSkinCatalog.ResolveTextureSourceAsync(choice.Id)
                .ConfigureAwait(true);
            if (avatar.Child is Grid grid &&
                grid.Children.Count > 1 &&
                grid.Children[1] is AsyncImage image)
            {
                image.SourceUrl = source;
            }
        }
        catch
        {
            // 解析失败时保留字母占位，不影响其它皮肤。
        }
    }

    private void OnSkinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OfflineSkinChoice choice })
            OverlayEffects.PopOut(this, () => Close(choice));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        OverlayEffects.PopOut(this, () => Close(null));
}
