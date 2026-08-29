using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Music;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 主界面音乐便携控件：显示当前曲目、播放/暂停/跳过、打开完整播放器。
/// 与音乐播放器页面共享同一个 <see cref="MusicPlayerService.Shared"/> 实例，
/// 保证两边播放状态完全同步。
/// </summary>
internal static class BuiltInMusicPlayerComponent
{
    private const string ActionOpenPlayer = "open-music-player";
    private const string ActionPlayPause = "music-play-pause";
    private const string ActionNext = "music-next";
    private const string ActionStop = "music-stop";

    public static PolygonComponentRegistration Create(Action<string> navigate)
    {
        var definition = new PolygonComponentBuilder(
                "nyalauncher.builtin/music-player",
                "音乐播放器")
            .WithDescription("便携音乐控制：播放、暂停、跳过，点击打开完整播放器")
            .WithGlyph("material:MusicNote")
            .WithSize(320, 180)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.025, 0.24, 0.075, 0.52))
            .WithTheme(new PolygonComponentTheme())
            // 声明动作
            .AddAction(ActionOpenPlayer)
            .AddAction(ActionPlayPause)
            .AddAction(ActionNext)
            .AddAction(ActionStop)
            // 元素布局
            .AddText(
                "title",
                new ComponentRect(0.08, 0.08, 0.84, 0.14),
                "音乐播放器",
                ComponentTextRole.Title,
                fontSize: 14)
            .AddText(
                "now-playing",
                new ComponentRect(0.08, 0.26, 0.84, 0.16),
                "未在播放",
                ComponentTextRole.Caption,
                fontSize: 12)
            .AddText(
                "track-info",
                new ComponentRect(0.08, 0.42, 0.84, 0.10),
                "",
                ComponentTextRole.Caption,
                fontSize: 10)
            // 播放控制按钮
            .AddButton(
                "play-pause-btn",
                new ComponentRect(0.20, 0.58, 0.25, 0.16),
                "material:Play",
                ActionPlayPause,
                glyph: "material:Play",
                isPrimary: true)
            .AddButton(
                "next-btn",
                new ComponentRect(0.55, 0.58, 0.25, 0.16),
                "material:SkipNext",
                ActionNext,
                glyph: "material:SkipNext")
            // 打开播放器按钮
            .AddButton(
                "open-player-btn",
                new ComponentRect(0.08, 0.80, 0.84, 0.14),
                "打开播放器",
                ActionOpenPlayer,
                glyph: "material:MusicNote")
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new MusicPlayerInstance(navigate))
        };
    }

    private sealed class MusicPlayerInstance : PolygonComponentInstanceBase
    {
        private readonly Action<string> _navigate;
        private readonly MusicLibrary _library = new();
        private readonly MusicPlayerService _player = MusicPlayerService.Shared;

        public MusicPlayerInstance(Action<string> navigate)
        {
            _navigate = navigate;

            // 后台扫描音乐库，避免大目录递归枚举阻塞 UI 线程；
            // 若共享播放器还没有播放列表，用扫描结果补齐，保证组件可直接切歌。
            if (!string.IsNullOrWhiteSpace(_library.FolderPath))
            {
                _ = Task.Run(() =>
                {
                    try { _library.Scan(); } catch { /* 扫描失败不影响组件 */ }

                    if (MusicPlayerService.Shared.Playlist.Count == 0 &&
                        _library.Tracks.Count > 0)
                    {
                        MusicPlayerService.Shared.Playlist = [.. _library.Tracks];
                    }
                });
            }

            // 监听共享播放器状态变化，保证与音乐页面同步
            _player.StateChanged += OnPlayerStateChanged;
            _player.TrackChanged += OnPlayerTrackChanged;

            SetState(CreateState(
                _player.CurrentTrack?.Title ?? "未在播放",
                _player.CurrentTrack is { } t ? t.MetaDisplay : "",
                _player.State == PlaybackState.Playing ? "material:Pause" : "material:Play",
                true));
        }

        public override async ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (IsDisposed)
                return ComponentActionResult.Failed("组件已释放。");

            switch (invocation.ActionId)
            {
                case ActionOpenPlayer:
                    // 组件动作经 PolygonComponentInstanceHost 在后台线程执行，
                    // 页面导航涉及 UI 操作，必须切回 UI 线程（与其它内置组件一致）
                    await Dispatcher.UIThread.InvokeAsync(() => _navigate("music-player"));
                    return ComponentActionResult.Completed("已打开音乐播放器。");

                case ActionPlayPause:
                    HandlePlayPause();
                    return ComponentActionResult.Completed(
                        _player.State == PlaybackState.Playing ? "正在播放。" : "已暂停。");

                case ActionNext:
                    HandleNext();
                    return ComponentActionResult.Completed("已跳到下一首。");

                case ActionStop:
                    _player.Stop();
                    return ComponentActionResult.Completed("已停止播放。");

                default:
                    return ComponentActionResult.Failed($"未知动作：{invocation.ActionId}");
            }
        }

        public override ValueTask DisposeAsync()
        {
            _player.StateChanged -= OnPlayerStateChanged;
            _player.TrackChanged -= OnPlayerTrackChanged;
            // 注意：不释放共享播放器实例（MusicPlayerService.Shared 由应用生命周期管理）
            return base.DisposeAsync();
        }

        private void HandlePlayPause()
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
                    var tracks = _player.Playlist;
                    if (tracks.Count > 0)
                        _player.Play(tracks[0]);
                    break;
            }
        }

        private void HandleNext()
        {
            if (!_player.Next() && _player.Playlist.Count > 0)
            {
                // 顺序模式播完列表后无下一首，手动点击则从头开始
                _player.Play(_player.Playlist[0]);
            }
        }

        private void OnPlayerStateChanged()
        {
            Publish(
                _player.CurrentTrack?.Title ?? "未在播放",
                _player.CurrentTrack is { } t ? t.MetaDisplay : "",
                _player.State == PlaybackState.Playing ? "material:Pause" : "material:Play",
                true);
        }

        private void OnPlayerTrackChanged()
        {
            Publish(
                _player.CurrentTrack?.Title ?? "未在播放",
                _player.CurrentTrack is { } t ? t.MetaDisplay : "",
                _player.State == PlaybackState.Playing ? "material:Pause" : "material:Play",
                true);
        }

        private void Publish(string title, string info, string btnText, bool btnEnabled)
        {
            SetState(CreateState(title, info, btnText, btnEnabled));
        }

        private ComponentStateSnapshot CreateState(
            string title, string info, string btnText, bool btnEnabled)
        {
            return new ComponentStateSnapshot
            {
                Elements = new Dictionary<string, ComponentElementState>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["now-playing"] = new ComponentElementState { Text = title },
                    ["track-info"] = new ComponentElementState { Text = info },
                    ["play-pause-btn"] = new ComponentElementState
                    {
                        Text = btnText,
                        IsEnabled = btnEnabled
                    },
                    ["open-player-btn"] = new ComponentElementState
                    {
                        Text = "打开播放器",
                        IsEnabled = true
                    }
                }
            };
        }
    }
}
