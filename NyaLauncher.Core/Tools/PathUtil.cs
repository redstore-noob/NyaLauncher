namespace NyaLauncher.Core.Tools;

/// <summary>
/// 路径比较与规范化工具。全项目的路径相等判断统一走这里，
/// 避免各页面/服务重复实现：Windows 忽略大小写，其他系统区分大小写。
/// </summary>
public static class PathUtil
{
    /// <summary>
    /// 规范化后比较两个路径是否相等（去掉尾部目录分隔符、展开为完整路径）。
    /// 任一为空或非法时按"不相等"处理，不抛异常。
    /// </summary>
    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // 非法路径（如含空字符）按不相等处理
            return false;
        }
    }
}
