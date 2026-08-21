using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Core.Launch.Auth;

/// <summary>一个可选用的启动账号。</summary>
public sealed class LaunchAccount
{
    public required string Type { get; init; } // "offline" | "microsoft" | future providers
    public required string DisplayName { get; init; }
    public string? OfflineName { get; init; }
    public string OfflineSkinId { get; set; } = "steve";
    public MicrosoftAccount? Microsoft { get; set; }

    public string Badge => Type switch
    {
        "microsoft" => "正版",
        "offline" => "离线",
        _ => "第三方"
    };

    public string TypeLabel => Type switch
    {
        "microsoft" => "正版账户",
        "offline" => "离线账户",
        _ => "第三方账户"
    };

    public string LoginModeLabel => Type switch
    {
        "microsoft" => "正版登录",
        "offline" => "离线登录",
        _ => "第三方登录"
    };
}
