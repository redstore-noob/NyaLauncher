namespace NyaLauncher.Core.Launch.Auth;

/// <summary>
/// Microsoft OAuth 设备码登录过程中需要展示给用户的信息。
/// 调用方应在回调中展示 UserCode 和验证地址，等待用户在浏览器中完成授权。
/// </summary>
public sealed record DeviceCodeInfo(
    string UserCode,
    string VerificationUri,
    string DeviceCode,
    TimeSpan ExpiresIn,
    int PollIntervalSeconds)
{
    /// <summary>预填了用户码的完整验证地址，可直接用于打开浏览器。</summary>
    public Uri VerificationUriFull =>
        new($"{VerificationUri}?user_code={UserCode}");
}
