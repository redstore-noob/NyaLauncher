using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>
/// 全局账号存储：所有页面共享同一份内存列表，并通过 config.json 持久化。
/// <see cref="Current"/> 是 ObservableCollection，UI 直接绑定后自动响应增删，
/// 无需手动刷新。增删/排序后触发 <see cref="Changed"/> 事件供页面做额外逻辑
/// （如修复下拉框选中项）。
/// </summary>
public static class AccountStore
{
    public const string AccountsConfigKey = "accounts";

    /// <summary>内存中的权威账号列表（可观察集合，UI 绑定后自动同步）。</summary>
    public static ObservableCollection<LaunchAccount> Current { get; } = LoadFromDisk();

    /// <summary>列表首项是所有页面和组件共享的当前账号。</summary>
    public static LaunchAccount? Selected => Current.FirstOrDefault();

    /// <summary>账号列表发生变化（增/删/排序）时触发。订阅者异常相互隔离，不影响他人。</summary>
    public static event Action? Changed;

    public static void Add(LaunchAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        Current.Insert(0, account);
        Save();
        RaiseChanged();
    }

    public static void Remove(LaunchAccount account)
    {
        // 用对象相等判断（LaunchAccount 是引用类型，默认引用相等）。
        Current.Remove(account);
        // 注意：删除后允许列表为空，不再自动补充默认账号，
        // 否则用户删除最后一个账号时"删了又出现"，看起来像删除失败。
        Save();
        RaiseChanged();
    }

    /// <summary>把指定账号设为默认（移到列表顶部）。</summary>
    public static void MoveToTop(LaunchAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (ReferenceEquals(Current.FirstOrDefault(), account))
            return;

        if (Current.Remove(account))
        {
            Current.Insert(0, account);
            Save();
            RaiseChanged();
        }
    }

    public static string GetStableKey(LaunchAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var identity = account.Type switch
        {
            "microsoft" => account.Microsoft?.Uuid,
            "offline" => account.OfflineName,
            _ => account.DisplayName
        };
        return $"{account.Type}:{(string.IsNullOrWhiteSpace(identity) ? account.DisplayName : identity)}";
    }

    /// <summary>通过持久身份切换当前账号；成功后该账号会移动到列表首项。</summary>
    public static bool SelectByStableKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var account = Current.FirstOrDefault(candidate => string.Equals(
            GetStableKey(candidate),
            key,
            StringComparison.OrdinalIgnoreCase));
        if (account is null)
            return false;

