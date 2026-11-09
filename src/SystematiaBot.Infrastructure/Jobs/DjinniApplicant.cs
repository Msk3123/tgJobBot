using System.Net;
using System.Net.Http.Headers;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Infrastructure.Jobs;

public sealed class DjinniOptions
{
    public const string Section = "Djinni";

    /// <summary>
    /// The owner's "sessionid" cookie from a browser where they are logged in to Djinni. It is the key to the account:
    /// only in .env / user-secrets, never in logs. Logging out in that browser invalidates it.
    /// </summary>
    public string? SessionId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SessionId);
}

/// <summary>
/// Djinni's "Відгукнутися" is a plain Django form: a multipart POST to the vacancy URL with the message,
/// the CSRF token from the page and the session cookie. This client does exactly what the browser does,
/// one vacancy per explicit tap, and reports anything unexpected instead of guessing.
/// </summary>
public sealed class DjinniApplicant(IHttpClientFactory httpFactory, IOptions<DjinniOptions> options, ILogger<DjinniApplicant> logger) : IJobApplicant
{
    public const string HttpClientName = "djinni-session";
    private static readonly Uri Origin = new("https://djinni.co");

    public string Source => DjinniMailParser.Source;

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<bool?> CheckSessionAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            using var request = Request(HttpMethod.Get, new Uri(Origin, "/my/profile/"), csrfCookie: null);
            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Djinni session check failed: {Error}", ex.Message);
            return null;
        }
    }

    public async Task<ApplyResult> ApplyAsync(Vacancy vacancy, string message, ApplyAttachment? cv, CancellationToken ct)
    {
        if (!IsConfigured)
            return new ApplyResult(ApplyOutcome.SessionExpired, Detail: "сесію Djinni не підключено");
        if (!DjinniMailParser.TryParseJobUrl(vacancy.Url, out _, out var canonical))
            return new ApplyResult(ApplyOutcome.Failed, Detail: "це не посилання на вакансію Djinni");

        var pageUrl = new Uri(canonical);
        var http = httpFactory.CreateClient(HttpClientName);
        try
        {
            using var get = Request(HttpMethod.Get, pageUrl, csrfCookie: null);
            using var pageResponse = await http.SendAsync(get, ct);
            if (IsLoginRedirect(pageResponse))
                return new ApplyResult(ApplyOutcome.SessionExpired);
            if (pageResponse.StatusCode == HttpStatusCode.NotFound)
                return new ApplyResult(ApplyOutcome.NotAvailable, Detail: "вакансію видалено");
            pageResponse.EnsureSuccessStatusCode();

            var page = DjinniApplyPage.Parse(await pageResponse.Content.ReadAsStringAsync(ct));
            if (page.Problem is { } problem)
                return problem;

            // Django pairs the form token with a csrftoken cookie set on this response (absent if tokens live in the session).
            var csrfCookie = CsrfCookie(pageResponse);
            using var form = page.BuildForm(message, cv);
            using var post = Request(HttpMethod.Post, page.Action is { } action ? new Uri(pageUrl, action) : pageUrl, csrfCookie);
            post.Content = form;
            using var postResponse = await http.SendAsync(post, ct);
            var result = await InterpretAsync(postResponse, ct);
            logger.LogInformation("Djinni apply #{Id}: {Status} → {Outcome}", vacancy.Id, (int)postResponse.StatusCode, result.Outcome);

            // The POST answer alone says little about where the conversation lives; the job page links to it after applying.
            if (result.Outcome == ApplyOutcome.Applied && result.InboxUrl is null)
                result = result with { InboxUrl = await FindInboxLinkAsync(http, pageUrl, ct) };
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Djinni apply #{Id} failed: {Error}", vacancy.Id, ex.Message);
            return new ApplyResult(ApplyOutcome.Failed, Detail: ex.Message);
        }
    }

    private async Task<ApplyResult> InterpretAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (IsLoginRedirect(response))
            return new ApplyResult(ApplyOutcome.SessionExpired);

        if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.MovedPermanently)
        {
            var location = response.Headers.Location is { } l ? new Uri(Origin, l).ToString() : null;
            return new ApplyResult(ApplyOutcome.Applied, location?.Contains("/my/inbox/", StringComparison.Ordinal) == true ? location : null);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new ApplyResult(ApplyOutcome.Failed, Detail: "Djinni відхилив запит (403, CSRF)");
        if (!response.IsSuccessStatusCode)
            return new ApplyResult(ApplyOutcome.Failed, Detail: $"HTTP {(int)response.StatusCode}");

        // 200: the page was rendered again, either with form errors or already in the "applied" state.
        var page = DjinniApplyPage.Parse(await response.Content.ReadAsStringAsync(ct));
        if (page.Errors.Count > 0)
            return new ApplyResult(ApplyOutcome.Rejected, Detail: string.Join("; ", page.Errors));
        if (page.Problem is { Outcome: ApplyOutcome.AlreadyApplied } applied)
            return applied with { Outcome = ApplyOutcome.Applied };
        return new ApplyResult(ApplyOutcome.Failed, Detail: "Djinni відповів сторінкою без підтвердження — перевір у чатах Djinni");
    }

    private async Task<string?> FindInboxLinkAsync(HttpClient http, Uri pageUrl, CancellationToken ct)
    {
        try
        {
            using var request = Request(HttpMethod.Get, pageUrl, csrfCookie: null);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? DjinniApplyPage.Parse(await response.Content.ReadAsStringAsync(ct)).InboxUrl : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, Uri url, string? csrfCookie)
    {
        var request = new HttpRequestMessage(method, url);
        var cookie = $"sessionid={options.Value.SessionId!.Trim()}";
        if (csrfCookie is not null)
            cookie += $"; csrftoken={csrfCookie}";
        request.Headers.Add("Cookie", cookie);
        if (method == HttpMethod.Post)
        {
            // Django checks the Referer on HTTPS POSTs; a browser sends both.
            request.Headers.Referrer = url;
            request.Headers.Add("Origin", Origin.GetLeftPart(UriPartial.Authority));
        }

        return request;
    }

    private static bool IsLoginRedirect(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 300 and < 400
        && response.Headers.Location?.ToString().Contains("login", StringComparison.OrdinalIgnoreCase) == true;

    private static string? CsrfCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("csrftoken=", StringComparison.Ordinal))?["csrftoken=".Length..]
            : null;
}

