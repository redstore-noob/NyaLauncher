namespace NyaLauncher.Core.Launch.Auth;

/// <summary>
/// Microsoft 正版认证流程（设备码、Xbox、Minecraft 服务）失败时抛出的异常。
/// <see cref="ErrorCode"/> 携带原始错误码，便于调用方区分失败原因。
/// </summary>
public sealed class MicrosoftAuthenticationException : Exception
{
    public MicrosoftAuthenticationException(string message)
        : base(message)
    {
    }

    public MicrosoftAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MicrosoftAuthenticationException(string message, string? errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>原始错误码；可能为 OAuth 错误字符串或 Xbox XErr 数字。</summary>
    public string? ErrorCode { get; }

    // ---- OAuth 设备码流程错误 ----
    public const string AuthorizationDeclined = "authorization_declined";
    public const string DeviceCodeExpired = "expired_token";

    // ---- XSTS 授权错误（XErr） ----
    /// <summary>账号没有 Xbox Live 档案。</summary>
    public const string XboxNoAccount = "2148916233";

    /// <summary>儿童账号，需要家长加入家庭组。</summary>
    public const string XboxChildAccount = "2148916235";

    /// <summary>需要家长同意（可能未满 18 岁）。</summary>
    public const string XboxConsentRequired = "2148916236";

    /// <summary>当前国家或地区不支持。</summary>
    public const string XboxRegionBlocked = "2148916237";

    /// <summary>需要年龄验证。</summary>
    public const string XboxAgeVerification = "2148916238";
}
