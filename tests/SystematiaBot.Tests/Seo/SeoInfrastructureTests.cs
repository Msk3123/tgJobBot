using System.Text;
using System.Xml.Linq;
using Shouldly;
using SystematiaBot.Core.Seo;
using SystematiaBot.Infrastructure.Export;
using SystematiaBot.Infrastructure.Seo;

namespace SystematiaBot.Tests.Seo;

public class SeoInfrastructureTests
{
    [Fact]
    public void Robots_respects_star_group_and_longest_match()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: Googlebot
            Disallow: /

            User-agent: *
            Disallow: /admin
            Disallow: /*?sort=
            Allow: /admin/public
            Sitemap: https://example.com/sitemap.xml
            """);

        robots.IsAllowed("/doors").ShouldBeTrue();
        robots.IsAllowed("/admin/users").ShouldBeFalse();
        robots.IsAllowed("/admin/public/page").ShouldBeTrue();
        robots.IsAllowed("/catalog?sort=price").ShouldBeFalse();
        robots.Sitemaps.ShouldBe(["https://example.com/sitemap.xml"]);
    }

    [Fact]
    public void Sitemap_index_and_urlset_are_parsed()
    {
        var index = XDocument.Parse("""
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>https://example.com/sitemap-1.xml</loc></sitemap>
              <sitemap><loc>https://example.com/sitemap-2.xml.gz</loc></sitemap>
            </sitemapindex>
            """);
        var urlset = XDocument.Parse("""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc> https://example.com/a </loc></url>
              <url><loc>https://example.com/b</loc><lastmod>2026-10-01</lastmod></url>
            </urlset>
            """);

        var (isIndex, sitemaps) = SitemapReader.Parse(index);
        isIndex.ShouldBeTrue();
        sitemaps.ShouldBe(["https://example.com/sitemap-1.xml", "https://example.com/sitemap-2.xml.gz"]);

        var (isUrlsetIndex, pages) = SitemapReader.Parse(urlset);
        isUrlsetIndex.ShouldBeFalse();
        pages.ShouldBe(["https://example.com/a", "https://example.com/b"]);
    }

    [Fact]
    public void Stratified_sample_covers_all_sections()
    {
        var urls = Enumerable.Range(0, 1000).Select(i => $"https://example.com/shop/item-{i}")
            .Concat(Enumerable.Range(0, 20).Select(i => $"https://example.com/blog/post-{i}"))
            .Concat(["https://example.com/", "https://example.com/countries/ua/"])
            .ToList();

        var sample = UrlSampler.Stratified(urls, 30);

        sample.Count.ShouldBe(30);
        sample.ShouldContain("https://example.com/");
        sample.ShouldContain("https://example.com/countries/ua/");
        sample.Count(u => u.Contains("/blog/")).ShouldBeGreaterThanOrEqualTo(10);
        sample.Count(u => u.Contains("/shop/")).ShouldBeLessThanOrEqualTo(20);
    }

    [Fact]
    public async Task Csv_uses_semicolons_and_bom()
    {
        var crawl = new SeoCrawlResult
        {
            StartUrl = "https://example.com/",
            Site = new SiteChecks { RobotsExists = true, RobotsHasSitemap = true },
            Pages = [new SeoPage { Url = "https://example.com/a", Status = 200, IsHtml = true, Title = "Двері; вхідні", H1 = ["Двері"], WordCount = 10 }]
        };
        var issues = new[] { new SeoIssue("THIN_CONTENT", SeoSeverity.Medium, "https://example.com/a") };
        var dir = Path.Combine(Path.GetTempPath(), "seo-csv-" + Guid.NewGuid().ToString("N"));

        var path = await new CsvExporter().WriteCsvAsync(crawl, issues, dir, CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes[..3].ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Url;Status;Title;TitleLen;Description;DescLen;H1Count;H1;Canonical;Robots;ImgNoAlt;Words;Issues");
        lines[1].ShouldBe("https://example.com/a;200;\"Двері; вхідні\";13;;0;1;Двері;;;;10;THIN_CONTENT");
        Directory.Delete(dir, recursive: true);
    }
}
