package launch

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"regexp"
	"runtime"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"nyalauncher/internal/auth"
	"nyalauncher/internal/config"
	"nyalauncher/internal/logs"
)

// GameLaunchPhase 启动生命周期阶段。
type GameLaunchPhase int

const (
	GameLaunchPhaseIdle GameLaunchPhase = iota
	GameLaunchPhasePreparing
	GameLaunchPhaseRunning
	GameLaunchPhaseFailed
	GameLaunchPhaseExited
)

// GameLaunchSnapshot 某一时刻的启动状态视图；不可变，发布后不再修改。
type GameLaunchSnapshot struct {
	Revision    int64
	Phase       GameLaunchPhase
	Title       string
	Message     string
	VersionId   string
	AccountName string
	ProcessId   int
}

// IsBusy 正在准备（校验/装配）阶段。
func (s GameLaunchSnapshot) IsBusy() bool { return s.Phase == GameLaunchPhasePreparing }

// IsGameRunning 游戏进程运行中。
func (s GameLaunchSnapshot) IsGameRunning() bool { return s.Phase == GameLaunchPhaseRunning }

// ShouldShowIndicator 是否应显示"游戏运行中"指示器。
func (s GameLaunchSnapshot) ShouldShowIndicator() bool {
	return s.Phase == GameLaunchPhasePreparing || s.Phase == GameLaunchPhaseRunning
}

// idleSnapshot 尚未发起任何启动时的初始快照。
func idleSnapshot() GameLaunchSnapshot {
	return GameLaunchSnapshot{
		Revision: 0,
		Phase:    GameLaunchPhaseIdle,
		Title:    "尚未启动游戏",
		Message:  "选择账号和游戏实例后即可启动。",
	}
}

// maximumLogLines 内存日志的行数上限，超出后丢弃最早的行。
const maximumLogLines = 2000

// GameLaunchService 启动页与组件共用的唯一活动启动管线。生命周期状态以不可变快照
// 发布；进程输出保留在有界内存日志中供日志窗口查看。
type GameLaunchService struct {
	gate     sync.Mutex
	logLines []string
	current  GameLaunchSnapshot
	revision int64
	launchId int64
	// launchInProgress 0/1 启动互斥标记：同一时间只允许一次启动尝试。
	launchInProgress atomic.Int32

	gameProcess   *exec.Cmd
	stopRequested bool

	// accounts 账号存储（可注入，默认取全局共享实例），便于测试与多存储目录隔离。
	accounts *auth.AccountStoreService
	// offlineLauncher / microsoftLauncher 启动管线实现。
	offlineLauncher   IOfflineMinecraftLauncher
	microsoftLauncher IMicrosoftMinecraftLauncher

	// OnChanged 快照变更回调（对应 C# Changed 多播事件；
	// Go 移植为单回调字段，多订阅方需自行分发——语义偏离见 PORTING_NOTES.md）。
	OnChanged func(GameLaunchSnapshot)
}

// NewGameLaunchService 构造启动服务；accountStore 为空时使用 auth.Shared。
func NewGameLaunchService(accountStore *auth.AccountStoreService) *GameLaunchService {
	if accountStore == nil {
		accountStore = auth.Shared
	}
	return &GameLaunchService{
		accounts:          accountStore,
		current:           idleSnapshot(),
		offlineLauncher:   NewOfflineMinecraftLauncher(nil),
		microsoftLauncher: NewMicrosoftMinecraftLauncher(nil),
	}
}

// Current 返回当前快照。
func (s *GameLaunchService) Current() GameLaunchSnapshot {
	s.gate.Lock()
	defer s.gate.Unlock()
	return s.current
}

// GetLogText 返回内存日志全文。
func (s *GameLaunchService) GetLogText() string {
	s.gate.Lock()
	defer s.gate.Unlock()
	return strings.Join(s.logLines, "\n")
}

