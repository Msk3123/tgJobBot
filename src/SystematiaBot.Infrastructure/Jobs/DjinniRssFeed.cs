using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Infrastructure.Jobs;

/// <summary>
/// Djinni's public RSS (djinni.co/jobs/rss/?primary_keyword=.NET&amp;exp_level=1y…) takes the same filters as the
/// job search and needs no login or mailbox. Items carry no company, so the job page is still fetched by the intake;
/// the source is "djinni", which makes an RSS item and the same vacancy from an e-mail alert one record.
/// </summary>
public sealed class DjinniRssFeed(IHttpClientFactory httpFactory, IOptions<JobOptions> options, ILogger<DjinniRssFeed> logger) : IVacancyFeed
{
    public string Source => DjinniMailParser.Source;

    public async Task<IReadOnlyList<VacancyCandidate>> FetchAsync(CancellationToken ct)
    {
        var result = new List<VacancyCandidate>();
        foreach (var url in options.Value.DjinniFeeds)
        {
            var xml = await httpFactory.CreateClient(DjinniPageFetcher.HttpClientName).GetStringAsync(url, ct);
            var items = DjinniRssParser.Parse(xml);
            logger.LogDebug("Djinni feed {Url}: {Count} items", url, items.Count);
            result.AddRange(items);
        }

        return result.DistinctBy(c => c.ExternalId).ToList();
    }
}

public static class DjinniRssParser
{
    public static List<VacancyCandidate> Parse(string xml)
    {
        var result = new List<VacancyCandidate>();
        foreach (var item in XDocument.Parse(xml).Descendants("item"))
        {
            if (!DjinniMailParser.TryParseJobUrl(item.Element("link")?.Value, out var id, out var url))
                continue;

            var title = item.Element("title")?.Value.Trim();
            if (string.IsNullOrEmpty(title))
                continue;

            var description = item.Element("description")?.Value is { Length: > 0 } html ? HtmlText.FromHtml(html) : null;
            DateTimeOffset? posted = DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;

            result.Add(new VacancyCandidate(DjinniMailParser.Source, id, url, title, Description: description, PostedAt: posted));
        }

        return result;
    }
}
