using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>一个可选用的启动账号：离线（名字）或正版（微软令牌）。</summary>
public sealed class LaunchAccount
{
    public required string Type { get; init; }           // "offline" | "microsoft"
    public required string DisplayName { get; init; }
    public string? OfflineName { get; init; }
    public MicrosoftAccount? Microsoft { get; set; }

    public string Badge => Type == "microsoft" ? "正版" : "离线";
    public string TypeLabel => Type == "microsoft" ? "正版账户" : "离线账户";
}
