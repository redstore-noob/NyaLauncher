using System;
using System.Collections.Generic;
using System.Text.Json;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Launch.Auth;

/// <summary>
/// 组件展示账号覆盖：允许单个组件（如皮肤与披风）独立展示指定的正版账号，
/// 而不是永远跟随全局当前账号。「组件 Id → 账号稳定键」映射持久化在
/// config.json 的 <see cref="ConfigKey"/> 键；未设置、配置损坏或覆盖账号
/// 已被删除时，回退为全局当前账号（<see cref="AccountStore.Selected"/>）。
/// </summary>
public static class ComponentDisplayAccount
{
    public const string ConfigKey = "componentDisplayAccounts";

    /// <summary>
    /// 解析组件应展示的账号：优先取覆盖账号（存在时），否则回退全局当前账号；
    /// 无任何账号时返回 null。
    /// </summary>
    public static LaunchAccount? Resolve(string componentId)
    {
        var key = GetKey(componentId);
        var account = key is null ? null : AccountStore.FindByStableKey(key);
        return account ?? AccountStore.Selected;
    }

    /// <summary>读取组件展示账号的稳定键；null 表示跟随全局当前账号。</summary>
    public static string? GetKey(string componentId)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            return null;

        try
        {
            var json = LauncherConfig.GetValue(ConfigKey);
            if (string.IsNullOrWhiteSpace(json) ||
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) is not { } map ||
                !map.TryGetValue(componentId, out var key) ||
                string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return key;
        }
        catch
        {
            // 配置损坏时视为未设置覆盖
            return null;
        }
    }

    /// <summary>设置组件展示的账号（传 null 或空白恢复跟随全局当前账号）。</summary>
    public static void SetKey(string componentId, string? accountKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        var map = LoadMap();
        if (string.IsNullOrWhiteSpace(accountKey))
            map.Remove(componentId);
        else
            map[componentId] = accountKey;

        LauncherConfig.SetValue(ConfigKey, JsonSerializer.Serialize(map));
    }

    private static Dictionary<string, string> LoadMap()
    {
        try
        {
            var json = LauncherConfig.GetValue(ConfigKey);
            if (!string.IsNullOrWhiteSpace(json) &&
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) is { } map)
            {
                return map;
            }
        }
        catch
        {
            // 配置损坏时从空映射重建
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
