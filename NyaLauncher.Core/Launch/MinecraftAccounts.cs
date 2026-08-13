using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NyaLauncher.Core.Launch;

/// <summary>可供 Minecraft 启动使用的统一账号契约。</summary>
public interface IMinecraftAccount
{
    string Username { get; }

    string Uuid { get; }

    string AccessToken { get; }

    string UserType { get; }

    string XboxUserId { get; }
}

/// <summary>不包含访问令牌的离线 Minecraft 账号。</summary>
public sealed record OfflineAccount : IMinecraftAccount
{
    private static readonly Regex UsernamePattern =
        new("^[A-Za-z0-9_]{1,16}$", RegexOptions.CultureInvariant);

    private OfflineAccount(string username, string uuid)
    {
        Username = username;
        Uuid = uuid;
    }

    public string Username { get; }

    /// <summary>与服务端离线模式一致的 32 位无连字符 UUID。</summary>
    public string Uuid { get; }

    public string AccessToken => "0";

    public string UserType => "legacy";

    public string XboxUserId => string.Empty;

    public static OfflineAccount Create(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        var normalized = username.Trim();
        if (!UsernamePattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "离线用户名必须为 1–16 位，只能包含英文字母、数字和下划线。",
                nameof(username));
        }

        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{normalized}"));

        // Java UUID.nameUUIDFromBytes 使用 UUID v3 和 RFC 4122 variant。
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return new OfflineAccount(normalized, Convert.ToHexStringLower(hash));
    }
}
