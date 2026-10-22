using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Seo;

namespace SystematiaBot.Infrastructure.Export;

/// <summary>Writes report.csv: ';'-separated, UTF-8 with BOM so Excel on Windows shows Cyrillic correctly.</summary>
public sealed class CsvExporter : ISeoReportExporter
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture) { Delimiter = ";" };

    public async Task<string> WriteCsvAsync(SeoCrawlResult crawl, IReadOnlyList<SeoIssue> issues, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var host = Uri.TryCreate(crawl.StartUrl, UriKind.Absolute, out var uri) ? uri.Host : "site";
        var path = Path.Combine(directory, $"seo-{host}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await WriteAsync(writer, crawl, issues, ct);
        return path;
    }

    public static async Task WriteAsync(TextWriter writer, SeoCrawlResult crawl, IReadOnlyList<SeoIssue> issues, CancellationToken ct)
    {
        var issuesByUrl = issues.GroupBy(i => i.Url).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.Code).Distinct()));

        await using var csv = new CsvWriter(writer, Config, leaveOpen: true);
        foreach (var header in new[] { "Url", "Status", "Title", "TitleLen", "Description", "DescLen", "H1Count", "H1", "Canonical", "Robots", "ImgNoAlt", "Words", "Issues" })
            csv.WriteField(header);
        await csv.NextRecordAsync();

        foreach (var page in crawl.Pages)
        {
            ct.ThrowIfCancellationRequested();
            csv.WriteField(page.Url);
            csv.WriteField(page.Error is null ? page.Status.ToString(CultureInfo.InvariantCulture) : $"ERR: {page.Error}");
            csv.WriteField(page.Title ?? "");
            csv.WriteField(page.Title?.Length ?? 0);
            csv.WriteField(page.Description ?? "");
            csv.WriteField(page.Description?.Length ?? 0);
            csv.WriteField(page.H1.Count);
            csv.WriteField(string.Join(" | ", page.H1));
            csv.WriteField(page.Canonical ?? "");
            csv.WriteField(page.Robots ?? "");
            csv.WriteField(page.Images > 0 ? $"{page.ImagesWithoutAlt}/{page.Images}" : "");
            csv.WriteField(page.IsHtml ? page.WordCount : 0);
            csv.WriteField(issuesByUrl.GetValueOrDefault(page.Url, ""));
            await csv.NextRecordAsync();
        }

        var siteIssues = issues.Where(i => crawl.Pages.All(p => p.Url != i.Url)).ToList();
        foreach (var issue in siteIssues)
        {
            csv.WriteField(issue.Url);
            for (var i = 0; i < 11; i++)
                csv.WriteField("");
            csv.WriteField($"{issue.Code}{(issue.Detail is null ? "" : $" ({issue.Detail})")}");
            await csv.NextRecordAsync();
        }

        await csv.FlushAsync();
    }
}
