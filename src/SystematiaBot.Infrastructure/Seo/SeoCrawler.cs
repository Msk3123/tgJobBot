using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Seo;

namespace SystematiaBot.Infrastructure.Seo;

public sealed class SeoCrawler(
    IHttpClientFactory httpFactory,
    SitemapReader sitemapReader,
    SeoIssueDetector detector,
    IOptions<SeoOptions> options,
    ILogger<SeoCrawler> logger) : ISeoCrawler
{
    public const string HttpClientName = "seo";
    private const int MaxHtmlBytes = 5 * 1024 * 1024;

    private readonly SeoOptions _options = options.Value;

    public async Task<SeoCrawlResult> CrawlAsync(string startUrl, int maxPages, IProgress<SeoProgress>? progress, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        var origin = await ResolveOriginAsync(http, startUrl, ct);
        logger.LogInformation("SEO audit of {Origin}, limit {Limit}", origin, maxPages);

        progress?.Report(new SeoProgress("robots.txt", 0, 0, maxPages, 0));
        var (robots, robotsExists) = await LoadRobotsAsync(http, origin, ct);
        var site = await CheckSiteAsync(http, origin, robots, robotsExists, ct);

        progress?.Report(new SeoProgress("sitemap", 0, 0, maxPages, 0));
        var sitemapSources = robots.Sitemaps.Count > 0 ? robots.Sitemaps : [new Uri(origin, "/sitemap.xml").ToString(), new Uri(origin, "/sitemap_index.xml").ToString()];
        var sitemapUrls = (await sitemapReader.ReadAsync(http, sitemapSources, ct))
            .Where(u => IsSameSite(u, origin) && robots.IsAllowed(PathAndQuery(u)))
            .ToList();
        logger.LogInformation("Sitemap: {Count} page URLs", sitemapUrls.Count);

        var pages = new ConcurrentBag<SeoPage>();
        var issuesFound = 0;
        var cancelled = false;

        try
        {
            if (sitemapUrls.Count > 0)
            {
                var sample = UrlSampler.Stratified(sitemapUrls, maxPages);
                await Parallel.ForEachAsync(sample, new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency, CancellationToken = ct }, async (url, token) =>
                {
                    var page = await FetchPageAsync(http, url, fromSitemap: true, token);
                    pages.Add(page);
                    Interlocked.Add(ref issuesFound, detector.DetectPage(page).Count);
                    progress?.Report(new SeoProgress("pages", sitemapUrls.Count, pages.Count, sample.Count, issuesFound));
                    await Task.Delay(_options.DelayMs, token);
                });
            }
            else
            {
                await CrawlLinksAsync(http, origin, robots, maxPages, pages, p =>
                {
                    issuesFound += detector.DetectPage(p).Count;
                    progress?.Report(new SeoProgress("links", 0, pages.Count, maxPages, issuesFound));
                }, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            cancelled = true;
            logger.LogInformation("SEO audit cancelled after {Count} pages", pages.Count);
        }

        return new SeoCrawlResult
        {
            StartUrl = origin.ToString(),
            Pages = pages.OrderBy(p => p.Url, StringComparer.Ordinal).ToList(),
            Site = site,
            SitemapUrlCount = sitemapUrls.Count,
            Cancelled = cancelled
        };
    }

    /// <summary>Fallback when there is no sitemap: breadth-first crawl of internal links from the home page.</summary>
    private async Task CrawlLinksAsync(HttpClient http, Uri origin, RobotsTxt robots, int maxPages, ConcurrentBag<SeoPage> pages, Action<SeoPage> onPage, CancellationToken ct)
    {
        var queue = new Queue<string>([origin.ToString()]);
        var seen = new HashSet<string>(queue);

        while (queue.Count > 0 && pages.Count < maxPages)
        {
            var batch = new List<string>();
            while (queue.Count > 0 && batch.Count < _options.Concurrency)
                batch.Add(queue.Dequeue());

            var fetched = await Task.WhenAll(batch.Select(u => FetchPageAsync(http, u, fromSitemap: false, ct)));
            foreach (var page in fetched)
            {
                pages.Add(page);
                onPage(page);
                foreach (var link in page.Links)
                {
                    var clean = link.Split('#')[0];
                    if (IsSameSite(clean, origin) && robots.IsAllowed(PathAndQuery(clean)) && !LooksLikeFile(clean) && seen.Add(clean))
                        queue.Enqueue(clean);
                }
            }

            await Task.Delay(_options.DelayMs, ct);
        }
    }

    private async Task<SeoPage> FetchPageAsync(HttpClient http, string url, bool fromSitemap, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                var status = (int)response.StatusCode;

                if (status is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    var target = location is null ? null : (location.IsAbsoluteUri ? location : new Uri(new Uri(url), location)).ToString();
                    return new SeoPage { Url = url, Status = status, RedirectTo = target, FromSitemap = fromSitemap };
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (status >= 400 || mediaType is null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                    return new SeoPage { Url = url, Status = status, FromSitemap = fromSitemap };

                var html = await ReadLimitedAsync(response.Content, ct);
                var xRobots = response.Headers.TryGetValues("X-Robots-Tag", out var values) ? string.Join(", ", values) : null;
                return PageAnalyzer.Analyze(url, status, html, fromSitemap, xRobots);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt < 2)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                logger.LogDebug("Fetch failed {Url}: {Error}", url, ex.Message);
                return new SeoPage { Url = url, Status = 0, Error = ex is TaskCanceledException ? "timeout" : ex.Message, FromSitemap = fromSitemap };
            }
        }
    }

    private async Task<Uri> ResolveOriginAsync(HttpClient http, string startUrl, CancellationToken ct)
    {
        var uri = new Uri(startUrl.Contains("://") ? startUrl : "https://" + startUrl);
        var current = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");

        // Follow redirects of the home page (http → https, www ↔ apex) to audit the canonical host.
        for (var i = 0; i < 5; i++)
        {
            try
            {
                using var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is null)
                    break;

                var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                var nextOrigin = new Uri(next.GetLeftPart(UriPartial.Authority) + "/");
                if (nextOrigin == current)
                    break;
                current = nextOrigin;
            }
            catch (HttpRequestException)
            {
                break;
            }
        }

        return current;
    }

    private async Task<(RobotsTxt Robots, bool Exists)> LoadRobotsAsync(HttpClient http, Uri origin, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(origin, "/robots.txt"), ct);
            if (response.StatusCode != HttpStatusCode.OK)
                return (RobotsTxt.Empty, false);

            return (RobotsTxt.Parse(await response.Content.ReadAsStringAsync(ct)), true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("robots.txt not loaded: {Error}", ex.Message);
            return (RobotsTxt.Empty, false);
        }
    }

    private async Task<SiteChecks> CheckSiteAsync(HttpClient http, Uri origin, RobotsTxt robots, bool robotsExists, CancellationToken ct)
    {
        var notFound = await StatusAsync(http, new Uri(origin, $"/__seo-check-404-{Guid.NewGuid():N}"), ct);

        bool? httpsRedirect = null;
        if (origin.Scheme == Uri.UriSchemeHttps)
        {
            var (status, location) = await HeadersAsync(http, new Uri($"http://{origin.Authority}/"), ct);
            httpsRedirect = status is null ? null : status is >= 300 and < 400 && location?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;
        }

        var otherHost = origin.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? origin.Host[4..] : "www." + origin.Host;
        var (wwwStatus, _) = await HeadersAsync(http, new Uri($"{origin.Scheme}://{otherHost}/"), ct);
        bool? consolidated = wwwStatus switch
        {
            null => null,
            >= 300 and < 400 => true,
            >= 200 and < 300 => false,
            _ => null
        };

        return new SiteChecks
        {
            RobotsExists = robotsExists,
            RobotsHasSitemap = robots.Sitemaps.Count > 0,
            NotFoundStatus = notFound,
            HttpRedirectsToHttps = httpsRedirect,
            WwwVariantsConsolidated = consolidated
        };
    }

    private async Task<int?> StatusAsync(HttpClient http, Uri uri, CancellationToken ct) => (await HeadersAsync(http, uri, ct)).Status;

    private async Task<(int? Status, string? Location)> HeadersAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            return ((int)response.StatusCode, response.Headers.Location?.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug("Site check {Url} failed: {Error}", uri, ex.Message);
            return (null, null);
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0 && buffer.Length < MaxHtmlBytes)
            buffer.Write(chunk, 0, read);

        var charset = content.Headers.ContentType?.CharSet;
        var encoding = System.Text.Encoding.UTF8;
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                encoding = System.Text.Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
            }
        }

        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static bool IsSameSite(string url, Uri origin) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && StripWww(uri.Host) == StripWww(origin.Host);

    private static string StripWww(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..].ToLowerInvariant() : host.ToLowerInvariant();

    private static string PathAndQuery(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;

    private static bool LooksLikeFile(string url) =>
        Path.GetExtension(PathAndQuery(url).Split('?')[0]) is { Length: > 0 } ext && ext.ToLowerInvariant() is not (".html" or ".htm" or ".php" or ".aspx");
}
