using System.Text.Json;
using System.Text.Json.Nodes;

namespace NyaLauncher.Core.Config;

/// <summary>
/// 管理启动器的 JSON 配置，同时保留旧版按键与 Java 路径 API。
/// </summary>
public class ConfigFileManager
{
    public struct JavaPathItem
    {
        public string JavaPath { get; set; }

        public string JavaVersion { get; set; }
    }

    private const string JavaPathKey = "javaPath";
    private const string MinecraftPathKey = "minecraftPath";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private string _filePath;
    private JsonObject _config = null!;

    public ConfigFileManager(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _config = LoadConfig();
    }

    /// <summary>
    /// 配置文件路径。切换路径时会立即加载目标配置，避免把旧文档写入新位置。
    /// </summary>
    public string FilePath
    {
        get
        {
            lock (_syncRoot)
                return _filePath;
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            lock (_syncRoot)
            {
                if (string.Equals(_filePath, value, StringComparison.Ordinal))
                    return;

                var previousPath = _filePath;
                _filePath = value;
                try
                {
                    _config = LoadConfig();
                }
                catch
                {
                    _filePath = previousPath;
                    throw;
                }
            }
        }
    }

    /// <summary>添加或更新字符串配置项。</summary>
    public bool ConfigItemAdd(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return false;

        return Update(config =>
        {
            config[key] = value;
            return true;
        }, "添加配置项");
    }

