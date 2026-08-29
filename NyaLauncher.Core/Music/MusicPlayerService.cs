using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NyaLauncher.Core.Music;

/// <summary>
/// 播放状态。
/// </summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// 播放模式：顺序（播完停）、列表循环、单曲循环、随机播放。
/// </summary>
public enum PlaybackMode
{
    Sequential,
    RepeatAll,
    RepeatOne,
    Shuffle
}

/// <summary>
/// 音乐播放器服务（全局共享单例）。
/// <para>Windows 平台使用 NAudio（WaveOutEvent + AudioFileReader/MediaFoundationReader），
/// 支持 MP3 / WAV / FLAC / M4A / AAC / WMA / OGG 等主流格式，
/// 并提供真正的暂停/恢复、进度、时长与实时音量控制。</para>
/// <para>macOS / Linux 平台回退到系统原生命令（afplay / mpv / ffplay）。</para>
/// <para>全局 UI（播放器页面、桌面小部件）必须统一使用 <see cref="Shared"/>，
/// 以保证播放状态完全同步；自动切歌由本服务按 <see cref="PlaybackMode"/> 统一处理。</para>
/// </summary>
public sealed class MusicPlayerService : IDisposable
{
    /// <summary>全局共享实例。所有界面（页面 / 桌面组件）都应使用该实例而不是各自新建。</summary>
    public static MusicPlayerService Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Random _random = new();
    private PlaybackState _state = PlaybackState.Stopped;
    private MusicTrack? _currentTrack;
    private int _volume = 80;
    private bool _manualStop;
    private PlaybackMode _playbackMode = PlaybackMode.Sequential;
    private IReadOnlyList<MusicTrack> _playlist = [];

    // Windows（NAudio）播放对象
    private WaveOutEvent? _waveOut;
    private WaveStream? _reader;
    private VolumeSampleProvider? _volumeProvider;

    // macOS / Linux 原生命令回退
    private Process? _process;

    /// <summary>播放/暂停/停止状态变化。</summary>
    public event Action? StateChanged;

    /// <summary>当前曲目变化（手动选择或自动切歌）。</summary>
    public event Action? TrackChanged;

    /// <summary>播放模式变化。</summary>
    public event Action? PlaybackModeChanged;

    /// <summary>顺序模式播完全部曲目、且没有下一首时触发（列表循环/随机模式不会触发）。</summary>
    public event Action? TrackFinished;

    public PlaybackState State
    {
        get { lock (_gate) return _state; }
    }

    public MusicTrack? CurrentTrack
    {
        get { lock (_gate) return _currentTrack; }
    }

    /// <summary>最近一次播放失败的原因（无错误时为 null）。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 播放列表。自动切歌、上一首/下一首都基于该列表；
    /// 页面应在扫描/排序后将其设置为完整（未过滤）的曲目列表。
    /// </summary>
    public IReadOnlyList<MusicTrack> Playlist
    {
        get { lock (_gate) return _playlist; }
        set { lock (_gate) _playlist = value ?? []; }
    }

    public PlaybackMode PlaybackMode
    {
        get { lock (_gate) return _playbackMode; }
        set
        {
            lock (_gate)
            {
                if (_playbackMode == value)
                    return;
                _playbackMode = value;
            }
            PlaybackModeChanged?.Invoke();
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            var provider = _volumeProvider;
            if (provider is not null)
                provider.Volume = _volume / 100f;
        }
    }

