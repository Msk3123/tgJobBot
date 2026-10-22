using System.Text.RegularExpressions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Validation;

/// <summary>Deterministic (no AI) validation of a generated post or article: limits, source link, copying and facts.</summary>
public sealed partial class TextValidator(FactChecker factChecker)
{
    public ValidationReport Validate(PostKind kind, string title, string body, string? sourceUrl, IReadOnlyCollection<string> sourceTexts)
    {
        var limits = TextLimits.For(kind);
        var issues = new List<ValidationIssue>();

        var titleLength = title.Trim().Length;
        if (titleLength < limits.TitleMin || titleLength > limits.TitleMax)
        {
            issues.Add(new("TITLE_LENGTH", IssueLevel.Error,
                limits.TitleMin > 1
                    ? $"Заголовок має {titleLength} символів, потрібно {limits.TitleMin}–{limits.TitleMax}"
                    : $"Заголовок має {titleLength} символів, максимум {limits.TitleMax}"));
        }

        var wordCount = CountWords(body);
        if (wordCount < limits.WordsMin || wordCount > limits.WordsMax)
            issues.Add(new("WORD_COUNT", IssueLevel.Error, $"Текст має {wordCount} слів, потрібно {limits.WordsMin}–{limits.WordsMax}"));

        var paragraphs = CountParagraphs(body);
        if (limits.ParagraphsMin is { } pMin && limits.ParagraphsMax is { } pMax && (paragraphs < pMin || paragraphs > pMax))
            issues.Add(new("PARAGRAPHS", IssueLevel.Error, $"Текст має {paragraphs} абзаців, потрібно {pMin}–{pMax} (розділені порожнім рядком)"));

        var hasLink = !string.IsNullOrWhiteSpace(sourceUrl) || UrlRegex().IsMatch(body);
        if (!hasLink)
            issues.Add(new("NO_SOURCE_LINK", IssueLevel.Error, "Немає посилання на джерело"));

        // Verbatim quotes are required to be exact, so they do not count as copying.
        var copied = FindCopiedFragment(QuoteRegex().Replace(body, " "), sourceTexts, TextLimits.MaxCopiedRun);
        if (copied is not null)
            issues.Add(new("COPIED", IssueLevel.Error, $"Фрагмент скопійовано з джерела, перепиши своїми словами: «{copied}»"));

        var facts = factChecker.Check($"{title.Trim()}\n{body}", sourceTexts);
        foreach (var fact in facts)
        {
            var message = fact.Level == IssueLevel.Error
                ? $"«{fact.Text}» — немає в джерелі, прибери або виправ"
                : $"«{fact.Text}» — не знайдено в джерелі (можливо, відмінювання)";
            issues.Add(new($"FACT_{fact.Kind.ToString().ToUpperInvariant()}", fact.Level, message));
        }

        return new ValidationReport
        {
            TitleLength = titleLength,
            TitleMin = limits.TitleMin,
            TitleMax = limits.TitleMax,
            WordCount = wordCount,
            WordsMin = limits.WordsMin,
            WordsMax = limits.WordsMax,
            ParagraphCount = paragraphs,
            ParagraphsMin = limits.ParagraphsMin,
            ParagraphsMax = limits.ParagraphsMax,
            HasSourceLink = hasLink,
            CopiedFragment = copied,
            Issues = issues,
            UnsupportedFacts = facts.ToList()
        };
    }

    /// <summary>Whitespace-separated tokens that contain at least one letter or digit (emoji-only tokens and URLs are skipped).</summary>
    public static int CountWords(string body) =>
        TextNormalizer.StripUrls(body)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Any(char.IsLetterOrDigit));

    public static int CountParagraphs(string body) =>
        ParagraphSplitRegex().Split(body.Replace("\r\n", "\n").Trim())
            .Count(p => !string.IsNullOrWhiteSpace(p));

    /// <summary>Returns the first run of more than <paramref name="maxRun"/> consecutive words that also appears in a source.</summary>
    public static string? FindCopiedFragment(string body, IEnumerable<string> sources, int maxRun)
    {
        var window = maxRun + 1;
        var bodyWords = TextNormalizer.Words(body);
        if (bodyWords.Count < window)
            return null;

        var sourceShingles = new HashSet<string>();
        foreach (var source in sources)
        {
            var words = TextNormalizer.Words(source);
            for (var i = 0; i + window <= words.Count; i++)
                sourceShingles.Add(string.Join(' ', words.Skip(i).Take(window)));
        }

        for (var i = 0; i + window <= bodyWords.Count; i++)
        {
            var shingle = string.Join(' ', bodyWords.Skip(i).Take(window));
            if (sourceShingles.Contains(shingle))
                return shingle;
        }

        return null;
    }

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex ParagraphSplitRegex();

    [GeneratedRegex("«[^«»]*»|“[^“”]*”|\"[^\"\n]*\"")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
