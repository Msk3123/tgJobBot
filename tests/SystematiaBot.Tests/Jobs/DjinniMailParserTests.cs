using Shouldly;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class DjinniMailParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Alert_yields_every_vacancy_with_canonical_url_title_company_and_details()
    {
        var mail = DjinniMailParser.Parse("Нові вакансії за вашим профілем", Fixture("djinni_alert.txt"));

        mail.Kind.ShouldBe(MailKind.VacancyAlert);
        mail.Vacancies.Count.ShouldBe(2);

        var first = mail.Vacancies[0];
        first.ExternalId.ShouldBe("786559");
        first.Url.ShouldBe("https://djinni.co/jobs/786559-creatio-developer/");
        first.Title.ShouldBe("Creatio Developer");
        first.Company.ShouldBe("Aurocraft");
        first.Salary.ShouldBe("$$");
        first.Details.ShouldBe("1 рік досвіду · B1 – Середній · Тільки віддалено · Україна");
        first.Snippet!.ShouldStartWith("AuroCraft — продуктова ІТ-компанія, резидент Дія.City.");
        first.Snippet.ShouldNotContain("Детальніше");
    }

    [Fact]
    public void Alert_title_wrapped_over_two_lines_is_joined()
    {
        var second = DjinniMailParser.Parse("Нові вакансії за вашим профілем", Fixture("djinni_alert.txt")).Vacancies[1];

        second.ExternalId.ShouldBe("812345");
        second.Title.ShouldBe("Junior C# / .NET Developer (with basic Delphi knowledge)");
        second.Company.ShouldBe("AlphaDevs");
        second.Salary.ShouldBe("до $1500");
        second.Details.ShouldBe("2 роки досвіду · B1 – Середній · Тільки віддалено · Весь світ");
    }

    [Fact]
    public void Polite_no_from_a_recruiter_is_a_rejection_with_its_vacancy_and_inbox_link()
    {
        var mail = DjinniMailParser.Parse("Повідомлення від Олена, ideal.com.ua", Fixture("djinni_message_rejection.txt"));

        mail.Kind.ShouldBe(MailKind.Rejection);
        mail.Company.ShouldBe("IDEAL");
        mail.Contact.ShouldBe("Олена");
        mail.InboxUrl.ShouldBe("https://djinni.co/my/inbox/11111111/");
        mail.Text.ShouldBe("Вітаємо!\n\nДякуємо за інтерес до нашої компанії! На жаль, ваш досвід наразі не зовсім відповідає наших завдань.\n\nБажаємо вам успіхів у пошуку нових можливостей!");

        var vacancy = mail.Vacancies.ShouldHaveSingleItem();
        vacancy.ExternalId.ShouldBe("850417");
        vacancy.Title.ShouldBe(".NET Developer");
        vacancy.Company.ShouldBe("IDEAL");
        vacancy.Salary.ShouldBe("до $1000");
    }

    [Fact]
    public void Djinni_rejection_notice_has_company_contact_and_body()
    {
        var mail = DjinniMailParser.Parse("Роботодавець зазначив, що ви не підійшли один одному", Fixture("djinni_rejection_notice.txt"));

        mail.Kind.ShouldBe(MailKind.Rejection);
        mail.Company.ShouldBe("Cacao Mobile");
        mail.Contact.ShouldBe("Ivan");
        mail.Text!.ShouldStartWith("Dear Candidate, Thank you for your application.");
        mail.Text.ShouldNotContain("Відповісти");
        mail.Vacancies.ShouldHaveSingleItem().Title.ShouldBe("Web Funnels Builder");
    }

    [Fact]
    public void Invitation_is_a_recruiter_message()
    {
        var mail = DjinniMailParser.Parse("Повідомлення від Марта, Example Soft", Fixture("djinni_message_invite.txt"));

        mail.Kind.ShouldBe(MailKind.RecruiterMessage);
        mail.Contact.ShouldBe("Марта");
        mail.Company.ShouldBe("Example Soft");
        mail.Text.ShouldBe("Привіт! Ваш профіль нам цікавий. Коли вам зручно поспілкуватися 20 хвилин цього тижня?");
        mail.Vacancies.ShouldHaveSingleItem().ExternalId.ShouldBe("860001");
    }

    [Fact]
    public void Automatic_thank_you_is_an_auto_reply()
    {
        DjinniMailParser.Parse("Повідомлення від Anna, Example Soft", Fixture("djinni_message_autoreply.txt"))
            .Kind.ShouldBe(MailKind.AutoReply);
    }

    [Theory]
    [InlineData("Статистика Джина за тиждень")]
    [InlineData("Конкуренція по технічних позиціях виросла на 20-30%")]
    public void Newsletters_are_ignored(string subject)
    {
        var mail = DjinniMailParser.Parse(subject, "Some text <https://djinni.co/jobs/1-x/>");

        mail.Kind.ShouldBe(MailKind.Other);
        mail.Vacancies.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://djinni.co/jobs/816423-junior-net-developer/?ref=email&utm_source=x", "816423", "https://djinni.co/jobs/816423-junior-net-developer/")]
    [InlineData("дивись djinni.co/jobs/42-net-dev", "42", "https://djinni.co/jobs/42-net-dev/")]
    public void Job_urls_are_normalized(string input, string id, string url)
    {
        DjinniMailParser.TryParseJobUrl(input, out var externalId, out var canonical).ShouldBeTrue();
        externalId.ShouldBe(id);
        canonical.ShouldBe(url);
    }

    [Fact]
    public void Non_job_urls_are_rejected()
    {
        DjinniMailParser.TryParseJobUrl("https://djinni.co/my/inbox/1/", out _, out _).ShouldBeFalse();
    }
}
