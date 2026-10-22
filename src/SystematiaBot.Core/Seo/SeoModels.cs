namespace SystematiaBot.Core.Seo;

public sealed class SeoOptions
{
    public const string Section = "Seo";

    public int QuickPages { get; set; } = 50;
    public int FullPages { get; set; } = 500;
    public int Concurrency { get; set; } = 4;
    public int DelayMs { get; set; } = 250;
    public int TimeoutSec { get; set; } = 20;
    public string UserAgent { get; set; } = "SystematiaSeoAudit/1.0 (+contact)";
}

public enum SeoSeverity
{
    Info,
    Low,
    Medium,
    High
}

public sealed record Hreflang(string Lang, string Href);

/// <summary>Everything collected about one crawled URL.</summary>
public sealed class SeoPage
{
    public required string Url { get; init; }
    public int Status { get; init; }
    public string? RedirectTo { get; init; }
    public string? Error { get; init; }
    public bool FromSitemap { get; init; }
    public bool IsHtml { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> H1 { get; init; } = [];
    public string? Canonical { get; init; }
    public string? Robots { get; init; }
    public IReadOnlyList<Hreflang> Hreflangs { get; init; } = [];
    public int Images { get; init; }
    public int ImagesWithoutAlt { get; init; }
    public int WordCount { get; init; }
    public IReadOnlyList<string> Links { get; init; } = [];

    public bool IsOk => Status is >= 200 and < 300 && Error is null;
    public bool IsRedirect => Status is >= 300 and < 400;
}

/// <summary>Site-wide technical checks done once per audit.</summary>
public sealed class SiteChecks
{
    public bool RobotsExists { get; init; }
    public bool RobotsHasSitemap { get; init; }
    public int? NotFoundStatus { get; init; }
    public bool? HttpRedirectsToHttps { get; init; }
    public bool? WwwVariantsConsolidated { get; init; }
}

public sealed record SeoIssue(string Code, SeoSeverity Severity, string Url, string? Detail = null);

public sealed class SeoCrawlResult
{
    public required string StartUrl { get; init; }
    public required IReadOnlyList<SeoPage> Pages { get; init; }
    public required SiteChecks Site { get; init; }

    /// <summary>Page URLs found in the sitemap(s) (before sampling).</summary>
    public int SitemapUrlCount { get; init; }

    public bool Cancelled { get; init; }
}

public sealed record SeoProgress(string Stage, int SitemapUrls, int Checked, int Total, int IssuesFound);

public sealed record SeoIssueGroup(string Code, SeoSeverity Severity, int Count, string ExampleUrl, string? ExampleDetail);
