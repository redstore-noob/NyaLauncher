using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NyaLauncher.Core.Launch.Auth;

/// <summary>
/// Microsoft 设备码认证实现，完整覆盖正版登录链路：
/// OAuth 设备码 → Microsoft access token → XBL 3.0 → XSTS → Minecraft 登录 → 档案获取。
/// 同时支持使用 refresh_token 进行无感刷新。
/// </summary>
public sealed class MicrosoftDeviceCodeAuthenticator : IMicrosoftAuthenticator, IDisposable
{
    /// <summary>
    /// 默认使用的 Azure 公共客户端 ID。
    /// 来自 Prism Launcher 公开构建配置（GPL 开源项目的公共应用注册），
    /// 社区启动器（HMCL、Prism、ATLauncher 等）均使用同类公共应用完成设备码登录。
    /// 微软官方启动器的旧 ID（00000000402b5328）已失效，返回 AADSTS700016。
    /// 可通过构造函数或环境变量 NYALAUNCHER_MSA_CLIENT_ID 覆盖为自建应用。
    /// </summary>
    public const string DefaultClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";

    private const string Scope = "XboxLive.signin offline_access";

    /// <summary>允许通过环境变量覆盖 client_id（例如自建 Azure 应用时）。</summary>
    public static string? ClientIdOverride =>
        Environment.GetEnvironmentVariable("NYALAUNCHER_MSA_CLIENT_ID");

    private static readonly Uri DeviceCodeEndpoint =
        new("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode");
    private static readonly Uri TokenEndpoint =
        new("https://login.microsoftonline.com/consumers/oauth2/v2.0/token");
    private static readonly Uri XboxLiveAuthenticateEndpoint =
        new("https://user.auth.xboxlive.com/user/authenticate");
    private static readonly Uri XstsAuthorizeEndpoint =
        new("https://xsts.auth.xboxlive.com/xsts/authorize");
    private static readonly Uri MinecraftLoginEndpoint =
        new("https://api.minecraftservices.com/authentication/login_with_xbox");
    private static readonly Uri MinecraftProfileEndpoint =
        new("https://api.minecraftservices.com/minecraft/profile");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _clientId;

