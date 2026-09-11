package launch

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	"nyalauncher/internal/auth"
	"nyalauncher/internal/download"
)

// MicrosoftMinecraftLauncher 正版（Microsoft 账号）Minecraft 启动器。
type MicrosoftMinecraftLauncher struct {
	launcher IOfflineMinecraftLauncher
}

// NewMicrosoftMinecraftLauncher 构造正版启动器；launcher 为空时使用离线启动器。
func NewMicrosoftMinecraftLauncher(launcher IOfflineMinecraftLauncher) *MicrosoftMinecraftLauncher {
	if launcher == nil {
		launcher = NewOfflineMinecraftLauncher(nil)
	}
	return &MicrosoftMinecraftLauncher{launcher: launcher}
}

// Launch 校验正版账号令牌后复用离线启动管线。
func (l *MicrosoftMinecraftLauncher) Launch(
	ctx context.Context,
	account *auth.MicrosoftAccount,
	options MinecraftLaunchOptions,
) (*MinecraftLaunchResult, error) {
	if account == nil {
		return nil, newLaunchError("账号不能为空。")
	}
	if strings.TrimSpace(account.AccessToken) == "" {
		return nil, newLaunchError("正版账号缺少访问令牌，请先完成登录。")
	}
	if account.IsExpired() {
		return nil, newLaunchError("正版账号的访问令牌已过期，请先通过认证器刷新或重新登录。")
	}

	accountCopy := *account
	return l.launcher.Launch(ctx, options.WithAccount(&accountCopy))
}

// OfflineMinecraftLauncher 从本地 Minecraft 目录构造并启动离线游戏进程。
// 本类不负责下载版本文件，也不读取或保存任何在线账号令牌。
type OfflineMinecraftLauncher struct {
	javaRuntimeLocator JavaRuntimeLocator
	profileLoader      MinecraftVersionProfileLoader
	libraryResolver    MinecraftLibraryResolver
	argumentBuilder    MinecraftArgumentBuilder
}

// NewOfflineMinecraftLauncher 构造离线启动器。
func NewOfflineMinecraftLauncher(javaRuntimeLocator JavaRuntimeLocator) *OfflineMinecraftLauncher {
	if javaRuntimeLocator == nil {
		javaRuntimeLocator = DefaultJavaRuntimeLocator{}
	}
	return &OfflineMinecraftLauncher{javaRuntimeLocator: javaRuntimeLocator}
}

// Launch 创建启动计划并拉起 Java 进程。
func (l *OfflineMinecraftLauncher) Launch(
	ctx context.Context,
	options MinecraftLaunchOptions,
) (*MinecraftLaunchResult, error) {
	plan, err := l.createPlan(ctx, options)
	if err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		TryDeleteDirectory(plan.NativeDirectory)
		return nil, err
	}

	command := exec.Command(plan.JavaExecutable, plan.Arguments...)
	command.Dir = plan.WorkingDirectory
	// Minecraft 统一以 UTF-8 输出；子进程继承启动器环境变量
	command.Env = buildChildEnvironment(plan)

	stdoutPipe, err := command.StdoutPipe()
	if err != nil {
		TryDeleteDirectory(plan.NativeDirectory)
		return nil, newLaunchErrorWrap("创建进程输出流失败。", err)
	}
	stderrPipe, err := command.StderrPipe()
	if err != nil {
		TryDeleteDirectory(plan.NativeDirectory)
		return nil, newLaunchErrorWrap("创建进程错误流失败。", err)
	}

	if err := command.Start(); err != nil {
		TryDeleteDirectory(plan.NativeDirectory)
		return nil, newLaunchErrorWrap("Java 进程未能启动。", err)
	}

	writeDebugArguments(plan)

	// 输出泵：逐行转发给回调（对应 C# BeginOutputReadLine / BeginErrorReadLine）。
	go pumpLines(stdoutPipe, false, options.GameOutputCallback)
	go pumpLines(stderrPipe, true, options.GameOutputCallback)

	// 退出观察：进程结束后清理 natives 目录并发布退出状态。
	result := &MinecraftLaunchResult{
		Cmd:                      command,
		exitCh:                   make(chan error, 1),
		VersionId:                options.VersionId,
		Username:                 options.Account.AccountUsername(),
		RequiredJavaMajorVersion: plan.RequiredJavaMajorVersion,
	}
	go func() {
		waitErr := command.Wait()
		TryDeleteDirectory(plan.NativeDirectory)
		result.exitCh <- waitErr
		close(result.exitCh)
	}()

	return result, nil
}

