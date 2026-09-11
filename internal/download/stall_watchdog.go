package download

import (
	"sync"
	"sync/atomic"
	"time"
)

// StallTimeout 连接彻底停滞（无任何字节进度）多久后判定失败并重试。
const StallTimeout = 2 * time.Minute

// stallWatchdog 进度感知的下载停滞看门狗。与固定时长的超时不同，空闲计时在每次
// Touch 时重置：大文件在慢速连接上下载十几分钟只要仍在推进就不会被误杀，
// 只有连接彻底停滞超过阈值才取消关联的 context（由重试逻辑接管）。
type stallWatchdog struct {
	cancel  contextCancelFunc
	timeout time.Duration

	ticker *time.Ticker
	done   chan struct{}

	lastActivity atomic.Int64 // unix nanos
	disposed     atomic.Bool

	disposeOnce sync.Once
}

// contextCancelFunc 取消函数签名（解耦 context.CancelFunc，便于测试）。
type contextCancelFunc func()

// newStallWatchdog 创建看门狗，每 15 秒检查一次停滞。
func newStallWatchdog(cancel contextCancelFunc, timeout time.Duration) *stallWatchdog {
	w := &stallWatchdog{
		cancel:  cancel,
		timeout: timeout,
		ticker:  time.NewTicker(15 * time.Second),
		done:    make(chan struct{}),
	}
	w.lastActivity.Store(time.Now().UnixNano())
	go w.loop()
	return w
}

// Touch 报告一次下载进度，重置停滞计时。
func (w *stallWatchdog) Touch() { w.lastActivity.Store(time.Now().UnixNano()) }

func (w *stallWatchdog) loop() {
	for {
		select {
		case <-w.done:
			return
		case <-w.ticker.C:
			w.checkStall()
		}
	}
}

func (w *stallWatchdog) checkStall() {
	if w.disposed.Load() {
		return
	}
	// 用户主动暂停期间不计时：停滞看门狗只负责网络停滞，不负责暂停时长
	if DownloadPauseGate.IsPaused() {
		return
	}
	idle := time.Since(time.Unix(0, w.lastActivity.Load()))
	if idle >= w.timeout {
		w.cancel()
	}
}

// Dispose 停止看门狗（幂等）。
func (w *stallWatchdog) Dispose() {
	w.disposeOnce.Do(func() {
		w.disposed.Store(true)
		w.ticker.Stop()
		close(w.done)
	})
}
