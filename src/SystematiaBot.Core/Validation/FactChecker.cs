using System.Globalization;
using System.Text.RegularExpressions;

namespace SystematiaBot.Core.Validation;

/// <summary>
/// Deterministic check that every number, date, quote and proper name in a generated text
/// can be found in the source text(s). Numbers, dates and quotes missing from the source are errors;
/// names are warnings because Ukrainian declension can defeat the stem match.
/// </summary>
public sealed partial class FactChecker
{
    private static readonly string[] MonthsGenitive =
        ["січня", "лютого", "березня", "квітня", "травня", "червня", "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"];

    private static readonly Dictionary<string, int> NumberWords = BuildNumberWords();

    public IReadOnlyList<UnsupportedFact> Check(string text, IReadOnlyCollection<string> sources)
    {
        var source = Prepare(string.Join("\n\n", sources));
        var sourceLower = source.ToLowerInvariant();
        var sourceDateMatches = ExtractDates(source);
        var sourceDates = sourceDateMatches.Select(d => d.Value).ToHashSet();
        var sourceNumbers = ExtractNumbers(RemoveSpans(source, sourceDateMatches.Select(d => d.Span)))
            .Select(n => n.Value)
            .Concat(sourceDates.SelectMany(d => new[] { d.Day.ToString(CultureInfo.InvariantCulture), d.Month.ToString(CultureInfo.InvariantCulture) }))
            .Concat(ExtractNumberWords(sourceLower).Select(n => n.Value))
            .ToHashSet();
        var sourceStems = TextNormalizer.Words(source).Distinct().Select(w => (Word: w, Stem: Stem(w))).ToList();
        var sourceQuoteText = Collapse(sourceLower);

        var post = Prepare(text);
        var result = new List<UnsupportedFact>();
        var seen = new HashSet<(FactKind, string)>();

        void Add(FactKind kind, string fact, IssueLevel level)
        {
            if (seen.Add((kind, fact.ToLowerInvariant())))
                result.Add(new UnsupportedFact(kind, fact, level));
        }

        var quotes = ExtractQuotes(post);
        foreach (var quote in quotes)
        {
            var normalized = Collapse(quote.Value.ToLowerInvariant()).Trim(' ', '.', ',', '!', '?', '…');
            if (normalized.Length > 0 && !sourceQuoteText.Contains(normalized, StringComparison.Ordinal))
                Add(FactKind.Quote, quote.Text, IssueLevel.Error);
        }

        var dates = ExtractDates(post);
        foreach (var date in dates)
        {
            if (!sourceDates.Contains(date.Value))
                Add(FactKind.Date, date.Text, IssueLevel.Error);
        }

        var withoutDatesAndQuotes = RemoveSpans(post, dates.Select(d => d.Span).Concat(quotes.Select(q => q.Span)));
        foreach (var number in ExtractNumbers(withoutDatesAndQuotes))
        {
            if (!sourceNumbers.Contains(number.Value))
                Add(FactKind.Number, number.Text, IssueLevel.Error);
        }

        foreach (var number in ExtractNumberWords(withoutDatesAndQuotes.ToLowerInvariant()))
        {
            if (!sourceNumbers.Contains(number.Value))
                Add(FactKind.Number, number.Text, IssueLevel.Error);
        }

        foreach (var name in ExtractNames(withoutDatesAndQuotes))
        {
            var lower = name.ToLowerInvariant().Trim('\'', '-');
            if (!sourceStems.Any(s => SameStem(lower, s.Word, s.Stem)))
                Add(FactKind.Name, name, IssueLevel.Warning);
        }

        return result;
    }

    /// <summary>Declension-tolerant stem: the first max(4, len − 2) characters.</summary>
    private static string Stem(string lowerWord) => lowerWord[..Math.Min(lowerWord.Length, Math.Max(4, lowerWord.Length - 2))];

    /// <summary>"Шептицькому" ~ "Шептицький": either word starts with the other's stem (stems shorter than 4 never match).</summary>
    private static bool SameStem(string word, string other, string otherStem)
    {
        var stem = Stem(word);
        return (stem.Length >= 4 || stem == word) && other.StartsWith(stem, StringComparison.Ordinal)
               || otherStem.Length >= 4 && word.StartsWith(otherStem, StringComparison.Ordinal);
    }

    private static string Prepare(string text) => TextNormalizer.Canonicalize(TextNormalizer.StripUrls(text));

    private static string Collapse(string text) => WhitespaceRegex().Replace(QuoteCharsRegex().Replace(text, " "), " ").Trim();

    private static string RemoveSpans(string text, IEnumerable<(int Start, int Length)> spans)
    {
        var chars = text.ToCharArray();
        foreach (var (start, length) in spans)
            Array.Fill(chars, ' ', start, length);
        return new string(chars);
    }

    private sealed record Found<T>(string Text, T Value, (int Start, int Length) Span);