// pumpLines 逐行读取进程输出并转发；行读取结束后通道自动关闭。
func pumpLines(pipe interface{ Read([]byte) (int, error) }, isStderr bool, callback func(string, bool)) {
	if callback == nil {
		// 即使没有回调也要持续排空管道，避免子进程写满缓冲区阻塞
		buffer := make([]byte, 8192)
		for {
			if _, err := pipe.Read(buffer); err != nil {
				return
			}
		}
	}
	buffer := make([]byte, 0, 64*1024)
	chunk := make([]byte, 8192)
	for {
		read, err := pipe.Read(chunk)
		if read > 0 {
			buffer = append(buffer, chunk[:read]...)
			for {
				newline := -1
				for index, character := range buffer {
					if character == '\n' {
						newline = index
						break
					}
				}
				if newline < 0 {
					break
				}
				line := string(buffer[:newline])
				buffer = buffer[newline+1:]
				callback(strings.TrimSuffix(line, "\r"), isStderr)
			}
		}
		if err != nil {
			if len(buffer) > 0 {
				callback(strings.TrimSuffix(string(buffer), "\r"), isStderr)
			}
			return
		}
	}
}

// buildChildEnvironment 构造子进程环境：父环境 + 注入变量 - 移除变量。
func buildChildEnvironment(plan *MinecraftLaunchPlan) []string {
	environment := os.Environ()
	overrides := map[string]string{}
	for name, value := range plan.EnvironmentVariables {
		overrides[strings.ToLower(name)] = value
	}
	removed := plan.RemovedEnvironmentVariables
	filtered := make([]string, 0, len(environment)+len(overrides))
	for _, entry := range environment {
		equals := strings.Index(entry, "=")
		name := entry
		if equals > 0 {
			name = entry[:equals]
		}
		key := strings.ToLower(name)
		if removed[key] {
			continue
		}
		if _, overridden := overrides[key]; overridden {
			continue
		}
		filtered = append(filtered, entry)
	}
	for key, value := range overrides {
		if removed[key] {
			continue
		}
		filtered = append(filtered, key+"="+value)
	}
	return filtered
}

// createPlan 装配启动计划：解析版本档案、依赖库、natives、Java 与最终参数。
func (l *OfflineMinecraftLauncher) createPlan(
	ctx context.Context,
	options MinecraftLaunchOptions,
) (*MinecraftLaunchPlan, error) {
	if strings.TrimSpace(options.MinecraftDirectory) == "" ||
		!directoryExists(options.MinecraftDirectory) {
		return nil, newLaunchError("Minecraft 目录不存在：" + options.MinecraftDirectory)
	}

	minecraftDirectory, err := filepath.Abs(options.MinecraftDirectory)
	if err != nil {
		return nil, err
	}
	gameDirectory := options.GameDirectory
	if strings.TrimSpace(gameDirectory) == "" {
		gameDirectory = minecraftDirectory
	}
	if gameDirectory, err = filepath.Abs(gameDirectory); err != nil {
		return nil, err
	}
	if !directoryExists(gameDirectory) {
		return nil, newLaunchError(fmt.Sprintf("实例游戏目录不存在：%s", gameDirectory))
	}

	profile, err := l.profileLoader.Load(ctx, minecraftDirectory, options.VersionId)
	if err != nil {
		return nil, err
	}

	features := MinecraftRuleEvaluator.CreateDefaultFeatures(
		options.WindowWidth > 0 && options.WindowHeight > 0)
	resolved, err := l.libraryResolver.Resolve(ctx, profile, minecraftDirectory, features)
	if err != nil {
		return nil, err
	}
	transform, err := resolveMinecraftTransform(options, profile.MainClass, gameDirectory, resolved.Classpath)
	if err != nil {
		return nil, err
	}
	nativeDirectory, err := l.libraryResolver.ExtractNatives(ctx, profile.Id, resolved.Natives, minecraftDirectory)
	if err != nil {
		return nil, err
	}

	configuredJava := transform.JavaExecutableOverride
	if configuredJava == "" {
		configuredJava = options.JavaExecutable
	}
	javaExecutable, err := l.resolveJavaExecutable(
		ctx,
		configuredJava,
		profile.RequiredJavaMajorVersion,
		options.JavaRuntimeDirectory,
		options.LogCallback)
	if err != nil {
		TryDeleteDirectory(nativeDirectory)
		return nil, err
	}
	if transform.JavaExecutableOverride != "" &&
		!transformPathsEqual(javaExecutable, transform.JavaExecutableOverride) {
		TryDeleteDirectory(nativeDirectory)
		return nil, newLaunchError(
			"Java override did not resolve to the requested compatible executable.")
	}

	arguments, err := l.argumentBuilder.Build(
		profile,
		options,
		nativeDirectory,
		transform.Classpath,
		transform.MainClass,
		transform.PrependJvmArguments,
		transform.AppendJvmArguments,
		transform.PrependGameArguments,
		transform.AppendGameArguments)
	if err != nil {
		TryDeleteDirectory(nativeDirectory)
		return nil, err
	}
	if err := validateFinalArguments(arguments); err != nil {
		TryDeleteDirectory(nativeDirectory)
		return nil, err
	}

	return &MinecraftLaunchPlan{
		JavaExecutable:              javaExecutable,
		WorkingDirectory:            transform.WorkingDirectory,
		NativeDirectory:             nativeDirectory,
		RequiredJavaMajorVersion:    profile.RequiredJavaMajorVersion,
		Arguments:                   arguments,
		EnvironmentVariables:        transform.EnvironmentVariables,
		RemovedEnvironmentVariables: transform.RemovedEnvironmentVariables,
	}, nil
}

