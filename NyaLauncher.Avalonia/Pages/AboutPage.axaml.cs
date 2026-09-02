using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Core;
using System;
using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Pages;

public partial class AboutPage : UserControl
{
    // 开发者名单彩蛋：连点卡片 7 次召唤「猫娘」
    private const int NekoTriggerClicks = 7;
    private const int CodexBlueTriggerClicks = 7;
    private const long CodexBlueClickIntervalMs = 2000;
    private const double ClickMovementTolerance = 6;
    private const string QQGroupNumber = "1108330006";
    private int _devClickCount;
    private DispatcherTimer? _devResetTimer;
    private int _codexBlueClickCount;
    private long _lastCodexBlueClickAt;
    private long _codexBluePressAt;
    private IPointer? _codexBluePressedPointer;
    private Point _codexBluePressPosition;
    private TopLevel? _inputRoot;
    private readonly List<Visual> _visibilityAncestors = [];

    public AboutPage()
    {
        InitializeComponent();
        _devResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _devResetTimer.Tick += OnDevResetTick;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputRoot = TopLevel.GetTopLevel(this);
        if (_inputRoot is not null)
        {
            // Tunnel also sees clicks handled by navigation controls outside this page.
            _inputRoot.AddHandler(PointerPressedEvent, OnRootPointerPressed,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            _inputRoot.AddHandler(PointerReleasedEvent, OnRootPointerReleased,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            _inputRoot.AddHandler(PointerMovedEvent, OnRootPointerMoved,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            _inputRoot.AddHandler(PointerWheelChangedEvent, OnRootPointerWheelChanged,
                RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        // Settings tabs hide the parent host without detaching the page.
        for (Visual? visual = this; visual is not null; visual = visual.GetVisualParent())
        {
            _visibilityAncestors.Add(visual);
            visual.PropertyChanged += OnAncestorPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inputRoot is not null)
        {
            _inputRoot.RemoveHandler(PointerPressedEvent, OnRootPointerPressed);
            _inputRoot.RemoveHandler(PointerReleasedEvent, OnRootPointerReleased);
            _inputRoot.RemoveHandler(PointerMovedEvent, OnRootPointerMoved);
            _inputRoot.RemoveHandler(PointerWheelChangedEvent, OnRootPointerWheelChanged);
            _inputRoot = null;
        }

        foreach (var visual in _visibilityAncestors)
            visual.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
        ResetCodexBlueClicks();
        OnDevResetTick(this, EventArgs.Empty);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is false)
        {
            ResetCodexBlueClicks();
            OnDevResetTick(this, EventArgs.Empty);
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TouristHName))
        {
            ResetCodexBlueClicks();
            return;
        }

        // The name belongs only to this hidden theme trigger, never the card's cat easter egg.
        e.Handled = true;
        OnDevResetTick(this, EventArgs.Empty);
        var properties = e.GetCurrentPoint(TouristHName).Properties;
        var validButton = e.Pointer.Type == PointerType.Touch ||
            (e.Pointer.Type == PointerType.Mouse &&
             properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed &&
             !properties.IsRightButtonPressed && !properties.IsMiddleButtonPressed);
        if (!validButton || _codexBluePressedPointer is not null || ThemeSettings.IsCodexBlueUnlocked())
        {
            ResetCodexBlueClicks();
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastCodexBlueClickAt > CodexBlueClickIntervalMs)
            ResetCodexBlueClicks();
        _codexBluePressedPointer = e.Pointer;
        _codexBluePressAt = now;
        _codexBluePressPosition = e.GetPosition(TouristHName);
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_codexBluePressedPointer is null)
            return;

        var now = Environment.TickCount64;
        var validRelease = ReferenceEquals(e.Pointer, _codexBluePressedPointer) &&
            (e.Pointer.Type == PointerType.Touch || e.InitialPressMouseButton == MouseButton.Left) &&
            new Rect(TouristHName.Bounds.Size).Contains(e.GetPosition(TouristHName)) &&
            now - _codexBluePressAt <= CodexBlueClickIntervalMs;
        _codexBluePressedPointer = null;
        if (!validRelease)
        {
            ResetCodexBlueClicks();
            return;
        }

        e.Handled = true;
        if (now - _lastCodexBlueClickAt > CodexBlueClickIntervalMs)
            _codexBlueClickCount = 0;
        _lastCodexBlueClickAt = now;
        if (++_codexBlueClickCount < CodexBlueTriggerClicks)
            return;

        ResetCodexBlueClicks();
        if (ThemeSettings.UnlockCodexBlue())
            NyaAlert.Success("已解锁 Codex蓝主题，可在启动器设置中选择。");
        else
            NyaAlert.Error("主题解锁保存失败，请稍后重试。");
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _codexBluePressedPointer))
            return;

        var delta = e.GetPosition(TouristHName) - _codexBluePressPosition;
        if (Math.Abs(delta.X) > ClickMovementTolerance || Math.Abs(delta.Y) > ClickMovementTolerance)
            ResetCodexBlueClicks();
    }

    private void OnRootPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        => ResetCodexBlueClicks();

    private void ResetCodexBlueClicks()
    {
        _codexBlueClickCount = 0;
        _lastCodexBlueClickAt = 0;
        _codexBluePressedPointer = null;
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
