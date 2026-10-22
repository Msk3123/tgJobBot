using SystematiaBot.Core.Seo;

namespace SystematiaBot.Core.Abstractions;

public interface ISeoReportExporter
{
    /// <summary>Writes the per-page report and returns the file path.</summary>
    Task<string> WriteCsvAsync(SeoCrawlResult crawl, IReadOnlyList<SeoIssue> issues, string directory, CancellationToken ct);
}
