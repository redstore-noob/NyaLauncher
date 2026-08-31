using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Core;
using System;

namespace NyaLauncher.Avalonia.Pages;

public partial class AboutPage : UserControl
{
    // 开发者名单彩蛋：连点卡片 7 次召唤「猫娘」
    private const int NekoTriggerClicks = 7;
    private const string QQGroupNumber = "1108330006";
    private int _devClickCount;
    private DispatcherTimer? _devResetTimer;

    public AboutPage()
    {
        InitializeComponent();
        _devResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _devResetTimer.Tick += OnDevResetTick;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        LauncherText.Text = NyaLauncherInfo.FormatVersionString();
    }

    /// <summary>
    /// 开发者名单彩蛋：连点卡片 7 次（2 秒内）召唤猫娘登场。
    /// </summary>
    private void DevCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _devClickCount++;
        _devResetTimer?.Stop();
        _devResetTimer?.Start();

        if (_devClickCount < NekoTriggerClicks)
        {
            EasterEggHint.Text = $"连点开发者卡片有惊喜（还需 {NekoTriggerClicks - _devClickCount} 次）";
            EasterEggHint.IsVisible = true;
            return;
        }

        _devClickCount = 0;
        _devResetTimer?.Stop();
        EasterEggHint.IsVisible = false;
        ShowNekoEasterEgg();
    }

    private void OnDevResetTick(object? sender, EventArgs e)
    {
        _devClickCount = 0;
        _devResetTimer?.Stop();
        EasterEggHint.IsVisible = false;
    }

    /// <summary>
    /// 猫娘登场：覆盖层淡入 + 卡片 M3 上浮入场（SlideFadeIn）。
    /// </summary>
    private void ShowNekoEasterEgg()
    {
        NekoOverlay.IsVisible = true;
        NekoOverlay.Opacity = 1;
        _ = AnimationHelper.SlideFadeInAsync(NekoCard, MaterialMotion.MediumTransitionMs, slideOffset: 32);
    }

    /// <summary>点击猫娘覆盖层任意位置关闭。</summary>
    private void NekoOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        NekoOverlay.IsVisible = false;
    }

    /// <summary>复制 QQ 群号到剪贴板。</summary>
    private async void OnCopyGroupClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            NyaAlert.Error("剪贴板不可用，请手动记录群号：" + QQGroupNumber);
            return;
        }

        await clipboard.SetTextAsync(QQGroupNumber);
        NyaAlert.Success($"群号已复制：{QQGroupNumber}，欢迎来玩喵~");
    }
}