    /// <summary>读取字符串配置项；不存在或类型不匹配时返回 null。</summary>
    public string? ConfigItemRead(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        lock (_syncRoot)
        {
            try
            {
                return ReadString(_config[key]);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"读取配置项失败: {exception.Message}");
                return null;
            }
        }
    }

    /// <summary>删除配置项。</summary>
    public bool ConfigItemDelete(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return Update(config => config.Remove(key), "删除配置项");
    }

    /// <summary>获取已保存的 Java 路径。</summary>
    public List<JavaPathItem> JavaPathGet()
    {
        lock (_syncRoot)
        {
            try
            {
                if (_config[JavaPathKey] is not JsonArray entries)
                    return [];

                var result = new List<JavaPathItem>(entries.Count);
                foreach (var entry in entries.OfType<JsonObject>())
                {
                    if (!entry.ContainsKey("path") || !entry.ContainsKey("version"))
                        continue;

                    result.Add(new JavaPathItem
                    {
                        JavaPath = ReadString(entry["path"]) ?? string.Empty,
                        JavaVersion = ReadString(entry["version"]) ?? string.Empty
                    });
                }

                return result;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"读取 Java 路径列表失败: {exception.Message}");
                return [];
            }
        }
    }

    /// <summary>
    /// 用唯一的首选项替换 Java 路径列表。整个替换只进行一次原子写入。
    /// </summary>
    public bool JavaPathSet(string javaPath, string javaVersion)
    {
        if (string.IsNullOrWhiteSpace(javaPath) || string.IsNullOrWhiteSpace(javaVersion))
            return false;

        return Update(config =>
        {
            config[JavaPathKey] = new JsonArray
            {
                new JsonObject
                {
                    ["path"] = javaPath,
                    ["version"] = javaVersion
                }
            };
            return true;
        }, "保存 Java 路径");
    }

    /// <summary>
    /// 追加一条 Java 路径。路径已存在时更新其版本（不重复添加），返回 true。
    /// </summary>
    public bool JavaPathAdd(string javaPath, string javaVersion)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return false;

        return Update(config =>
        {
            var entries = GetOrCreateJavaEntries(config);
            foreach (var entry in entries)
            {
                if (ReadString(entry["path"]) is { } existing &&
                    string.Equals(existing, javaPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(javaVersion))
                        entry["version"] = javaVersion;
                    return true;
                }
            }

            entries.Add(new JsonObject
            {
                ["path"] = javaPath,
                ["version"] = javaVersion ?? string.Empty
            });
            return true;
        }, "添加 Java 路径");
    }

    /// <summary>移除指定路径的 Java 条目；不存在时返回 false。</summary>
    public bool JavaPathRemove(string javaPath)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return false;

        return Update(config =>
        {
            var entries = GetOrCreateJavaEntries(config);
            var removed = false;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (ReadString(entries[i]["path"]) is { } existing &&
                    string.Equals(existing, javaPath, StringComparison.OrdinalIgnoreCase))
                {
                    entries.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }, "移除 Java 路径");
    }

    /// <summary>把指定路径移到列表首位（成为默认 Java）；路径不存在时返回 false。</summary>
    public bool JavaPathSetPrimary(string javaPath)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return false;

        return Update(config =>
        {
            var entries = GetOrCreateJavaEntries(config);
            for (var i = 0; i < entries.Count; i++)
            {
                if (ReadString(entries[i]["path"]) is { } existing &&
                    string.Equals(existing, javaPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (i == 0)
                        return true;
                    var item = entries[i];
                    entries.RemoveAt(i);
                    entries.Insert(0, item);
                    return true;
                }
            }
            return false;
        }, "设为默认 Java");
    }

    private static JsonArray GetOrCreateJavaEntries(JsonObject config)
    {
        if (config[JavaPathKey] is JsonArray array)
            return array;
        var created = new JsonArray();
        config[JavaPathKey] = created;
        return created;
    }

    /// <summary>获取 Minecraft 游戏路径；未配置时返回空字符串。</summary>
    public string MinecraftPathGet()
    {
        lock (_syncRoot)
        {
            try
            {
                return ReadString(_config[MinecraftPathKey]) ?? string.Empty;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"读取 Minecraft 路径失败: {exception.Message}");
                return string.Empty;
            }
        }
    }

    /// <summary>设置 Minecraft 游戏路径。</summary>
    public bool MinecraftPathSet(string minecraftPath) =>
        !string.IsNullOrWhiteSpace(minecraftPath) &&
        ConfigItemAdd(MinecraftPathKey, minecraftPath);

    private JsonObject LoadConfig()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
                return root ?? throw new JsonException("配置文件根节点必须是 JSON 对象。");
            }
        }
        catch (JsonException exception)
        {
            // 仅"内容损坏"才走备份+重建：瞬时 IO 失败绝不能用默认配置覆盖原文件
            Console.WriteLine($"配置文件损坏: {exception.Message}，已备份并重建默认配置");
            var backupPath = FilePath + $".corrupted-{DateTime.Now:yyyyMMddHHmmss}.bak";
            try
            {
                File.Copy(FilePath, backupPath, overwrite: true);
                Console.WriteLine($"已备份损坏的配置文件到: {backupPath}");
            }
            catch (Exception copyException)
            {
                // 备份失败：为避免数据彻底丢失，抛出异常让调用方处理，而非覆盖原文件
                throw new IOException(
                    $"配置文件损坏且无法备份：{FilePath}（{copyException.Message}）", exception);
            }

            var rebuiltConfig = CreateDefaultConfig();
            if (!SaveConfig(rebuiltConfig))
                throw new IOException($"无法重建配置文件：{Path.GetFullPath(FilePath)}");
            return rebuiltConfig;
        }
        catch (IOException exception)
        {
            // 文件被占用、磁盘抖动等瞬时 IO 失败：不覆盖原文件，向上抛出
            throw new IOException($"读取配置文件失败：{FilePath}", exception);
        }

        var defaultConfig = CreateDefaultConfig();
        if (!SaveConfig(defaultConfig))
        {
            throw new IOException($"无法创建配置文件：{Path.GetFullPath(FilePath)}");
        }

        return defaultConfig;
    }

    private bool Update(Func<JsonObject, bool> mutation, string operationName)
    {
        lock (_syncRoot)
        {
            JsonObject? previous = null;
            try
            {
                previous = (JsonObject)_config.DeepClone();
                if (!mutation(_config))
                    return false;

                if (SaveConfig(_config))
                    return true;

                _config = previous;
                return false;
            }
            catch (Exception exception)
            {
                if (previous is not null)
                    _config = previous;

                Console.WriteLine($"{operationName}失败: {exception.Message}");
                return false;
            }
        }
    }

    private bool SaveConfig(JsonObject config)
    {
        string? temporaryPath = null;
        try
        {
            var fullPath = Path.GetFullPath(FilePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            // 临时文件写入同一目录，确保 rename 是同一文件系统内的原子操作
            temporaryPath = Path.Combine(
                directory ?? Directory.GetCurrentDirectory(),
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(
                temporaryPath,
                config.ToJsonString(SerializerOptions));

            if (OperatingSystem.IsWindows() && File.Exists(fullPath))
            {
                // Windows 原子替换：File.Replace 先写新文件再原子交换，避免"先删后移"丢失配置
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, fullPath, overwrite: true);
            }

            temporaryPath = null;
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"保存配置文件失败: {exception.Message}");
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // 临时文件清理失败不应掩盖原始保存结果。
                }
            }
        }
    }

    private static JsonObject CreateDefaultConfig() => new()
    {
        [JavaPathKey] = new JsonArray(),
        [MinecraftPathKey] = string.Empty
    };

    private static string? ReadString(JsonNode? value)
    {
        if (value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var result))
        {
            return null;
        }

        return result;
    }
}
