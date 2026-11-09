using System.Text;
using Shouldly;
using SystematiaBot.Bot.Handlers;
using SystematiaBot.Core.Jobs;
using SystematiaBot.Infrastructure.Cv;

namespace SystematiaBot.Tests.Jobs;

public class CvTests
{
    private static CvData Sample() => new()
    {
        Name = "Test User",
        Headline = ".NET Developer",
        Summary = "Original summary.",
        Contacts = [new CvLink { Label = "github.com/test", Url = "https://github.com/test" }],
        Experience = [new CvSection { Id = "job", Title = "Intern — Acme", Period = "2025", Bullets = [new CvBullet { Id = "job-a", Text = "Did things." }] }],
        Projects =
        [
            Project("shop", "shop-api", "shop-db", "shop-ui"),
            Project("bot", "bot-a", "bot-b"),
            Project("site", "site-a", "site-b", "site-c")
        ],
        Skills =
        [
            new CvSkillGroup { Id = "backend", Label = "Backend", Items = "C#, ASP.NET Core" },
            new CvSkillGroup { Id = "front", Label = "Frontend", Items = "React" },
            new CvSkillGroup { Id = "db", Label = "Databases", Items = "PostgreSQL" }
        ],
        Education = ["University"],
        Languages = "English — B2"
    };

    private static CvSection Project(string id, params string[] bullets) => new()
    {
        Id = id, Title = id.ToUpperInvariant(), Tech = "C#",
        Bullets = bullets.Select(b => new CvBullet { Id = b, Text = $"Bullet {b}" }).ToList()
    };

    [Fact]
    public void Plan_reorders_projects_bullets_and_skills_and_ignores_unknown_ids()
    {
        var plan = new CvPlan("Junior .NET Developer · ASP.NET Core", "Tailored summary.",
            [new CvProjectPick("bot", ["bot-b", "bot-a", "made-up"]), new CvProjectPick("shop", ["shop-db", "shop-api"]), new CvProjectPick("ghost", ["x"])],
            ["db", "backend"]);

        var cv = CvTailor.Apply(Sample(), plan);

        cv.Projects.Select(p => p.Id).ShouldBe(["bot", "shop"]);
        cv.Projects[0].Bullets.Select(b => b.Id).ShouldBe(["bot-b", "bot-a"]);
        cv.Projects[1].Bullets.Select(b => b.Id).ShouldBe(["shop-db", "shop-api"]);
        cv.Skills.Select(s => s.Id).ShouldBe(["db", "backend", "front"]);
        cv.Headline.ShouldBe("Junior .NET Developer · ASP.NET Core");
        cv.Summary.ShouldBe("Tailored summary.");
    }

    [Fact]
    public void Bullet_texts_always_come_from_the_base_cv()
    {
        var cv = CvTailor.Apply(Sample(), new CvPlan(null, null, [new("site", ["site-c", "site-a"]), new("bot", ["bot-a", "bot-b"])], null));

        cv.Projects.SelectMany(p => p.Bullets).ShouldAllBe(b => b.Text == $"Bullet {b.Id}");
    }

    [Fact]
    public void Weak_plans_fall_back_to_the_base_cv()
    {
        var baseCv = Sample();

        // One known project only: keep the original order. Too long headline: keep the original headline.
        var cv = CvTailor.Apply(baseCv, new CvPlan(new string('x', 200), null, [new("bot", ["bot-a", "bot-b"])], null));
        cv.Projects.Select(p => p.Id).ShouldBe(["shop", "bot", "site"]);
        cv.Headline.ShouldBe(".NET Developer");

        // Fewer than two valid bullets: the project's first bullets are used.
        cv = CvTailor.Apply(baseCv, new CvPlan(null, null, [new("shop", ["nope"]), new("site", ["site-b", "site-a"])], null));
        cv.Projects[0].Bullets.Select(b => b.Id).ShouldBe(["shop-api", "shop-db", "shop-ui"]);

        CvTailor.Apply(baseCv, null).Projects.Count.ShouldBe(3);
        baseCv.Projects[0].Bullets.Count.ShouldBe(3, "the base CV must never be mutated");
    }

    [Fact]
    public void Cv_json_is_validated()
    {
        CvData.TryParse("{", out var error).ShouldBeNull();
        error.ShouldNotBeNull();
        CvData.TryParse("""{"name":"X","projects":[]}""", out error).ShouldBeNull();
        error.ShouldBe("немає жодного проєкту в projects");
        CvData.TryParse("""{"name":"X","projects":[{"id":"a","title":"A","bullets":[{"id":"b","text":"1"},{"id":"b","text":"2"}]}]}""", out error).ShouldBeNull();
        error!.ShouldContain("«b»");

        var json = System.Text.Json.JsonSerializer.Serialize(Sample(), CvData.Json);
        CvData.TryParse(json, out error).ShouldNotBeNull();
        error.ShouldBeNull();
    }

    [Fact]
    public void Pdf_is_rendered_with_embedded_fonts_and_cyrillic()
    {
        var cv = Sample();
        cv.Summary = "Розробник .NET зі Львова — перевірка кирилиці.";

        var pdf = new QuestPdfCvRenderer().Render(cv);

        pdf.Length.ShouldBeGreaterThan(5_000);
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Theory]
    [InlineData("Maksym Paliy", "SoftServe Inc.", "Maksym_Paliy_CV_SoftServe_Inc.pdf")]
    [InlineData("Maksym Paliy", "ТОВ «Рога і Копита»", "Maksym_Paliy_CV_ТОВ_Рога_і_Копита.pdf")]
    [InlineData("Maksym Paliy", null, "Maksym_Paliy_CV.pdf")]
    [InlineData("", "../../etc", "CV_etc.pdf")]
    public void File_name_is_safe_and_readable(string name, string? company, string expected)
    {
        CvHandler.FileName(name, company).ShouldBe(expected);
    }
}
