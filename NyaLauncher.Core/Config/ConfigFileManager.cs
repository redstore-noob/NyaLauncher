using System.Text.Json;
using System.Text.Json.Nodes;

namespace NyaLauncher.Core.Config;

/// <summary>
/// 以原子替换方式读写启动器的 JSON 配置。
/// </summary>
internal sealed class ConfigFileManager
{
    internal readonly record struct JavaPathItem(string JavaPath, string JavaVersion);

    private const string JavaPathKey = "javaPath";
    private const string MinecraftPathKey = "minecraftPath";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private JsonObject _config;

    public ConfigFileManager(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _config = Load();
    }

    public bool ConfigItemAdd(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return false;

        return Update(config =>
        {
            config[key] = value;
            return true;
        });
    }

    public string? ConfigItemRead(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        lock (_gate)
            return ReadString(_config[key]);
    }

    public bool ConfigItemDelete(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return Update(config => config.Remove(key));
    }

    public IReadOnlyList<JavaPathItem> JavaPathGet()
    {
        lock (_gate)
        {
            if (_config[JavaPathKey] is not JsonArray entries)
                return [];

            return entries
                .OfType<JsonObject>()
                .Select(entry => new JavaPathItem(
                    ReadString(entry["path"]) ?? string.Empty,
                    ReadString(entry["version"]) ?? string.Empty))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.JavaPath))
                .ToArray();
        }
    }

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
        });
    }

    public string MinecraftPathGet()
    {
        lock (_gate)
            return ReadString(_config[MinecraftPathKey]) ?? string.Empty;
    }

    public bool MinecraftPathSet(string minecraftPath) =>
        !string.IsNullOrWhiteSpace(minecraftPath) &&
        ConfigItemAdd(MinecraftPathKey, minecraftPath);

    private JsonObject Load()
    {
        if (!File.Exists(_filePath))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(_filePath)) as JsonObject ??
                throw new JsonException("配置文件根节点必须是 JSON 对象。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"加载配置文件失败：{exception.Message}");
            return new JsonObject();
        }
    }

    private bool Update(Func<JsonObject, bool> update)
    {
        lock (_gate)
        {
            var candidate = (JsonObject)_config.DeepClone();
            if (!update(candidate))
                return false;

            Save(candidate);
            _config = candidate;
            return true;
        }
    }

    private void Save(JsonObject candidate)
    {
        var directory = Path.GetDirectoryName(_filePath) ??
            throw new InvalidOperationException("配置文件缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, candidate.ToJsonString(SerializerOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
}
