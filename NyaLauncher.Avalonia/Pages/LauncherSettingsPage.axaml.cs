using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Themes;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>
/// 「启动器设置」标签页：与游戏无关的启动器自身配置
/// （快捷键 / 个性化主题 / AI 功能），自 SettingsPage 迁出。
/// </summary>
public partial class LauncherSettingsPage : UserControl
{
    private bool _initializingThemeSettings = true;

    public LauncherSettingsPage()
    {
        InitializeComponent();
        InitializeThemeSettings();
        ReloadHotkeys();

        AttachedToVisualTree += (_, _) =>
        {
            ThemeSettings.ThemeAvailabilityChanged += RefreshThemeAvailability;
            RefreshThemeAvailability();
        };

        // 离开设置页时中止快捷键录制，避免按键继续被吞
        DetachedFromVisualTree += (_, _) =>
        {
            ThemeSettings.ThemeAvailabilityChanged -= RefreshThemeAvailability;
            if (AppHotkeys.IsCapturing)
            {
                AppHotkeys.EndCapture();
                ReloadHotkeys();
            }
        };
    }

    // ------------------------------------------------------------------
    // 设置页搜索（由 SettingsHubPage 调用）
    // ------------------------------------------------------------------

    /// <summary>
    /// 应用搜索过滤：仅保留标题或关键词命中的卡片，返回命中卡片数；
    /// 查询为空白时恢复全部卡片并返回 -1（表示非搜索态）。
    /// </summary>
    public int ApplySearchFilter(string? query)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            CardHotkeys.IsVisible = CardLauncher.IsVisible = CardAi.IsVisible = true;
            return -1;
        }

        var hits = 0;
        void Match(Border card, string title, params string[] aliases)
        {
            var matched = Hit(title) || aliases.Any(Hit);
            card.IsVisible = matched;
            if (matched)
                hits++;
        }

        Match(CardHotkeys, "快捷键", "热键", "打开设置", "快速启动", "按键", "录制");
        Match(CardLauncher, "个性化", "主题", "初音", "未来", "深色", "浅色", "明暗", "跟随系统", "氛围", "星尘", "圆环", "点击", "背景");
        Match(CardAi, "AI 设置", "ai", "人工智能", "模型", "对话");
        return hits;

        bool Hit(string text) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // 滚动焦点效果：视口中心的卡片略微放大，边缘的卡片缩小
    // ------------------------------------------------------------------

    private Border[]? _cards;

    private void OnSettingsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _cards ??= [CardHotkeys, CardLauncher, CardAi];

        var scroller = SettingsScroller;
        if (scroller is null) return;

        var viewportCenter = scroller.Offset.Y + scroller.Viewport.Height / 2.0;

        foreach (var card in _cards)
        {
            // 卡片在滚动容器中的垂直中心位置
            var cardTop = card.Bounds.Top;
            var cardCenter = cardTop + card.Bounds.Height / 2.0;
            var distance = Math.Abs(cardCenter - viewportCenter);

            // 距离越近越大：1.0 → 0.97（克制的微缩放，与全局微交互幅度一致）
            var maxDistance = scroller.Viewport.Height * 0.6;
            var t = Math.Clamp(distance / maxDistance, 0, 1);
            var scale = 1.0 - 0.03 * t;

            card.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    // ------------------------------------------------------------------
    // 快捷键设置（打开设置 / 快捷启动）
    // ------------------------------------------------------------------

    private HotkeyAction? _capturingAction;

    private void ReloadHotkeys()
    {
        _capturingAction = null;
        OpenSettingsHotkeyButton.Content = AppHotkeys.Format(AppHotkeys.OpenSettingsGesture);
        OpenSettingsHotkeyHintText.Text =
            "在启动器任意界面按下即可打开设置页。点击右侧按钮录制新组合键。";

        var quickLaunch = AppHotkeys.QuickLaunchGesture;
        QuickLaunchHotkeyButton.Content = quickLaunch is not null
            ? AppHotkeys.Format(quickLaunch)
            : "未设置";
        QuickLaunchClearButton.IsVisible = quickLaunch is not null;
        QuickLaunchHotkeyHintText.Text =
            "以当前选中的实例与账户直接启动游戏（需先在启动页选好版本）。默认未设置。";
    }

    private void OnOpenSettingsHotkeyClick(object? sender, RoutedEventArgs e) =>
        StartHotkeyCapture(HotkeyAction.OpenSettings);

    private void OnQuickLaunchHotkeyClick(object? sender, RoutedEventArgs e) =>
        StartHotkeyCapture(HotkeyAction.QuickLaunch);

    private void OnQuickLaunchClearClick(object? sender, RoutedEventArgs e)
    {
        AppHotkeys.Clear(HotkeyAction.QuickLaunch);
        ReloadHotkeys();
    }

    private void StartHotkeyCapture(HotkeyAction action)
    {
        // 录制中再次点击同一行 = 取消
        if (_capturingAction == action)
        {
            AppHotkeys.EndCapture();
            ReloadHotkeys();
            return;
        }

        _capturingAction = action;
        AppHotkeys.BeginCapture((outcome, gesture) => OnHotkeyCaptureResult(action, outcome, gesture));

        var button = action == HotkeyAction.OpenSettings
            ? OpenSettingsHotkeyButton
            : QuickLaunchHotkeyButton;
        button.Content = "按下组合键…";
        SetHotkeyHint(action, "请按下新组合键（需包含 Ctrl 或 Alt）· Esc 取消 · 再点一次按钮取消");
    }

    private void OnHotkeyCaptureResult(HotkeyAction action, HotkeyCaptureOutcome outcome, KeyGesture? gesture)
    {
        switch (outcome)
        {
            case HotkeyCaptureOutcome.Captured when gesture is not null:
                AppHotkeys.Save(action, gesture);
                ReloadHotkeys();
                break;
            case HotkeyCaptureOutcome.Cancelled:
                ReloadHotkeys();
                break;
            case HotkeyCaptureOutcome.Rejected:
                // 捕获仍在进行，提示后继续等待下一次按键
                SetHotkeyHint(action, "无效组合：需要包含 Ctrl 或 Alt，再试一次（Esc 取消）");
                break;
        }
    }

    private void SetHotkeyHint(HotkeyAction action, string text)
    {
        if (action == HotkeyAction.OpenSettings)
            OpenSettingsHotkeyHintText.Text = text;
        else
            QuickLaunchHotkeyHintText.Text = text;
    }

    // ------------------------------------------------------------------
    // 个性化设置（主题 / 明暗 / 彩虹背景 / 星尘特效）
    // ------------------------------------------------------------------

    private void InitializeThemeSettings()
    {
        var currentFamily = ThemeSettings.LoadThemeFamily();
        var currentMode = ThemeSettings.LoadThemeMode();
        RefreshThemeAvailability();

        // 主题色卡：同步选中态（_initializingThemeSettings 守卫避免触发应用逻辑）
        ThemeCardMiku.IsChecked = currentFamily == "HatsuneMiku";
        ThemeCardDeepSeek.IsChecked = currentFamily == "DeepSeekPurple";
        ThemeCardCodexBlue.IsChecked = currentFamily == "CodexBlue";
        ThemeCardZhiShu.IsChecked = currentFamily == "ZhiShuBlue";
        ThemeCardMojang.IsChecked = currentFamily == "MojangRed";

        // 明暗分段按钮：同步选中态（System = 跟随系统）
        ThemeModeDarkChip.IsChecked = currentMode == "Dark";
        ThemeModeSystemChip.IsChecked = currentMode == "System";
        ThemeModeLightChip.IsChecked = currentMode == "Light";
        // 「彩虹背景」开关：初始化时同步到 AmbientGradient 全局开关
        AmbientSwitch.IsChecked = ThemeSettings.LoadAmbientGradient();
        AmbientGradient.AmbientGradientEnabled = AmbientSwitch.IsChecked == true;
        // 「星尘特效」开关：同步到 SparkleTrail 全局开关
        SparkleSwitch.IsChecked = ThemeSettings.LoadSparkleTrail();
        SparkleTrail.SparkleTrailEnabled = SparkleSwitch.IsChecked == true;
        // 「点击圆环」开关：同步到 ClickRing 全局开关
        ClickRingSwitch.IsChecked = ThemeSettings.LoadClickRing();
        ClickRing.ClickRingEnabled = ClickRingSwitch.IsChecked == true;
        // 「自定义背景图」：同步不透明度滑条与清除按钮可见性
        var customBg = ThemeSettings.LoadCustomBackground();
        CustomBgOpacitySlider.Value = ThemeSettings.LoadCustomBackgroundOpacity();
        CustomBgBlurSlider.Value = ThemeSettings.LoadCustomBackgroundBlur();
        UpdateCustomBgState(customBg);
        _initializingThemeSettings = false;
    }

    private void RefreshThemeAvailability() =>
        ThemeCardCodexBlue.IsVisible = ThemeSettings.IsCodexBlueUnlocked();

    private void OnAmbientChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        var enabled = AmbientSwitch.IsChecked == true;
        AmbientGradient.AmbientGradientEnabled = enabled;
        AmbientGradient.RefreshGlobal(); // 立即生效：关则移除渐变层，开则重新注入
        ThemeSettings.SaveAmbientGradient(enabled);
    }

    private void OnSparkleChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        var enabled = SparkleSwitch.IsChecked == true;
        SparkleTrail.SparkleTrailEnabled = enabled;
        SparkleTrail.RefreshGlobal(); // 立即生效：关则移除星星层，开则重新注入
        ThemeSettings.SaveSparkleTrail(enabled);
    }

    private void OnClickRingChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        // 圆环层常驻注入，开关只守卫生成时机，直接改全局布尔即时生效
        ClickRing.ClickRingEnabled = ClickRingSwitch.IsChecked == true;
        ThemeSettings.SaveClickRing(ClickRing.ClickRingEnabled);
    }

    // ------------------------------------------------------------------
    // 自定义背景图（选择 / 清除 / 不透明度）
    // ------------------------------------------------------------------

    private static string BackgroundDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nya-launcher", "backgrounds");

    private void UpdateCustomBgState(string? path)
    {
        CustomBgClearButton.IsVisible = !string.IsNullOrWhiteSpace(path);
        CustomBgOpacitySlider.IsEnabled = !string.IsNullOrWhiteSpace(path);
        CustomBgBlurSlider.IsEnabled = !string.IsNullOrWhiteSpace(path);
    }

    private async void OnCustomBgPickClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背景图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片文件")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
                },
            ],
        });
        if (files.Count == 0) return;

        try
        {
            var source = files[0];
            var ext = Path.GetExtension(source.Name);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            Directory.CreateDirectory(BackgroundDir);
            // 以来源路径 MD5 命名，同源重复选择直接覆盖，不堆积副本
            var fileName = "bg_" + Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes(source.Path?.ToString() ?? source.Name)))
                .ToLowerInvariant() + ext;
            var dest = Path.Combine(BackgroundDir, fileName);

            await using (var src = await source.OpenReadAsync())
            await using (var output = File.Create(dest))
            {
                await src.CopyToAsync(output);
            }

            ThemeSettings.SaveCustomBackground(dest);
            CustomBackgroundImage.SetImage(dest);
            UpdateCustomBgState(dest);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LauncherSettings] 选择背景图失败：{ex}");
        }
    }

    private void OnCustomBgClearClick(object? sender, RoutedEventArgs e)
    {
        var saved = ThemeSettings.LoadCustomBackground();
        ThemeSettings.SaveCustomBackground(null);
        CustomBackgroundImage.SetImage(null);
        UpdateCustomBgState(null);

        // 仅清理启动器自管目录内的副本，用户原图不受影响
        if (!string.IsNullOrWhiteSpace(saved) &&
            saved.StartsWith(BackgroundDir, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(saved)) File.Delete(saved);
            }
            catch
            {
            }
        }
    }

    private void OnCustomBgOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        ThemeSettings.SaveCustomBackgroundOpacity(e.NewValue);
        CustomBackgroundImage.SetOpacity(e.NewValue);
    }

    private void OnCustomBgBlurChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        ThemeSettings.SaveCustomBackgroundBlur(e.NewValue);
        CustomBackgroundImage.SetBlur(e.NewValue);
    }

    private void OnThemeFamilyChecked(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        // IsCheckedChanged 在取消选中时也会触发，只响应选中
        if (sender is RadioButton { Tag: string family, IsChecked: true } card)
        {
            if (!ThemeSettings.IsThemeFamilyAvailable(family))
            {
                card.IsChecked = false;
                return;
            }
            ThemeSettings.SaveThemeFamily(family);
            ApplyThemeHot(family, ThemeSettings.LoadThemeMode());
        }
    }

    private void OnThemeModeChecked(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        if (sender is RadioButton { Tag: string mode, IsChecked: true })
        {
            ThemeSettings.SaveThemeMode(mode);
            ApplyThemeHot(ThemeSettings.LoadThemeFamily(), mode);
        }
    }

    /// <summary>
    /// 主题热重载（无需重启应用）；异常时回退到传统重启方案。
    /// </summary>
    private static void ApplyThemeHot(string family, string mode)
    {
        try
        {
            ThemeManager.ApplyTheme(family, mode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LauncherSettings] 主题热重载失败，回退重启：{ex}");
            RestartApplication();
        }
    }

    private static void RestartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false
            };

            if (Path.GetFileNameWithoutExtension(exe) is "dotnet" or "dotnet.exe")
            {
                var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(dll))
                    return;
                psi.FileName = exe;
                psi.Arguments = $"\"{dll}\"";
            }
            else
            {
                psi.FileName = exe;
            }

            Process.Start(psi);
            Environment.Exit(0);
        }
        catch
        {
        }
    }
}
