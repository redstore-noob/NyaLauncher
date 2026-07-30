using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NyaLauncher.Core.Launch;

/// <summary>
/// 不包含任何访问令牌的离线 Minecraft 账号。
/// </summary>
public sealed record OfflineAccount
{
    private static readonly Regex UsernamePattern =
        new("^[A-Za-z0-9_]{1,16}$", RegexOptions.CultureInvariant);

    private OfflineAccount(string username, string uuid)
    {
        Username = username;
        Uuid = uuid;
    }

    public string Username { get; }

    /// <summary>
    /// 与服务端离线模式一致的 32 位无连字符 UUID。
    /// </summary>
    public string Uuid { get; }

    public static OfflineAccount Create(string username)
    {
        var normalized = username.Trim();
        if (!UsernamePattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "离线用户名必须为 1–16 位，只能包含英文字母、数字和下划线。",
                nameof(username));
        }

        var source = Encoding.UTF8.GetBytes($"OfflinePlayer:{normalized}");
        var hash = MD5.HashData(source);

        // Java UUID.nameUUIDFromBytes 使用 UUID v3，并设置 RFC 4122 variant。
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return new OfflineAccount(normalized, Convert.ToHexStringLower(hash));
    }
}