    private static List<Found<string>> ExtractQuotes(string text) =>
        QuoteRegex().Matches(text)
            .Select(m => (Match: m, Group: m.Groups["q"]))
            .Where(x => x.Group.Success && x.Group.Value.Trim().Length > 0)
            .Select(x => new Found<string>(x.Group.Value.Trim(), x.Group.Value, (x.Match.Index, x.Match.Length)))
            .ToList();

    private static List<Found<(int Day, int Month)>> ExtractDates(string text)
    {
        var found = new List<Found<(int, int)>>();
        foreach (Match m in DateWordRegex().Matches(text))
        {
            var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            var month = Array.IndexOf(MonthsGenitive, m.Groups["m"].Value.ToLowerInvariant()) + 1;
            if (day is >= 1 and <= 31)
                found.Add(new(m.Value, (day, month), (m.Index, m.Length)));
        }

        foreach (Match m in DateNumericRegex().Matches(text))
        {
            var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (day is >= 1 and <= 31 && month is >= 1 and <= 12)
                found.Add(new(m.Value, (day, month), (m.Index, m.Length)));
        }

        return found;
    }

    private static List<Found<string>> ExtractNumbers(string text) =>
        NumberRegex().Matches(text)
            .Select(m => new Found<string>(m.Value, NormalizeNumber(m.Value), (m.Index, m.Length)))
            .ToList();

    private static string NormalizeNumber(string value)
    {
        var dotted = value.Replace(',', '.');
        if (!dotted.Contains('.'))
            return dotted.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0";

        return decimal.TryParse(dotted, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("0.############", CultureInfo.InvariantCulture)
            : dotted;
    }

    private static List<Found<string>> ExtractNumberWords(string lowerText) =>
        WordTokenRegex().Matches(lowerText)
            .Where(m => NumberWords.ContainsKey(m.Value))
            .Select(m => new Found<string>(m.Value, NumberWords[m.Value].ToString(CultureInfo.InvariantCulture), (m.Index, m.Length)))
            .ToList();

    /// <summary>Capitalized words that do not start a sentence (names, organizations, places).</summary>
    private static IEnumerable<string> ExtractNames(string text)
    {
        foreach (Match m in CapitalizedRegex().Matches(text))
        {
            var i = m.Index - 1;
            while (i >= 0 && text[i] != '\n' && (char.IsWhiteSpace(text[i]) || text[i] is '"' or '(' or '«' or '“' or '-' or '*'))
                i--;

            var sentenceStart = i < 0 || text[i] is '.' or '!' or '?' or '…' or '\n' or ':' || !char.IsLetterOrDigit(text[i]) && !char.IsPunctuation(text[i]);
            if (!sentenceStart)
                yield return m.Value;
        }
    }

    private static Dictionary<string, int> BuildNumberWords()
    {
        var forms = new (int Value, string[] Words)[]
        {
            (2, ["два", "дві", "двоє", "двох", "двом", "двома", "двоїх"]),
            (3, ["три", "троє", "трьох", "трьом", "трьома"]),
            (4, ["чотири", "четверо", "чотирьох", "чотирьом", "чотирма"]),
            (5, ["п'ять", "п'ятеро", "п'яти", "п'ятьох", "п'ятьом", "п'ятьма"]),
            (6, ["шість", "шестеро", "шести", "шістьох", "шістьом", "шістьма"]),
            (7, ["сім", "семеро", "семи", "сімох", "сімом"]),
            (8, ["вісім", "восьмеро", "восьми", "вісьмох", "вісьмом"]),
            (9, ["дев'ять", "дев'ятеро", "дев'яти", "дев'ятьох", "дев'ятьом", "дев'ятьма"]),
            (10, ["десять", "десятеро", "десяти", "десятьох", "десятьом", "десятьма"])
        };

        return forms.SelectMany(f => f.Words.Select(w => (w, f.Value))).ToDictionary(x => x.w, x => x.Value);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[«»“”„\"]")]
    private static partial Regex QuoteCharsRegex();

    [GeneratedRegex("«(?<q>[^«»]{1,400})»|“(?<q>[^“”]{1,400})”|„(?<q>[^„“”]{1,400})[“”]|\"(?<q>[^\"\\n]{1,400})\"")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"(?<![\d.])(?<d>\d{1,2})\s+(?<m>січня|лютого|березня|квітня|травня|червня|липня|серпня|вересня|жовтня|листопада|грудня)", RegexOptions.IgnoreCase)]
    private static partial Regex DateWordRegex();

    [GeneratedRegex(@"(?<![\d.,])(?<d>\d{1,2})\.(?<m>\d{2})(?:\.(?:\d{4}|\d{2}))?(?![\d,]|\.\d)")]
    private static partial Regex DateNumericRegex();

    [GeneratedRegex(@"(?<![\p{L}\d])\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[\p{L}][\p{L}']*")]
    private static partial Regex WordTokenRegex();

    [GeneratedRegex(@"(?<![\p{L}'\-])\p{Lu}[\p{L}'\-]+")]
    private static partial Regex CapitalizedRegex();
}
