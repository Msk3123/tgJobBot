using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Infrastructure.Jobs;

/// <summary>
/// DOU publishes category feeds (jobs.dou.ua/vacancies/feeds/?category=.NET&amp;exp=0-1) with the full description,
/// so no page is fetched. One request per feed every Jobs:FeedPollMinutes.
/// </summary>
public sealed class DouRssFeed(IHttpClientFactory httpFactory, IOptions<JobOptions> options, ILogger<DouRssFeed> logger) : IVacancyFeed
{
    public const string HttpClientName = "dou";

    public string Source => DouRssParser.Source;

    public async Task<IReadOnlyList<VacancyCandidate>> FetchAsync(CancellationToken ct)
    {
        var result = new List<VacancyCandidate>();
        foreach (var url in options.Value.DouFeeds)
        {
            var xml = await httpFactory.CreateClient(HttpClientName).GetStringAsync(url, ct);
            var items = DouRssParser.Parse(xml);
            logger.LogDebug("DOU feed {Url}: {Count} items", url, items.Count);
            result.AddRange(items);
        }

        return result.DistinctBy(c => c.ExternalId).ToList();
    }
}

public static partial class DouRssParser
{
    public const string Source = "dou";

    public static List<VacancyCandidate> Parse(string xml)
    {
        var result = new List<VacancyCandidate>();
        foreach (var item in XDocument.Parse(xml).Descendants("item"))
        {
            var link = item.Element("link")?.Value.Trim() ?? "";
            if (VacancyIdRegex().Match(link) is not { Success: true } id)
                continue;

            var (title, company, location) = SplitTitle(item.Element("title")?.Value.Trim() ?? "");
            var description = item.Element("description")?.Value is { } html ? HtmlText.FromHtml(html) : null;
            if (description is not null)
                description = ReplyLinkRegex().Replace(description, "").Trim();

            DateTimeOffset? posted = DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;

            result.Add(new VacancyCandidate(
                Source,
                id.Groups["id"].Value,
                link[..(link.IndexOf('?') is var q and >= 0 ? q : link.Length)],
                title,
                company,
                Details: location,
                Description: description,
                PostedAt: posted));
        }

        return result;
    }

    /// <summary>"Junior .NET Developer в Bliscore, Львів, віддалено" → (title, "Bliscore", "Львів, віддалено").</summary>
    public static (string Title, string? Company, string? Location) SplitTitle(string value)
    {
        var at = value.LastIndexOf(" в ", StringComparison.Ordinal);
        if (at <= 0)
            return (value, null, null);

        var title = value[..at].Trim();
        var rest = value[(at + 3)..].Trim();
        var comma = rest.IndexOf(", ", StringComparison.Ordinal);
        return comma < 0 ? (title, rest, null) : (title, rest[..comma].Trim(), rest[(comma + 2)..].Trim());
    }

    [GeneratedRegex(@"/vacancies/(?<id>\d+)/")]
    private static partial Regex VacancyIdRegex();

    [GeneratedRegex(@"Відгукнутись на вакансію\s*$")]
    private static partial Regex ReplyLinkRegex();
}