// LaunchSelected 启动当前选中的实例与账号（对应 C# LaunchSelectedAsync）。
// serverHost/serverPort 非空时直接进服。
func (s *GameLaunchService) LaunchSelected(
	ctx context.Context,
	serverHost string,
	serverPort *int,
) LaunchResult {
	// 原子占位：结束（含异常）时在 finally 释放
	if !s.launchInProgress.CompareAndSwap(0, 1) {
		return FailedLaunch("游戏正在启动，请稍候。")
	}
	defer s.launchInProgress.Store(0)

	// 前置校验：进程未运行、实例扫描完成、目录有效、已选实例，
	// 且不是暂不支持直接启动的外部启动器实例。
	if s.isProcessRunning() {
		return FailedLaunch("游戏已经在运行。")
	}
	instance := currentInstanceSnapshot()
	if instance.IsLoading {
		return FailedLaunch("游戏实例仍在扫描，请稍候。")
	}
	if strings.TrimSpace(instance.ErrorMessage) != "" {
		return FailedLaunch(fmt.Sprintf("Minecraft 目录无效：%s", instance.ErrorMessage))
	}
	if strings.TrimSpace(instance.SelectedVersionId) == "" ||
		strings.TrimSpace(instance.MinecraftDirectory) == "" {
		return FailedLaunch("请先选择一个已安装的游戏实例。")
	}

	// 外部启动器（MultiMC/CurseForge 等）的实例可管理内容，但暂不能直接启动
	if external, ok := resolveExternalInstance(instance.SourcePath); ok &&
		strings.EqualFold(external.InstanceId, instance.SelectedVersionId) {
		return FailedLaunch(fmt.Sprintf(
			"已识别 %s 实例并可管理其内容，但其原生版本补丁元数据暂不能由 NyaLauncher 直接启动。",
			external.Provider))
	}

	// 校验已选账号
	selectedAccount := s.accounts.Selected()
	if selectedAccount == nil {
		if len(s.accounts.Current()) == 0 {
			return FailedLaunch("请先添加并选择一个账号。")
		}
		return FailedLaunch("请先选择账号。")
	}

	versionId := instance.SelectedVersionId
	launchId := atomic.AddInt64(&s.launchId, 1)
	s.resetLog()
	s.appendLog(fmt.Sprintf("准备启动 Minecraft %s。", versionId), "LAUNCH")
	s.publishPreparing(instance, selectedAccount, "正在准备账号与 Java 启动参数…")

	// 启动前按配置校验并补全缺失的游戏文件；失败只记录日志，不阻断启动。
	if config.VerifyFilesBeforeLaunch() {
		s.appendLog("正在校验游戏文件完整性…", "LAUNCH")
		s.publishPreparing(instance, selectedAccount, "正在校验游戏文件完整性…")
		var verifier downloadVerifier
		repaired, err := verifier.verifyAndRepair(ctx, instance.MinecraftDirectory, versionId, func(status string) {
			s.appendLog(status, "LAUNCH")
		})
		if err != nil {
			// 校验失败不阻断启动：缺失文件由游戏侧自行暴露
			s.appendLog(fmt.Sprintf("文件校验异常（将继续启动）：%v", err), "LAUNCH")
		} else if repaired > 0 {
			s.appendLog(fmt.Sprintf("文件校验完成，已补全 %d 项缺失文件。", repaired), "LAUNCH")
		} else {
			s.appendLog("文件校验完成，所有文件正常。", "LAUNCH")
		}
	}

	if err := ctx.Err(); err != nil {
		s.appendLog("启动操作已取消。", "LAUNCH")
		s.publishFailure("启动已取消", "游戏启动操作已取消。", 0)
		return FailedLaunch("游戏启动已取消。")
	}

	// 校验所选账号凭据并准备启动用账号对象。
	launchAccount, prepareErr := s.prepareAccount(ctx, selectedAccount)
	if prepareErr != nil {
		return s.reportPrepareFailure(prepareErr)
	}

	options, optionsErr := s.buildLaunchOptions(instance, versionId, launchAccount, serverHost, serverPort)
	if optionsErr != nil {
		s.appendLog(fmt.Sprintf("启动失败：%v", optionsErr), "LAUNCH")
		s.publishFailure("启动失败", optionsErr.Error(), 0)
		return FailedLaunch(optionsErr.Error())
	}

	return s.runLauncher(selectedAccount, launchAccount, *options, launchId)
}

