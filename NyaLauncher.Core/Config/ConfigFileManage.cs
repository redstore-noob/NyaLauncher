using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq;

namespace NyaLauncher.Core.Config
{
    public struct JavaPathItem
    {
        public string JavaPath { get; set; }
        public string JavaVersion { get; set; }
    }

    /// <summary>
    /// 配置文件管理类，处理 JSON 格式的配置文件，包含 Java 路径、Java 版本、游戏路径等信息
    /// </summary>
    public class ConfigFileManage
    {
        public string FilePath { get; set; }
        private JsonDocument _configDoc;

        /// <summary>
        /// 初始化配置文件管理器
        /// </summary>
        /// <param name="filePath">配置文件路径</param>
        public ConfigFileManage(string filePath)
        {
            FilePath = filePath;
            LoadConfig();
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    CreateDefaultConfig();
                    return;
                }

                string jsonContent = File.ReadAllText(FilePath);
                _configDoc = JsonDocument.Parse(jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配置文件失败: {ex.Message}，将创建默认配置");
                CreateDefaultConfig();
            }
        }

        /// <summary>
        /// 创建默认配置文件
        /// </summary>
        private void CreateDefaultConfig()
        {
            var defaultConfig = new
            {
                javaPath = new List<dynamic>(),
                minecraftPath = ""
            };

            string jsonContent = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, jsonContent);
            _configDoc = JsonDocument.Parse(jsonContent);
        }

        /// <summary>
        /// 保存配置文件到磁盘
        /// </summary>
        private bool SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                using (var stream = File.Create(FilePath))
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    _configDoc.WriteTo(writer);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置文件失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加或更新配置项
        /// </summary>
        /// <param name="key">配置项键</param>
        /// <param name="value">配置项值</param>
        /// <returns>操作是否成功</returns>
        public bool ConfigItemAdd(string key, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var rootElement = _configDoc.RootElement;

                // 读取现有配置
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    rootElement.GetRawText(), options) ?? new Dictionary<string, JsonElement>();

                // 更新或添加键值对
                config[key] = JsonSerializer.SerializeToElement(value);

                // 重新序列化并重新加载
                string jsonContent = JsonSerializer.Serialize(config, options);
                _configDoc = JsonDocument.Parse(jsonContent);

                return SaveConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加配置项失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取配置项的值
        /// </summary>
        /// <param name="key">配置项键</param>
        /// <returns>配置项值，不存在时返回 null</returns>
        public string ConfigItemRead(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                var rootElement = _configDoc.RootElement;

                if (rootElement.TryGetProperty(key, out var value))
                {
                    return value.GetString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取配置项失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 删除配置项
        /// </summary>
        /// <param name="key">配置项键</param>
        /// <returns>操作是否成功</returns>
        public bool ConfigItemDelete(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return false;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var rootElement = _configDoc.RootElement;

                // 读取现有配置
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    rootElement.GetRawText(), options) ?? new Dictionary<string, JsonElement>();

                // 删除键
                if (config.Remove(key))
                {
                    // 重新序列化并重新加载
                    string jsonContent = JsonSerializer.Serialize(config, options);
                    _configDoc = JsonDocument.Parse(jsonContent);
                    return SaveConfig();
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除配置项失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取 Java 路径列表
        /// </summary>
        /// <returns>关于 JavaPath 的列表，列表中每个项代表一个 Java 路径 + Java 版本</returns>
        public List<JavaPathItem> JavaPathGet()
        {
            var javaPathList = new List<JavaPathItem>();

            try
            {
                var rootElement = _configDoc.RootElement;

                if (rootElement.TryGetProperty("javaPath", out var javaPathArrayElement))
                {
                    if (javaPathArrayElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in javaPathArrayElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("path", out var pathElement) &&
                                item.TryGetProperty("version", out var versionElement))
                            {
                                javaPathList.Add(new JavaPathItem
                                {
                                    JavaPath = pathElement.GetString() ?? "",
                                    JavaVersion = versionElement.GetString() ?? ""
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取 Java 路径列表失败: {ex.Message}");
            }

            return javaPathList;
        }

        /// <summary>
        /// 添加 Java 路径
        /// </summary>
        /// <param name="javaPath">Java 路径</param>
        /// <param name="javaVersion">Java 版本</param>
        /// <returns>操作是否成功</returns>
        public bool JavaPathAdd(string javaPath, string javaVersion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(javaPath) || string.IsNullOrWhiteSpace(javaVersion))
                {
                    return false;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var rootElement = _configDoc.RootElement;

                // 读取现有配置
                var config = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    rootElement.GetRawText(), options) ?? new Dictionary<string, object>();

                // 获取或创建 javaPath 数组
                if (!config.ContainsKey("javaPath"))
                {
                    config["javaPath"] = new List<Dictionary<string, string>>();
                }

                var javaPathList = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(
                    JsonSerializer.Serialize(config["javaPath"])) ?? new List<Dictionary<string, string>>();

                // 检查是否已存在
                if (!javaPathList.Any(j => j["path"] == javaPath))
                {
                    javaPathList.Add(new Dictionary<string, string>
                    {
                        { "path", javaPath },
                        { "version", javaVersion }
                    });

                    config["javaPath"] = javaPathList;

                    // 重新序列化并重新加载
                    string jsonContent = JsonSerializer.Serialize(config, options);
                    _configDoc = JsonDocument.Parse(jsonContent);
                    return SaveConfig();
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加 Java 路径失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取 Minecraft 路径
        /// </summary>
        /// <returns>Minecraft 游戏路径</returns>
        public string MinecraftPathGet()
        {
            try
            {
                var rootElement = _configDoc.RootElement;

                if (rootElement.TryGetProperty("minecraftPath", out var minecraftPathElement))
                {
                    return minecraftPathElement.GetString() ?? "";
                }

                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取 Minecraft 路径失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 设置 Minecraft 路径
        /// </summary>
        /// <param name="minecraftPath">Minecraft 游戏路径</param>
        /// <returns>操作是否成功</returns>
        public bool MinecraftPathSet(string minecraftPath)
        {
            if (string.IsNullOrWhiteSpace(minecraftPath))
            {
                return false;
            }

            return ConfigItemAdd("minecraftPath", minecraftPath);
        }
    }
}
