# PORTING_NOTES — internal/auth（移植自 NyaLauncher.Core/Launch/Auth，12 个文件）

## 文件对应关系

| Go 文件 | C# 源 |
|---|---|
| errors.go | MicrosoftAuthenticationException.cs + RotatedCredentialsException.cs + AuthlibAuthenticationException（AuthlibAuthentication.cs 内）|
| account.go | LaunchInfo.cs 中的 IMinecraftAccount（移入 auth，见下）|
| microsoft_account.go | MicrosoftAccount.cs + AuthlibAccount.cs（含 AuthlibCredential）|
| launch_account.go | LaunchAccount.cs + DeviceCodeInfo.cs |
| microsoft_authenticator.go（microsoft_device_code_authenticator.go 内）| IMicrosoftAuthenticator.cs + MicrosoftAuthentication.cs |
| microsoft_device_code_authenticator.go | MicrosoftDeviceCodeAuthenticator.cs |
| account_secret_protector.go + secret_protector_windows.go / secret_protector_other.go | AccountSecretProtector.cs |
| account_store_service.go | AccountStore.cs + AccountStoreService.cs |
| component_display_account.go | ComponentDisplayAccount.cs |
| authlib_authentication.go | AuthlibAuthentication.cs（AuthlibAuthenticator）|
| authlib_injector_runtime.go | AuthlibInjectorRuntime.cs |

## 认证密钥保护选型（AccountSecretProtector）

- **Windows**：DPAPI（CurrentUser），与 C# 完全一致——通过标准库 `syscall.NewLazyDLL`
  调用 crypt32.dll 的 `CryptProtectData` / `CryptUnprotectData`（纯 syscall，无 cgo），
  附加熵与 C# 相同的 16 字节，存储格式同为 `nyaenc1:` + Base64(DPAPI blob)，
  旧版 config.json 的密文可直接解密，互相兼容。
- **非 Windows**：AES-256-GCM，密钥文件 `account.secret.key`（0600）保存在
  `config.DefaultStorageDirectory()`；附加熵作为 GCM 的 AAD 绑定应用身份。
  同样使用 `nyaenc1:` 前缀。文件权限方案替代 DPAPI 的按用户隔离，接口
  （`Protect` / `Unprotect` / `IsAvailable`）保持一致，宿主可按需替换实现。

## 有意的语义偏离

- **IMinecraftAccount 移入 auth 包**：C# 定义在 Launch 命名空间；Go 中 internal/launch
  必须依赖 auth（AccountStoreService），反向定义会构成循环导入。接口增加
  `AccountKind() string`（C# 用类型模式匹配区分账号）；方法名加 `Account` 前缀
  （AccountUsername 等），避免与结构体字段（如 MicrosoftAccount.AccessToken）冲突。
- **事件/多播 → 单回调字段**：`AccountStoreService.OnChanged`（对应 Changed 事件）；
  回调异常以 recover 隔离，与 C# GetInvocationList 逐个 try/catch 的隔离语义一致。
- **ObservableCollection → 普通切片**：`Current()` 返回快照副本；UI 绑定改由 Wails
  事件推送。`Selected()`/`Current()` 均线程安全。
- **async/await → 同步 + context**：Authenticate / Refresh / Validate / ValidateOrRefresh /
  ResolveServer / Authenticate 等均以 `context.Context` 首参。
- **DeviceCodeInfo.VerificationUriFull**：C# 为属性，Go 为方法。
- **HttpClient**：认证器内部各自持有 30 秒超时客户端（C# 相同）；注入器下载为
  2 分钟超时独立客户端。未复用 tools.SharedHTTPClient（15 秒超时不够）。
- **浏览器打开**：C# Process.Start(UseShellExecute)；Go 按 GOOS 用
  rundll32/open/xdg-open，失败静默（与 C# 一致）。
- **accountDto JSON 字段名保持 C# PascalCase**（含 ExpiresAt），兼容既有 config.json；
  时间用 `time.Time`（RFC3339），C# System.Text.Json 默认输出 ISO 8601，互通。
- **WithRefreshLock**：泛型 `Task<T>` → `func() error` 回调，结果经闭包捕获。
- **AccountStore 静态门面**：C# 静态类 → Go `Shared` 实例 + `AccountStoreXxx` 门面函数。
- **MicrosoftAuthentication.Shared / AuthlibAuthentication.Shared** →
  `DefaultMicrosoftAuthenticator` / `DefaultAuthlibAuthenticator`（另保留
  `MicrosoftAuthentication` 结构体包装以贴近 C# 命名）。
