using Shouldly;
using SystematiaBot.Core.News;
using SystematiaBot.Infrastructure.Sources;

namespace SystematiaBot.Tests.Sources;

public class SourceParsingTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Telegram_preview_page_is_parsed_without_network()
    {
        var page = TelegramChannelSource.Parse(Fixture("tme_sheptytska_mr.html"));

        page.Messages.Count.ShouldBeGreaterThan(5);
        page.Messages.ShouldAllBe(m => m.Url.StartsWith("https://t.me/sheptytska_mr/") && m.Text.Length > 0);
        page.Messages.ShouldAllBe(m => m.PublishedAt.Offset == TimeSpan.Zero && m.PublishedAt.Year == 2026);
        page.OldestId.ShouldBe("17830");
        page.OldestDate.ShouldBe(new DateTimeOffset(2026, 10, 21, 19, 2, 1, TimeSpan.Zero));
    }

    [Fact]
    public void Telegram_media_only_posts_are_skipped_and_reposts_keep_text()
    {
        const string html = """
            <div class="tgme_widget_message" data-post="chan/1">
              <a class="tgme_widget_message_photo_wrap"></a>
              <a class="tgme_widget_message_date" href="https://t.me/chan/1"><time datetime="2026-10-20T10:00:00+00:00"></time></a>
            </div>
            <div class="tgme_widget_message" data-post="chan/2">
              <div class="tgme_widget_message_forwarded_from">Forwarded from <a>Поліція</a></div>
              <div class="tgme_widget_message_text">Перший рядок<br/>Другий рядок</div>
              <a class="tgme_widget_message_date" href="https://t.me/chan/2"><time datetime="2026-10-20T11:00:00+00:00"></time></a>
            </div>
            """;

        var page = TelegramChannelSource.Parse(html);

        var message = page.Messages.ShouldHaveSingleItem();
        message.Url.ShouldBe("https://t.me/chan/2");
        message.Text.ShouldBe("Перший рядок\nДругий рядок");
        page.OldestId.ShouldBe("1");
    }

    [Theory]
    [InlineData("https://t.me/s/echo80100", "https://t.me/s/echo80100")]
    [InlineData("https://t.me/echo80100/", "https://t.me/s/echo80100")]
    [InlineData("@echo80100", "https://t.me/s/echo80100")]
    public void Telegram_url_is_normalized_to_preview(string input, string expected)
    {
        TelegramChannelSource.PreviewUrl(input).ShouldBe(expected);
    }

    [Fact]
    public void Html_list_page_is_parsed_with_selectors()
    {
        var selectors = new Dictionary<string, string>
        {
            ["item"] = ".archive_page ul.list > li",
            ["title"] = ".news-title",
            ["link"] = "a[href]",
            ["date"] = ".time",
            ["dateFormat"] = "HH:mm dd-MM-yyyy"
        };

        var entries = HtmlListSource.ParseList(Fixture("zaxid_tag.html"), new Uri("https://zaxid.net/chervonograd_tag48789/"), selectors);

        entries.Count.ShouldBeGreaterThan(10);
        var first = entries[0];
        first.Title.ShouldBe("У Шептицькому районі грибники виявили частину зенітної ракети");
        first.Url.ShouldEndWith("_n1648196");
        // 12:42 Kyiv time (UTC+3 in October) → 09:42 UTC.
        first.PublishedAt.ShouldBe(new DateTimeOffset(2026, 10, 19, 9, 42, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Locality_filter_matches_declined_forms_and_aliases()
    {
        var city = new CityConfig { Id = "sheptytsky", Name = "Шептицький", Aliases = ["Червоноград"], LocalTerms = ["Шептицьк", "Боб'ятин"] };

        LocalityFilter.MentionsCity("У ШЕПТИЦЬКОМУ відкрили сквер", city).ShouldBeTrue();
        LocalityFilter.MentionsCity("Подія в Бобʼятині", city).ShouldBeTrue();
        LocalityFilter.MentionsCity("У Львові відкрили сквер", city).ShouldBeFalse();
    }
}
