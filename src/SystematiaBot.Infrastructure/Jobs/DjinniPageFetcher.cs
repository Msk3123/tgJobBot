using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;

namespace SystematiaBot.Infrastructure.Jobs;

/// <summary>
/// Loads a public djinni.co/jobs/… page (allowed by Djinni's robots.txt). One request per new vacancy,
/// with an honest User-Agent: a handful of requests a day, not a crawler.
/// </summary>
public sealed class DjinniPageFetcher(IHttpClientFactory httpFactory, ILogger<DjinniPageFetcher> logger) : IVacancyPageFetcher
{
    public const string HttpClientName = "djinni";
    public const string UserAgent = "Mozilla/5.0 (compatible; JobHunterBot/1.0; personal job search assistant)";

    public async Task<VacancyPage?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            var html = await httpFactory.CreateClient(HttpClientName).GetStringAsync(url, ct);
            return DjinniPageParser.Parse(html);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Could not load vacancy page {Url}: {Error}", url, ex.Message);
            return null;
        }
    }
}

public static class DjinniPageParser
{
    private static readonly string[] InactiveMarkers = ["Ця вакансія зараз неактивна", "Эта вакансия сейчас неактивна", "This job is no longer active", "This job is not active"];

    public static VacancyPage Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);

        // og:title is "Junior .NET Developer в TemaBit Fozzy Group" — the cleanest source for both fields.
        var ogTitle = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")?.Trim();
        string? title = null, company = null;
        if (!string.IsNullOrEmpty(ogTitle))
        {
            var split = ogTitle.LastIndexOf(" в ", StringComparison.Ordinal);
            if (split < 0)
                split = ogTitle.LastIndexOf(" at ", StringComparison.Ordinal);
            (title, company) = split > 0
                ? (ogTitle[..split].Trim(), ogTitle[(split + 3)..].Trim())
                : (ogTitle, null);
        }

        title ??= document.QuerySelector("h1")?.ChildNodes.FirstOrDefault(n => n.NodeType == NodeType.Text && n.TextContent.Trim().Length > 0)?.TextContent.Trim();

        var descriptionElement = document.QuerySelector(".job-post__description");
        var description = descriptionElement is not null
            ? HtmlText.FromElement(descriptionElement)
            : document.QuerySelector("meta[property='og:description']")?.GetAttribute("content")?.Trim();

        var bodyText = document.Body?.TextContent ?? "";
        var inactive = InactiveMarkers.Any(m => bodyText.Contains(m, StringComparison.OrdinalIgnoreCase));
        var (posted, months) = ReadJobPosting(document);

        return new VacancyPage(NullIfEmpty(title), NullIfEmpty(company), NullIfEmpty(description), inactive, posted, months);
    }

    /// <summary>
    /// Active vacancies carry schema.org JobPosting JSON-LD (for Google Jobs): datePosted, given without an offset
    /// and in UTC, and experienceRequirements.monthsOfExperience. Inactive ones drop it.
    /// </summary>
    private static (DateTimeOffset? Posted, int? Months) ReadJobPosting(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var postings = json.RootElement.ValueKind == JsonValueKind.Array
                    ? json.RootElement.EnumerateArray().ToList()
                    : [json.RootElement];
                foreach (var posting in postings)
                {
                    if (!posting.TryGetProperty("@type", out var type) || type.GetString() != "JobPosting")
                        continue;

                    DateTimeOffset? posted = posting.TryGetProperty("datePosted", out var date)
                        && DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                            ? parsed
                            : null;
                    int? months = posting.TryGetProperty("experienceRequirements", out var exp)
                        && exp.ValueKind == JsonValueKind.Object
                        && exp.TryGetProperty("monthsOfExperience", out var m)
                        && m.TryGetDouble(out var value)
                            ? (int)Math.Round(value)
                            : null;
                    return (posted, months);
                }
            }
            catch (JsonException)
            {
                // Malformed JSON-LD is not worth failing the whole vacancy for.
            }
        }

        return (null, null);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
