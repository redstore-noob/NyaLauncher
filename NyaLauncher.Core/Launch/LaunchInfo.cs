using NyaLauncher.Core.Launch.Auth;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NyaLauncher.Core.Launch;
    /// <summary>
    /// 离线启动所需的外部配置。版本文件、依赖库和资源应已存在于游戏目录。
    /// </summary>
    public sealed class MinecraftLaunchOptions
    {
        public required string MinecraftDirectory { get; init; }

        /// <summary>
        /// 可选的实例游戏目录，用于隔离 mods、config、saves 等内容。
        /// 为空时使用 MinecraftDirectory。
        /// </summary>
        public string? GameDirectory { get; init; }

        public required string VersionId { get; init; }

        /// <summary>
        /// 启动使用的账号；可为离线账号或正版（Microsoft）账号。
        /// </summary>
        public required IMinecraftAccount Account { get; init; }

        /// <summary>
        /// 可选的 Java 可执行文件。为空时依次检查 NYALAUNCHER_JAVA、JAVA_HOME 和 PATH。
        /// </summary>
        public string? JavaExecutable { get; init; }

        /// <summary>
        /// 可选的 Minecraft runtime 根目录；启动器会递归查找并选择版本要求的 Java。
        /// </summary>
        public string? JavaRuntimeDirectory { get; init; }

        public int MinimumMemoryMb { get; init; } = 512;

        public int MaximumMemoryMb { get; init; } = 4096;

        public int WindowWidth { get; init; } = 854;

        public int WindowHeight { get; init; } = 480;

        public string LauncherName { get; init; } = "NyaLauncher";

        public string LauncherVersion { get; init; } = "0.1.0";

        public IReadOnlyList<string> AdditionalJvmArguments { get; init; } = [];

        public IReadOnlyList<string> AdditionalGameArguments { get; init; } = [];

        /// <summary>
        /// 启动过程中的文本日志回调（如 Java 自动下载阶段的进度提示）；可为空。
        /// </summary>
        public Action<string>? LogCallback { get; init; }

        /// <summary>
        /// 返回一个除账号外其余配置完全相同的副本，用于在启动前替换账号。
        /// </summary>
        public MinecraftLaunchOptions WithAccount(IMinecraftAccount account) => new()
        {
            MinecraftDirectory = MinecraftDirectory,
            GameDirectory = GameDirectory,
            VersionId = VersionId,
            Account = account,
            JavaExecutable = JavaExecutable,
            JavaRuntimeDirectory = JavaRuntimeDirectory,
            MinimumMemoryMb = MinimumMemoryMb,
            MaximumMemoryMb = MaximumMemoryMb,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            LauncherName = LauncherName,
            LauncherVersion = LauncherVersion,
            AdditionalJvmArguments = AdditionalJvmArguments,
            AdditionalGameArguments = AdditionalGameArguments,
            LogCallback = LogCallback
        };
    }
    /// <summary>
    /// 可供 Minecraft 启动使用的统一账号抽象。
    /// 离线账号与正版（Microsoft）账号都实现此接口。
    /// </summary>
    public interface IMinecraftAccount
    {
        /// <summary>游戏内玩家名（auth_player_name）。</summary>
        string Username { get; }

        /// <summary>
        /// 用于 auth_uuid 的 UUID 字符串。
        /// 正版账号为带连字符的档案 UUID，离线账号为无连字符的离线 UUID。
        /// </summary>
        string Uuid { get; }

        /// <summary>用于 auth_access_token 的访问令牌；离线账号固定为 "0"。</summary>
        string AccessToken { get; }

        /// <summary>用于 user_type 的启动参数：离线为 "legacy"，正版为 "msa"。</summary>
        string UserType { get; }

        /// <summary>用于 auth_xuid 的 Xbox 用户 ID；离线账号为空字符串。</summary>
        string XboxUserId { get; }
    }
    /// <summary>
    /// 不包含任何访问令牌的离线 Minecraft 账号。
    /// </summary>
    public sealed record OfflineAccount : IMinecraftAccount
    {
        private static readonly Regex UsernamePattern =
            new("^[A-Za-z0-9_]{1,16}$", RegexOptions.CultureInvariant);

        private OfflineAccount(string username, string uuid)
        {
            Username = username;
            Uuid = uuid;
        }

        public string Username { get; }

        /// <summary>
        /// 与服务端离线模式一致的 32 位无连字符 UUID。
        /// </summary>
        public string Uuid { get; }

        public string AccessToken => "0";

        public string UserType => "legacy";

        public string XboxUserId => string.Empty;

        public static OfflineAccount Create(string username)
        {
            var normalized = username.Trim();
            if (!UsernamePattern.IsMatch(normalized))
            {
                throw new ArgumentException(
                    "离线用户名必须为 1–16 位，只能包含英文字母、数字和下划线。",
                    nameof(username));
            }

            var source = Encoding.UTF8.GetBytes($"OfflinePlayer:{normalized}");
            var hash = MD5.HashData(source);

            // Java UUID.nameUUIDFromBytes 使用 UUID v3，并设置 RFC 4122 variant。
            hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

            return new OfflineAccount(normalized, Convert.ToHexStringLower(hash));
        }
    }

    /// <summary>
    /// Minecraft启动约定
    /// </summary>
    public interface IOfflineMinecraftLauncher
    {
        Task<MinecraftLaunchResult> LaunchAsync(
            MinecraftLaunchOptions options,
            CancellationToken cancellationToken = default);
    }
    /// <summary>
    /// 使用正版（Microsoft）账号启动 Minecraft 的入口。
    /// 登录与令牌刷新由 <see cref="Auth.IMicrosoftAuthenticator"/> 完成，
    /// 本类负责校验令牌并复用现有的离线启动管线（进程构造、参数构建、资源解析）。
    /// </summary>
    public interface IMicrosoftMinecraftLauncher
    {
        Task<MinecraftLaunchResult> LaunchAsync(
            MicrosoftAccount account,
            MinecraftLaunchOptions options,
            CancellationToken cancellationToken = default);
    }
    /// <summary>
    /// Minecraft启动错误时抛出的异常类
    /// </summary>
    public sealed class MinecraftLaunchException : Exception
    {
        public MinecraftLaunchException(string message)
            : base(message)
        {
        }

        public MinecraftLaunchException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
    /// <summary>
    /// Minecraft启动计划
    /// 启动Minecraft时，需要传递的参数
    /// </summary>
    /// <param name="JavaExecutable">Java可执行文件路径</param>
    /// <param name="WorkingDirectory">工作目录(.minecraft文件夹)</param>
    /// <param name="NativeDirectory">库目录</param>
    /// <param name="RequiredJavaMajorVersion">所需的Java主版本</param>
    /// <param name="Arguments">启动参数</param>
    internal sealed record MinecraftLaunchPlan(
    string JavaExecutable,
    string WorkingDirectory,
    string NativeDirectory,
    int? RequiredJavaMajorVersion,
    IReadOnlyList<string> Arguments);
    /// <summary>
    /// 
    /// </summary>
    /// <param name="Process"></param>
    /// <param name="VersionId"></param>
    /// <param name="Username"></param>
    /// <param name="RequiredJavaMajorVersion"></param>

    /// <summary>
    /// Minecraft实例启动后结果
    /// </summary>
    public sealed record MinecraftLaunchResult(
    Process Process,
    string VersionId,
    string Username,
    int? RequiredJavaMajorVersion);
