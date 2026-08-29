using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Network;

/// <summary>服务器状态查询结果（Minecraft Server List Ping 协议）。</summary>
public sealed record MinecraftServerStatus(
    string Motd,
    string? VersionName,
    int ProtocolVersion,
    int OnlinePlayers,
    int MaxPlayers,
    string? IconPath = null)
{
    public static MinecraftServerStatus Unreachable(string reason) =>
        new(reason, null, 0, 0, 0);
}

/// <summary>
/// 原版 Minecraft 服务器状态查询（Server List Ping）：
/// TCP 连接 → 握手包（ nextState=1）→ 状态请求 → 读取 JSON 状态响应。
/// 仅依赖原版协议，不额外引入依赖。
/// </summary>
public static class MinecraftServerPinger
{
    // 跨版本服务器（ViaVersion 等）可能拒绝特定协议的握手，依次尝试代表性协议直至成功：
    // 47=1.8.9（大多数服务器兼容）、767=1.21、763=1.20.1、340=1.12.2
    private static readonly int[] HandshakeProtocolCandidates = [47, 767, 763, 340];
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 解析 "host" / "host:port" / "[ipv6]:port" 形式的服务器地址。
    /// 无端口时使用默认端口 25565。
    /// </summary>
    public static (string Host, int Port) ParseAddress(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var address = input.Trim();
        if (address.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            address = address["tcp://".Length..];

        // [ipv6]:port
        if (address.StartsWith('['))
        {
            var closing = address.IndexOf(']');
            if (closing > 0)
            {
                var host = address[1..closing];
                var port = 25565;
                if (closing + 1 < address.Length && address[closing + 1] == ':')
                    port = ParsePort(address[(closing + 2)..]);
                return (host, port);
            }
        }

        // host:port（仅一个冒号时按 IPv4/域名处理；多个冒号视为裸 IPv6）
        var firstColon = address.IndexOf(':');
        var lastColon = address.LastIndexOf(':');
        if (firstColon > 0 && firstColon == lastColon &&
            int.TryParse(address[(firstColon + 1)..], out var parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            return (address[..firstColon], parsedPort);
        }

        return (address, 25565);
    }

    /// <summary>查询服务器状态；失败时抛出异常（调用方决定降级展示）。</summary>
    public static async Task<MinecraftServerStatus> PingAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallCts.CancelAfter(OverallTimeout);

        Exception? lastError = null;
        foreach (var protocol in HandshakeProtocolCandidates)
        {
            try
            {
                return await PingOnceAsync(host, port, protocol, overallCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 总超时耗尽：不再重试，抛出可读的超时错误
                if (overallCts.IsCancellationRequested)
                    throw new IOException("连接服务器超时，请检查地址或稍后重试。", exception);
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("无法连接服务器。");
    }

    private static async Task<MinecraftServerStatus> PingOnceAsync(
        string host,
        int port,
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();

        // 握手包：packetId=0x00 + 协议版本 + 主机名 + 端口 + nextState=1
        using var handshakeBody = new MemoryStream();
        WriteVarint(handshakeBody, 0x00);
        WriteVarint(handshakeBody, protocolVersion);
        WriteString(handshakeBody, host);
        WriteUnsignedShort(handshakeBody, (ushort)port);
        WriteVarint(handshakeBody, 1);
        await WritePacketAsync(stream, handshakeBody.ToArray(), timeoutCts.Token).ConfigureAwait(false);

        // 状态请求：仅 packetId=0x00
        await WritePacketAsync(stream, [0x00], timeoutCts.Token).ConfigureAwait(false);

        var payload = await ReadPacketAsync(stream, timeoutCts.Token).ConfigureAwait(false);
        if (payload.Length == 0 || payload[0] != 0x00)
            throw new InvalidOperationException("服务器返回了意外的状态响应。");

        using var reader = new MemoryStream(payload, 1, payload.Length - 1);
        var json = ReadString(reader);

        using var document = JsonDocument.Parse(json);
        return ParseStatus(document.RootElement, host, port);
    }

    private static MinecraftServerStatus ParseStatus(JsonElement root, string host, int port)
    {
        var motd = ExtractDescription(root);
        var versionName = default(string?);
        var protocol = 0;
        if (root.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.Object)
        {
            if (version.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                versionName = name.GetString();
            }
            if (version.TryGetProperty("protocol", out var protocolElement) &&
                protocolElement.TryGetInt32(out var parsedProtocol))
            {
                protocol = parsedProtocol;
            }
        }

        var online = 0;
        var max = 0;
        if (root.TryGetProperty("players", out var players) &&
            players.ValueKind == JsonValueKind.Object)
        {
            if (players.TryGetProperty("online", out var onlineElement) &&
                onlineElement.TryGetInt32(out var parsedOnline))
            {
                online = parsedOnline;
            }
            if (players.TryGetProperty("max", out var maxElement) &&
                maxElement.TryGetInt32(out var parsedMax))
            {
                max = parsedMax;
            }
        }

        return new MinecraftServerStatus(
            motd, versionName, protocol, online, max, TryCacheFavicon(root, host, port));
    }

    /// <summary>
    /// 解析状态响应中的 favicon 字段（"data:image/png;base64,…"），解码后缓存为本地 PNG
    /// 并返回其路径；字段缺失、格式非法或磁盘写入失败返回 null。
    /// </summary>
    private static string? TryCacheFavicon(JsonElement root, string host, int port)
    {
        try
        {
            if (!root.TryGetProperty("favicon", out var favicon) ||
                favicon.ValueKind != JsonValueKind.String ||
                favicon.GetString() is not { } dataUri)
                return null;

            const string prefix = "data:image/png;base64,";
            if (!dataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var bytes = Convert.FromBase64String(dataUri[prefix.Length..]);
            if (bytes.Length == 0 || bytes.Length > 128 * 1024)
                return null;

            var directory = Path.Combine(LauncherConfig.StorageDirectory, "cache", "server-icons");
            Directory.CreateDirectory(directory);
            var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{host}:{port}")))[..16].ToLowerInvariant();
            var path = Path.Combine(directory, hash + ".png");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// description 兼容三种历史形态：纯字符串、{"text": ...}、含 extra 数组的聊天组件树。
    /// 递归展开为纯文本；JSON 组件的 color/bold 等样式属性转译为 § 样式码保留。
    /// </summary>
    private static string ExtractDescription(JsonElement root)
    {
        if (!root.TryGetProperty("description", out var description))
            return "无 MOTD";
        return FlattenChatComponent(description).TrimEnd();
    }

    private static string FlattenChatComponent(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                // 历史形态：字符串本身可能已含 § 样式码
                return element.GetString() ?? string.Empty;

            case JsonValueKind.Array:
            {
                var builder = new StringBuilder();
                foreach (var child in element.EnumerateArray())
                    builder.Append(FlattenChatComponent(child));
                return builder.ToString();
            }

            case JsonValueKind.Object:
            {
                var builder = new StringBuilder();
                AppendStylePrefix(builder, element);
                if (element.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
                if (element.TryGetProperty("extra", out var extra) &&
                    extra.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in extra.EnumerateArray())
                        builder.Append(FlattenChatComponent(child));
                }
                return builder.ToString();
            }

            default:
                return string.Empty;
        }
    }

    /// <summary>把 JSON 聊天组件的样式属性转译为 § 样式码前缀。</summary>
    private static void AppendStylePrefix(StringBuilder builder, JsonElement element)
    {
        if (element.TryGetProperty("color", out var color) &&
            color.ValueKind == JsonValueKind.String)
        {
            var code = MinecraftTextColorCode(color.GetString());
            if (code is not null)
                builder.Append('§').Append(code);
        }
        if (IsTrue(element, "bold"))
            builder.Append("§l");
        if (IsTrue(element, "italic"))
            builder.Append("§o");
        if (IsTrue(element, "underlined"))
            builder.Append("§n");
        if (IsTrue(element, "strikethrough"))
            builder.Append("§m");
    }

    private static bool IsTrue(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static char? MinecraftTextColorCode(string? name) => name?.ToLowerInvariant() switch
    {
        "black" => '0',
        "dark_blue" => '1',
        "dark_green" => '2',
        "dark_aqua" => '3',
        "dark_red" => '4',
        "dark_purple" => '5',
        "gold" => '6',
        "gray" => '7',
        "dark_gray" => '8',
        "blue" => '9',
        "green" => 'a',
        "aqua" => 'b',
        "red" => 'c',
        "light_purple" => 'd',
        "yellow" => 'e',
        "white" => 'f',
        _ => null
    };

    private static async Task WritePacketAsync(Stream stream, byte[] body, CancellationToken token)
    {
        using var packet = new MemoryStream();
        WriteVarint(packet, body.Length);
        packet.Write(body);
        await stream.WriteAsync(packet.ToArray(), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>读取一个完整数据包：长度前缀 → 包内容（含 packetId）。</summary>
    private static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken token)
    {
        var length = await ReadVarintAsync(stream, token).ConfigureAwait(false);
        if (length is < 0 or > 2_097_151)
            throw new InvalidOperationException("服务器返回了非法的数据包长度。");

        var payload = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(
                payload.AsMemory(offset, length - offset), token).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("服务器连接被提前关闭。");
            offset += read;
        }
        return payload;
    }

    private static async Task<int> ReadVarintAsync(Stream stream, CancellationToken token)
    {
        var result = 0;
        for (var shift = 0; shift < 32; shift += 7)
        {
            var buffer = new byte[1];
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("服务器连接被提前关闭。");

            result |= (buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0)
                return result;
        }
        throw new InvalidOperationException("VarInt 超出长度限制。");
    }

    private static void WriteVarint(Stream stream, int value)
    {
        var unsigned = (uint)value;
        while (true)
        {
            if ((unsigned & ~0x7Fu) == 0)
            {
                stream.WriteByte((byte)unsigned);
                return;
            }
            stream.WriteByte((byte)(unsigned & 0x7F | 0x80));
            unsigned >>= 7;
        }
    }

    private static void WriteUnsignedShort(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static string ReadString(Stream stream)
    {
        var length = ReadVarint(stream);
        if (length is < 0 or > 1_048_576)
            throw new InvalidOperationException("服务器返回了非法的字符串长度。");

        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static int ReadVarint(Stream stream)
    {
        var result = 0;
        for (var shift = 0; shift < 32; shift += 7)
        {
            var value = stream.ReadByte();
            if (value < 0)
                throw new InvalidOperationException("服务器连接被提前关闭。");

            result |= (value & 0x7F) << shift;
            if ((value & 0x80) == 0)
                return result;
        }
        throw new InvalidOperationException("VarInt 超出长度限制。");
    }

    private static int ParsePort(string text) =>
        int.TryParse(text.Trim(), out var port) && port is > 0 and <= 65535
            ? port
            : throw new ArgumentException($"非法端口：{text}");
}