/// <summary>What the client needs from a vacancy page: login state, the apply form and anything that blocks applying.</summary>
public sealed class DjinniApplyPage
{
    private readonly List<KeyValuePair<string, string>> _fields = [];

    public bool LoggedIn { get; private init; }
    public bool HasForm { get; private init; }
    public string? Action { get; private init; }
    public string? FileField { get; private init; }
    public string? InboxUrl { get; private init; }
    public IReadOnlyList<string> Errors { get; private init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> Fields => _fields;

    /// <summary>Why applying is impossible from this page, or null when the form is ready.</summary>
    public ApplyResult? Problem => this switch
    {
        { LoggedIn: false } => new ApplyResult(ApplyOutcome.SessionExpired),
        { HasForm: false, InboxUrl: { } inbox } => new ApplyResult(ApplyOutcome.AlreadyApplied, inbox),
        { HasForm: false } => new ApplyResult(ApplyOutcome.NotAvailable, Detail: "на сторінці немає форми відгуку"),
        _ => null
    };

    public static DjinniApplyPage Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var form = document.QuerySelector("form#apply_form");
        var inbox = document.QuerySelectorAll("a[href*='/my/inbox/']")
            .Select(a => a.GetAttribute("href"))
            .FirstOrDefault(h => h is not null && System.Text.RegularExpressions.Regex.IsMatch(h, @"/my/inbox/\d+"));

        var page = new DjinniApplyPage
        {
            LoggedIn = document.QuerySelector("a[href='/logout'], a[href^='/logout?']") is not null,
            HasForm = form is not null,
            Action = NullIfEmpty(form?.GetAttribute("action")),
            FileField = form?.QuerySelector("input[type=file]")?.GetAttribute("name"),
            InboxUrl = inbox is null ? null : new Uri(new Uri("https://djinni.co"), inbox).ToString(),
            // Only inside the form: Djinni pages carry hidden alert templates elsewhere.
            Errors = form?.QuerySelectorAll(".errorlist li, .invalid-feedback, .alert-danger")
                .Select(e => e.TextContent.Trim())
                .Where(t => t.Length > 0 && t != "Помилка")
                .Distinct()
                .ToList() ?? []
        };

        if (form is not null)
            page.CollectDefaults(form);
        return page;
    }

    /// <summary>What a browser would send if the owner only typed the message and pressed the submit button.</summary>
    public MultipartFormDataContent BuildForm(string message, ApplyAttachment? cv)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in _fields)
            content.Add(new StringContent(name == "message" ? message : value), name);

        if (cv is not null && FileField is not null)
        {
            var file = new ByteArrayContent(cv.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(file, FileField, cv.FileName);
        }

        return content;
    }

    private void CollectDefaults(IElement form)
    {
        foreach (var element in form.QuerySelectorAll("input[name], textarea[name], select[name], button[type=submit][name]"))
        {
            var name = element.GetAttribute("name")!;
            switch (element.LocalName, element.GetAttribute("type")?.ToLowerInvariant())
            {
                case ("input", "file"):
                    continue;
                case ("input", "checkbox" or "radio") when !element.HasAttribute("checked"):
                    continue;
                case ("textarea", _):
                    _fields.Add(new(name, element.TextContent));
                    break;
                case ("select", _):
                    var option = element.QuerySelector("option[selected]") ?? element.QuerySelector("option");
                    _fields.Add(new(name, option?.GetAttribute("value") ?? option?.TextContent ?? ""));
                    break;
                case ("input", "checkbox" or "radio"):
                    _fields.Add(new(name, element.GetAttribute("value") ?? "on"));
                    break;
                default:
                    _fields.Add(new(name, element.GetAttribute("value") ?? ""));
                    break;
            }
        }

        if (_fields.All(f => f.Key != "message"))
            _fields.Add(new("message", ""));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
