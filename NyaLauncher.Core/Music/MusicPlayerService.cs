using System.Diagnostics;

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
/// 音乐播放器服务。使用平台原生命令播放音频文件。
/// </summary>
public sealed class MusicPlayerService : IDisposable
{
    private Process? _process;
    private readonly System.Timers.Timer _tickTimer;
    private readonly object _gate = new();
    private PlaybackState _state = PlaybackState.Stopped;
    private MusicTrack? _currentTrack;
    private int _volume = 80;

    public event Action? StateChanged;
    public event Action? TrackFinished;

    public MusicPlayerService()
    {
        _tickTimer = new System.Timers.Timer(500);
        _tickTimer.Elapsed += (_, _) =>
        {
            lock (_gate)
            {
                if (_state == PlaybackState.Playing && (_process is null || _process.HasExited))
                {
                    _state = PlaybackState.Stopped;
                    _tickTimer.Stop();
                    TrackFinished?.Invoke();
                    StateChanged?.Invoke();
                }
            }
        };
    }

    public PlaybackState State
    {
        get { lock (_gate) return _state; }
    }

    public MusicTrack? CurrentTrack
    {
        get { lock (_gate) return _currentTrack; }
    }

    public int Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// 播放指定曲目。
    /// </summary>
    public void Play(MusicTrack track)
    {
        Stop();
        lock (_gate)
        {
            _currentTrack = track;
            _state = PlaybackState.Playing;
        }

        try
        {
            _process = CreatePlayerProcess(track.FilePath, _volume);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                lock (_gate)
                {
                    if (_state == PlaybackState.Playing)
                    {
                        _state = PlaybackState.Stopped;
                    }
                }
                // 在锁外触发事件，避免死锁
                TrackFinished?.Invoke();
                StateChanged?.Invoke();
            };
            _process.Start();
            _tickTimer.Start();
        }
        catch
        {
            lock (_gate)
            {
                _state = PlaybackState.Stopped;
                _currentTrack = null;
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// 暂停当前播放。
    /// </summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_state != PlaybackState.Playing)
                return;
            _state = PlaybackState.Paused;
        }
        try { _process?.Kill(); } catch { }
        _tickTimer.Stop();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 恢复播放。
    /// </summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (_state != PlaybackState.Paused || _currentTrack is null)
                return;
        }
        if (_currentTrack is not null)
            Play(_currentTrack);
    }

    /// <summary>
    /// 停止播放。
    /// </summary>
    public void Stop()
    {
        _tickTimer.Stop();
        try { _process?.Kill(); } catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
        lock (_gate)
        {
            _state = PlaybackState.Stopped;
        }
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _tickTimer.Dispose();
    }

    /// <summary>
    /// 创建平台特定的音频播放进程。
    /// </summary>
    private static Process CreatePlayerProcess(string filePath, int volume)
    {
        var volumeNormalized = volume / 100.0;

        if (OperatingSystem.IsWindows())
        {
            // 使用 PowerShell 播放
            var escapedPath = filePath.Replace("'", "''");
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"(New-Object Media.SoundPlayer '{escapedPath}').PlaySync()\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
        }

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
