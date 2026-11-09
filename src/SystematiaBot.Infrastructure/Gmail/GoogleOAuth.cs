using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SystematiaBot.Infrastructure.Gmail;

public sealed class GmailOptions
{
    public const string Section = "Gmail";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Long-lived token from `SystematiaBot.Cli gmail-auth`. Treat it like a password: it reads the whole mailbox.</summary>
    public string? RefreshToken { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(RefreshToken);
}

public sealed record GoogleToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("scope")] string? Scope);

/// <summary>
/// OAuth 2.0 for an installed app: authorization code + PKCE on a loopback redirect, then refresh tokens.
/// PKCE ties the code to this process, so a code leaked from the browser history is useless to anyone else;
/// <c>state</c> stops a malicious page from completing someone else's login into our listener.
/// </summary>
public static class GoogleOAuth
{
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>Read-only mail and nothing else: the bot cannot send, delete or modify mail. Calendar comes back only with a feature that uses it.</summary>
    public static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/gmail.readonly"
    ];

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string BuildConsentUrl(string clientId, string redirectUri, string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            // offline + consent: Google returns a refresh token only on a fresh consent.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };
        return AuthEndpoint + "?" + string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public static Task<GoogleToken> ExchangeCodeAsync(HttpClient http, string clientId, string clientSecret, string code, string verifier, string redirectUri, CancellationToken ct) =>
        PostAsync(http, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        }, ct);

    public static Task<GoogleToken> RefreshAsync(HttpClient http, string clientId, string clientSecret, string refreshToken, CancellationToken ct) =>
        PostAsync(http, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, ct);

    private static async Task<GoogleToken> PostAsync(HttpClient http, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
        {
            // The body is an OAuth error object ({"error":"invalid_grant",...}); it never contains tokens.
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new GoogleOAuthException((int)response.StatusCode, body.Contains("invalid_grant", StringComparison.Ordinal), body.Length > 300 ? body[..300] : body);
        }

        return await response.Content.ReadFromJsonAsync<GoogleToken>(ct) ?? throw new GoogleOAuthException((int)response.StatusCode, false, "empty token response");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class GoogleOAuthException(int status, bool isInvalidGrant, string details)
    : Exception($"Google OAuth returned HTTP {status}: {details}")
{
    /// <summary>The refresh token was revoked, expired or belongs to another client.</summary>
    public bool IsInvalidGrant { get; } = isInvalidGrant;
}
