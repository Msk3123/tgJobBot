using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SystematiaBot.Core.Seo;

namespace SystematiaBot.Infrastructure.Seo;

/// <summary>Extracts SEO-relevant fields from an HTML document.</summary>
public static class PageAnalyzer
{
    private static readonly string[] NonContentSelectors = ["script", "style", "noscript", "template", "svg", "nav", "header", "footer", "iframe"];

    public static SeoPage Analyze(string url, int status, string html, bool fromSitemap, string? xRobotsTag = null)
    {
        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(html);
        var baseUri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;

        string? Meta(string name) =>
            doc.QuerySelectorAll("meta[name]")
                .FirstOrDefault(m => string.Equals(m.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))
                ?.GetAttribute("content")?.Trim();

        var robots = string.Join(", ", new[] { Meta("robots"), xRobotsTag }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var canonical = doc.QuerySelectorAll("link[rel]")
            .FirstOrDefault(l => HasRel(l, "canonical"))
            ?.GetAttribute("href");

        var hreflangs = doc.QuerySelectorAll("link[rel][hreflang]")
            .Where(l => HasRel(l, "alternate"))
            .Select(l => new Hreflang(l.GetAttribute("hreflang")!.Trim(), Resolve(baseUri, l.GetAttribute("href")) ?? ""))
            .ToList();

        var images = doc.QuerySelectorAll("img").ToList();
        var withoutAlt = images.Count(i => string.IsNullOrWhiteSpace(i.GetAttribute("alt")));

        var links = doc.QuerySelectorAll("a[href]")
            .Select(a => Resolve(baseUri, a.GetAttribute("href")))
            .Where(h => h is not null)
            .Select(h => h!)
            .Distinct()
            .ToList();

        var title = doc.QuerySelector("head title")?.TextContent ?? doc.QuerySelector("title")?.TextContent;

        return new SeoPage
        {
            Url = url,
            Status = status,
            FromSitemap = fromSitemap,
            IsHtml = true,
            Title = Clean(title),
            Description = Clean(Meta("description")),
            H1 = doc.QuerySelectorAll("h1").Select(h => Clean(h.TextContent) ?? "").ToList(),
            Canonical = Resolve(baseUri, canonical),
            Robots = robots.Length > 0 ? robots : null,
            Hreflangs = hreflangs,
            Images = images.Count,
            ImagesWithoutAlt = withoutAlt,
            WordCount = CountContentWords(doc),
            Links = links
        };
    }

    private static int CountContentWords(IDocument doc)
    {
        var body = doc.Body;
        if (body is null)
            return 0;

        foreach (var element in body.QuerySelectorAll(string.Join(',', NonContentSelectors)).ToList())
            element.Remove();

        return body.TextContent
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(t => t.Any(char.IsLetter));
    }

    private static bool HasRel(IElement element, string rel) =>
        element.GetAttribute("rel")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(r => r.Equals(rel, StringComparison.OrdinalIgnoreCase)) == true;

    private static string? Clean(string? text) =>
        text is null ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Resolve(Uri? baseUri, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        href = href.Trim();
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https")
            return abs.GetLeftPart(UriPartial.Query);
        if (baseUri is not null && !href.StartsWith('#') && !href.Contains(':') && Uri.TryCreate(baseUri, href, out var rel))
            return rel.GetLeftPart(UriPartial.Query);
        if (baseUri is not null && href.StartsWith("//") && Uri.TryCreate($"{baseUri.Scheme}:{href}", UriKind.Absolute, out var proto))
            return proto.GetLeftPart(UriPartial.Query);

        return null;
    }
}
