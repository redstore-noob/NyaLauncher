using NyaLauncher.Core.Models;

namespace NyaLauncher.Core.Download;

/// <summary>
/// Minecraft 版本筛选服务
/// </summary>
public static class VersionFilter
{
    /// <summary>
    /// 根据筛选类型过滤版本列表
    /// </summary>
    /// <param name="versions">原版本列表</param>
    /// <param name="filter">筛选键: "all" 全部, "release" 正式版, "snapshot" 快照版, "old" 远古版本</param>
    /// <returns>过滤后的版本列表</returns>
    public static List<MinecraftVersion> Apply(List<MinecraftVersion> versions, string filter)
    {
        return filter switch
        {
            "release"  => [.. versions.Where(v => v.Type == "release")],
            "snapshot" => [.. versions.Where(v => v.Type == "snapshot")],
            "old"      => [.. versions.Where(v => v.Type is "old_beta" or "old_alpha")],
            _          => versions
        };
    }
}
