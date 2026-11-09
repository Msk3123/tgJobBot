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

    [Fact]
    public void Publication_date_and_experience_come_from_job_posting_json_ld()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="Junior .NET Developer в Acme">
            <script type="application/ld+json">{"@context":"https://schema.org","@type":"BreadcrumbList"}</script>
            <script type="application/ld+json">
            {"@context":"https://schema.org/","@type":"JobPosting","title":"Junior .NET Developer",
             "datePosted":"2026-11-05T10:15:00","experienceRequirements":{"@type":"OccupationalExperienceRequirements","monthsOfExperience":12}}
            </script>
            </head><body><div class="job-post__description">Text</div></body></html>
            """;

        var page = DjinniPageParser.Parse(html);

        page.PostedAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 10, 15, 0, TimeSpan.Zero));
        page.ExperienceMonths.ShouldBe(12);
    }

    [Fact]
    public void Missing_or_broken_json_ld_is_not_an_error()
    {
        var page = DjinniPageParser.Parse("""<html><head><script type="application/ld+json">{broken</script></head><body></body></html>""");

        page.PostedAt.ShouldBeNull();
        page.ExperienceMonths.ShouldBeNull();
    }
}