// resolveJavaExecutable 解析启动用 Java：优先已存在的精确匹配版本；找不到则自动下载
// 所需 Java（官方启动器同款行为）；自动下载失败再回退到"最低版本满足"的现有 Java
// （可能不兼容，但让真实错误浮现）。
func (l *OfflineMinecraftLauncher) resolveJavaExecutable(
	ctx context.Context,
	configuredPath string,
	requiredMajorVersion *int,
	runtimeDirectory string,
	log func(string),
) (string, error) {
	// 无版本要求：直接查找
	if requiredMajorVersion == nil {
		return l.javaRuntimeLocator.FindJavaExecutable(configuredPath, nil, runtimeDirectory)
	}
	required := *requiredMajorVersion

	// 1. 优先用已存在的精确匹配 Java（避免不必要的下载）
	if exactMatch := l.javaRuntimeLocator.FindExactMatchJava(configuredPath, required, runtimeDirectory); exactMatch != "" {
		return exactMatch, nil
	}

	// 2. 没有精确匹配：自动下载所需 Java（与 Mojang 官方启动器行为一致）
	logLog(log, fmt.Sprintf("未检测到 Java %d，正在自动下载…", required))
	if installed := tryAutoInstallJava(ctx, required, log); installed != "" {
		logLog(log, fmt.Sprintf("Java %d 安装完成：%s", required, installed))
		return installed, nil
	}

	// 3. 自动下载失败：回退到现有最佳 Java
	return l.javaRuntimeLocator.FindJavaExecutable(configuredPath, requiredMajorVersion, runtimeDirectory)
}

// tryAutoInstallJava 自动下载并安装指定主版本的 Java（优先 Temurin，带 SHA-256 校验）。
// 安装到启动器 runtime 目录，供后续启动直接复用。失败返回空串。
func tryAutoInstallJava(ctx context.Context, requiredMajorVersion int, log func(string)) string {
	supported := false
	for _, version := range download.SupportedJavaVersions {
		if version == requiredMajorVersion {
			supported = true
			break
		}
	}
	if !supported {
		return ""
	}

	candidates, err := download.QueryAvailableJavaVersions(ctx, download.JavaVendorTemurin)
	if err != nil {
		logLog(log, fmt.Sprintf("自动下载 Java %d 失败：%v", requiredMajorVersion, err))
		return ""
	}
	var candidate *download.JavaDownloadCandidate
	for index := range candidates {
		if candidates[index].MajorVersion == requiredMajorVersion {
			candidate = &candidates[index]
			break
		}
	}
	if candidate == nil {
		logLog(log, fmt.Sprintf("Temurin 源未找到 Java %d 候选。", requiredMajorVersion))
		return ""
	}

	var installer download.JavaRuntimeInstaller
	installed, err := installer.InstallCandidate(ctx, *candidate, func(progress download.JavaRuntimeInstallProgress) {
		logLog(log, progress.Phase)
	})
	if err != nil {
		logLog(log, fmt.Sprintf("自动下载 Java %d 失败：%v", requiredMajorVersion, err))
		return ""
	}
	return installed.JavaExecutablePath
}

// writeDebugArguments 调试辅助：设置环境变量 NYALAUNCHER_DEBUG_ARGS=1 时，
// 将实际启动参数写入临时目录 nya_launcher_debug_args.txt，便于排查登录/会话问题。
// 与 GameLaunchService.redactSecrets 同等强度脱敏：dump 文件长期留在临时目录，
// 绝不能写入真实的 accessToken / 会话令牌。
func writeDebugArguments(plan *MinecraftLaunchPlan) {
	if os.Getenv("NYALAUNCHER_DEBUG_ARGS") != "1" {
		return
	}

	var builder strings.Builder
	fmt.Fprintf(&builder, "java=%s\n", plan.JavaExecutable)
	fmt.Fprintf(&builder, "cwd=%s\n", plan.WorkingDirectory)
	builder.WriteString("--- arguments ---\n")
	for _, argument := range plan.Arguments {
		builder.WriteString(RedactSecrets(argument))
		builder.WriteString("\n")
	}
	temporaryDirectory := os.TempDir()
	targetPath := filepath.Join(temporaryDirectory, "nya_launcher_debug_args.txt")
	// 调试日志失败不影响游戏启动。
	_ = os.WriteFile(targetPath, []byte(builder.String()), 0o644)
}

func logLog(log func(string), message string) {
	if log != nil {
		log(message)
	}
}
