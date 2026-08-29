using NyaLauncher.Core.Config;

namespace NyaLauncher.Core.Music;

/// <summary>
/// 音乐库管理：扫描文件夹、管理播放列表、持久化设置。
/// </summary>
public sealed class MusicLibrary
{
    private const string FolderKey = "musicFolder";
    private const string SortKey = "musicSortMode";
    private const string VolumeKey = "musicVolume";
    private const string PlaybackModeKey = "musicPlaybackMode";

    private readonly object _gate = new();
    private List<MusicTrack> _tracks = [];
    private List<MusicTrack> _sortedTracks = [];
    private MusicSortMode _sortMode = MusicSortMode.FileName;

    /// <summary>当前播放列表（已排序）。</summary>
    public IReadOnlyList<MusicTrack> Tracks
    {
        get { lock (_gate) return _sortedTracks; }
    }

    /// <summary>当前排序模式。</summary>
    public MusicSortMode SortMode
    {
        get => _sortMode;
        set
        {
            lock (_gate)
            {
                _sortMode = value;
                _sortedTracks = SortTracks(_tracks, _sortMode);
            }
            LauncherConfig.SetValue(SortKey, value.ToString());
        }
    }

    /// <summary>当前音乐文件夹路径。</summary>
    public string? FolderPath
    {
        get => LauncherConfig.GetValue(FolderKey);
    }

    /// <summary>音量 (0-100)。</summary>
    public int Volume
    {
        get
        {
            var val = LauncherConfig.GetValue(VolumeKey);
            return int.TryParse(val, out var v) ? Math.Clamp(v, 0, 100) : 80;
        }
        set => LauncherConfig.SetValue(VolumeKey, Math.Clamp(value, 0, 100).ToString());
    }

    /// <summary>播放模式（顺序 / 列表循环 / 单曲循环 / 随机）。</summary>
    public PlaybackMode PlaybackMode
    {
        get
        {
            var val = LauncherConfig.GetValue(PlaybackModeKey);
            return Enum.TryParse<PlaybackMode>(val, out var mode) ? mode : PlaybackMode.Sequential;
        }
        set => LauncherConfig.SetValue(PlaybackModeKey, value.ToString());
    }

    /// <summary>
    /// 设置音乐文件夹并扫描。
    /// </summary>
    public void SetFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        LauncherConfig.SetValue(FolderKey, path.Trim());
        Scan();
    }

    /// <summary>
    /// 扫描当前设置的音乐文件夹。
    /// </summary>
    public void Scan()
    {
        var folder = FolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            lock (_gate)
            {
                _tracks = [];
                _sortedTracks = [];
            }
            return;
        }

        var tracks = new List<MusicTrack>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                if (!MusicTrack.IsSupported(file))
                    continue;

                try
                {
                    var info = new FileInfo(file);
                    tracks.Add(new MusicTrack
                    {
                        FilePath = file,
                        FileSize = info.Length,
                        LastModified = info.LastWriteTime
                    });
                }
                catch
                {
                    // 跳过无法读取的文件
                }
            }
        }
        catch
        {
            // 扫描失败，保留空列表
        }

        // 加载保存的排序模式
        var savedSort = LauncherConfig.GetValue(SortKey);
        if (!string.IsNullOrWhiteSpace(savedSort) && Enum.TryParse<MusicSortMode>(savedSort, out var mode))
            _sortMode = mode;

        lock (_gate)
        {
            _tracks = tracks;
            _sortedTracks = SortTracks(tracks, _sortMode);
        }
    }

    /// <summary>
    /// 根据文件名模糊搜索。
    /// </summary>
    public List<MusicTrack> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return [.. Tracks];

        return Tracks
            .Where(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 获取排序后的列表。
    /// </summary>
    public List<MusicTrack> GetSorted(MusicSortMode mode)
    {
        lock (_gate)
        {
            return SortTracks(_tracks, mode);
        }
    }

    private static List<MusicTrack> SortTracks(List<MusicTrack> tracks, MusicSortMode mode) => mode switch
    {
        MusicSortMode.FileName => [.. tracks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)],
        MusicSortMode.FileNameDesc => [.. tracks.OrderByDescending(t => t.Title, StringComparer.OrdinalIgnoreCase)],
        MusicSortMode.DateModified => [.. tracks.OrderBy(t => t.LastModified)],
        MusicSortMode.DateModifiedDesc => [.. tracks.OrderByDescending(t => t.LastModified)],
        MusicSortMode.FileSize => [.. tracks.OrderBy(t => t.FileSize)],
        MusicSortMode.FileSizeDesc => [.. tracks.OrderByDescending(t => t.FileSize)],
        _ => [.. tracks]
    };
}

/// <summary>
/// 音乐列表排序模式。
/// </summary>
public enum MusicSortMode
{
    FileName,
    FileNameDesc,
    DateModified,
    DateModifiedDesc,
    FileSize,
    FileSizeDesc
}
