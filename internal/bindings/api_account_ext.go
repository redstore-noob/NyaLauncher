package bindings

// AccountAPI 扩展：取消微软登录轮询。
// internal/auth 的 Authenticate 本身接受 ctx（取消即中断轮询），
// 因此无需改动 internal/auth，仅在 bindings 层记录当前登录的 cancel 即可。

import (
	"context"
	"errors"
)

// errNoLoginInProgress 当前没有进行中的微软登录。
var errNoLoginInProgress = errors.New("no microsoft login in progress")

// CancelMicrosoftLogin 取消进行中的微软设备码登录（中断后台轮询）。
// 无进行中的登录时返回错误，前端可忽略。
func (a *AccountAPI) CancelMicrosoftLogin() error {
	a.loginMu.Lock()
	cancel := a.loginCancel
	a.loginMu.Unlock()
	if cancel == nil {
		return errNoLoginInProgress
	}
	cancel()
	return nil
}

// beginMicrosoftLogin 启动一次可取消的登录 ctx；若有旧登录先取消。
// 返回的 done 需在登录结束时调用以清空句柄。
func (a *AccountAPI) beginMicrosoftLogin() (context.Context, func()) {
	ctx, cancel := context.WithCancel(callCtx(a.ctx))
	a.loginMu.Lock()
	if a.loginCancel != nil {
		a.loginCancel()
	}
	a.loginCancel = cancel
	a.loginMu.Unlock()
	done := func() {
		cancel()
		a.loginMu.Lock()
		// 若期间已有新登录覆盖了句柄，这里置 nil 无害（新登录用自己的 cancel）。
		a.loginCancel = nil
		a.loginMu.Unlock()
	}
	return ctx, done
}
