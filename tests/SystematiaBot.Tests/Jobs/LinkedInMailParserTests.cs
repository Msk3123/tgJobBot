using Shouldly;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class LinkedInMailParserTests
{
    private static readonly ParsedMail Mail =
        LinkedInMailParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "linkedin_alert.txt")));

    [Fact]
    public void Every_job_block_becomes_a_vacancy_and_footer_links_are_ignored()
    {
        Mail.Kind.ShouldBe(MailKind.VacancyAlert);
        Mail.Vacancies.Select(v => v.ExternalId).ShouldBe(["4100000001", "4100000002"]);
        Mail.Vacancies.ShouldAllBe(v => v.Source == LinkedInMailParser.Source);
    }

    [Fact]
    public void Title_company_and_location_are_read_and_the_alert_header_is_not_a_title()
    {
        var first = Mail.Vacancies[0];
        first.Title.ShouldBe("Junior .NET Developer");
        first.Company.ShouldBe("Acme Soft");
        first.Details.ShouldBe("Lviv, Lviv, Ukraine (Hybrid) · Ця компанія активно наймає персонал");

        var second = Mail.Vacancies[1];
        second.Title.ShouldBe("Senior Full Stack Engineer");
        second.Company.ShouldBe("Example Corp");
        second.Details.ShouldBe("Lviv");
    }

    [Fact]
    public void Only_the_canonical_url_is_kept_without_personal_tracking_tokens()
    {
        Mail.Vacancies[0].Url.ShouldBe("https://www.linkedin.com/jobs/view/4100000001/");
        Mail.Vacancies.ShouldAllBe(v => !v.Url.Contains("otpToken") && !v.Url.Contains("trackingId"));
    }

    [Fact]
    public void Mail_without_job_links_is_other()
    {
        LinkedInMailParser.Parse("Привіт! Подивіться, хто переглядав ваш профіль: https://www.linkedin.com/me/profile-views/").Kind.ShouldBe(MailKind.Other);
    }

    [Fact]
    public void Recommendation_mail_section_headers_with_search_links_are_not_vacancies()
    {
        const string text = """
            Розгорнути пошук

            Рекомендації на основі вашої діяльності.

            Віддалена робота для пошуку вакансій
            https://www.linkedin.com/comm/jobs/search-results/?keywords=x&originToLandingJobPostings=4200000001,4200000002&otpToken=AAA

            Middle .NET Engineer
            Acme Soft
            Lviv
            Переглянути вакансію: https://www.linkedin.com/comm/jobs/view/4200000001/?trackingId=BBB&otpToken=CCC

            ---------------------------------------------------------

            Junior AI/ML Engineer (GenAI, AWS)
            Example Corp
            Ukraine
            Подати заявку за допомогою резюме і профілю
            Переглянути вакансію: https://www.linkedin.com/comm/jobs/view/4200000002/?trackingId=DDD&otpToken=EEE
            """;

        var mail = LinkedInMailParser.Parse(text);

        mail.Vacancies.Select(v => (v.Title, v.Company)).ShouldBe([("Middle .NET Engineer", "Acme Soft"), ("Junior AI/ML Engineer (GenAI, AWS)", "Example Corp")]);
        mail.Vacancies[1].Details.ShouldBe("Ukraine · Подати заявку за допомогою резюме і профілю");
    }
}