    /// <summary>
    /// Xbox Live 与 XSTS API 要求属性名为 PascalCase（实测 camelCase 会返回 400）。
    /// 该选项保持匿名对象的 C# 属性名原样输出。
    /// </summary>
    private static readonly JsonSerializerOptions XboxJsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public MicrosoftDeviceCodeAuthenticator(
        string? clientId = null,
        HttpClient? httpClient = null)
    {
        _clientId = !string.IsNullOrWhiteSpace(clientId)
            ? clientId
            : ClientIdOverride ?? DefaultClientId;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NyaLauncher/1.0");
        return client;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    public async Task<MicrosoftAccount> AuthenticateAsync(
        Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeHandler = null,
        CancellationToken cancellationToken = default)
    {
        var deviceCode = await RequestDeviceCodeAsync(cancellationToken).ConfigureAwait(false);

        if (deviceCodeHandler is not null)
        {
            // 等待调用方完成设备码展示（例如 UI 提示 + 自动打开浏览器）。
            await deviceCodeHandler(deviceCode, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            TryOpenVerificationBrowser(deviceCode.VerificationUriFull);
        }

        var (accessToken, refreshToken) = await PollForTokenAsync(
            deviceCode, cancellationToken).ConfigureAwait(false);

        return await ExchangeForMinecraftAccountAsync(
            accessToken, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MicrosoftAccount> RefreshAsync(
        MicrosoftAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            throw new MicrosoftAuthenticationException(
                "账号没有可用的刷新令牌，请重新登录。");
        }

        var (accessToken, refreshToken) = await RequestTokenByRefreshTokenAsync(
            account.RefreshToken,
            // 服务端未返回新 refresh_token（无轮换策略）时回退保留旧值，避免账号被清空
            account.RefreshToken,
            cancellationToken).ConfigureAwait(false);

        return await ExchangeForMinecraftAccountAsync(
            accessToken, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    public Task<MicrosoftAccount> ValidateAsync(
        MicrosoftAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.IsExpired
            ? RefreshAsync(account, cancellationToken)
            : Task.FromResult(account);
    }

    // ------------------------------------------------------------------
    // 内部实现
    // ------------------------------------------------------------------

    private async Task<DeviceCodeInfo> RequestDeviceCodeAsync(
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = Scope
        });

        using var response = await _httpClient.PostAsync(
            DeviceCodeEndpoint, content, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthenticationException(
                $"请求设备码失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        var payload = JsonSerializer.Deserialize<DeviceCodeResponse>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.DeviceCode))
        {
            throw new MicrosoftAuthenticationException("设备码响应格式不正确。");
        }

        return new DeviceCodeInfo(
            payload.UserCode,
            payload.VerificationUri,
            payload.DeviceCode,
            TimeSpan.FromSeconds(Math.Max(1, payload.ExpiresInSeconds)),
            Math.Max(1, payload.IntervalSeconds));
    }

    private async Task<(string AccessToken, string RefreshToken)> PollForTokenAsync(
        DeviceCodeInfo deviceCode,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + deviceCode.ExpiresIn;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(
                TimeSpan.FromSeconds(deviceCode.PollIntervalSeconds),
                cancellationToken).ConfigureAwait(false);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = _clientId,
                ["device_code"] = deviceCode.DeviceCode
            });

            using var response = await _httpClient.PostAsync(
                TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            var token = DeserializeToken(body);

            if (!string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                return (token.AccessToken, token.RefreshToken ?? string.Empty);
            }

            switch (token?.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    // OAuth 2.0 规范：服务器要求放慢轮询，间隔 +5 秒后继续等待
                    deviceCode = deviceCode with
                    {
                        PollIntervalSeconds = deviceCode.PollIntervalSeconds + 5
                    };
                    continue;
                case "authorization_declined":
                    throw new MicrosoftAuthenticationException(
                        "你在浏览器中拒绝了授权请求。",
                        MicrosoftAuthenticationException.AuthorizationDeclined);
                case "expired_token":
                    throw new MicrosoftAuthenticationException(
                        "设备码已过期，请重新开始登录。",
                        MicrosoftAuthenticationException.DeviceCodeExpired);
                case "bad_verification_code":
                    throw new MicrosoftAuthenticationException(
                        "设备码无效，请重新开始登录。", "bad_verification_code");
                default:
                    throw new MicrosoftAuthenticationException(
                        $"设备码登录失败：{token?.ErrorDescription ?? token?.Error ?? "未知错误"}",
                        token?.Error);
            }
        }

        throw new MicrosoftAuthenticationException(
            "设备码登录超时，请重试。",
            MicrosoftAuthenticationException.DeviceCodeExpired);
    }

    private async Task<(string AccessToken, string RefreshToken)> RequestTokenByRefreshTokenAsync(
        string refreshToken,
        string fallbackRefreshToken,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _clientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        });

        using var response = await _httpClient.PostAsync(
            TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        var token = DeserializeToken(body);

        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new MicrosoftAuthenticationException(
                $"刷新令牌失败（HTTP {(int)response.StatusCode}）：{token?.ErrorDescription ?? body}",
                token?.Error);
        }

        // 服务端未返回新 refresh_token（无轮换策略）时回退保留旧值，防止账号刷新后被清空
        return (token.AccessToken,
            string.IsNullOrWhiteSpace(token.RefreshToken) ? fallbackRefreshToken : token.RefreshToken);
    }

    /// <summary>
    /// 反序列化令牌响应；非 JSON 错误体（代理错误页等）包装为友好异常。
    /// </summary>
    private static TokenResponse? DeserializeToken(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<TokenResponse>(body);
        }
        catch (JsonException exception)
        {
            throw new MicrosoftAuthenticationException(
                $"令牌服务返回了无法解析的响应：{Truncate(body)}", exception);
        }
    }

