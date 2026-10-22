using System.ServiceModel.Syndication;
using System.Xml;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Sources;

/// <summary>Standard RSS 2.0 / Atom feeds.</summary>
public sealed class RssSource(IHttpClientFactory httpFactory) : INewsSource
{
    public SourceKind Kind => SourceKind.Rss;

    public async Task<IReadOnlyList<FetchedNews>> FetchAsync(Source source, DateTimeOffset since, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(NewsHttp.ClientName);
        await using var stream = await http.GetStreamAsync(source.Url, ct);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore });
        return Parse(SyndicationFeed.Load(reader));
    }

    public static IReadOnlyList<FetchedNews> Parse(SyndicationFeed feed) =>
        feed.Items
            .Select(item =>
            {
                var link = item.Links.FirstOrDefault(l => l.RelationshipType is null or "alternate")?.Uri.ToString() ?? item.Id;
                var html = item.ElementExtensions
                               .Where(e => e.OuterName == "encoded")
                               .Select(e => e.GetObject<string>())
                               .FirstOrDefault()
                           ?? (item.Content as TextSyndicationContent)?.Text
                           ?? item.Summary?.Text
                           ?? "";
                var date = item.PublishDate != default ? item.PublishDate : item.LastUpdatedTime;
                return new FetchedNews(link, item.Title?.Text?.Trim(), HtmlText.FromHtml(html), date.ToUniversalTime());
            })
            .Where(n => !string.IsNullOrWhiteSpace(n.Url))
            .ToList();
}
