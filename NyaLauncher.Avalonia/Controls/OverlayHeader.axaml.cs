using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 可复用的遮罩标题栏：图标（文字 / 图片 / MaterialIcon 三选一）+ 标题 + 副标题 + 关闭按钮。
/// 关闭按钮仅触发 <see cref="CloseRequested"/>，由内容视图决定关闭行为（如调用 Host.Close）。
/// </summary>
public partial class OverlayHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<OverlayHeader, string>(nameof(Title));

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<OverlayHeader, string>(nameof(Subtitle));

    public static readonly StyledProperty<MaterialIconKind> GlyphProperty =
        AvaloniaProperty.Register<OverlayHeader, MaterialIconKind>(
            nameof(Glyph), MaterialIconKind.PackageVariantClosed);

    /// <summary>文字图标（如 "⛏"），优先级最高；为空时回落到图片图标 / MaterialIcon。</summary>
    public static readonly StyledProperty<string?> GlyphTextProperty =
        AvaloniaProperty.Register<OverlayHeader, string?>(nameof(GlyphText));

    /// <summary>图片图标地址（AsyncImage 加载），优先级第二；为空时回落 MaterialIcon。</summary>
    public static readonly StyledProperty<string?> IconUrlProperty =
        AvaloniaProperty.Register<OverlayHeader, string?>(nameof(IconUrl));

    /// <summary>是否显示关闭按钮（默认 true）。</summary>
    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<OverlayHeader, bool>(nameof(ShowCloseButton), true);

    /// <summary>可选第三行小字（如统计信息）；为空时不显示。</summary>
    public static readonly StyledProperty<string> ExtraTextProperty =
        AvaloniaProperty.Register<OverlayHeader, string>(nameof(ExtraText));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public MaterialIconKind Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? GlyphText
    {
        get => GetValue(GlyphTextProperty);
        set => SetValue(GlyphTextProperty, value);
    }

    public string? IconUrl
    {
        get => GetValue(IconUrlProperty);
        set => SetValue(IconUrlProperty, value);
    }

    public bool ShowCloseButton
    {
        get => GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    public string ExtraText
    {
        get => GetValue(ExtraTextProperty);
        set => SetValue(ExtraTextProperty, value);
    }

    /// <summary>关闭按钮被点击时触发；内容视图在此处理关闭（通常调用 Host.Close）。</summary>
    public event EventHandler? CloseRequested;

    public OverlayHeader()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
            TitleText.Text = change.GetNewValue<string>();
        else if (change.Property == SubtitleProperty)
            SubtitleText.Text = change.GetNewValue<string>();
        else if (change.Property == GlyphProperty)
            TypeGlyph.Kind = change.GetNewValue<MaterialIconKind>();
        else if (change.Property == GlyphTextProperty)
            UpdateGlyphLayers();
        else if (change.Property == IconUrlProperty)
        {
            ProjectIcon.SourceUrl = change.GetNewValue<string?>();
            UpdateGlyphLayers();
        }
        else if (change.Property == ShowCloseButtonProperty)
        {
            if (CloseButton is not null)
                CloseButton.IsVisible = change.GetNewValue<bool>();
        }
        else if (change.Property == ExtraTextProperty)
        {
            var extra = change.GetNewValue<string>();
            ExtraLine.Text = extra;
            ExtraLine.IsVisible = !string.IsNullOrWhiteSpace(extra);
        }
    }

    private void UpdateGlyphLayers()
    {
        var hasText = !string.IsNullOrWhiteSpace(GlyphText);
        var hasImage = !string.IsNullOrWhiteSpace(IconUrl);
        GlyphTextBlock.IsVisible = hasText;
        GlyphTextBlock.Text = GlyphText;
        ProjectIcon.IsVisible = !hasText && hasImage;
        TypeGlyph.IsVisible = !hasText && !hasImage;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);
}
