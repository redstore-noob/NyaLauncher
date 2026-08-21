using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Music;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 主界面音乐便携控件：显示当前曲目、播放/暂停/跳过、打开完整播放器。
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
            .WithGlyph("♪")
            .WithSize(320, 180)
            .WithShape(PolygonShapeDefinition.Rectangle())
            .WithDragHandle(new ComponentRect(0.025, 0.24, 0.075, 0.52))
            .WithTheme(ThemePolygonHelper.CreateDefaultTheme())
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
                "▶",
                ActionPlayPause,
                glyph: "▶",
                isPrimary: true)
            .AddButton(
                "next-btn",
                new ComponentRect(0.55, 0.58, 0.25, 0.16),
                "⏭",
                ActionNext,
                glyph: "⏭")
            // 打开播放器按钮
            .AddButton(
                "open-player-btn",
                new ComponentRect(0.08, 0.80, 0.84, 0.14),
                "打开播放器",
                ActionOpenPlayer,
                glyph: "♪")
            .Build();

        return new PolygonComponentRegistration
        {
            Definition = definition,
            Factory = new DelegatePolygonComponentFactory(
                _ => new MusicPlayerInstance(navigate))
        };
    }

    private sealed class MusicPlayerInstance : IPolygonComponentInstance
    {
        private readonly Action<string> _navigate;
        private readonly MusicLibrary _library = new();
        private readonly MusicPlayerService _player = new();
        private ComponentStateSnapshot _currentState;
        private long _revision;
        private int _isDisposed;

        public MusicPlayerInstance(Action<string> navigate)
        {
            _navigate = navigate;

            // 加载音乐库
            if (!string.IsNullOrWhiteSpace(_library.FolderPath))
                _library.Scan();

            // 监听播放状态变化
            _player.StateChanged += OnPlayerStateChanged;
            _player.TrackFinished += OnTrackFinished;

            _currentState = CreateState("未在播放", "", "▶", true);
        }

        public ComponentStateSnapshot CurrentState => Volatile.Read(ref _currentState);

        public event EventHandler<ComponentStateChangedEventArgs>? StateChanged;

        public ValueTask<ComponentActionResult> InvokeAsync(
            ComponentActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return ValueTask.FromResult(ComponentActionResult.Failed("组件已释放。"));

            switch (invocation.ActionId)
            {
                case ActionOpenPlayer:
                    _navigate("music-player");
                    return ValueTask.FromResult(ComponentActionResult.Completed("已打开音乐播放器。"));

                case ActionPlayPause:
                    HandlePlayPause();
                    return ValueTask.FromResult(ComponentActionResult.Completed(
                        _player.State == PlaybackState.Playing ? "正在播放。" : "已暂停。"));

                case ActionNext:
                    HandleNext();
                    return ValueTask.FromResult(ComponentActionResult.Completed("已跳到下一首。"));

                case ActionStop:
                    _player.Stop();
                    return ValueTask.FromResult(ComponentActionResult.Completed("已停止播放。"));

                default:
                    return ValueTask.FromResult(ComponentActionResult.Failed($"未知动作：{invocation.ActionId}"));
            }
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _isDisposed, 1);
            _player.StateChanged -= OnPlayerStateChanged;
            _player.TrackFinished -= OnTrackFinished;
            _player.Dispose();
            StateChanged = null;
            return ValueTask.CompletedTask;
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
                    var tracks = _library.Tracks;
                    if (tracks.Count > 0)
                        _player.Play(tracks[0]);
                    break;
            }
        }

        private void HandleNext()
        {
            var tracks = _library.Tracks;
            if (tracks.Count == 0) return;

            var current = _player.CurrentTrack;
            var index = current is not null
                ? tracks.ToList().IndexOf(current)
                : -1;
            var next = index >= tracks.Count - 1 ? 0 : index + 1;
            _player.Play(tracks[next]);
        }

        private void OnPlayerStateChanged()
        {
            var track = _player.CurrentTrack;
            var title = track?.Title ?? "未在播放";
            var info = track is not null
                ? $"{track.Extension.TrimStart('.')} · {track.SizeDisplay}"
                : "";
            var btnText = _player.State == PlaybackState.Playing ? "⏸" : "▶";

            Publish(title, info, btnText, true);
        }

        private void OnTrackFinished()
        {
            // 自动播放下一首
            HandleNext();
        }

        private void Publish(string title, string info, string btnText, bool btnEnabled)
        {
            if (Volatile.Read(ref _isDisposed) != 0) return;

            var next = CreateState(title, info, btnText, btnEnabled);
            Volatile.Write(ref _currentState, next);
            StateChanged?.Invoke(this, new ComponentStateChangedEventArgs(next));
        }

        private ComponentStateSnapshot CreateState(
            string title, string info, string btnText, bool btnEnabled)
        {
            return new ComponentStateSnapshot
            {
                Revision = Interlocked.Increment(ref _revision),
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