        MoveToTop(account);
        return true;
    }

    public static LaunchAccount? FindByStableKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return Current.FirstOrDefault(candidate => string.Equals(
            GetStableKey(candidate),
            key,
            StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 在配置存储目录切换后，从新的 config.json 重新载入账号，同时保留集合实例，
    /// 使已经绑定到该集合的页面自动刷新。
    /// </summary>
    public static void Reload()
    {
        var loaded = LoadFromDisk();
        Current.Clear();
        foreach (var account in loaded)
            Current.Add(account);

        RaiseChanged();
    }

    public static void UpdateMicrosoftAccount(
        LaunchAccount account,
        MicrosoftAccount microsoftAccount)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(microsoftAccount);
        if (!Current.Contains(account) ||
            !string.Equals(account.Type, "microsoft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能更新账号存储中的正版账号。");
        }

        account.Microsoft = microsoftAccount;
        Save();
        RaiseChanged();
    }

    public static void UpdateOfflineSkin(LaunchAccount account, string skinId)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(skinId);
        if (!Current.Contains(account) ||
            !string.Equals(account.Type, "offline", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能更新账号存储中的离线账号。");
        }

        account.OfflineSkinId = skinId;
        Save();
        RaiseChanged();
    }

    /// <summary>是否已存在同名离线账号（忽略大小写）。</summary>
    public static bool HasOfflineName(string name) =>
        Current.Any(a => a.Type == "offline" &&
                         string.Equals(a.OfflineName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>把当前列表写回 config.json。</summary>
    public static void Save()
    {
        var dtos = Current.Select(account =>
            account.Type == "microsoft" && account.Microsoft is { } ms
                ? new AccountDto
                {
                    Type = "microsoft",
                    Username = ms.Username,
                    Uuid = ms.Uuid,
                    AccessToken = ms.AccessToken,
                    RefreshToken = ms.RefreshToken,
                    XboxUserId = ms.XboxUserId,
                    ClientId = ms.ClientId,
                    ExpiresAt = ms.ExpiresAt
                }
                : new AccountDto
                {
                    Type = "offline",
                    OfflineName = account.OfflineName,
                    OfflineSkinId = account.OfflineSkinId
                }).ToList();

        var json = JsonSerializer.Serialize(dtos);
        if (!string.IsNullOrWhiteSpace(json))
        {
            LauncherConfig.SetValue(AccountsConfigKey, json);
        }
    }

    /// <summary>触发 Changed 事件；每个订阅者单独 try/catch，防止一个页面出错中断其它页面。</summary>
    private static void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null)
            return;

        foreach (Action subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber();
            }
            catch (Exception ex)
            {
                // 记录但不中断：一个页面的刷新异常不应影响其它页面或阻塞账号操作。
                System.Diagnostics.Debug.WriteLine($"AccountStore.Changed 订阅者异常：{ex}");
            }
        }
    }

    private static ObservableCollection<LaunchAccount> LoadFromDisk()
    {
        var accounts = new ObservableCollection<LaunchAccount>();
        var json = LauncherConfig.GetValue(AccountsConfigKey);

        // 只要保存过 accounts（包括空数组 []），就按内容加载，保持用户的选择（允许空列表）。
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var dtos = JsonSerializer.Deserialize<List<AccountDto>>(json);
                if (dtos is not null)
                {
                    foreach (var dto in dtos)
                    {
                        if (dto.Type == "microsoft" && !string.IsNullOrWhiteSpace(dto.Username))
                        {
                            accounts.Add(new LaunchAccount
                            {
                                Type = "microsoft",
                                DisplayName = dto.Username,
                                Microsoft = new MicrosoftAccount
                                {
                                    Username = dto.Username,
                                    Uuid = dto.Uuid ?? string.Empty,
                                    AccessToken = dto.AccessToken ?? string.Empty,
                                    RefreshToken = dto.RefreshToken ?? string.Empty,
                                    XboxUserId = dto.XboxUserId ?? string.Empty,
                                    ClientId = dto.ClientId ?? string.Empty,
                                    ExpiresAt = dto.ExpiresAt ?? DateTimeOffset.MinValue
                                }
                            });
                        }
                        else if (dto.Type == "offline" && !string.IsNullOrWhiteSpace(dto.OfflineName))
                        {
                            accounts.Add(new LaunchAccount
                            {
                                Type = "offline",
                                DisplayName = dto.OfflineName,
                                OfflineName = dto.OfflineName,
                                OfflineSkinId = string.IsNullOrWhiteSpace(dto.OfflineSkinId)
                                    ? "steve"
                                    : dto.OfflineSkinId
                            });
                        }
                    }
                }

                return accounts;
            }
            catch
            {
                // 配置损坏时忽略，走下面的默认账号逻辑。
            }
        }

        // 从未配置过账号（或配置损坏）：兼容旧版 offlineUsername。
        var legacy = LauncherConfig.GetValue("offlineUsername");
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            accounts.Add(new LaunchAccount
            {
                Type = "offline",
                DisplayName = legacy,
                OfflineName = legacy
            });
            return accounts;
        }

        // 全新安装：提供一个默认离线账号，保证首次打开即可启动。
        accounts.Add(new LaunchAccount
        {
            Type = "offline",
            DisplayName = "Player_01",
            OfflineName = "Player_01"
        });
        return accounts;
    }

    private sealed class AccountDto
    {
        public string? Type { get; set; }
        public string? Username { get; set; }
        public string? Uuid { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? XboxUserId { get; set; }
        public string? ClientId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? OfflineName { get; set; }
        public string? OfflineSkinId { get; set; }
    }
}
