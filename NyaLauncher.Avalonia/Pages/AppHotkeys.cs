using System;
using System.Collections.Generic;
using Avalonia.Input;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>应用内快捷键的用途标识。</summary>
internal enum HotkeyAction
{
    /// <summary>打开设置页。</summary>
    OpenSettings,
    /// <summary>快捷启动当前选中的实例。</summary>
    QuickLaunch
}

internal enum HotkeyCaptureOutcome
{
    /// <summary>捕获到有效组合键。</summary>
    Captured,
    /// <summary>用户按 Esc 或再次点击按钮取消。</summary>
    Cancelled,
    /// <summary>按键无效（缺少 Ctrl/Alt 修饰），捕获继续。</summary>
    Rejected
}

/// <summary>
/// 应用内快捷键助手：config.json KV 存取（格式为 KeyGesture 序列化文本，如 "Ctrl+OemComma"）、
/// 按键匹配与界面录制流程。配置文本解析失败时回落默认值；「快捷启动」默认未设置，避免误触拉起游戏。
/// </summary>
internal static class AppHotkeys
{
    private static readonly Dictionary<HotkeyAction, KeyGesture?> Cache = new();
    public static readonly KeyGesture OpenSettingsDefault = new(Key.OemComma, KeyModifiers.Control);

    private static Action<HotkeyCaptureOutcome, KeyGesture?>? _captureCallback;

    /// <summary>「打开设置」快捷键（必有值：无配置或解析失败时回落默认 Ctrl+,）。</summary>
    public static KeyGesture OpenSettingsGesture => Resolve(HotkeyAction.OpenSettings)!;

    /// <summary>「快捷启动」快捷键；未设置时为 null。</summary>
    public static KeyGesture? QuickLaunchGesture => Resolve(HotkeyAction.QuickLaunch);

    /// <summary>是否正处于「等待用户按下新快捷键」的录制状态。</summary>
    public static bool IsCapturing => _captureCallback is not null;

    /// <summary>
    /// 开始录制。MainWindow 的窗口级按键处理检测到录制状态后，
    /// 会把按键优先转交给 <see cref="FeedKey"/>。
    /// </summary>
    public static void BeginCapture(Action<HotkeyCaptureOutcome, KeyGesture?> onResult) =>
        _captureCallback = onResult;

    /// <summary>结束录制。</summary>
    public static void EndCapture() => _captureCallback = null;

    /// <summary>把一次按键喂给录制流程；由 MainWindow 的窗口级按键处理调用。</summary>
    public static void FeedKey(KeyEventArgs e)
    {
        var callback = _captureCallback;
        if (callback is null)
            return;

        var key = e.Key;

        // Esc = 取消
        if (key == Key.Escape)
        {
            EndCapture();
            callback(HotkeyCaptureOutcome.Cancelled, null);
            e.Handled = true;
            return;
        }

        // 纯修饰键按下不作为结果，继续等待
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        // 必须带 Ctrl 或 Alt：裸按键会与文本输入冲突
        var modifiers = e.KeyModifiers;
        if (!modifiers.HasFlag(KeyModifiers.Control) && !modifiers.HasFlag(KeyModifiers.Alt))
        {
            callback(HotkeyCaptureOutcome.Rejected, null);
            e.Handled = true;
            return;
        }

        var gesture = new KeyGesture(key, modifiers);
        EndCapture();
        callback(HotkeyCaptureOutcome.Captured, gesture);
        e.Handled = true;
    }

    /// <summary>保存快捷键并立即生效。</summary>
    public static void Save(HotkeyAction action, KeyGesture gesture)
    {
        LauncherConfig.SetValue(KeyFor(action), gesture.ToString());
        Cache[action] = gesture;
    }

    /// <summary>
    /// 清除快捷键配置：「打开设置」回落默认 Ctrl+,；「快捷启动」变为未设置。
    /// </summary>
    public static void Clear(HotkeyAction action)
    {
        LauncherConfig.ClearValue(KeyFor(action));
        Cache[action] = action == HotkeyAction.OpenSettings ? OpenSettingsDefault : null;
    }

    /// <summary>解析配置文本；格式非法时返回 null。</summary>
    public static KeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return KeyGesture.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>转成界面友好文本（如 "Ctrl+OemComma" → "Ctrl+,"）。</summary>
    public static string Format(KeyGesture gesture) => FormatTokens(gesture.ToString());

    private static KeyGesture? Resolve(HotkeyAction action)
    {
        if (Cache.TryGetValue(action, out var cached))
            return cached;

        var gesture = Parse(LauncherConfig.GetValue(KeyFor(action)));
        if (gesture is null && action == HotkeyAction.OpenSettings)
            gesture = OpenSettingsDefault;

        Cache[action] = gesture;
        return gesture;
    }

    private static string KeyFor(HotkeyAction action) => action switch
    {
        HotkeyAction.OpenSettings => "settingsOpenHotkey",
        HotkeyAction.QuickLaunch => "quickLaunchHotkey",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static string FormatTokens(string text) => text
        .Replace("OemComma", ",")
        .Replace("OemPeriod", ".")
        .Replace("OemPlus", "=")
        .Replace("OemMinus", "-")
        .Replace("OemQuestion", "/")
        .Replace("OemSemicolon", ";")
        .Replace("OemQuotes", "'")
        .Replace("OemOpenBrackets", "[")
        .Replace("OemCloseBrackets", "]")
        .Replace("OemPipe", "\\")
        .Replace("OemTilde", "`")
        .Replace("Space", "空格")
        .Replace("NumPad", "Num")
        .Replace("D1", "1").Replace("D2", "2").Replace("D3", "3").Replace("D4", "4")
        .Replace("D5", "5").Replace("D6", "6").Replace("D7", "7").Replace("D8", "8")
        .Replace("D9", "9").Replace("D0", "0");
}
