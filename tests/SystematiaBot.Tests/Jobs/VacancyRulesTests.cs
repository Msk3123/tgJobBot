using Shouldly;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class VacancyPrefilterTests
{
    private const int Max = 30;

    [Theory]
    [InlineData("Senior .NET Developer", "рівень senior")]
    [InlineData("Team Lead (.NET)", "рівень lead")]
    [InlineData("Solution Architect", "рівень architect")]
    [InlineData("Java Developer", "інший стек (java)")]
    [InlineData("Junior PHP Developer", "інший стек (php)")]
    [InlineData("Manual QA Engineer", "інший стек (qa)")]
    [InlineData("iOS Developer", "інший стек (ios)")]
    public void Clear_mismatches_are_filtered_without_the_model(string title, string reason)
    {
        VacancyPrefilter.Check(title, null, null, Max).ShouldBe(new PrefilterResult(false, reason));
    }

    [Theory]
    [InlineData("Junior .NET Developer")]
    [InlineData("Junior/Middle C# Developer (lead generation product)")] // "junior" wins over "lead"
    [InlineData("Python / C# Developer")]                                 // .NET is in the title
    [InlineData("JavaScript Full Stack Developer")]                       // not Java
    [InlineData("Trainee Software Engineer")]
    [InlineData("AI Engineer (LLM, RAG)")]                                // AI roles are the model's call
    [InlineData("Middle .NET Developer")]
    public void Ambiguous_or_matching_titles_go_to_the_model(string title)
    {
        VacancyPrefilter.Check(title, null, null, Max).Pass.ShouldBeTrue();
    }

    [Theory]
    [InlineData("$$ 3 роки досвіду · B2", "потрібно 3+ р. досвіду")]
    [InlineData("5+ years of experience", "потрібно 5+ р. досвіду")]
    public void Experience_from_the_mail_line_is_checked(string details, string reason)
    {
        VacancyPrefilter.Check("Software Engineer", details, null, Max).ShouldBe(new PrefilterResult(false, reason));
    }

    [Fact]
    public void Experience_from_the_page_wins_over_the_mail_line()
    {
        VacancyPrefilter.Check(".NET Developer", "5 років досвіду", experienceMonths: 12, Max).Pass.ShouldBeTrue();
        VacancyPrefilter.Check(".NET Developer", "Без досвіду", experienceMonths: 36, Max).Pass.ShouldBeFalse();
    }

    [Theory]
    [InlineData("1 рік досвіду", 12)]
    [InlineData("2 роки досвіду", 24)]
    [InlineData("1.5 years", 18)]
    [InlineData("Без досвіду", 0)]
    [InlineData("B2 · Office", null)]
    public void Months_are_parsed_from_text(string text, int? months)
    {
        VacancyPrefilter.MonthsFromText(text).ShouldBe(months);
    }
}

public class VacancyDedupTests
{
    [Fact]
    public void Same_vacancy_on_two_boards_gets_the_same_key_even_with_cyrillic_lookalikes()
    {
        // DOU title with Cyrillic "С" in "С#", Djinni title with Latin "C#".
        var dou = VacancyDedup.Key("IT-Enterprise", "Junior Back-End Developer (С#, .Net)");
        var djinni = VacancyDedup.Key("IT Enterprise", "Junior Back-End Developer (C#, .NET)");

        dou.ShouldNotBeNull();
        dou.ShouldBe(djinni);
    }

    [Fact]
    public void Different_roles_at_one_company_differ()
    {
        VacancyDedup.Key("Acme", "Junior .NET Developer").ShouldNotBe(VacancyDedup.Key("Acme", "Middle .NET Developer"));
    }

    [Fact]
    public void No_company_means_no_key()
    {
        VacancyDedup.Key(null, "Junior .NET Developer").ShouldBeNull();
        VacancyDedup.Key("  ", "Junior .NET Developer").ShouldBeNull();
    }
}

public class VacancyPriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static Vacancy Vacancy(int? fit, double? ageDays, double foundDaysAgo = 0) => new()
    {
        Source = "djinni", ExternalId = "1", Url = "https://djinni.co/jobs/1-x/", Title = "x",
        FitScore = fit, PostedAt = ageDays is { } d ? Now.AddDays(-d) : null, FoundAt = Now.AddDays(-foundDaysAgo)
    };

    [Fact]
    public void A_fresh_good_match_beats_an_old_slightly_better_one()
    {
        var fresh = VacancyPriority.Score(Vacancy(70, 0.5), Now);
        var old = VacancyPriority.Score(Vacancy(78, 20), Now);

        fresh.ShouldBe(70);
        old.ShouldBe(47);
        fresh!.Value.ShouldBeGreaterThan(old!.Value);
    }

    [Fact]
    public void Unscored_vacancy_has_no_priority_and_found_time_is_the_fallback()
    {
        VacancyPriority.Score(Vacancy(null, 0), Now).ShouldBeNull();
        VacancyPriority.Age(Vacancy(80, null, foundDaysAgo: 2), Now).ShouldBe(TimeSpan.FromDays(2));
    }

    [Theory]
    [InlineData(0.01, "щойно")]
    [InlineData(0.25, "6 год тому")]
    [InlineData(3.5, "3 дн тому")]
    public void Age_is_human_readable(double days, string expected)
    {
        VacancyPriority.FormatAge(TimeSpan.FromDays(days)).ShouldBe(expected);
    }
}
