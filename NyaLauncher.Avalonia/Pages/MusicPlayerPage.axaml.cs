using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Core.Music;

namespace NyaLauncher.Avalonia.Pages;

public partial class MusicPlayerPage : UserControl
{
    private readonly MusicLibrary _library = new();
    private readonly MusicPlayerService _player = new();
    private bool _synchronizing;
    private List<MusicTrack> _displayTracks = [];

    public MusicPlayerPage()
    {
        InitializeComponent();
        InitializeSortComboBox();
        _player.StateChanged += OnPlayerStateChanged;
        _player.TrackFinished += OnTrackFinished;

        // 加载已保存的音量
        VolumeSlider.Value = _library.Volume;
        _player.Volume = _library.Volume;

        // 如果已有文件夹设置，自动扫描
        if (!string.IsNullOrWhiteSpace(_library.FolderPath))
            RefreshLibrary();
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

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        _library.Scan();
        _displayTracks = [.. _library.Tracks];
        TrackList.ItemsSource = _displayTracks
            .Select(t => $"{t.Title}  [{t.Extension.TrimStart('.')} · {t.SizeDisplay}]")
            .ToList();

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
    // 排序
    // ------------------------------------------------------------------

    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing) return;
        _library.SortMode = (MusicSortMode)SortComboBox.SelectedIndex;
        _displayTracks = [.. _library.Tracks];
        TrackList.ItemsSource = _displayTracks
            .Select(t => $"{t.Title}  [{t.Extension.TrimStart('.')} · {t.SizeDisplay}]")
            .ToList();
    }

    // ------------------------------------------------------------------
    // 搜索
    // ------------------------------------------------------------------

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var keyword = SearchBox.Text ?? "";
        _displayTracks = _library.Search(keyword);
        TrackList.ItemsSource = _displayTracks
            .Select(t => $"{t.Title}  [{t.Extension.TrimStart('.')} · {t.SizeDisplay}]")
            .ToList();
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
        UpdateNowPlaying(track);
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

    private void OnPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_displayTracks.Count == 0) return;
        var currentIndex = _displayTracks.IndexOf(_player.CurrentTrack!);
        var prevIndex = currentIndex <= 0 ? _displayTracks.Count - 1 : currentIndex - 1;
        _player.Play(_displayTracks[prevIndex]);
        TrackList.SelectedIndex = prevIndex;
        UpdateNowPlaying(_displayTracks[prevIndex]);
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (_displayTracks.Count == 0) return;
        var currentIndex = _displayTracks.IndexOf(_player.CurrentTrack!);
        var nextIndex = currentIndex >= _displayTracks.Count - 1 ? 0 : currentIndex + 1;
        _player.Play(_displayTracks[nextIndex]);
        TrackList.SelectedIndex = nextIndex;
        UpdateNowPlaying(_displayTracks[nextIndex]);
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _player.Stop();
        NowPlayingTitle.Text = "未在播放";
        NowPlayingInfo.Text = "";
    }

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
            PlayPauseButton.Content = _player.State == PlaybackState.Playing ? "⏸" : "▶";
        });
    }

    private void OnTrackFinished()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // 自动播放下一首
            if (_displayTracks.Count == 0) return;
            var currentIndex = _player.CurrentTrack is not null
                ? _displayTracks.IndexOf(_player.CurrentTrack)
                : -1;
            var nextIndex = currentIndex >= _displayTracks.Count - 1 ? 0 : currentIndex + 1;
            _player.Play(_displayTracks[nextIndex]);
            TrackList.SelectedIndex = nextIndex;
            UpdateNowPlaying(_displayTracks[nextIndex]);
        });
    }

    private void UpdateNowPlaying(MusicTrack track)
    {
        NowPlayingTitle.Text = track.Title;
        NowPlayingInfo.Text = $"{track.Extension.TrimStart('.')} · {track.SizeDisplay}";
    }
}
