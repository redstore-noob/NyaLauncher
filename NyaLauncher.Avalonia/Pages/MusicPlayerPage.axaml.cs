using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Material.Icons;
using NyaLauncher.Core.Music;

namespace NyaLauncher.Avalonia.Pages;

public partial class MusicPlayerPage : UserControl
{
    private readonly MusicLibrary _library = new();
    private readonly MusicPlayerService _player = MusicPlayerService.Shared;
    private readonly DispatcherTimer _progressTimer;
    private bool _synchronizing;
    private bool _syncingProgress;
    private bool _userDragging;
    private List<MusicTrack> _displayTracks = [];

    public MusicPlayerPage()
    {
        InitializeComponent();
        InitializeSortComboBox();
        _player.StateChanged += OnPlayerStateChanged;
        _player.TrackChanged += OnTrackChanged;

        // 加载已保存的音量与播放模式
        VolumeSlider.Value = _library.Volume;
        _player.Volume = _library.Volume;
        _player.PlaybackMode = _library.PlaybackMode;
        UpdateModeButton();

        // 进度轮询：刷新进度条与时间显示（每 250ms）
        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _progressTimer.Start();

        // 如果已有文件夹设置，自动扫描
        if (!string.IsNullOrWhiteSpace(_library.FolderPath))
            RefreshLibrary();

        // 若共享播放器已有曲目在播（例如桌面组件先操作），同步界面
        if (_player.CurrentTrack is not null)
            OnTrackChanged();
    }

    private void InitializeSortComboBox()
    {
        _synchronizing = true;
        SortComboBox.Items.Add("文件名 A-Z");
        SortComboBox.Items.Add("文件名 Z-A");
        SortComboBox.Items.Add("修改时间 ↑");
        SortComboBox.Items.Add("修改时间 ↓");
        SortComboBox.Items.Add("文件大小 ↑");
        SortComboBox.Items.Add("文件大小 ↓");
        SortComboBox.SelectedIndex = (int)_library.SortMode;
        _synchronizing = false;
    }

    // ------------------------------------------------------------------
    // 文件夹选择
    // ------------------------------------------------------------------