    private static string Truncate(string value, int maxLength = 200) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    /// <summary>XBL 3.0 认证，返回 XBL 令牌与用户哈希（uhs）。</summary>
    private async Task<(string Token, string Uhs)> AuthenticateWithXboxLiveAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, XboxLiveAuthenticateEndpoint);
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        request.Content = JsonContent.Create(new
        {
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={microsoftAccessToken}"
            }
        }, options: XboxJsonOptions);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthenticationException(
                $"Xbox Live 认证失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        var payload = JsonSerializer.Deserialize<XboxResponse>(body);
        var token = payload?.Token;
        var uhs = payload?.DisplayClaims?.Xui?.FirstOrDefault()?.Uhs;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(uhs))
        {
            throw new MicrosoftAuthenticationException("Xbox Live 认证响应格式不正确。");
        }

        return (token, uhs);
    }

    /// <summary>
    /// XSTS 授权，返回 XSTS 身份令牌与 Xbox 用户 ID（xuid，取自 xui[0].xid）。
    /// xuid 是正版会话的关键标识，官方启动器通过 --xuid 参数传入游戏。
    /// </summary>
    private async Task<(string Token, string Xuid)> AuthenticateWithXstsAsync(
        string xblToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, XstsAuthorizeEndpoint);
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        request.Content = JsonContent.Create(new
        {
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            }
        }, options: XboxJsonOptions);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowForXstsError(response, body);
        }

        var payload = JsonSerializer.Deserialize<XboxResponse>(body);
        var token = payload?.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new MicrosoftAuthenticationException("XSTS 授权响应格式不正确。");
        }

        var xuid = payload?.DisplayClaims?.Xui?.FirstOrDefault()?.Xid ?? string.Empty;
        return (token, xuid);
    }

    private static void ThrowForXstsError(HttpResponseMessage response, string body)
    {
        string? errorCode = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("XErr", out var xErr) &&
                xErr.ValueKind == JsonValueKind.Number)
            {
                errorCode = xErr.GetInt64().ToString();
            }
        }
        catch (JsonException)
        {
            // 无法解析时回退到通用错误信息。
        }

        var message = errorCode switch
        {
            MicrosoftAuthenticationException.XboxNoAccount =>
                "该 Microsoft 账号没有 Xbox Live 档案，无法完成 Minecraft 登录。",
            MicrosoftAuthenticationException.XboxChildAccount =>
                "该账号是儿童账号，需要家长将其加入家庭组后才能游玩。",
            MicrosoftAuthenticationException.XboxConsentRequired =>
                "该账号需要获得家长同意（可能未满 18 岁），请先完成相关流程。",
            MicrosoftAuthenticationException.XboxRegionBlocked =>
                "当前国家或地区不支持 Minecraft 服务。",
            MicrosoftAuthenticationException.XboxAgeVerification =>
                "该账号需要先完成年龄验证才能游玩。",
            _ => $"XSTS 授权失败（HTTP {(int)response.StatusCode}）：{body}"
        };

        throw new MicrosoftAuthenticationException(message, errorCode);
    }

    /// <summary>使用 XSTS 身份令牌换取 Minecraft 访问令牌。</summary>
    private async Task<(string AccessToken, int ExpiresInSeconds)> LoginWithMinecraftAsync(
        string uhs,
        string xstsToken,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MinecraftLoginEndpoint,
            new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" },
            cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden &&
                body.Contains("ACCOUNT_SUSPENDED", StringComparison.OrdinalIgnoreCase))
            {
                throw new MicrosoftAuthenticationException(
                    "该账号已被 Minecraft 服务封禁（ACCOUNT_SUSPENDED），无法登录，需联系官方客服申诉。");
            }

            throw new MicrosoftAuthenticationException(
                $"Minecraft 登录失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        var payload = JsonSerializer.Deserialize<MinecraftLoginResponse>(body);
        if (string.IsNullOrWhiteSpace(payload?.AccessToken))
        {
            throw new MicrosoftAuthenticationException("Minecraft 登录响应格式不正确。");
        }

        return (payload.AccessToken, payload.ExpiresInSeconds);
    }

    /// <summary>获取玩家档案（UUID 与游戏名），顺带确认账号拥有 Minecraft。</summary>
    private async Task<MinecraftProfileResponse> FetchMinecraftProfileAsync(
        string minecraftToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        request.Headers.Authorization = new("Bearer", minecraftToken);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MicrosoftAuthenticationException(
                "该 Microsoft 账号尚未购买 Minecraft（Java 版），无法启动游戏。");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAuthenticationException(
                $"获取 Minecraft 档案失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        var payload = JsonSerializer.Deserialize<MinecraftProfileResponse>(body);
        if (string.IsNullOrWhiteSpace(payload?.Id) || string.IsNullOrWhiteSpace(payload.Name))
        {
            throw new MicrosoftAuthenticationException("Minecraft 档案响应格式不正确。");
        }

        return payload;
    }

    /// <summary>将 Microsoft 令牌逐步交换为完整的正版账号。</summary>
    private async Task<MicrosoftAccount> ExchangeForMinecraftAccountAsync(
        string microsoftAccessToken,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var (xblToken, uhs) = await AuthenticateWithXboxLiveAsync(
            microsoftAccessToken, cancellationToken).ConfigureAwait(false);
        var (xstsToken, xstsXuid) = await AuthenticateWithXstsAsync(
            xblToken, cancellationToken).ConfigureAwait(false);
        var (minecraftToken, expiresInSeconds) = await LoginWithMinecraftAsync(
            uhs, xstsToken, cancellationToken).ConfigureAwait(false);

        // xuid（Xbox 用户 ID）优先取 XSTS 响应中的 xui[0].xid，
        // 该字段是官方记录的 xuid 权威来源；解析失败时回退到
        // 从 Minecraft token 的 JWT payload 中提取。
        // 新版 Minecraft 会将 xuid 为空判定为离线模式（皮肤不加载）。
        var xuid = !string.IsNullOrWhiteSpace(xstsXuid)
            ? xstsXuid
            : ExtractXuidFromMinecraftToken(minecraftToken);
        var profile = await FetchMinecraftProfileAsync(
            minecraftToken, cancellationToken).ConfigureAwait(false);

        // FetchMinecraftProfileAsync 已确保 Id / Name 非空，此处显式断言以消除可空警告。
        return new MicrosoftAccount
        {
            Username = profile.Name!,
            Uuid = FormatUuid(profile.Id!),
            AccessToken = minecraftToken,
            RefreshToken = refreshToken,
            XboxUserId = xuid,
            ClientId = _clientId,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresInSeconds))
        };
    }

    /// <summary>
    /// 从 Minecraft 访问令牌（JWT）的 payload 中解析 Xbox 用户 ID（xuid）。
    /// XSTS 授权响应只返回 uhs 而不返回 xid，而 Minecraft token 的 JWT
    /// payload 中含有 "xuid" claim（例如 "2535423690656432"）。
    /// </summary>
    private static string ExtractXuidFromMinecraftToken(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2)
                return string.Empty;

            // base64url → base64
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("xuid", out var xuid) &&
                xuid.ValueKind == JsonValueKind.String)
            {
                return xuid.GetString() ?? string.Empty;
            }
        }
        catch (FormatException)
        {
            // JWT 解析失败不致命；xuid 为空时游戏仍可离线启动。
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    /// <summary>
    /// 将档案 UUID 归一化为 32 位无连字符格式（Minecraft profile API 原生格式）。
    /// 官方启动器与主流启动器（HMCL、Prism Launcher 等）均以无连字符格式向游戏
    /// 传递 --uuid 与 --session 中的 uuid；游戏端 UUIDTypeAdapter.fromString
    /// 虽可兼容两种格式，但会话/皮肤相关接口统一使用无连字符格式，
    /// 故此处始终输出无连字符形式。
    /// </summary>
    private static string FormatUuid(string id)
    {
        if (string.IsNullOrEmpty(id))
            return id;

        return id.Length == 32 ? id : id.Replace("-", "");
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryOpenVerificationBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch
        {
            // 打开浏览器失败不影响登录流程，用户仍可手动输入验证地址。
        }
    }

    // ------------------------------------------------------------------
    // 响应模型
    // ------------------------------------------------------------------

    private sealed record DeviceCodeResponse(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_uri")] string VerificationUri,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds,
        [property: JsonPropertyName("interval")] int IntervalSeconds);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record XboxResponse(
        [property: JsonPropertyName("Token")] string? Token,
        [property: JsonPropertyName("DisplayClaims")] DisplayClaims? DisplayClaims);

    private sealed record DisplayClaims(
        [property: JsonPropertyName("xui")] IReadOnlyList<XuiClaim>? Xui);

    private sealed record XuiClaim(
        [property: JsonPropertyName("uhs")] string? Uhs,
        [property: JsonPropertyName("xid")] string? Xid);

    private sealed record MinecraftLoginResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds);

    private sealed record MinecraftProfileResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);
}
