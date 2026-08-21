using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public MinecraftProfileService(
        HttpClient? httpClient = null,
        IMicrosoftAuthenticator? authenticator = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _authenticator = authenticator ?? new MicrosoftDeviceCodeAuthenticator();
    }

    public event Action<string>? AppearanceChanged;

    public async Task<MinecraftProfile> GetProfileAsync(
        LaunchAccount account,
        CancellationToken cancellationToken = default)
    {
        var microsoft = await EnsureFreshAccountAsync(account, cancellationToken)
            .ConfigureAwait(false);
        using var request = CreateAuthorizedRequest(HttpMethod.Get, ProfileEndpoint, microsoft);
        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return await ReadProfileAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadSkinAsync(
        LaunchAccount account,
        string filePath,
        MinecraftSkinModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var microsoft = await EnsureFreshAccountAsync(account, cancellationToken)
            .ConfigureAwait(false);
        await using var input = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(model == MinecraftSkinModel.Slim ? "slim" : "classic"),
            "variant");
        using var fileContent = new StreamContent(input);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var request = CreateAuthorizedRequest(HttpMethod.Post, SkinEndpoint, microsoft);
        request.Content = content;
        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        RaiseAppearanceChanged(account);
    }

    public async Task SetActiveCapeAsync(
        LaunchAccount account,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        var microsoft = await EnsureFreshAccountAsync(account, cancellationToken)
            .ConfigureAwait(false);
        using var request = CreateAuthorizedRequest(
            string.IsNullOrWhiteSpace(capeId) ? HttpMethod.Delete : HttpMethod.Put,
            ActiveCapeEndpoint,
            microsoft);
        if (!string.IsNullOrWhiteSpace(capeId))
            request.Content = JsonContent.Create(new { capeId });

        using var response = await _httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        RaiseAppearanceChanged(account);
    }

    private async Task<MicrosoftAccount> EnsureFreshAccountAsync(
        LaunchAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Type, "microsoft", StringComparison.OrdinalIgnoreCase) ||
            account.Microsoft is null)
        {
            throw new InvalidOperationException("当前账号不是可编辑皮肤的正版账号。");
        }

        if (!account.Microsoft.IsExpired)
            return account.Microsoft;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!account.Microsoft.IsExpired)
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
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            var error = JsonSerializer.Deserialize<ErrorDto>(json, JsonOptions);
            detail = error?.ErrorMessage ?? error?.Path ?? string.Empty;
        }
        catch (JsonException)
        {
            // The status code remains useful if the service returned non-JSON.
        }

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"Minecraft 档案服务返回 {(int)response.StatusCode}。"
                : $"Minecraft 档案服务返回 {(int)response.StatusCode}：{detail}",
            null,
            response.StatusCode);
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

internal sealed record MinecraftProfile(
    string Id,
    string Name,
    IReadOnlyList<MinecraftProfileTexture> Skins,
    IReadOnlyList<MinecraftProfileTexture> Capes)
{
    public MinecraftProfileTexture? ActiveSkin =>
        Skins.FirstOrDefault(texture => texture.IsActive) ?? Skins.FirstOrDefault();
}

internal sealed record MinecraftProfileTexture(
    string Id,
    string Url,
    string Alias,
    string Variant,
    bool IsActive);
