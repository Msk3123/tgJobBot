using Shouldly;
using SystematiaBot.Bot.Handlers;
using SystematiaBot.Bot.UI;

namespace SystematiaBot.Tests.Bot;

public class MessageFormatterTests
{
    [Fact]
    public void Escape_encodes_html()
    {
        MessageFormatter.Escape("<b>Двері & вікна</b>").ShouldBe("&lt;b&gt;Двері &amp; вікна&lt;/b&gt;");
    }

    [Theory]
    [InlineData(1, "1 сторінка")]
    [InlineData(3, "3 сторінки")]
    [InlineData(5, "5 сторінок")]
    [InlineData(11, "11 сторінок")]
    [InlineData(21, "21 сторінка")]
    [InlineData(8716, "8 716 сторінок")]
    public void Plural_follows_ukrainian_rules(int n, string expected)
    {
        MessageFormatter.Plural(n, "сторінка", "сторінки", "сторінок").ShouldBe(expected);
    }

    [Fact]
    public void Long_text_is_split_by_paragraphs_within_limit()
    {
        var paragraphs = Enumerable.Range(0, 30).Select(i => $"Абзац {i}: " + new string('х', 300)).ToList();

        var parts = MessageFormatter.Split(string.Join("\n\n", paragraphs));

        parts.Count.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(p => p.Length <= MessageFormatter.MaxMessageLength);
        string.Join("\n\n", parts).ShouldBe(string.Join("\n\n", paragraphs));
    }

    [Fact]
    public void Huge_paragraph_is_hard_split()
    {
        var parts = MessageFormatter.Split(new string('х', 10_000));

        parts.ShouldAllBe(p => p.Length <= MessageFormatter.MaxMessageLength);
        parts.Sum(p => p.Length).ShouldBe(10_000);
    }

    [Theory]
    [InlineData("maxidveri.com", true, "https://maxidveri.com")]
    [InlineData("https://maxidveri.com/blog/x", true, "https://maxidveri.com")]
    [InlineData("http://example.com.ua", true, "http://example.com.ua")]
    [InlineData("привіт", false, "")]
    [InlineData("ftp://example.com", false, "")]
    public void Seo_url_is_normalized(string input, bool ok, string expected)
    {
        SeoHandler.TryNormalizeUrl(input, out var url).ShouldBe(ok);
        url.ShouldBe(expected);
    }
}
