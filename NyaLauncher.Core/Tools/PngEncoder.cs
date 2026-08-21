using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace NyaLauncher.Core.Tools;

/// <summary>
/// 极简 RGBA → PNG 编码器（8 位色深 + 真彩 RGBA 颜色类型）。
/// 离线皮肤贴图与实例图标共用这一份实现，避免两处重复手写 PNG 块与 CRC32。
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>把 RGBA 像素数组编码为 PNG 字节流。</summary>
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        EncodeTo(output, width, height, rgba);
        return output.ToArray();
    }

    /// <summary>把 RGBA 像素数组编码为 PNG 并写入指定流。</summary>
    public static void EncodeTo(Stream target, int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // 每行前缀 1 字节过滤类型（0 = None），随后是该行 RGBA 数据
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            raw.CopyTo(zlib);

        target.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // color type: RGBA
        WriteChunk(target, "IHDR", header);
        WriteChunk(target, "IDAT", compressed.ToArray());
        WriteChunk(target, "IEND", []);
    }

    private static void WriteChunk(Stream target, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        target.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        target.Write(typeBytes);
        target.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc32(typeBytes, data));
        target.Write(crc);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in typeBytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }

        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }

        return ~crc;
    }
}
