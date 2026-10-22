using Shouldly;
using SystematiaBot.Core.Seo;
using SystematiaBot.Infrastructure.Seo;

namespace SystematiaBot.Tests.Seo;

public class SeoIssueDetectorTests
{
    private const string GoodTitle = "Вхідні двері у Львові — ціни та монтаж під ключ";
    private const string GoodDescription = "Купити вхідні двері у Львові: понад 300 моделей, безкоштовний замір, доставка і монтаж за 3 дні. Гарантія 5 років.";

    private readonly SeoIssueDetector _detector = new();

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("слово", count));

    private static string Html(
        string? title = GoodTitle,
        string? description = GoodDescription,
        string h1 = "<h1>Вхідні двері</h1>",
        string head = "",
        string body = "")
    {
        var titleTag = title is null ? "" : $"<title>{title}</title>";
        var descTag = description is null ? "" : $"<meta name=\"description\" content=\"{description}\">";
        return $"<html><head>{titleTag}{descTag}{head}</head><body>{h1}<p>{Words(200)}</p>{body}</body></html>";
    }

    private static SeoPage Page(string html, string url = "https://example.com/doors", bool fromSitemap = true) =>
        PageAnalyzer.Analyze(url, 200, html, fromSitemap);

    private IReadOnlyList<string> Codes(SeoPage page) => _detector.Detect([page]).Select(i => i.Code).ToList();

    [Fact]
    public void Good_page_has_no_issues()
    {
        Codes(Page(Html())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(404, "STATUS_4XX")]
    [InlineData(410, "STATUS_4XX")]
    [InlineData(500, "STATUS_5XX")]
    public void Error_status_is_detected(int status, string code)
    {
        Codes(new SeoPage { Url = "https://example.com/x", Status = status, FromSitemap = true }).ShouldBe([code]);
    }

    [Fact]
    public void Redirect_in_sitemap_is_detected()
    {
        Codes(new SeoPage { Url = "https://example.com/x", Status = 301, RedirectTo = "https://example.com/y", FromSitemap = true })
            .ShouldBe(["REDIRECT_IN_SITEMAP"]);
    }

    [Fact]
    public void Empty_title_description_and_h1_are_detected()
    {
        Codes(Page(Html(title: null, description: null, h1: ""))).ShouldBe(["TITLE_EMPTY", "DESC_EMPTY", "H1_EMPTY"], ignoreOrder: true);
    }

    [Fact]
    public void Multiple_h1_is_detected()
    {
        Codes(Page(Html(h1: "<h1>Один</h1><h1>Два</h1>"))).ShouldBe(["H1_MULTIPLE"]);
    }

    [Theory]
    [InlineData(66, "TITLE_LONG")]
    [InlineData(24, "TITLE_SHORT")]
    public void Title_length_is_checked(int length, string code)
    {
        Codes(Page(Html(title: new string('т', length)))).ShouldBe([code]);
    }

    [Theory]
    [InlineData(161, "DESC_LONG")]
    [InlineData(69, "DESC_SHORT")]
    public void Description_length_is_checked(int length, string code)
    {
        Codes(Page(Html(description: new string('о', length)))).ShouldBe([code]);
    }

    [Fact]
    public void Duplicate_titles_and_descriptions_across_urls_are_detected()
    {
        var issues = _detector.Detect([Page(Html(), "https://example.com/a"), Page(Html(), "https://example.com/b")]);

        issues.Count(i => i.Code == "TITLE_DUP").ShouldBe(2);
        issues.Count(i => i.Code == "DESC_DUP").ShouldBe(2);
    }

    [Fact]
    public void Canonical_to_other_url_is_info()
    {
        var issue = _detector.Detect([Page(Html(head: "<link rel=\"canonical\" href=\"https://example.com/other\">"))]).ShouldHaveSingleItem();

        issue.Code.ShouldBe("CANONICAL_OTHER");
        issue.Severity.ShouldBe(SeoSeverity.Info);
    }

    [Fact]
    public void Self_canonical_is_fine()
    {
        Codes(Page(Html(head: "<link rel=\"canonical\" href=\"/doors\">"))).ShouldBeEmpty();
    }

    [Fact]
    public void Canonical_pointing_to_redirect_is_detected()
    {
        var page = Page(Html(head: "<link rel=\"canonical\" href=\"https://example.com/old\">"));
        var redirect = new SeoPage { Url = "https://example.com/old", Status = 301, RedirectTo = "https://example.com/new" };

        _detector.Detect([page, redirect]).Select(i => i.Code).ShouldContain("CANONICAL_REDIRECT");
    }

    [Fact]
    public void Noindex_in_sitemap_url_is_detected()
    {
        Codes(Page(Html(head: "<meta name=\"robots\" content=\"noindex, follow\">"))).ShouldBe(["NOINDEX"]);
    }

    [Fact]
    public void Noindex_outside_sitemap_is_fine()
    {
        Codes(Page(Html(head: "<meta name=\"robots\" content=\"noindex\">"), fromSitemap: false)).ShouldBeEmpty();
    }

    [Fact]
    public void Duplicate_hreflang_is_detected()
    {
        const string head = "<link rel=\"alternate\" hreflang=\"uk\" href=\"/uk/a\"><link rel=\"alternate\" hreflang=\"uk\" href=\"/uk/b\">" +
                            "<link rel=\"alternate\" hreflang=\"x-default\" href=\"/\"><link rel=\"alternate\" hreflang=\"x-default\" href=\"/en\">";
        Codes(Page(Html(head: head))).ShouldBe(["HREFLANG_DUP"]);
    }

    [Fact]
    public void Images_without_alt_over_20_percent_are_detected()
    {
        Codes(Page(Html(body: "<img src=a alt=\"Двері\"><img src=b alt=\"Ручка\"><img src=c alt=\"Замок\"><img src=d><img src=e alt=\"\">")))
            .ShouldBe(["IMG_NO_ALT"]);
        Codes(Page(Html(body: "<img src=a alt=\"Двері\"><img src=b alt=\"Ручка\"><img src=c alt=\"Замок\"><img src=d alt=\"Петля\"><img src=e>")))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Thin_content_ignores_navigation_and_scripts()
    {
        var html = $"<html><head><title>{GoodTitle}</title><meta name=\"description\" content=\"{GoodDescription}\"></head>" +
                   $"<body><nav>{Words(300)}</nav><h1>Двері</h1><p>{Words(50)}</p><script>var a = '{Words(300)}';</script><footer>{Words(300)}</footer></body></html>";

        Codes(Page(html)).ShouldBe(["THIN_CONTENT"]);
    }

    [Theory]
    [InlineData("Lorem ipsum dolor sit amet — сторінка товару", "https://example.com/doors")]
    [InlineData(GoodTitle, "https://example.com/test-page")]
    [InlineData("Тест — нова сторінка каталогу вхідних дверей", "https://example.com/doors")]
    public void Test_pages_are_detected(string title, string url)
    {
        Codes(Page(Html(title: title), url)).ShouldContain("TEST_PAGE");
    }

    [Fact]
    public void Words_containing_test_are_not_test_pages()
    {
        Codes(Page(Html(title: "Testimonials — відгуки клієнтів про двері"), "https://example.com/contest")).ShouldNotContain("TEST_PAGE");
    }

    [Theory]
    [InlineData("https://example.com/cities/kam039janec-podilskyy")]
    [InlineData("https://example.com/doors-quot-model-quot")]
    [InlineData("https://example.com/a-amp-b")]
    [InlineData("https://example.com/a%7Cb")]
    public void Html_entities_in_url_are_detected(string url)
    {
        Codes(Page(Html(), url)).ShouldContain("URL_ENTITIES");
    }

    [Fact]
    public void Site_checks_produce_issues()
    {
        var site = new SiteChecks { RobotsExists = true, RobotsHasSitemap = false, NotFoundStatus = 200, HttpRedirectsToHttps = false, WwwVariantsConsolidated = false };

        _detector.Detect([Page(Html())], site).Select(i => i.Code)
            .ShouldBe(["ROBOTS_NO_SITEMAP", "SOFT_404", "NO_HTTPS_REDIRECT", "WWW_DUPLICATE"], ignoreOrder: true);
    }
}