    private async void OnSelectFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择音乐文件夹",
                AllowMultiple = false
            });

            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            _library.SetFolder(path);
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            NowPlayingInfo.Text = $"选择文件夹失败：{ex.Message}";
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        _library.Scan();
        _displayTracks = [.. _library.Tracks];
        TrackList.ItemsSource = _displayTracks;

        // 共享播放列表 = 完整（未过滤）曲目列表，供自动切歌/上下一首使用
        _player.Playlist = [.. _library.Tracks];

        if (_library.Tracks.Count > 0)
        {
            TrackCountText.Text = $"{_library.Tracks.Count} 首歌曲 · {_library.FolderPath}";
        }
        else if (!string.IsNullOrWhiteSpace(_library.FolderPath))
        {
            TrackCountText.Text = $"未找到音频文件 · {_library.FolderPath}";
        }
        else
        {
            TrackCountText.Text = "请点击「选择文件夹」加载音乐";
        }
    }

    // ------------------------------------------------------------------
    // 排序 / 搜索
    // ------------------------------------------------------------------

    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing) return;
        _library.SortMode = (MusicSortMode)SortComboBox.SelectedIndex;
        _displayTracks = [.. _library.Tracks];
        TrackList.ItemsSource = _displayTracks;
        _player.Playlist = [.. _library.Tracks];
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var keyword = SearchBox.Text ?? "";
        _displayTracks = _library.Search(keyword);
        TrackList.ItemsSource = _displayTracks;
    }

    // ------------------------------------------------------------------
    // 播放控制
    // ------------------------------------------------------------------

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TrackList.SelectedIndex < 0 || TrackList.SelectedIndex >= _displayTracks.Count)
            return;

        var track = _displayTracks[TrackList.SelectedIndex];
        _player.Play(track);
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        switch (_player.State)
        {
            case PlaybackState.Playing:
                _player.Pause();
                break;
            case PlaybackState.Paused:
                _player.Resume();
                break;
            case PlaybackState.Stopped:
                // 如果有选中的曲目，播放它
                if (TrackList.SelectedIndex >= 0 && TrackList.SelectedIndex < _displayTracks.Count)
                    _player.Play(_displayTracks[TrackList.SelectedIndex]);
                else if (_displayTracks.Count > 0)
                    _player.Play(_displayTracks[0]);
                break;
        }
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e) => _player.Previous();

    private void OnNextClick(object? sender, RoutedEventArgs e) => _player.Next();

    private void OnStopClick(object? sender, RoutedEventArgs e) => _player.Stop();

    // ------------------------------------------------------------------
    // 播放模式
    // ------------------------------------------------------------------

    private void OnModeClick(object? sender, RoutedEventArgs e)
    {
        _player.PlaybackMode = NextMode(_player.PlaybackMode);
        _library.PlaybackMode = _player.PlaybackMode; // 持久化
        UpdateModeButton();
    }

    private static PlaybackMode NextMode(PlaybackMode mode) => mode switch
    {
        PlaybackMode.Sequential => PlaybackMode.RepeatAll,
        PlaybackMode.RepeatAll => PlaybackMode.RepeatOne,
        PlaybackMode.RepeatOne => PlaybackMode.Shuffle,
        _ => PlaybackMode.Sequential
    };

    private void UpdateModeButton()
    {
        var (kind, tip) = _player.PlaybackMode switch
        {
            PlaybackMode.Sequential => (MaterialIconKind.PlaylistPlay, "顺序播放"),
            PlaybackMode.RepeatAll => (MaterialIconKind.Repeat, "列表循环"),
            PlaybackMode.RepeatOne => (MaterialIconKind.RepeatOne, "单曲循环"),
            _ => (MaterialIconKind.Shuffle, "随机播放")
        };
        ModeGlyph.Kind = kind;
        ToolTip.SetTip(ModeButton, tip);
        // 非默认模式高亮显示
        ModeGlyph.Foreground = _player.PlaybackMode == PlaybackMode.Sequential
            ? FindBrush("MutedTextBrush")
            : FindBrush("SystemAccentColor");
    }

    private IBrush? FindBrush(string key) =>
        Application.Current?.Resources.TryGetResource(key, null, out var value) == true
            ? value as IBrush
            : null;

    // ------------------------------------------------------------------
    // 进度条
    // ------------------------------------------------------------------

    private void OnProgressPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _userDragging = true;
    }

    private void OnProgressPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _userDragging = false;
        // 拖拽/点击结束后提交跳转
        _player.Seek(TimeSpan.FromSeconds(Math.Max(0, ProgressSlider.Value)));
    }

    private void OnProgressPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 指针捕获丢失（例如拖出窗口）时复位，避免卡在拖拽状态
        _userDragging = false;
    }

    private void OnProgressValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingProgress) return;

        var seconds = Math.Max(0, e.NewValue);
        TimeCurrentText.Text = FormatTime(TimeSpan.FromSeconds(seconds));

        // 非拖拽中的数值变化（键盘方向键 / 点击轨道）立即跳转
        if (!_userDragging)
            _player.Seek(TimeSpan.FromSeconds(seconds));
    }

    private void UpdateProgress()
    {
        var duration = _player.Duration;
        var position = _player.Position;
        var hasTrack = _player.State != PlaybackState.Stopped && duration > TimeSpan.Zero;

        _syncingProgress = true;
        try
        {
            ProgressSlider.IsEnabled = hasTrack;
            if (hasTrack)
            {
                ProgressSlider.Maximum = duration.TotalSeconds;
                if (!_userDragging)
                    ProgressSlider.Value = position.TotalSeconds;
            }
            else
            {
                ProgressSlider.Value = 0;
            }
        }
        finally
        {
            _syncingProgress = false;
        }

        TimeCurrentText.Text = FormatTime(position);
        TimeTotalText.Text = duration > TimeSpan.Zero ? FormatTime(duration) : "--:--";
    }

    private void ResetProgress()
    {
        _syncingProgress = true;
        ProgressSlider.Value = 0;
        ProgressSlider.Maximum = 100;
        ProgressSlider.IsEnabled = false;
        _syncingProgress = false;
        TimeCurrentText.Text = "--:--";
        TimeTotalText.Text = "--:--";
    }

    private static string FormatTime(TimeSpan time) =>
        time <= TimeSpan.Zero
            ? "0:00"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    // ------------------------------------------------------------------
    // 音量
    // ------------------------------------------------------------------

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var volume = (int)Math.Round(e.NewValue);
        _player.Volume = volume;
        _library.Volume = volume;
        VolumeText.Text = $"{volume}%";
    }

    // ------------------------------------------------------------------
    // 播放器状态回调
    // ------------------------------------------------------------------

    private void OnPlayerStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // 播放/暂停图标切换：单一 MaterialIcon 切换 Kind（播放中→Pause，否则→Play）
            var playing = _player.State == PlaybackState.Playing;
            PlayPauseIcon.Kind = playing ? MaterialIconKind.Pause : MaterialIconKind.Play;

            // 播放失败时展示具体原因
            if (_player.State == PlaybackState.Stopped && !string.IsNullOrWhiteSpace(_player.LastError))
            {
                NowPlayingTitle.Text = "播放失败";
                NowPlayingInfo.Text = $"播放出错：{_player.LastError}";
            }
            else if (_player.State == PlaybackState.Stopped && _player.CurrentTrack is null)
            {
                NowPlayingTitle.Text = "未在播放";
                NowPlayingInfo.Text = "";
                ResetProgress();
            }
        });
    }

    private void OnTrackChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var track = _player.CurrentTrack;
            if (track is null)
            {
                NowPlayingTitle.Text = "未在播放";
                NowPlayingInfo.Text = "";
                ResetProgress();
                return;
            }

            UpdateNowPlaying(track);
            ResetProgress();

            // 让列表高亮当前曲目（自动切歌后跟随）
            var index = _displayTracks.IndexOf(track);
            if (index >= 0 && TrackList.SelectedIndex != index)
                TrackList.SelectedIndex = index;
        });
    }

    private void UpdateNowPlaying(MusicTrack track)
    {
        NowPlayingTitle.Text = track.Title;
        NowPlayingInfo.Text = track.MetaDisplay;
    }
}