// reportPrepareFailure 发布账号准备失败的快照并返回失败结果。
// RotatedCredentialsError 属于特殊路径：凭据已刷新但档案交换失败，
// 刷新锁内已拿到轮换后的新凭据（在 prepareAccount 内先落库再返回错误）。
func (s *GameLaunchService) reportPrepareFailure(prepareErr error) LaunchResult {
	message := prepareErr.Error()
	s.appendLog(fmt.Sprintf("启动失败：%s", message), "LAUNCH")
	s.publishFailure("启动失败", message, 0)
	return FailedLaunch(message)
}

// prepareAccount 校验所选账号凭据并返回启动用账号对象：微软账号走凭据刷新，
// 皮肤站账号走令牌校验/刷新，离线账号直接构造。
func (s *GameLaunchService) prepareAccount(
	ctx context.Context,
	selectedAccount *auth.LaunchAccount,
) (MinecraftAccount, error) {
	switch selectedAccount.Type {
	case "microsoft":
		return s.prepareMicrosoftAccount(ctx, selectedAccount)
	case "authlib":
		return s.prepareAuthlibAccount(ctx, selectedAccount)
	default:
		s.appendLog("已准备离线账号。", "LAUNCH")
		return NewOfflineAccount(fallbackOfflineName(selectedAccount))
	}
}

func fallbackOfflineName(selectedAccount *auth.LaunchAccount) string {
	if strings.TrimSpace(selectedAccount.OfflineName) != "" {
		return selectedAccount.OfflineName
	}
	return "Player_01"
}

// prepareMicrosoftAccount 微软账号：与皮肤/档案服务共用同一把按账号的刷新锁，
// 锁内重读最新凭据，避免与并发刷新先后用同一个 refresh_token
// （轮换策略下会强制下线）。
func (s *GameLaunchService) prepareMicrosoftAccount(
	ctx context.Context,
	selectedAccount *auth.LaunchAccount,
) (MinecraftAccount, error) {
	s.appendLog("正在校验正版账号凭据。", "LAUNCH")

	var refreshed *auth.MicrosoftAccount
	if selectedAccount.Microsoft != nil {
		candidate := *selectedAccount.Microsoft
		refreshed = &candidate
	}
	var validated *auth.MicrosoftAccount
	var validateErr error
	lockErr := s.accounts.WithRefreshLock(selectedAccount, func() error {
		// 锁内重读最新凭据：等待锁的期间可能已有并发刷新写入轮换后的令牌
		current := selectedAccount.Microsoft
		if current == nil {
			return newMicrosoftCredentialsError("账号缺少正版凭据，请重新登录。")
		}
		result, err := auth.MicrosoftAuthentication.Validate(ctx, *current)
		if err != nil {
			// 刷新锁内已拿到轮换后的新凭据：先落库再返回错误，由上层提示重新登录
			var rotated *auth.RotatedCredentialsError
			if asRotatedCredentials(err, &rotated) {
				rotatedCopy := rotated.RefreshedAccount
				s.accounts.UpdateMicrosoftAccount(selectedAccount, &rotatedCopy)
			}
			validateErr = err
			return err
		}
		validated = &result
		return nil
	})
	if lockErr != nil {
		if validateErr != nil {
			return nil, validateErr
		}
		return nil, lockErr
	}
	_ = refreshed

	// 通过账号存储更新，确保 UI 订阅者收到变更通知
	s.accounts.UpdateMicrosoftAccount(selectedAccount, validated)
	s.appendLog("正版账号凭据校验完成。", "LAUNCH")
	accountCopy := *validated
	return &accountCopy, nil
}

