using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MyoTrack.Api.Services;

/// <summary>
/// Credenciais do OAuth client (tipo "Web application") criado no Google Cloud
/// Console. O redirect autorizado deve ser <c>App:PublicBaseUrl</c> +
/// <c>/api/auth/google/callback</c>. Sem as duas credenciais o recurso fica
/// desligado e o botão "Continuar com Google" não aparece na tela de login.
/// </summary>
public class GoogleOAuthOptions
{
    public const string SectionName = "Auth:Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class GoogleUserInfo
{
    [JsonPropertyName("sub")] public string Sub { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("email_verified")] public bool EmailVerified { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("given_name")] public string? GivenName { get; set; }
}

public class GoogleOAuthException(string message) : Exception(message);

/// <summary>
/// Login social com Google via OAuth2 (authorization-code flow). Em três passos:
/// (1) redireciona ao Google com um <c>state</c> anti-CSRF; (2) o Google volta
/// com um <c>code</c>, trocado por um access token servidor-a-servidor (com o
/// client secret, que nunca vai ao browser); (3) com o token buscamos o perfil
/// (nome + e-mail verificado).
/// </summary>
public class GoogleOAuthService(
    IOptions<GoogleOAuthOptions> options,
    IHttpClientFactory httpClientFactory)
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string Scope = "openid email profile";

    private readonly GoogleOAuthOptions _options = options.Value;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret);

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state,
            ["access_type"] = "online",
            ["prompt"] = "select_account",
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(AuthEndpoint, query);
    }

    public async Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
            }), ct);

        if (!response.IsSuccessStatusCode)
            throw new GoogleOAuthException($"Token endpoint retornou {(int)response.StatusCode}.");

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (string.IsNullOrEmpty(payload?.AccessToken))
            throw new GoogleOAuthException("Token endpoint não devolveu access_token.");

        return payload.AccessToken;
    }

    public async Task<GoogleUserInfo> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new GoogleOAuthException($"Userinfo endpoint retornou {(int)response.StatusCode}.");

        return await response.Content.ReadFromJsonAsync<GoogleUserInfo>(ct)
            ?? throw new GoogleOAuthException("Userinfo endpoint devolveu resposta vazia.");
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }
}
