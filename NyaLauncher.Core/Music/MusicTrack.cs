namespace NyaLauncher.Core.Music;

/// <summary>
/// 音乐文件元数据。
/// </summary>
public sealed record MusicTrack
{
    /// <summary>文件完整路径。</summary>
    public required string FilePath { get; init; }

    /// <summary>文件名（不含扩展名）作为显示标题。</summary>
    public string Title => Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>文件扩展名（如 .mp3）。</summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();

    /// <summary>文件大小（字节）。</summary>
    public long FileSize { get; init; }

    /// <summary>文件大小的格式化显示。</summary>
    public string SizeDisplay => FileSize switch
    {
        >= 1048576 => $"{FileSize / 1048576.0:0.1} MB",
        >= 1024 => $"{FileSize / 1024.0:0.0} KB",
        _ => $"{FileSize} B"
    };

    /// <summary>最后修改时间。</summary>
    public DateTime LastModified { get; init; }

    /// <summary>最后修改时间的格式化显示。</summary>
    public string DateDisplay => LastModified.ToString("yyyy-MM-dd HH:mm");

    /// <summary>支持的音频文件扩展名。</summary>
    public static readonly string[] SupportedExtensions =
        [".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a", ".opus"];

    /// <summary>判断文件是否为支持的音频格式。</summary>
    public static bool IsSupported(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());

    public override string ToString() => Title;
}