// prepareAuthlibAccount 皮肤站账号：校验访问令牌，过期时自动刷新并写回账号存储。
// 凭据数据缺失时回退为离线账号。
func (s *GameLaunchService) prepareAuthlibAccount(
	ctx context.Context,
	selectedAccount *auth.LaunchAccount,
) (MinecraftAccount, error) {
	if selectedAccount.Authlib == nil {
		s.appendLog("已准备离线账号。", "LAUNCH")
		return NewOfflineAccount(fallbackOfflineName(selectedAccount))
	}

	s.appendLog("正在校验皮肤站账号凭据。", "LAUNCH")
	credential := *selectedAccount.Authlib
	var accessToken string
	var validateErr error
	lockErr := s.accounts.WithRefreshLock(selectedAccount, func() error {
		token, err := auth.DefaultAuthlibAuthenticator.ValidateOrRefresh(ctx, &credential, "")
		if err != nil {
			validateErr = err
			return err
		}
		accessToken = token
		return nil
	})
	if lockErr != nil {
		if validateErr != nil {
			return nil, validateErr
		}
		return nil, lockErr
	}

	if accessToken != credential.AccessToken {
		// 令牌已刷新：通过账号存储更新，确保 UI 订阅者收到变更通知
		credential.AccessToken = accessToken
		s.accounts.UpdateAuthlibAccount(selectedAccount, &credential)
		s.appendLog("皮肤站令牌已自动刷新。", "LAUNCH")
	}

	s.appendLog("皮肤站账号凭据校验完成。", "LAUNCH")
	return auth.AuthlibAccount{
		ProfileName: credential.ProfileName,
		ProfileUuid: credential.ProfileUuid,
		AccessToken: accessToken,
		ApiRoot:     credential.ApiRoot,
	}, nil
}

// runLauncher 启动 Java 进程并发布"运行中"快照，随后交由进程观察器接管退出事件。
func (s *GameLaunchService) runLauncher(
	selectedAccount *auth.LaunchAccount,
	launchAccount MinecraftAccount,
	options MinecraftLaunchOptions,
	launchId int64,
) LaunchResult {
	s.appendLog("正在解析版本、依赖库与 Java 运行时。", "LAUNCH")
	// 微软账号走正版分支（带在线会话校验），其余账号走离线/第三方分支
	var result *MinecraftLaunchResult
	var err error
	if microsoft, ok := launchAccount.(*auth.MicrosoftAccount); ok {
		result, err = s.microsoftLauncher.Launch(context.Background(), microsoft, options)
	} else {
		result, err = s.offlineLauncher.Launch(context.Background(), options)
	}
	if err != nil {
		message := err.Error()
		s.appendLog(fmt.Sprintf("启动失败：%s", message), "LAUNCH")
		s.publishFailure("启动失败", message, 0)
		return FailedLaunch(message)
	}

	javaHint := describeJavaRequirement(result.RequiredJavaMajorVersion)
	s.appendLog(fmt.Sprintf("Java 进程已启动，进程 ID：%d。", result.Pid()), "LAUNCH")
	s.publish(GameLaunchSnapshot{
		Revision:    s.nextRevision(),
		Phase:       GameLaunchPhaseRunning,
		Title:       fmt.Sprintf("%s 正在运行", result.VersionId),
		Message:     fmt.Sprintf("账号：%s · %s", result.Username, javaHint),
		VersionId:   result.VersionId,
		AccountName: selectedAccount.DisplayName,
		ProcessId:   result.Pid(),
	})

	// 进程观察与退出收尾（对应 C# PrepareProcessObservation / CompleteProcessExit）
	s.observeProcess(result, launchId)
	return CompletedLaunch(fmt.Sprintf("已启动 %s。", result.VersionId))
}

// describeJavaRequirement 把版本 JSON 里的 Java 主版本号翻译成用户可读的要求说明。
func describeJavaRequirement(requiredJavaMajorVersion *int) string {
	if requiredJavaMajorVersion != nil {
		return fmt.Sprintf("至少需要 Java %d", *requiredJavaMajorVersion)
	}
	return "Java 版本要求已满足"
}

// ---------------------------------------------------------------------------
// 快照与日志管线
// ---------------------------------------------------------------------------

