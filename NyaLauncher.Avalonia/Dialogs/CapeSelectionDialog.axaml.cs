using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Avalonia.Themes;

using NyaLauncher.Avalonia.Animations.Helpers;

namespace NyaLauncher.Avalonia.Dialogs;

public partial class CapeSelectionDialog : Window
{
    public CapeSelectionDialog()
    {
        InitializeComponent();
    }

    internal CapeSelectionDialog(MinecraftProfile profile) : this()
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Capes.Count == 0)
        {
            CapeList.Children.Add(new TextBlock
            {
                Text = "该账号当前没有可选择的披风。",
                Foreground = ThemePolygonHelper.Muted,
                Margin = new Thickness(2, 8)
            });
            return;
        }

        for (var index = 0; index < profile.Capes.Count; index++)
        {
            var cape = profile.Capes[index];
            var label = string.IsNullOrWhiteSpace(cape.Alias)
                ? $"披风 {index + 1}"
                : cape.Alias;
            var button = new Button
            {
                Tag = cape.Id,
                Content = cape.IsActive ? $"{label} · 当前使用" : label,
                Padding = new Thickness(16, 12),
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                Background = cape.IsActive ? ThemePolygonHelper.SkinButtonBgCurrent : ThemePolygonHelper.SkinButtonBg,
                BorderBrush = cape.IsActive ? ThemePolygonHelper.SkinButtonBorderCurrent : ThemePolygonHelper.SkinButtonBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9)
            };
            button.Click += OnCapeClick;
            CapeList.Children.Add(button);
        }
    }

    private void OnCapeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string capeId })
            OverlayEffects.PopOut(this, () => Close(new CapeSelectionResult(capeId)));
    }

    private void OnDisableCapeClick(object? sender, RoutedEventArgs e) =>
        OverlayEffects.PopOut(this, () => Close(new CapeSelectionResult(null)));

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        OverlayEffects.PopOut(this, () => Close(null));
}

internal sealed record CapeSelectionResult(string? CapeId);
