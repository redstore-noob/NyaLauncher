package bindings

// audioBridge music.AudioPlayer 的前端桥接实现：
// 所有播放动作转成 Wails 事件交给前端 Web Audio 执行；
// 进度由前端通过 Music.ReportPlaybackProgress 回传。

import (
	"time"
)

type audioBridge struct {
	api *MusicAPI
}

func (b *audioBridge) Play(filePath string) error {
	emit(b.api.ctx, "music:play", map[string]string{"filePath": filePath})
	return nil
}

func (b *audioBridge) Pause() { emit(b.api.ctx, "music:pause") }

func (b *audioBridge) Resume() error {
	emit(b.api.ctx, "music:resume")
	return nil
}

func (b *audioBridge) Stop() {
	emit(b.api.ctx, "music:stop")
	b.api.positionNs.Store(0)
}

func (b *audioBridge) Seek(position time.Duration) {
	emit(b.api.ctx, "music:seek", map[string]int64{"positionMs": position.Milliseconds()})
}

func (b *audioBridge) Position() time.Duration {
	return time.Duration(b.api.positionNs.Load())
}

func (b *audioBridge) Duration() time.Duration {
	return time.Duration(b.api.durationNs.Load())
}

func (b *audioBridge) SetVolume(percent int) {
	emit(b.api.ctx, "music:volume", map[string]int{"percent": percent})
}

// SetOnFinished 注册自然播完回调：由前端 Music.NotifyTrackFinished 触发。
func (b *audioBridge) SetOnFinished(callback func()) {
	b.api.finishedMu.Lock()
	b.api.onTrackFinished = callback
	b.api.finishedMu.Unlock()
}
