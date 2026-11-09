using Shouldly;
using SystematiaBot.Infrastructure.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class DouRssParserTests
{
    private static readonly string Xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dou_feed.xml"));

    [Fact]
    public void Items_become_candidates_with_canonical_links_and_items_without_id_are_skipped()
    {
        var items = DouRssParser.Parse(Xml);

        items.Select(i => i.ExternalId).ShouldBe(["300001", "300002"]);
        items[0].Url.ShouldBe("https://jobs.dou.ua/companies/acme/vacancies/300001/");
        items.ShouldAllBe(i => i.Source == DouRssParser.Source);
    }

    [Fact]
    public void Title_is_split_into_role_company_and_location()
    {
        var items = DouRssParser.Parse(Xml);

        items[0].Title.ShouldBe("Junior Back-End Developer (С#, .Net)");
        items[0].Company.ShouldBe("Acme Soft");
        items[0].Details.ShouldBe("Київ");
        items[1].Company.ShouldBe("Example Corp");
        items[1].Details.ShouldBeNull();
    }

    [Fact]
    public void Description_is_plain_text_without_the_apply_link_and_pub_date_keeps_the_offset()
    {
        var item = DouRssParser.Parse(Xml)[0];

        item.Description.ShouldNotBeNull();
        item.Description.ShouldContain("• C#, ASP.NET Core.");
        item.Description.ShouldNotContain("Відгукнутись");
        item.PostedAt.ShouldBe(new DateTimeOffset(2026, 11, 3, 13, 24, 10, TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData("Junior .NET Developer в Bliscore, Львів, віддалено", "Junior .NET Developer", "Bliscore", "Львів, віддалено")]
    [InlineData("Стажер в Example", "Стажер", "Example", null)]
    [InlineData("Developer", "Developer", null, null)]
    public void Split_title(string value, string title, string? company, string? location)
    {
        DouRssParser.SplitTitle(value).ShouldBe((title, company, location));
    }
}
