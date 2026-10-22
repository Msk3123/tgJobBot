using Shouldly;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Validation;

namespace SystematiaBot.Tests.Validation;

public class TextValidatorTests
{
    private const string Url = "https://t.me/s/example/1";
    private readonly TextValidator _validator = new(new FactChecker());

    private static string Words(int count, int paragraphs = 2)
    {
        var words = Enumerable.Range(0, count).Select(i => $"слово{(char)('а' + i % 30)}").ToList();
        var perParagraph = (int)Math.Ceiling(count / (double)paragraphs);
        return string.Join("\n\n", words.Chunk(perParagraph).Select(c => string.Join(' ', c)));
    }

    private ValidationReport Social(string title, string body) => _validator.Validate(PostKind.Social, title, body, Url, [body]);

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public void Social_title_limit_is_100_chars(int length, bool hasError)
    {
        var report = Social(new string('а', length), Words(150));

        report.TitleLength.ShouldBe(length);
        report.Issues.Any(i => i.Code == "TITLE_LENGTH").ShouldBe(hasError);
    }

    [Fact]
    public void Title_length_is_measured_after_trim()
    {
        Social("  " + new string('а', 100) + "  ", Words(150)).Issues.ShouldNotContain(i => i.Code == "TITLE_LENGTH");
    }

    [Theory]
    [InlineData(99, true)]
    [InlineData(100, false)]
    [InlineData(200, false)]
    [InlineData(201, true)]
    public void Social_body_must_have_100_to_200_words(int count, bool hasError)
    {
        var report = Social("Заголовок", Words(count));

        report.WordCount.ShouldBe(count);
        report.Issues.Any(i => i.Code == "WORD_COUNT").ShouldBe(hasError);
    }

    [Fact]
    public void Emoji_only_tokens_and_urls_are_not_counted_as_words()
    {
        TextValidator.CountWords("Пожежа 🔥 у місті — https://example.com 👇").ShouldBe(3);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Social_body_must_have_2_to_3_paragraphs(int paragraphs, bool hasError)
    {
        var report = Social("Заголовок", Words(120, paragraphs));

        report.ParagraphCount.ShouldBe(paragraphs);
        report.Issues.Any(i => i.Code == "PARAGRAPHS").ShouldBe(hasError);
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    [InlineData(85, false)]
    [InlineData(86, true)]
    public void Article_title_must_be_50_to_85_chars(int length, bool hasError)
    {
        var body = Words(400, 6);
        var report = _validator.Validate(PostKind.Article, new string('а', length), body, Url, [body]);

        report.Issues.Any(i => i.Code == "TITLE_LENGTH").ShouldBe(hasError);
        report.Issues.ShouldNotContain(i => i.Code == "PARAGRAPHS");
    }

    [Fact]
    public void Missing_source_link_is_an_error()
    {
        var body = Words(120);
        _validator.Validate(PostKind.Social, "Заголовок", body, null, [body])
            .Issues.ShouldContain(i => i.Code == "NO_SOURCE_LINK" && i.Level == IssueLevel.Error);
    }

    [Fact]
    public void Long_run_copied_from_source_is_an_error()
    {
        const string source = "Сьогодні вранці на вулиці Шевченка водій легковика не впорався з керуванням і в'їхав у стовп біля зупинки громадського транспорту";
        var body = "Інший вступ. " + source + "\n\nЩе абзац.";

        _validator.Validate(PostKind.Social, "Заголовок", body, Url, [source])
            .Issues.ShouldContain(i => i.Code == "COPIED");
    }

    [Fact]
    public void Twelve_shared_words_are_still_allowed()
    {
        const string source = "один два три чотири п'ять шість сім вісім дев'ять десять одинадцять дванадцять тринадцять";
        var body = "інше " + string.Join(' ', source.Split(' ').Take(12)) + " кінець";

        TextValidator.FindCopiedFragment(body, [source], TextLimits.MaxCopiedRun).ShouldBeNull();
    }
}
