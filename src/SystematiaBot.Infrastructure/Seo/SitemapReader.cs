using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace SystematiaBot.Infrastructure.Seo;

/// <summary>Reads page URLs from sitemap.xml, following sitemap indexes and gzip-compressed sitemaps.</summary>
public sealed class SitemapReader(ILogger<SitemapReader> logger)
{
    private const int MaxSitemaps = 200;
    private const int MaxUrls = 200_000;

    private static readonly string[] SkippedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".avif", ".pdf"];

    public async Task<List<string>> ReadAsync(HttpClient http, IEnumerable<string> sitemapUrls, CancellationToken ct)
    {
        var queue = new Queue<string>(sitemapUrls.Distinct());
        var visited = new HashSet<string>();
        var pages = new List<string>();
        var seenPages = new HashSet<string>();

        while (queue.Count > 0 && visited.Count < MaxSitemaps && pages.Count < MaxUrls)
        {
            var url = queue.Dequeue();
            if (!visited.Add(url))
                continue;

            XDocument doc;
            try
            {
                doc = await LoadAsync(http, url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or XmlException or InvalidDataException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Sitemap {Url} skipped: {Error}", url, ex.Message);
                continue;
            }

            var (isIndex, locs) = Parse(doc);
            foreach (var loc in locs)
            {
                if (isIndex)
                    queue.Enqueue(loc);
                else if (!IsSkipped(loc) && seenPages.Add(loc))
                    pages.Add(loc);
            }
        }

        return pages;
    }

    /// <summary>Returns whether the document is a sitemap index and the &lt;loc&gt; values of its entries.</summary>
    public static (bool IsIndex, List<string> Locs) Parse(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return (false, []);

        var isIndex = root.Name.LocalName.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase);
        var entryName = isIndex ? "sitemap" : "url";
        var locs = root.Elements()
            .Where(e => e.Name.LocalName == entryName)
            .Select(e => e.Elements().FirstOrDefault(c => c.Name.LocalName == "loc")?.Value.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(l => l!)
            .ToList();

        return (isIndex, locs);
    }

    private static async Task<XDocument> LoadAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        Stream stream = new MemoryStream(bytes);
        if (bytes is [0x1F, 0x8B, ..])
            stream = new GZipStream(stream, CompressionMode.Decompress);

        await using (stream)
            return await XDocument.LoadAsync(stream, LoadOptions.None, ct);
    }

    private static bool IsSkipped(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return SkippedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}
