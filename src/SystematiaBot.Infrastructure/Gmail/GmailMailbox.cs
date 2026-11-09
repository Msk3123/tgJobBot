using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;

namespace SystematiaBot.Infrastructure.Gmail;

/// <summary>
/// Gmail REST API over plain HttpClient: two endpoints (messages.list, messages.get) do not justify
/// the Google.Apis client library. The access token lives in memory and is refreshed shortly before it expires.
/// </summary>
public sealed partial class GmailMailbox(IHttpClientFactory httpFactory, IOptions<GmailOptions> options, ILogger<GmailMailbox> logger) : IMailbox
{
    public const string HttpClientName = "gmail";
    private const string Api = "https://gmail.googleapis.com/gmail/v1/users/me/";

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int max, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"messages?q={Uri.EscapeDataString(query)}&maxResults={max}", ct);
        if (!doc.RootElement.TryGetProperty("messages", out var messages))
            return [];

        return messages.EnumerateArray().Select(m => m.GetProperty("id").GetString()!).ToList();
    }

    public async Task<MailMessage> GetAsync(string id, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"messages/{Uri.EscapeDataString(id)}?format=full", ct);
        var root = doc.RootElement;
        var payload = root.GetProperty("payload");

        string Header(string name) => payload.TryGetProperty("headers", out var headers)
            ? headers.EnumerateArray().FirstOrDefault(h => string.Equals(h.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)) is { ValueKind: JsonValueKind.Object } h
                ? h.GetProperty("value").GetString() ?? ""
                : ""
            : "";

        var received = root.TryGetProperty("internalDate", out var internalDate) && long.TryParse(internalDate.GetString(), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.UtcNow;

        var text = FindPart(payload, "text/plain") ?? StripHtml(FindPart(payload, "text/html") ?? "");
        return new MailMessage(id, Header("From"), Header("Subject"), text, received);
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 1, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, Api + relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                continue;
            if (!response.IsSuccessStatusCode)
                throw new MailboxException($"Gmail API відповів {(int)response.StatusCode} {response.ReasonPhrase}");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _accessToken;

            var o = options.Value;
            if (!o.IsConfigured)
                throw new MailboxException("Gmail не налаштовано: потрібні Gmail:ClientId, Gmail:ClientSecret, Gmail:RefreshToken");

            try
            {
                var token = await GoogleOAuth.RefreshAsync(httpFactory.CreateClient(HttpClientName), o.ClientId!, o.ClientSecret!, o.RefreshToken!, ct);
                _accessToken = token.AccessToken;
                _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 120));
                logger.LogDebug("Gmail access token refreshed, valid for {Seconds}s", token.ExpiresIn);
                return _accessToken;
            }
            catch (GoogleOAuthException ex) when (ex.IsInvalidGrant)
            {
                throw new MailboxException("Google відкликав доступ до пошти (invalid_grant). Запусти gmail-auth ще раз і онови Gmail__RefreshToken.", ex);
            }
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>Depth-first search through multipart/* for the first part of the given type.</summary>
    private static string? FindPart(JsonElement part, string mimeType)
    {
        if (part.TryGetProperty("mimeType", out var mime) && mime.GetString() == mimeType
            && part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data))
            return DecodeBase64Url(data.GetString() ?? "");

        if (part.TryGetProperty("parts", out var parts))
        {
            foreach (var child in parts.EnumerateArray())
            {
                if (FindPart(child, mimeType) is { } found)
                    return found;
            }
        }

        return null;
    }

    public static string DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string StripHtml(string html) => WebUtility.HtmlDecode(TagRegex().Replace(html, " "));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
