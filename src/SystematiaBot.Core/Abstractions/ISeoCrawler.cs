using SystematiaBot.Core.Seo;

namespace SystematiaBot.Core.Abstractions;

public interface ISeoCrawler
{
    Task<SeoCrawlResult> CrawlAsync(string startUrl, int maxPages, IProgress<SeoProgress>? progress, CancellationToken ct);
}
