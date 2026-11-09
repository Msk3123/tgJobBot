using Shouldly;
using SystematiaBot.Infrastructure.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class DjinniPageParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Title_and_company_come_from_og_title_and_description_keeps_structure()
    {
        var page = DjinniPageParser.Parse(Fixture("djinni_job_page.html"));

        page.Title.ShouldBe("Junior .NET Developer");
        page.Company.ShouldBe("TemaBit Fozzy Group");
        page.IsInactive.ShouldBeFalse();
        page.Description.ShouldBe("TemaBit Fozzy Group is an IT company.\nRequirements:\n• 1+ year of experience in .NET\n• Good knowledge of .NET 8+\nWe offer:\nremote work");
    }

    [Fact]
    public void Inactive_vacancy_is_detected()
    {
        DjinniPageParser.Parse(Fixture("djinni_job_page_inactive.html")).IsInactive.ShouldBeTrue();
    }
}
