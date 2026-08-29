using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NyaLauncher.Core.Launch.Auth;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// Authenticated Minecraft Java profile operations used by the built-in skin
/// editor. Authentication refresh stays centralized with the shared account.
/// </summary>
internal sealed class MinecraftProfileService
{
    private static readonly Uri ProfileEndpoint =
        new("https://api.minecraftservices.com/minecraft/profile");
    private static readonly Uri SkinEndpoint =
        new("https://api.minecraftservices.com/minecraft/profile/skins");
    private static readonly Uri ActiveCapeEndpoint =
        new("https://api.minecraftservices.com/minecraft/profile/capes/active");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IMicrosoftAuthenticator _authenticator;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// 创建档案服务。两个依赖都可省略，省略时使用默认实现（30 秒超时的
    /// <see cref="System.Net.Http.HttpClient"/> 与 <see cref="MicrosoftDeviceCodeAuthenticator"/>）。
    /// </summary>
    /// <param name="httpClient">复用的 HttpClient；为空时内部新建一个（超时 30 秒）。</param>
    /// <param name="authenticator">微软认证器；为空时使用设备码认证器。</param>
    public MinecraftProfileService(
        HttpClient? httpClient = null,
        IMicrosoftAuthenticator? authenticator = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _authenticator = authenticator ?? new MicrosoftDeviceCodeAuthenticator();
    }

    /// <summary>
    /// 外观（皮肤/披风）发生变化时触发，参数为发生变化的账号标识。
    /// 订阅方（如皮肤组件）应据此刷新显示。
    /// </summary>
    public event Action<string>? AppearanceChanged;

    public async Task<MinecraftProfile> GetProfileAsync(
        LaunchAccount account,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendWithFreshTokenAsync(
            microsoft => CreateAuthorizedRequest(HttpMethod.Get, ProfileEndpoint, microsoft),
            account,
            cancellationToken).ConfigureAwait(false);
        return await ReadProfileAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadSkinAsync(
        LaunchAccount account,
        string filePath,
        MinecraftSkinModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        // 请求工厂在每个重试轮次重新构建（含重新打开文件流），随请求一起释放
        using var response = await SendWithFreshTokenAsync(microsoft =>
        {
            var input = File.OpenRead(filePath);
            var content = new MultipartFormDataContent();
            content.Add(
                new StringContent(model == MinecraftSkinModel.Slim ? "slim" : "classic"),
                "variant");
            var fileContent = new StreamContent(input);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", Path.GetFileName(filePath));

            var request = CreateAuthorizedRequest(HttpMethod.Post, SkinEndpoint, microsoft);
            request.Content = content;
            return request;
        }, account, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        RaiseAppearanceChanged(account);
    }

    public async Task SetActiveCapeAsync(
        LaunchAccount account,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendWithFreshTokenAsync(microsoft =>
        {
            var request = CreateAuthorizedRequest(
                string.IsNullOrWhiteSpace(capeId) ? HttpMethod.Delete : HttpMethod.Put,
                ActiveCapeEndpoint,
                microsoft);
            if (!string.IsNullOrWhiteSpace(capeId))
                request.Content = JsonContent.Create(new { capeId });
            return request;
        }, account, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        RaiseAppearanceChanged(account);
    }

    /// <summary>
    /// 用当前令牌发送授权请求；若服务端返回 401/403（令牌可能已在服务端被吊销，
    /// 例如账号在其他设备登录过），强制刷新令牌后重试一次。
    /// </summary>
    private async Task<HttpResponseMessage> SendWithFreshTokenAsync(
        Func<MicrosoftAccount, HttpRequestMessage> requestFactory,
        LaunchAccount account,
        CancellationToken cancellationToken)
    {
        var microsoft = await EnsureFreshAccountAsync(account, cancellationToken)
            .ConfigureAwait(false);
        using (var request = requestFactory(microsoft))
        {
            var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
                return response;

            response.Dispose();
        }

        microsoft = await EnsureFreshAccountAsync(account, cancellationToken, forceRefresh: true)
            .ConfigureAwait(false);
        using var retryRequest = requestFactory(microsoft);
        return await _httpClient.SendAsync(retryRequest, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MicrosoftAccount> EnsureFreshAccountAsync(
        LaunchAccount account,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Type, "microsoft", StringComparison.OrdinalIgnoreCase) ||
            account.Microsoft is null)
        {
            throw new InvalidOperationException("当前账号不是可编辑皮肤的正版账号。");
        }

        if (!forceRefresh && !account.Microsoft.IsExpired)
            return account.Microsoft;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && !account.Microsoft.IsExpired)
                return account.Microsoft;

            var refreshed = await _authenticator.RefreshAsync(account.Microsoft, cancellationToken)
                .ConfigureAwait(false);
            AccountStore.UpdateMicrosoftAccount(account, refreshed);
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        Uri endpoint,
        MicrosoftAccount account)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        return request;
    }

    private static async Task<MinecraftProfile> ReadProfileAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var dto = await response.Content.ReadFromJsonAsync<ProfileDto>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidDataException("Minecraft 档案响应缺少玩家身份信息。");

        return new MinecraftProfile(
            dto.Id,
            dto.Name,
            ConvertTextures(dto.Skins),
            ConvertTextures(dto.Capes));
    }

