namespace SystematiaBot.Core.Seo;

/// <summary>Issues grouped by code with counts and an example URL, ordered by severity then scale.</summary>
public sealed class SeoSummary
{
    public required string StartUrl { get; init; }
    public int PagesChecked { get; init; }
    public int SitemapUrlCount { get; init; }
    public bool Cancelled { get; init; }
    public required IReadOnlyList<SeoIssueGroup> Groups { get; init; }

    public int TotalIssues => Groups.Where(g => g.Severity > SeoSeverity.Info).Sum(g => g.Count);

    public static SeoSummary Build(SeoCrawlResult crawl, IReadOnlyList<SeoIssue> issues)
    {
        var groups = issues
            .GroupBy(i => i.Code)
            .Select(g =>
            {
                var example = g.First();
                return new SeoIssueGroup(g.Key, g.Max(i => i.Severity), g.Select(i => i.Url).Distinct().Count(), example.Url, example.Detail);
            })
            .OrderByDescending(g => g.Severity)
            .ThenByDescending(g => g.Count)
            .ToList();

        return new SeoSummary
        {
            StartUrl = crawl.StartUrl,
            PagesChecked = crawl.Pages.Count,
            SitemapUrlCount = crawl.SitemapUrlCount,
            Cancelled = crawl.Cancelled,
            Groups = groups
        };
    }

    public static string Describe(string code) => code switch
    {
        "STATUS_4XX" => "сторінки з помилкою 4xx",
        "STATUS_5XX" => "сторінки з помилкою сервера 5xx",
        "FETCH_ERROR" => "сторінки не відкрились",
        "REDIRECT_IN_SITEMAP" => "редиректи в sitemap",
        "TITLE_EMPTY" => "порожній Title",
        "DESC_EMPTY" => "порожній Description",
        "H1_EMPTY" => "немає H1",
        "H1_MULTIPLE" => "кілька H1",
        "TITLE_LONG" => $"Title довший за {SeoIssueDetector.TitleMax}",
        "TITLE_SHORT" => $"Title коротший за {SeoIssueDetector.TitleMin}",
        "DESC_LONG" => $"Description довший за {SeoIssueDetector.DescMax}",
        "DESC_SHORT" => $"Description коротший за {SeoIssueDetector.DescMin}",
        "TITLE_DUP" => "дубльований Title",
        "DESC_DUP" => "дубльований Description",
        "CANONICAL_OTHER" => "canonical на інший URL",
        "CANONICAL_REDIRECT" => "canonical веде на редирект",
        "NOINDEX" => "noindex у sitemap-URL",
        "HREFLANG_DUP" => "дублі hreflang",
        "IMG_NO_ALT" => "картинки без alt (>20%)",
        "THIN_CONTENT" => $"мало тексту (<{SeoIssueDetector.ThinContentWords} слів)",
        "TEST_PAGE" => "тестові сторінки",
        "URL_ENTITIES" => "сміття в URL (quot/amp/039)",
        "ROBOTS_MISSING" => "немає robots.txt",
        "ROBOTS_NO_SITEMAP" => "robots.txt без Sitemap",
        "SOFT_404" => "неіснуюча сторінка не віддає 404",
        "NO_HTTPS_REDIRECT" => "http не редиректить на https",
        "WWW_DUPLICATE" => "www і без www — два сайти",
        _ => code
    };
}
