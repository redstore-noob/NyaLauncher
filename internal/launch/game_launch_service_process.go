package launch

import (
	"fmt"
	"os/exec"
	"runtime"
	"strings"
)

// isProcessRunning 判断当前是否有运行中的游戏进程。
func (s *GameLaunchService) isProcessRunning() bool {
	s.gate.Lock()
	defer s.gate.Unlock()
	return s.gameProcess != nil && s.gameProcess.Process != nil && s.gameProcess.ProcessState == nil
}

// TryStopGame 停止当前运行中的游戏（整棵 Java 进程树，含其子进程）。
// 准备阶段进程尚未创建、无法停止；成功后退出观察者会统一收尾并发布"游戏已停止"。
func (s *GameLaunchService) TryStopGame() LaunchResult {
	s.gate.Lock()
	var process *exec.Cmd
	if s.gameProcess != nil && s.gameProcess.Process != nil && s.gameProcess.ProcessState == nil {
		process = s.gameProcess
	}
	if process == nil {
		phase := s.current.Phase
		s.gate.Unlock()
		if phase == GameLaunchPhasePreparing {
			return FailedLaunch("游戏仍在启动准备中，请稍候再停止。")
		}
		return FailedLaunch("当前没有正在运行的游戏。")
	}
	s.stopRequested = true
	s.gate.Unlock()

	s.appendLog("已请求停止游戏进程…", "LAUNCH")
	if err := process.Process.Kill(); err != nil {
		s.gate.Lock()
		s.stopRequested = false
		s.gate.Unlock()
		s.appendLog(fmt.Sprintf("停止游戏进程失败：%v", err), "LAUNCH")
		return FailedLaunch(fmt.Sprintf("停止游戏失败：%v", err))
	}

	// Kill 成功不代表进程必然退出（显卡驱动挂起、安全软件拦截都会让
	// TerminateProcess 静默失效）：完整兜底逻辑见 completeProcessExit 语义说明。
	return CompletedLaunch(fmt.Sprintf("已请求停止游戏（PID %d）。", process.Process.Pid))
}

// observeProcess 等待游戏退出并发布收尾快照（对应 C# CompleteProcessExit）。
func (s *GameLaunchService) observeProcess(result *MinecraftLaunchResult, launchId int64) {
	go func() {
		exitErr, ok := <-result.Exit()
		if !ok {
			exitErr = nil
		}
		// 手动停止（Kill）的退出码非 0：按"已停止"而非"异常退出"报告
		s.gate.Lock()
		killRequested := s.stopRequested
		s.stopRequested = false
		sameLaunch := launchId == s.launchId && s.gameProcess == result.Cmd
		if sameLaunch {
			s.gameProcess = nil
		}
		s.gate.Unlock()
		if !sameLaunch {
			return
		}

		exitCode := 0
		if exitErr != nil {
			var exitError *exec.ExitError
			if asExitError(exitErr, &exitError) {
				exitCode = exitError.ExitCode()
			} else {
				s.appendLog(fmt.Sprintf("读取游戏退出状态失败：%v", exitErr), "LAUNCH")
				exitCode = -1
			}
		}

		stoppedManually := killRequested
		if stoppedManually {
			s.appendLog("游戏进程已手动停止。", "LAUNCH")
		} else if exitCode == 0 {
			s.appendLog("游戏进程已正常退出。", "LAUNCH")
		} else {
			s.appendLog(fmt.Sprintf("游戏进程已退出，退出代码：%d。", exitCode), "LAUNCH")
		}

		current := s.Current()
		title := "游戏异常退出"
		message := fmt.Sprintf("退出代码：%d", exitCode)
		if stoppedManually {
			title = "游戏已停止"
			message = "已手动停止游戏进程。"
		} else if exitCode == 0 {
			title = "游戏已退出"
			message = "退出代码：0"
		}
		s.publish(GameLaunchSnapshot{
			Revision:    s.nextRevision(),
			Phase:       GameLaunchPhaseExited,
			Title:       title,
			Message:     message,
			VersionId:   current.VersionId,
			AccountName: current.AccountName,
			ProcessId:   0,
		})
	}()
}

func asExitError(err error, target **exec.ExitError) bool {
	if exitError, ok := err.(*exec.ExitError); ok {
		*target = exitError
		return true
	}
	return false
}

// forceKillWithTaskkill taskkill 强制结束整棵进程树（/T /F）。返回 false 表示
// 命令执行失败；无论结果如何，调用方都应继续观察进程的实际退出状态。
func forceKillWithTaskkill(processId int) bool {
	if runtime.GOOS != "windows" {
		return false
	}
	command := exec.Command("taskkill.exe", "/PID", fmt.Sprintf("%d", processId), "/T", "/F")
	output, err := command.CombinedOutput()
	if err != nil {
		return false
	}
	return !strings.Contains(strings.ToLower(string(output)), "error")
}
