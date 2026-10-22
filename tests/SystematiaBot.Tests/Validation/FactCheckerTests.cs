using Shouldly;
using SystematiaBot.Core.Validation;

namespace SystematiaBot.Tests.Validation;

public class FactCheckerTests
{
    private const string Source =
        "20 жовтня у Шептицькому на вулиці Шевченка сталася ДТП. Постраждали двоє дітей, 47-річного водія затримали. " +
        "Ремонт коштуватиме 1 200 000 гривень, повідомив міський голова Андрій Залівський: «Ми відновимо зупинку до кінця місяця».";

    private readonly FactChecker _checker = new();

    private IReadOnlyList<UnsupportedFact> Check(string text) => _checker.Check(text, [Source]);

    [Fact]
    public void Number_present_in_source_passes()
    {
        Check("Водію 47 років, його затримали.").ShouldBeEmpty();
    }

    [Fact]
    public void Number_absent_from_source_is_error()
    {
        var facts = Check("Водію 48 років.");

        facts.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            f => f.Kind.ShouldBe(FactKind.Number),
            f => f.Text.ShouldBe("48"),
            f => f.Level.ShouldBe(IssueLevel.Error));
    }

    [Fact]
    public void Number_with_suffix_is_extracted()
    {
        Check("За кермом був 52-річний чоловік.").ShouldContain(f => f.Text == "52");
    }

    [Fact]
    public void Digit_grouping_is_ignored()
    {
        Check("Ремонт коштуватиме 1200000 гривень.").ShouldBeEmpty();
    }

    [Fact]
    public void Number_word_in_source_matches_digit_in_text()
    {
        Check("Постраждали 2 дитини.").ShouldBeEmpty();
    }

    [Fact]
    public void Number_word_in_text_matches_digit_in_source()
    {
        _checker.Check("Постраждали троє людей.", ["Постраждали 3 людини."]).ShouldBeEmpty();
    }

    [Fact]
    public void Wrong_number_word_is_error()
    {
        Check("Постраждали троє дітей.").ShouldContain(f => f.Kind == FactKind.Number && f.Text == "троє");
    }

    [Fact]
    public void Date_in_words_matches_numeric_date_in_source()
    {
        _checker.Check("Аварія сталася 20 жовтня.", ["Аварія 20.10.2026 біля школи."]).ShouldBeEmpty();
    }

    [Fact]
    public void Wrong_date_is_error()
    {
        Check("Це сталося 21 жовтня.").ShouldContain(f => f.Kind == FactKind.Date && f.Level == IssueLevel.Error);
    }

    [Fact]
    public void Declined_name_matches_by_stem()
    {
        _checker.Check("Новина про Шептицький район.", ["Сьогодні у Шептицькому відкрили школу."]).ShouldBeEmpty();
        _checker.Check("Подія сталася у Шептицькому.", ["Шептицький готується до зими."]).ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_name_is_warning_only()
    {
        var facts = Check("Про це повідомив Петро Іваненко.");

        facts.Select(f => f.Text).ShouldBe(["Петро", "Іваненко"]);
        facts.ShouldAllBe(f => f.Kind == FactKind.Name && f.Level == IssueLevel.Warning);
    }

    [Fact]
    public void Word_at_sentence_start_is_not_a_name()
    {
        Check("Водія затримали. Поліція працює.").ShouldBeEmpty();
    }

    [Fact]
    public void Quote_from_source_passes()
    {
        Check("Голова пообіцяв: «Ми відновимо зупинку до кінця місяця».").ShouldBeEmpty();
    }

    [Fact]
    public void Quote_not_in_source_is_error()
    {
        Check("Голова сказав: «Винних покарають».")
            .ShouldContain(f => f.Kind == FactKind.Quote && f.Text == "Винних покарають" && f.Level == IssueLevel.Error);
    }
}
