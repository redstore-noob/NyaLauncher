using System;
using System.IO;
using System.Linq;

namespace NyaLauncher.Core.Config;

/// <summary>
/// 启动器配置的统一入口。底层由 <see cref="ConfigFileManage"/> 负责 JSON 读写，
/// 配置文件为应用目录下的 config.json。
/// </summary>
public static class LauncherConfig
{
    /// <summary>配置文件路径：应用基目录下的 config.json。</summary>
    public static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly ConfigFileManage Store = new(FilePath);

    /// <summary>游戏根目录（.minecraft 或自定义目录）；未配置时返回 null。</summary>
    public static string? GameDirectory
    {
        get
        {
            var value = Store.MinecraftPathGet();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>首选 Java 可执行文件（java.exe）路径；未配置时返回 null。</summary>
    public static string? JavaExecutable
    {
        get
        {
            var item = Store.JavaPathGet().FirstOrDefault();
            return string.IsNullOrWhiteSpace(item.JavaPath) ? null : item.JavaPath;
        }
    }

    /// <summary>首选 Java 的版本号（如 21）；未配置时返回 null。</summary>
    public static string? JavaVersion
    {
        get
        {
            var item = Store.JavaPathGet().FirstOrDefault();
            return string.IsNullOrWhiteSpace(item.JavaVersion) ? null : item.JavaVersion;
        }
    }

    /// <summary>保存游戏目录。</summary>
    public static bool SaveGameDirectory(string path) =>
        !string.IsNullOrWhiteSpace(path) && Store.MinecraftPathSet(path.Trim());

    /// <summary>
    /// 保存首选 Java（java.exe 路径 + 版本）。采用「先清空再写入」策略，
    /// 保证 config.json 中的 javaPath 始终只有一条首选配置。
    /// </summary>
    public static bool SaveJava(string javaPath, string javaVersion)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return false;

        Store.ConfigItemDelete("javaPath");
        return Store.JavaPathAdd(
            javaPath.Trim(),
            string.IsNullOrWhiteSpace(javaVersion) ? "unknown" : javaVersion.Trim());
    }

    /// <summary>删除已保存的 Java 配置（恢复自动检测）。</summary>
    public static void ClearJava() => Store.ConfigItemDelete("javaPath");

    /// <summary>保存/更新任意字符串配置项。</summary>
    public static bool SetValue(string key, string value) =>
        !string.IsNullOrWhiteSpace(key) &&
        !string.IsNullOrWhiteSpace(value) &&
        Store.ConfigItemAdd(key, value);

    /// <summary>读取任意字符串配置项；不存在时返回 null。</summary>
    public static string? GetValue(string key)
    {
        var value = Store.ConfigItemRead(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
