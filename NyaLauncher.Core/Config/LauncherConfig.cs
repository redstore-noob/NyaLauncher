using System;
using System.IO;
using System.Linq;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Core.Config;

/// <summary>
/// 启动器配置的统一入口。底层由 <see cref="ConfigFileManager"/> 负责 JSON 读写。
/// 配置文件为存储目录下的 config.json，默认目录与 workspace.json 保持一致
/// （%USERPROFILE%\NyaLauncher），可通过 <see cref="SetStorageDirectory"/> 同步工作区存储目录。
/// </summary>
public static class LauncherConfig
{
    /// <summary>默认存储目录：%USERPROFILE%\NyaLauncher（与 workspace.json 默认目录一致，便于用户直接找到并编辑）。</summary>
    public static string DefaultStorageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "NyaLauncher");

    /// <summary>
    /// 旧版默认存储目录：%LOCALAPPDATA%\NyaLauncher。
    /// 仅用于从旧版本一次性迁移配置到 <see cref="DefaultStorageDirectory"/>。
    /// </summary>
    public static string LegacyDefaultStorageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NyaLauncher");

    private static readonly object SyncRoot = new();
    private static string _storageDirectory = DefaultStorageDirectory;
    private static ConfigFileManager? _store;

    /// <summary>config.json 所在目录；默认与 workspace.json 同目录。</summary>
    public static string StorageDirectory
    {
        get
        {
            lock (SyncRoot)
                return _storageDirectory;
        }
        private set
        {
            lock (SyncRoot)
                _storageDirectory = value;
        }
    }

    /// <summary>配置文件路径：存储目录下的 config.json。</summary>
    public static string FilePath
    {
        get
        {
            lock (SyncRoot)
                return GetFilePath();
        }
    }

    /// <summary>
    /// 切换 config.json 的读取目录。文件迁移与冲突处理由前端的配置存储协调流程完成；
    /// 本方法只重置底层存储，使后续读取立即应用新目录中的配置。
    /// </summary>
    public static void SetStorageDirectory(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageDirectory));
        lock (SyncRoot)
        {
            if (PathUtil.PathsEqual(normalized, _storageDirectory))
                return;

            StorageDirectory = normalized;
            _store = null; // 下次访问时用新路径重新加载
        }
    }

    /// <summary>游戏根目录（.minecraft 或自定义目录）；未配置时返回 null。</summary>
    public static string? GameDirectory
    {
        get => WithStore(store =>
        {
            var value = store.MinecraftPathGet();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        });
    }

    /// <summary>首选 Java 可执行文件（java.exe）路径；未配置时返回 null。</summary>
    public static string? JavaExecutable
    {
        get => WithStore(store =>
        {
            var item = store.JavaPathGet().FirstOrDefault();
            return string.IsNullOrWhiteSpace(item.JavaPath) ? null : item.JavaPath;
        });
    }

    /// <summary>首选 Java 的版本号（如 21）；未配置时返回 null。</summary>
    public static string? JavaVersion
    {
        get => WithStore(store =>
        {
            var item = store.JavaPathGet().FirstOrDefault();
            return string.IsNullOrWhiteSpace(item.JavaVersion) ? null : item.JavaVersion;
        });
    }

    /// <summary>保存游戏目录。</summary>
    public static bool SaveGameDirectory(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        WithStore(store => store.MinecraftPathSet(path.Trim()));

    /// <summary>
    /// 保存首选 Java（java.exe 路径 + 版本）。采用「先清空再写入」策略，
    /// 保证 config.json 中的 javaPath 始终只有一条首选配置。
    /// </summary>
    public static bool SaveJava(string javaPath, string javaVersion)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return false;

        return WithStore(store => store.JavaPathSet(
            javaPath.Trim(),
            string.IsNullOrWhiteSpace(javaVersion) ? "unknown" : javaVersion.Trim()));
    }

    /// <summary>删除已保存的 Java 配置（恢复自动检测）。</summary>
    public static void ClearJava() =>
        WithStore(store => store.ConfigItemDelete("javaPath"));

    /// <summary>已保存的全部 Java 路径（列表首位为默认 Java）。</summary>
    public static IReadOnlyList<ConfigFileManager.JavaPathItem> GetJavaPaths() =>
        WithStore(store => store.JavaPathGet());

    /// <summary>添加一条 Java 路径；路径已存在时更新其版本。返回是否成功。</summary>
    public static bool AddJava(string javaPath, string javaVersion)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return false;
        return WithStore(store => store.JavaPathAdd(
            javaPath.Trim(),
            string.IsNullOrWhiteSpace(javaVersion) ? "unknown" : javaVersion.Trim()));
    }

    /// <summary>移除一条 Java 路径。返回是否移除成功。</summary>
    public static bool RemoveJava(string javaPath) =>
        !string.IsNullOrWhiteSpace(javaPath) &&
        WithStore(store => store.JavaPathRemove(javaPath.Trim()));

    /// <summary>把指定路径设为默认 Java（列表首位）。返回是否成功。</summary>
    public static bool SetPrimaryJava(string javaPath) =>
        !string.IsNullOrWhiteSpace(javaPath) &&
        WithStore(store => store.JavaPathSetPrimary(javaPath.Trim()));

    /// <summary>
    /// 全局默认版本隔离设置。true = 新实例默认隔离（mods/config/saves 各实例独立），
    /// false = 默认共享 .minecraft 根目录。未配置时默认 true（启用隔离，更安全、实例互不污染）。
    /// 仅在版本自身的 <c>IsVersionIsolationEnabled</c> 为 null 时生效。
    /// </summary>
    public static bool? DefaultVersionIsolation
    {
        get
        {
            var value = GetValue("defaultVersionIsolation");
            // 缺省启用隔离：避免不同实例的 mods/saves/config 互相污染
            return bool.TryParse(value, out var result) ? result : true;
        }
    }

    /// <summary>保存全局默认版本隔离设置。</summary>
    public static void SaveDefaultVersionIsolation(bool? value)
    {
        if (value.HasValue)
            SetValue("defaultVersionIsolation", value.Value.ToString());
        else
            WithStore(store => store.ConfigItemDelete("defaultVersionIsolation"));
    }

    /// <summary>
    /// 启动前是否校验游戏文件完整性并自动补全缺失文件。默认 true。
    /// </summary>
    public static bool VerifyFilesBeforeLaunch
    {
        get
        {
            var value = GetValue("verifyFilesBeforeLaunch");
            // 未设置时默认开启
            return value is null || bool.TryParse(value, out var result) && result;
        }
    }

    /// <summary>保存启动前文件校验设置。</summary>
    public static void SaveVerifyFilesBeforeLaunch(bool enabled) =>
        SetValue("verifyFilesBeforeLaunch", enabled.ToString());

    /// <summary>保存/更新任意字符串配置项。</summary>
    public static bool SetValue(string key, string value) =>
        !string.IsNullOrWhiteSpace(key) &&
        !string.IsNullOrWhiteSpace(value) &&
        WithStore(store => store.ConfigItemAdd(key, value));

    /// <summary>读取任意字符串配置项；不存在时返回 null。</summary>
    public static string? GetValue(string key)
    {
        return WithStore(store =>
        {
            var value = store.ConfigItemRead(key);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        });
    }

    /// <summary>删除配置项；不存在时无副作用。返回是否删除成功。</summary>
    public static bool ClearValue(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        WithStore(store => store.ConfigItemDelete(key));

    private static string GetFilePath() =>
        Path.Combine(_storageDirectory, "config.json");

    private static T WithStore<T>(Func<ConfigFileManager, T> action)
    {
        lock (SyncRoot)
        {
            _store ??= new ConfigFileManager(GetFilePath());
            return action(_store);
        }
    }
}
