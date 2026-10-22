using System.Text.RegularExpressions;

namespace SystematiaBot.Core.Seo;

/// <summary>Turns crawled pages into SEO issues (rules from SPEC §7.5).</summary>
public sealed partial class SeoIssueDetector
{
    public const int TitleMax = 65;
    public const int TitleMin = 25;
    public const int DescMax = 160;
    public const int DescMin = 70;
    public const int ThinContentWords = 150;
    public const double MaxImagesWithoutAltShare = 0.2;

    public IReadOnlyList<SeoIssue> Detect(IReadOnlyList<SeoPage> pages, SiteChecks? site = null)
    {
        var issues = new List<SeoIssue>();
        var byUrl = pages.GroupBy(p => NormalizeUrl(p.Url)).ToDictionary(g => g.Key, g => g.First());

        foreach (var page in pages)
            issues.AddRange(DetectPage(page, byUrl));

        issues.AddRange(DetectDuplicates(pages, p => p.Title, "TITLE_DUP"));
        issues.AddRange(DetectDuplicates(pages, p => p.Description, "DESC_DUP"));

        if (site is not null)
            issues.AddRange(DetectSite(site, pages.FirstOrDefault()?.Url));

        return issues;
    }

    public IReadOnlyList<SeoIssue> DetectPage(SeoPage page, IReadOnlyDictionary<string, SeoPage>? crawled = null)
    {
        var issues = new List<SeoIssue>();
        void Add(string code, SeoSeverity severity, string? detail = null) => issues.Add(new(code, severity, page.Url, detail));

        if (page.Error is not null)
        {
            Add("FETCH_ERROR", SeoSeverity.High, page.Error);
            return issues;
        }

        if (page.Status >= 500)
            Add("STATUS_5XX", SeoSeverity.High, page.Status.ToString());
        else if (page.Status >= 400)
            Add("STATUS_4XX", SeoSeverity.High, page.Status.ToString());
        else if (page.IsRedirect && page.FromSitemap)
            Add("REDIRECT_IN_SITEMAP", SeoSeverity.Medium, $"{page.Status} → {page.RedirectTo}");

        if (UrlEntitiesRegex().IsMatch(Uri.UnescapeDataString(page.Url)))
            Add("URL_ENTITIES", SeoSeverity.Medium);

        if (!page.IsOk || !page.IsHtml)
            return issues;

        var title = page.Title?.Trim() ?? "";
        if (title.Length == 0)
            Add("TITLE_EMPTY", SeoSeverity.High);
        else if (title.Length > TitleMax)
            Add("TITLE_LONG", SeoSeverity.Medium, $"{title.Length} симв.");
        else if (title.Length < TitleMin)
            Add("TITLE_SHORT", SeoSeverity.Medium, $"{title.Length} симв.");

        var description = page.Description?.Trim() ?? "";
        if (description.Length == 0)
            Add("DESC_EMPTY", SeoSeverity.High);
        else if (description.Length > DescMax)
            Add("DESC_LONG", SeoSeverity.Low, $"{description.Length} симв.");
        else if (description.Length < DescMin)
            Add("DESC_SHORT", SeoSeverity.Low, $"{description.Length} симв.");

        var h1 = page.H1.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        if (h1.Count == 0)
            Add("H1_EMPTY", SeoSeverity.High);
        else if (page.H1.Count > 1)
            Add("H1_MULTIPLE", SeoSeverity.Medium, $"{page.H1.Count} шт.");

        if (!string.IsNullOrWhiteSpace(page.Canonical) && NormalizeUrl(page.Canonical) != NormalizeUrl(page.Url))
        {
            if (crawled is not null && crawled.TryGetValue(NormalizeUrl(page.Canonical), out var target) && target.IsRedirect)
                Add("CANONICAL_REDIRECT", SeoSeverity.Medium, page.Canonical);
            else
                Add("CANONICAL_OTHER", SeoSeverity.Info, page.Canonical);
        }

        if (page.FromSitemap && page.Robots?.Contains("noindex", StringComparison.OrdinalIgnoreCase) == true)
            Add("NOINDEX", SeoSeverity.High, page.Robots);

        var langs = page.Hreflangs.Select(h => h.Lang.Trim().ToLowerInvariant()).ToList();
        if (langs.Count != langs.Distinct().Count())
            Add("HREFLANG_DUP", SeoSeverity.Medium, string.Join(", ", langs.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key)));

        if (page.Images > 0 && page.ImagesWithoutAlt > page.Images * MaxImagesWithoutAltShare)
            Add("IMG_NO_ALT", SeoSeverity.Low, $"{page.ImagesWithoutAlt}/{page.Images}");

        if (page.WordCount < ThinContentWords)
            Add("THIN_CONTENT", SeoSeverity.Medium, $"{page.WordCount} слів");

        var path = Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ? Uri.UnescapeDataString(uri.AbsolutePath) : page.Url;
        if (new[] { title, string.Join(' ', h1), path }.Any(s => TestPageRegex().IsMatch(s)))
            Add("TEST_PAGE", SeoSeverity.High);

        return issues;
    }

    private static IEnumerable<SeoIssue> DetectDuplicates(IReadOnlyList<SeoPage> pages, Func<SeoPage, string?> selector, string code) =>
        pages.Where(p => p.IsOk && p.IsHtml && !string.IsNullOrWhiteSpace(selector(p)))
            .GroupBy(p => selector(p)!.Trim().ToLowerInvariant())
            .Where(g => g.Select(p => NormalizeUrl(p.Url)).Distinct().Count() > 1)
            .SelectMany(g => g.Select(p => new SeoIssue(code, SeoSeverity.High, p.Url, $"{g.Count()} сторінок: «{Truncate(selector(p)!.Trim(), 60)}»")));

    private static IEnumerable<SeoIssue> DetectSite(SiteChecks site, string? anyUrl)
    {
        var root = Uri.TryCreate(anyUrl, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Host}/" : "/";

        if (!site.RobotsExists)
            yield return new("ROBOTS_MISSING", SeoSeverity.Medium, root + "robots.txt");
        else if (!site.RobotsHasSitemap)
            yield return new("ROBOTS_NO_SITEMAP", SeoSeverity.Low, root + "robots.txt");

        if (site.NotFoundStatus is { } status && status != 404 && status != 410)
            yield return new("SOFT_404", SeoSeverity.High, root, $"неіснуюча сторінка повертає {status}");

        if (site.HttpRedirectsToHttps == false)
            yield return new("NO_HTTPS_REDIRECT", SeoSeverity.High, root);

        if (site.WwwVariantsConsolidated == false)
            yield return new("WWW_DUPLICATE", SeoSeverity.Medium, root, "www і без www віддають сайт без редиректу");
    }

    /// <summary>Lowercase scheme/host, no fragment, no default port; path and query kept as is.</summary>
    public static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return url.Trim();

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{uri.PathAndQuery}";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    [GeneratedRegex(@"lorem\s+ipsum|\btest\b|\bтест\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestPageRegex();

    [GeneratedRegex(@"039|quot(?!a)|ltb|(?<![a-z])amp(?![a-z])|\||%7c", RegexOptions.IgnoreCase)]
    private static partial Regex UrlEntitiesRegex();
}
