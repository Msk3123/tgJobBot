using System.Net.Http;
using Shouldly;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Infrastructure.Jobs;

namespace SystematiaBot.Tests.Jobs;

public class DjinniApplyPageTests
{
    private const string Header = """<a href="/my/profile/">Профіль</a><a href="/my/inbox/">Чати</a><a href="/logout">Вийти</a>""";

    // Mirrors the real form: hidden "apply", textarea, two unchecked checkboxes, a file input, an empty number, the submit button and the CSRF token.
    private const string Form = """
        <form method="post" enctype="multipart/form-data" class="mt-5 js-inbox-reply-form" id="apply_form">
          <input type="hidden" name="apply" value="true">
          <textarea name="message" id="message"></textarea>
          <input type="checkbox" name="save_msg_template" id="save_msg_template">
          <input type="text" name="msg_template_name" id="msg_template_name">
          <input type="file" name="cv_file" id="cv_file_input" class="js-cv-file-input">
          <input type="checkbox" name="save_profile_cv" id="save_profile_cv">
          <input type="number" name="salary_changed">
          <button type="button">Скасувати зміну</button>
          <button type="submit" name="job_apply" id="job_apply">Відгукнутися</button>
          <input type="hidden" name="csrfmiddlewaretoken" value="tok123">
        </form>
        """;

    [Fact]
    public void Logged_in_page_with_form_yields_what_a_browser_would_submit()
    {
        var page = DjinniApplyPage.Parse($"<html><body>{Header}{Form}</body></html>");

        page.Problem.ShouldBeNull();
        page.FileField.ShouldBe("cv_file");
        page.Fields.Select(f => f.Key).ShouldBe(["apply", "message", "msg_template_name", "salary_changed", "job_apply", "csrfmiddlewaretoken"]);
        page.Fields.First(f => f.Key == "csrfmiddlewaretoken").Value.ShouldBe("tok123");
        page.Fields.First(f => f.Key == "apply").Value.ShouldBe("true");
    }

    [Fact]
    public async Task Built_form_carries_the_message_and_the_cv_and_skips_unchecked_boxes()
    {
        var page = DjinniApplyPage.Parse($"<html><body>{Header}{Form}</body></html>");

        using var content = page.BuildForm("Привіт, я .NET розробник", new ApplyAttachment("CV.pdf", [0x25, 0x50, 0x44, 0x46]));
        var body = await content.ReadAsStringAsync();

        body.ShouldContain("name=message");
        body.ShouldContain("Привіт, я .NET розробник");
        body.ShouldContain("name=cv_file; filename=CV.pdf");
        body.ShouldContain("application/pdf");
        body.ShouldNotContain("save_profile_cv");
        body.ShouldNotContain("save_msg_template");
    }

    [Fact]
    public void Anonymous_page_means_the_session_expired()
    {
        var page = DjinniApplyPage.Parse($"""<html><body><a href="/login?next=/jobs/1-x/">Увійти</a>{Form}</body></html>""");

        page.Problem.ShouldNotBeNull().Outcome.ShouldBe(ApplyOutcome.SessionExpired);
    }

    [Fact]
    public void Page_without_form_but_with_a_conversation_means_already_applied()
    {
        var page = DjinniApplyPage.Parse($"""<html><body>{Header}<a href="/my/inbox/123456/">Перейти до чату</a></body></html>""");

        page.Problem.ShouldBe(new ApplyResult(ApplyOutcome.AlreadyApplied, "https://djinni.co/my/inbox/123456/"));
    }

    [Fact]
    public void Page_without_form_and_conversation_is_not_available()
    {
        DjinniApplyPage.Parse($"<html><body>{Header}<p>Ця вакансія зараз неактивна</p></body></html>")
            .Problem.ShouldNotBeNull().Outcome.ShouldBe(ApplyOutcome.NotAvailable);
    }

    [Fact]
    public void Form_errors_are_read_from_the_form_only()
    {
        var withError = Form.Replace("""<textarea name="message" id="message"></textarea>""",
            """<textarea name="message" id="message"></textarea><ul class="errorlist"><li>Додайте LinkedIn або резюме</li></ul>""");

        var page = DjinniApplyPage.Parse($"""<html><body>{Header}<div class="alert alert-danger d-none">Шаблон помилки</div>{withError}</body></html>""");

        page.Errors.ShouldBe(["Додайте LinkedIn або резюме"]);
    }
}
