using Shouldly;
using SystematiaBot.Core.Jobs;
using SystematiaBot.Infrastructure.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class DjinniRssParserTests
{
    private static readonly List<VacancyCandidate> Items =
        DjinniRssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "djinni_feed.xml")));

    [Fact]
    public void Job_items_share_ids_and_urls_with_mail_alerts_so_both_land_in_one_record()
    {
        Items.Select(i => i.ExternalId).ShouldBe(["700001", "700002"]);
        Items.ShouldAllBe(i => i.Source == DjinniMailParser.Source);
        Items[0].Url.ShouldBe("https://djinni.co/jobs/700001-trainee-junior-net-developer/");
    }

    [Fact]
    public void Title_description_and_date_are_read_and_company_is_left_to_the_page()
    {
        var item = Items[0];

        item.Title.ShouldBe("Trainee/Junior .NET Developer");
        item.Company.ShouldBeNull();
        item.Description.ShouldBe("Acme is a product company in Lviv.\n• C#, ASP.NET Core\n• SQL");
        item.PostedAt.ShouldBe(new DateTimeOffset(2026, 11, 3, 15, 37, 33, TimeSpan.FromHours(2)));
    }
}
