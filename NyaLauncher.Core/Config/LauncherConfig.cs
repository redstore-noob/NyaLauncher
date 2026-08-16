using System;
using System.IO;
using System.Linq;
using NyaLauncher.Core.Tools;

namespace NyaLauncher.Core.Config;

/// <summary>
/// 启动器配置的统一入口。底层由 <see cref="ConfigFileManage"/> 负责 JSON 读写。
/// 配置文件为存储目录下的 config.json，默认目录与 workspace.json 保持一致
/// （%LOCALAPPDATA%\NyaLauncher），可通过 <see cref="SetStorageDirectory"/> 同步工作区存储目录。
/// </summary>
public static class LauncherConfig
{
    /// <summary>默认存储目录：%LOCALAPPDATA%\NyaLauncher（与 workspace.json 默认目录一致）。</summary>
    public static string DefaultStorageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NyaLauncher");

    private static readonly object SyncRoot = new();
    private static string _storageDirectory = DefaultStorageDirectory;
    private static ConfigFileManage? _store;

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
            if (PathsEqual(normalized, _storageDirectory))
                return;

            StorageDirectory = normalized;
            _store = null; // 下次访问时用新路径重新加载
        }
    }

    private static bool PathsEqual(string left, string right) =>
        PathUtil.PathsEqual(left, right);

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

    private static string GetFilePath() =>
        Path.Combine(_storageDirectory, "config.json");

    private static T WithStore<T>(Func<ConfigFileManage, T> action)
    {
        lock (SyncRoot)
        {
            _store ??= new ConfigFileManage(GetFilePath());
            return action(_store);
        }
    }
}
