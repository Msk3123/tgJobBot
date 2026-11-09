using System.Text;
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
            ? ToText(descriptionElement)
            : document.QuerySelector("meta[property='og:description']")?.GetAttribute("content")?.Trim();

        var bodyText = document.Body?.TextContent ?? "";
        var inactive = InactiveMarkers.Any(m => bodyText.Contains(m, StringComparison.OrdinalIgnoreCase));

        return new VacancyPage(NullIfEmpty(title), NullIfEmpty(company), NullIfEmpty(description), inactive);
    }

    /// <summary>TextContent glues paragraphs together; this keeps one line per paragraph, list item and &lt;br&gt;.</summary>
    private static string ToText(IElement root)
    {
        var sb = new StringBuilder();
        Walk(root, sb);
        var lines = sb.ToString().Split('\n').Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        return string.Join('\n', lines.Where(l => l.Length > 0));
    }

    private static void Walk(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    sb.Append(text.Data);
                    break;
                case IElement { TagName: "BR" }:
                    sb.Append('\n');
                    break;
                case IElement element:
                    var block = element.TagName is "P" or "DIV" or "LI" or "UL" or "OL" or "H1" or "H2" or "H3" or "H4" or "TR";
                    if (block)
                        sb.Append('\n');
                    if (element.TagName == "LI")
                        sb.Append("• ");
                    Walk(element, sb);
                    if (block)
                        sb.Append('\n');
                    break;
            }
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
