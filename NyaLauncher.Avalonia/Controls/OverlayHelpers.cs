using System.IO;
using Avalonia.Controls;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 遮罩内容视图的通用胶水工具（替代原 ModalOverlayBase 内的辅助方法）：
/// 主题画刷查找、状态提示设置、实例名校验。所有逻辑均为纯函数/静态，无 UI 状态。
/// </summary>
internal static class OverlayHelpers
{
    /// <summary>从当前主题资源中查找画刷（兼容 Color 值，自动包装为 SolidColorBrush）。</summary>
    public static IBrush FindBrush(string key) => OverlayTheme.FindBrush(key);

    /// <summary>设置状态提示文本（错误用 ErrorBrush，其余用 HintTextBrush），并确保可见。</summary>
    public static void SetStatus(TextBlock? statusText, string message, bool isError = false)
    {
        if (statusText is null)
            return;
        statusText.Text = message;
        statusText.Foreground = FindBrush(isError ? "ErrorBrush" : "HintTextBrush");
        statusText.IsVisible = true;
    }

    /// <summary>
    /// 校验自定义实例名 / 版本 id：拒绝空、"."/".."、非法文件名字符与路径分隔符（防路径穿越）。
    /// </summary>
    public static bool IsValidInstanceName(string name, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "请输入新实例的名字。";
            return false;
        }
        if (name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/', System.StringComparison.Ordinal) ||
            name.Contains('\\', System.StringComparison.Ordinal))
        {
            error = "实例名包含不安全字符，请换一个名字。";
            return false;
        }
        return true;
    }
}
