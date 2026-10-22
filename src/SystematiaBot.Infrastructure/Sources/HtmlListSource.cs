using System.Globalization;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Sources;

/// <summary>
/// Sites without RSS: a list page parsed with CSS selectors from sources.json
/// (item, title, link, date, dateFormat), then each article page is loaded for the full text (content).
/// </summary>
public sealed class HtmlListSource(IHttpClientFactory httpFactory, ILogger<HtmlListSource> logger) : INewsSource
{
    private const int MaxArticles = 20;

    public SourceKind Kind => SourceKind.HtmlList;

    public sealed record ListEntry(string Url, string Title, DateTimeOffset PublishedAt);

    public async Task<IReadOnlyList<FetchedNews>> FetchAsync(Source source, DateTimeOffset since, CancellationToken ct)
    {
        var selectors = ParseSelectors(source.Selectors);
        var http = httpFactory.CreateClient(NewsHttp.ClientName);
        var listHtml = await http.GetStringAsync(source.Url, ct);
        var entries = ParseList(listHtml, new Uri(source.Url), selectors)
            .Where(e => e.PublishedAt >= since)
            .Take(MaxArticles)
            .ToList();

        var result = new List<FetchedNews>();
        foreach (var entry in entries)
        {
            var text = entry.Title;
            if (selectors.TryGetValue("content", out var contentSelector))
            {
                try
                {
                    var articleHtml = await http.GetStringAsync(entry.Url, ct);
                    var content = ParseContent(articleHtml, contentSelector);
                    if (!string.IsNullOrWhiteSpace(content))
                        text = content;
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning("Article {Url} not loaded: {Error}", entry.Url, ex.Message);
                }
            }

            result.Add(new FetchedNews(entry.Url, entry.Title, text, entry.PublishedAt));
        }

        return result;
    }

    public static IReadOnlyList<ListEntry> ParseList(string html, Uri baseUri, IReadOnlyDictionary<string, string> selectors)
    {
        using var doc = new HtmlParser().ParseDocument(html);
        var format = selectors.GetValueOrDefault("dateFormat", "dd.MM.yyyy HH:mm");
        var result = new List<ListEntry>();

        foreach (var item in doc.QuerySelectorAll(selectors["item"]))
        {
            var linkElement = selectors.TryGetValue("link", out var linkSel) ? item.QuerySelector(linkSel) : item.QuerySelector("a[href]");
            var href = linkElement?.GetAttribute("href");
            var title = (selectors.TryGetValue("title", out var titleSel) ? item.QuerySelector(titleSel)?.TextContent : linkElement?.TextContent)?.Trim();
            var dateText = selectors.TryGetValue("date", out var dateSel) ? item.QuerySelector(dateSel)?.TextContent.Trim() : null;

            if (href is null || string.IsNullOrWhiteSpace(title) || dateText is null || !Uri.TryCreate(baseUri, href, out var url))
                continue;
            if (!TryParseKyivDate(dateText, format, out var date))
                continue;

            result.Add(new ListEntry(url.ToString(), string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), date));
        }

        return result;
    }

    public static string ParseContent(string html, string selector)
    {
        using var doc = new HtmlParser().ParseDocument(html);
        var parts = doc.QuerySelectorAll(selector).Select(HtmlText.ToText).Where(t => t.Length > 0);
        return string.Join("\n\n", parts);
    }

    /// <summary>Site dates are local (Europe/Kyiv) wall-clock time.</summary>
    public static bool TryParseKyivDate(string text, string format, out DateTimeOffset date)
    {
        date = default;
        if (!DateTime.TryParseExact(text.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            return false;

        var zone = KyivTime.Zone;
        date = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        return true;
    }

    private static Dictionary<string, string> ParseSelectors(string? json)
    {
        var selectors = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (selectors is null || !selectors.ContainsKey("item"))
            throw new InvalidOperationException("HtmlList source needs selectors with at least \"item\"");
        return selectors;
    }
}