    private static IReadOnlyList<MinecraftProfileTexture> ConvertTextures(
        IReadOnlyList<TextureDto>? source)
    {
        if (source is null)
            return [];

        return source
            .Select(texture => (Texture: texture, Url: NormalizeTextureUrl(texture.Url)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Texture.Id) && item.Url is not null)
            .Select(item => new MinecraftProfileTexture(
                item.Texture.Id!,
                item.Url!,
                item.Texture.Alias ?? string.Empty,
                item.Texture.Variant ?? string.Empty,
                string.Equals(item.Texture.State, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string? NormalizeTextureUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return null;
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return uri.AbsoluteUri;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "textures.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        }.Uri.AbsoluteUri;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = string.Empty;
        string? rawError = null;
        try
        {
            rawError = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var error = JsonSerializer.Deserialize<ErrorDto>(rawError, JsonOptions);
            detail = error?.ErrorMessage ?? error?.Path ?? string.Empty;
        }
        catch (JsonException)
        {
            // The status code remains useful if the service returned non-JSON.
        }

        if (response.StatusCode == HttpStatusCode.Forbidden &&
            rawError?.Contains("ACCOUNT_SUSPENDED", StringComparison.OrdinalIgnoreCase) == true)
        {
            WarnAccountSuspendedOnce();
            throw new HttpRequestException(
                "正版账号已被 Minecraft 服务封禁（ACCOUNT_SUSPENDED），档案与皮肤不可用，需联系官方客服申诉。",
                null,
                response.StatusCode);
        }

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"Minecraft 档案服务返回 {(int)response.StatusCode}。"
                : $"Minecraft 档案服务返回 {(int)response.StatusCode}：{detail}",
            null,
            response.StatusCode);
    }

    private static bool _suspensionAlertShown;

    /// <summary>账号被封禁时向用户弹一次警示，避免无声回退让人困惑。</summary>
    private static void WarnAccountSuspendedOnce()
    {
        if (_suspensionAlertShown)
            return;

        _suspensionAlertShown = true;
        NyaAlert.Error(
            "正版账号已被 Minecraft 服务封禁（ACCOUNT_SUSPENDED），皮肤与档案暂不可用，需联系官方客服申诉。");
    }

    private void RaiseAppearanceChanged(LaunchAccount account)
    {
        var key = AccountStore.GetStableKey(account);
        foreach (Action<string> subscriber in AppearanceChanged?.GetInvocationList() ?? [])
        {
            try
            {
                subscriber(key);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"AppearanceChanged 订阅者异常：{exception}");
            }
        }
    }

    private sealed record ProfileDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("skins")] IReadOnlyList<TextureDto>? Skins,
        [property: JsonPropertyName("capes")] IReadOnlyList<TextureDto>? Capes);

    private sealed record TextureDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("variant")] string? Variant,
        [property: JsonPropertyName("alias")] string? Alias);

    private sealed record ErrorDto(
        [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
        [property: JsonPropertyName("path")] string? Path);
}

internal enum MinecraftSkinModel
{
    Classic,
    Slim
}

/// <summary>一个 Minecraft 正版档案：角色 Id、名称与可选的皮肤/披风列表。</summary>
/// <param name="Id">角色 Id。</param>
/// <param name="Name">玩家名。</param>
/// <param name="Skins">皮肤纹理列表，最多一个处于激活态。</param>
/// <param name="Capes">披风纹理列表。</param>
internal sealed record MinecraftProfile(
    string Id,
    string Name,
    IReadOnlyList<MinecraftProfileTexture> Skins,
    IReadOnlyList<MinecraftProfileTexture> Capes)
{
    /// <summary>
    /// 当前生效的皮肤：优先取标记为激活的那一件；
    /// 没有任何皮肤被标记为激活时退回第一件；两者都没有则为 <c>null</c>。
    /// </summary>
    public MinecraftProfileTexture? ActiveSkin =>
        Skins.FirstOrDefault(texture => texture.IsActive) ?? Skins.FirstOrDefault();
}

/// <summary>一件皮肤或披风纹理。</summary>
/// <param name="Id">纹理 Id。</param>
/// <param name="Url">纹理下载地址。</param>
/// <param name="Alias">显示别名（如 <c>steve</c> / <c>slim</c>）。</param>
/// <param name="Variant">变体标识（如 <c>classic</c> / <c>slim</c>）。</param>
/// <param name="IsActive">是否为当前启用的一件。</param>
internal sealed record MinecraftProfileTexture(
    string Id,
    string Url,
    string Alias,
    string Variant,
    bool IsActive);