    /// <summary>当前播放位置（未在播放时为 0）。</summary>
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                if (_reader is null || _state == PlaybackState.Stopped)
                    return TimeSpan.Zero;
                return _reader.CurrentTime;
            }
        }
    }

    /// <summary>当前曲目总时长（未知时为 0）。</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                if (_reader is null)
                    return TimeSpan.Zero;
                return _reader.TotalTime;
            }
        }
    }

    /// <summary>
    /// 播放指定曲目（手动选择）。
    /// </summary>
    public void Play(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        PlayCore(track);
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 暂停当前播放（保留进度，可继续）。
    /// </summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_state != PlaybackState.Playing)
                return;
            _state = PlaybackState.Paused;
        }

        try { _waveOut?.Pause(); } catch { }
        try { _process?.Kill(); } catch { }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 从暂停位置恢复播放。
    /// </summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (_state != PlaybackState.Paused || _currentTrack is null)
                return;
            _state = PlaybackState.Playing;
            _manualStop = false;
        }

        try
        {
            if (_waveOut is not null)
            {
                _waveOut.Play();
            }
            else if (OperatingSystem.IsWindows())
            {
                // 暂停时被清理的异常情况：直接重新打开文件
                StartWindowsPlayback(_currentTrack.FilePath);
            }
            else
            {
                // 原生命令无法续播，从头开始
                StartNativePlayback(_currentTrack.FilePath);
            }
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            lock (_gate)
            {
                _state = PlaybackState.Stopped;
                _currentTrack = null;
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// 停止播放并归零进度。
    /// </summary>
    public void Stop() => StopCore(notify: true);

    /// <summary>
    /// 播放列表中的下一首（按当前模式选曲，手动切换时顺序模式也会循环回第一首）。
    /// </summary>
    public bool Next()
    {
        MusicTrack? next;
        lock (_gate)
        {
            next = PickNextLocked(manual: true);
        }
        if (next is null)
            return false;

        PlayCore(next);
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 播放列表中的上一首（循环到末尾）。
    /// </summary>
    public bool Previous()
    {
        MusicTrack? previous;
        lock (_gate)
        {
            if (_playlist.Count == 0)
                return false;
            if (_currentTrack is null)
            {
                previous = _playlist[0];
            }
            else
            {
                var index = IndexOfTrack(_playlist, _currentTrack);
                previous = _playlist[(index - 1 + _playlist.Count) % _playlist.Count];
            }
        }

        PlayCore(previous);
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 跳转到指定位置。
    /// </summary>
    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            return;

        lock (_gate)
        {
            if (_reader is null || _state == PlaybackState.Stopped)
                return;

            try
            {
                var total = _reader.TotalTime;
                _reader.CurrentTime = position > total ? total : position;
            }
            catch
            {
                // 个别格式（如流式）不支持精确跳转，忽略即可
            }
        }
    }

    public void Dispose() => StopCore(notify: false);

    // ------------------------------------------------------------------
    // 内部播放控制
    // ------------------------------------------------------------------

    /// <summary>
    /// 核心播放逻辑：清理旧播放、打开新曲目，但不触发任何事件（由调用方统一触发）。
    /// 轻量清理可避免在 WaveOut 回调内对其自身调用 Stop 造成重入问题。
    /// </summary>
    private void PlayCore(MusicTrack track)
    {
        var oldWave = _waveOut;
        var oldReader = _reader;
        _waveOut = null;
        _reader = null;
        _volumeProvider = null;

        if (oldWave is not null)
        {
            try { oldWave.PlaybackStopped -= OnPlaybackStopped; } catch { }
            try { oldWave.Dispose(); } catch { }
        }
        oldReader?.Dispose();
        CleanupNativePlayback();

        lock (_gate)
        {
            _currentTrack = track;
            _state = PlaybackState.Playing;
            _manualStop = false;
        }
        LastError = null;

        try
        {
            if (OperatingSystem.IsWindows())
                StartWindowsPlayback(track.FilePath);
            else
                StartNativePlayback(track.FilePath);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            CleanupWindowsPlayback();
            CleanupNativePlayback();
            lock (_gate)
            {
                _state = PlaybackState.Stopped;
                _currentTrack = null;
            }
        }
    }

    /// <summary>
    /// 按当前模式挑选下一首（需在 <see cref="_gate"/> 锁内调用）。
    /// </summary>
    /// <param name="manual">是否为用户手动切换（顺序模式手动切换会循环回第一首）。</param>
    private MusicTrack? PickNextLocked(bool manual)
    {
        if (_playlist.Count == 0)
            return null;
        if (_currentTrack is null)
            return _playlist[0];

        var index = IndexOfTrack(_playlist, _currentTrack);
        switch (_playbackMode)
        {
            case PlaybackMode.Shuffle:
                return _playlist[_random.Next(_playlist.Count)];
            case PlaybackMode.RepeatOne:
                return manual
                    ? _playlist[(index + 1) % _playlist.Count]
                    : _currentTrack;
            case PlaybackMode.RepeatAll:
                return _playlist[(index + 1) % _playlist.Count];
            default: // Sequential
                return manual
                    ? (index >= _playlist.Count - 1 ? _playlist[0] : _playlist[index + 1])
                    : (index >= _playlist.Count - 1 ? null : _playlist[index + 1]);
        }
    }

    /// <summary>
    /// 纯逻辑版"下一首索引"，便于单元测试。
    /// 返回 -1 表示没有下一首（顺序模式播完列表尾部）。
    /// </summary>
    internal static int SelectNextIndex(
        PlaybackMode mode,
        int playlistCount,
        int currentIndex,
        bool manual,
        Random? random = null)
    {
        if (playlistCount <= 0)
            return -1;
        if (currentIndex < 0)
            return 0;

        switch (mode)
        {
            case PlaybackMode.Shuffle:
                return (random ?? new Random()).Next(playlistCount);
            case PlaybackMode.RepeatOne:
                return manual ? (currentIndex + 1) % playlistCount : currentIndex;
            case PlaybackMode.RepeatAll:
                return (currentIndex + 1) % playlistCount;
            default: // Sequential
                if (manual)
                    return (currentIndex + 1) % playlistCount;
                return currentIndex >= playlistCount - 1 ? -1 : currentIndex + 1;
        }
    }

    /// <summary>按值查找曲目在播放列表中的索引（record 值相等即可命中）。</summary>
    private static int IndexOfTrack(IReadOnlyList<MusicTrack> playlist, MusicTrack track)
    {
        for (var i = 0; i < playlist.Count; i++)
        {
            if (EqualityComparer<MusicTrack>.Default.Equals(playlist[i], track))
                return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------
    // Windows（NAudio）播放实现
    // ------------------------------------------------------------------

    private void StartWindowsPlayback(string filePath)
    {
        // AudioFileReader 原生支持 WAV/MP3/AIFF/AU，其余格式自动回退 MediaFoundation；
        // 仍失败时再直接尝试 MediaFoundationReader（MP3/M4A/AAC/WMA/FLAC 等）。
        WaveStream reader;
        try
        {
            reader = new AudioFileReader(filePath);
        }
        catch (Exception)
        {
            reader = new MediaFoundationReader(filePath);
        }

        _reader = reader;
        _volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
        {
            Volume = _volume / 100f
        };

        var waveOut = new WaveOutEvent();
        waveOut.Init(_volumeProvider);
        waveOut.PlaybackStopped += OnPlaybackStopped;
        _waveOut = waveOut;
        waveOut.Play();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool naturallyFinished;
        MusicTrack? next = null;
        lock (_gate)
        {
            // 仅自然播放完毕（非手动停止/暂停）才视为一曲结束
            naturallyFinished = !_manualStop && _state == PlaybackState.Playing;
            if (naturallyFinished)
            {
                _state = PlaybackState.Stopped;
                next = PickNextLocked(manual: false);
            }
        }

        if (!naturallyFinished)
        {
            StateChanged?.Invoke();
            return;
        }

        if (next is not null)
        {
            // 按模式自动切歌（重复/循环/随机），事件由本回调统一触发
            PlayCore(next);
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
            return;
        }

        // 顺序模式播完整个列表：清理资源并通知
        var reader = _reader;
        _reader = null;
        _volumeProvider = null;
        reader?.Dispose();
        TrackFinished?.Invoke();
        StateChanged?.Invoke();
    }

    private void CleanupWindowsPlayback()
    {
        var waveOut = _waveOut;
        var reader = _reader;
        _waveOut = null;
        _reader = null;
        _volumeProvider = null;

        if (waveOut is not null)
        {
            try { waveOut.PlaybackStopped -= OnPlaybackStopped; } catch { }
            try { waveOut.Stop(); } catch { }
            waveOut.Dispose();
        }
        reader?.Dispose();
    }

    // ------------------------------------------------------------------
    // macOS / Linux 原生命令回退
    // ------------------------------------------------------------------

    private void StartNativePlayback(string filePath)
    {
        _process = CreatePlayerProcess(filePath, _volume);
        _process.EnableRaisingEvents = true;
        _process.Exited += OnNativeProcessExited;
        _process.Start();
    }

    private void OnNativeProcessExited(object? sender, EventArgs e)
    {
        bool naturallyFinished;
        MusicTrack? next = null;
        lock (_gate)
        {
            naturallyFinished = !_manualStop && _state == PlaybackState.Playing;
            if (naturallyFinished)
            {
                _state = PlaybackState.Stopped;
                next = PickNextLocked(manual: false);
            }
        }

        if (!naturallyFinished)
        {
            StateChanged?.Invoke();
            return;
        }

        if (next is not null)
        {
            PlayCore(next);
            TrackChanged?.Invoke();
            StateChanged?.Invoke();
            return;
        }

        TrackFinished?.Invoke();
        StateChanged?.Invoke();
    }

    private void CleanupNativePlayback()
    {
        var process = _process;
        _process = null;
        if (process is null)
            return;

        try { process.Exited -= OnNativeProcessExited; } catch { }
        try { process.Kill(); } catch { }
        try { process.Dispose(); } catch { }
    }

    private void StopCore(bool notify)
    {
        _manualStop = true;
        CleanupWindowsPlayback();
        CleanupNativePlayback();
        lock (_gate)
        {
            _state = PlaybackState.Stopped;
        }
        if (notify)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// 创建平台特定的音频播放进程（macOS / Linux 使用）。
    /// </summary>
    private static Process CreatePlayerProcess(string filePath, int volume)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "afplay",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
        }

        // Linux: 尝试 mpv > ffplay > aplay
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "mpv",
                Arguments = $"--no-video --volume={volume} \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
    }
}