// publishPreparing 发布"准备中"阶段快照。
func (s *GameLaunchService) publishPreparing(
	instance GameInstanceSnapshot,
	account *auth.LaunchAccount,
	message string,
) {
	s.publish(GameLaunchSnapshot{
		Revision:    s.nextRevision(),
		Phase:       GameLaunchPhasePreparing,
		Title:       fmt.Sprintf("正在启动 %s", instance.SelectedVersionId),
		Message:     message,
		VersionId:   instance.SelectedVersionId,
		AccountName: account.DisplayName,
	})
}

// publishFailure 发布失败快照：保留上一次的版本与账号信息便于定位。
func (s *GameLaunchService) publishFailure(title, message string, processId int) {
	lastSnapshot := s.Current()
	s.publish(GameLaunchSnapshot{
		Revision:    s.nextRevision(),
		Phase:       GameLaunchPhaseFailed,
		Title:       title,
		Message:     message,
		VersionId:   lastSnapshot.VersionId,
		AccountName: lastSnapshot.AccountName,
		ProcessId:   processId,
	})
}

func (s *GameLaunchService) nextRevision() int64 {
	return atomic.AddInt64(&s.revision, 1)
}

func (s *GameLaunchService) resetLog() {
	s.gate.Lock()
	s.logLines = nil
	s.gate.Unlock()
}

func (s *GameLaunchService) appendLog(line, logType string) {
	if strings.TrimSpace(line) == "" {
		return
	}

	// 内存日志与日志文件共用同一条脱敏结果，保证两边口径一致。
	redacted := RedactSecrets(line)
	s.gate.Lock()
	s.logLines = append(s.logLines, fmt.Sprintf("[%s] %s", time.Now().Format("15:04:05"), redacted))
	if len(s.logLines) > maximumLogLines {
		s.logLines = s.logLines[len(s.logLines)-maximumLogLines:]
	}
	s.gate.Unlock()

	// 文件写入放在 gate 之外：logs.Write 内部有独立写锁并做磁盘 I/O，
	// 持 gate 写文件会阻塞快照查询与 stdout 读取线程。
	logs.Write(logType, redacted)
}

// 凭据脱敏正则组：--accessToken 参数、旧版 token:<token>:<uuid> 会话、Bearer 头。
var (
	accessTokenArgumentPattern = regexp.MustCompile(`--accessToken\s+\S+`)
	legacySessionTokenPattern  = regexp.MustCompile(`token:[^:\s"]{8,}(:[0-9a-fA-F]{32})`)
	bearerTokenPattern         = regexp.MustCompile(`(?i)bearer\s+[A-Za-z0-9._\-]{16,}`)
)

// RedactSecrets 凭据脱敏：游戏 stdout 或后续日志点若带入访问令牌
// （--accessToken 参数、旧版 token:<token>:<uuid> 会话、Bearer 头），
// 进入内存日志前统一打码；玩家 UUID 非敏感，保留以便排查。
func RedactSecrets(line string) string {
	masked := accessTokenArgumentPattern.ReplaceAllString(line, "--accessToken ***")
	masked = legacySessionTokenPattern.ReplaceAllString(masked, "token:***$1")
	masked = bearerTokenPattern.ReplaceAllString(masked, "Bearer ***")
	return masked
}

// publish 更新当前快照并通知订阅者；回调异常被隔离，不扩散。
func (s *GameLaunchService) publish(snapshot GameLaunchSnapshot) {
	s.gate.Lock()
	s.current = snapshot
	handler := s.OnChanged
	s.gate.Unlock()

	if handler == nil {
		return
	}
	func() {
		defer func() {
			// 单个订阅者异常不扩散
			_ = recover()
		}()
		handler(snapshot)
	}()
}

// newMicrosoftCredentialsError 微软凭据缺失的错误包装。
func newMicrosoftCredentialsError(message string) error {
	return fmt.Errorf("%s", message)
}

// asRotatedCredentials errors.As 包装（独立函数便于将来替换错误语义）。
func asRotatedCredentials(err error, target **auth.RotatedCredentialsError) bool {
	if rotated, ok := err.(*auth.RotatedCredentialsError); ok {
		*target = rotated
		return true
	}
	return false
}

var _ = runtime.GOOS
var _ = os.Getenv
