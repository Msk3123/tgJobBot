using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SystematiaBot.Core.Validation;

namespace SystematiaBot.Core.News;

/// <summary>
/// Groups texts about the same event. Two texts are duplicates when their 5-word shingles have Jaccard ≥ 0.5
/// (near-copies), or — to catch rewrites of one event by different sources — when their word stems overlap strongly.
/// </summary>
public sealed partial class DedupService
{
    public const int ShingleSize = 5;
    public const double ShingleThreshold = 0.5;
    public const double StemOverlapThreshold = 0.35;
    public const int MinStemsForFuzzyMatch = 8;
    public const int MinDocumentsForIdf = 10;

    private static readonly HashSet<string> StopStems =
    [
        "який", "яка", "яке", "які", "також", "через", "після", "перед", "проте", "тому", "щодо", "серед", "більш", "може",
        "буде", "будут", "було", "були", "була", "свої", "свою", "цього", "цьому", "цієї", "наші", "ваші", "сього", "вчора",
        "завтр", "зараз", "облас", "львів", "украї", "повід", "інфор", "район", "міськ", "грома"
    ];

    public sealed record Text(int Id, string Value, DateTimeOffset PublishedAt);

    /// <summary>
    /// Returns clusters (lists of ids) of size ≥ 2. Leader clustering: texts are visited from the fullest one and each
    /// joins the first cluster whose leader it duplicates, so A~B and B~C never chain unrelated A and C together.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Cluster(IReadOnlyList<Text> texts, TimeSpan? maxTimeGap = null)
    {
        var gap = maxTimeGap ?? TimeSpan.FromDays(3);
        var features = texts
            .OrderByDescending(t => t.Value.Length)
            .Select(t => new Features(t.Id, t.PublishedAt, Shingles(t.Value), Stems(t.Value), Dates(t.Value)))
            .ToList();
        var weights = StemWeights(features.Select(f => f.Stems).ToList());
        var clusters = new List<(Features Leader, List<int> Members)>();

        foreach (var item in features)
        {
            var match = clusters.FirstOrDefault(c => (c.Leader.PublishedAt - item.PublishedAt).Duration() <= gap && IsDuplicate(c.Leader, item, weights));
            if (match.Members is not null)
                match.Members.Add(item.Id);
            else
                clusters.Add((item, [item.Id]));
        }

        return clusters.Where(c => c.Members.Count > 1).Select(c => (IReadOnlyList<int>)c.Members).ToList();
    }

    private sealed record Features(int Id, DateTimeOffset PublishedAt, HashSet<string> Shingles, HashSet<string> Stems, HashSet<string> Dates);

    public bool AreDuplicates(string a, string b) =>
        IsDuplicate(new Features(0, default, Shingles(a), Stems(a), Dates(a)), new Features(0, default, Shingles(b), Stems(b), Dates(b)), null);

    /// <summary>
    /// IDF-weighted overlap coefficient of stems: shared rare words (names, places, numbers) count much more
    /// than words every local post contains ("сесія", "депутати"). Weights are uniform for small batches.
    /// </summary>
    public static double StemSimilarity(HashSet<string> a, HashSet<string> b, IReadOnlyDictionary<string, double>? weights)
    {
        double Weight(string stem) => weights?.GetValueOrDefault(stem, 1.0) ?? 1.0;

        var common = a.Where(b.Contains).Sum(Weight);
        var smaller = Math.Min(a.Sum(Weight), b.Sum(Weight));
        return smaller <= 0 ? 0 : common / smaller;
    }

    private static Dictionary<string, double>? StemWeights(IReadOnlyList<HashSet<string>> documents)
    {
        if (documents.Count < MinDocumentsForIdf)
            return null;

        return documents.SelectMany(d => d)
            .GroupBy(s => s)
            .ToDictionary(g => g.Key, g => Math.Log(1.0 + documents.Count / (double)g.Count()));
    }

    private static bool IsDuplicate(Features a, Features b, IReadOnlyDictionary<string, double>? weights)
    {
        // Same template, different dates ("на щиті 20 жовтня" vs "23 жовтня") — different events.
        if (a.Dates.Count > 0 && b.Dates.Count > 0 && !a.Dates.Overlaps(b.Dates))
            return false;

        if (Jaccard(a.Shingles, b.Shingles) >= ShingleThreshold)
            return true;

        if (a.Stems.Count < MinStemsForFuzzyMatch || b.Stems.Count < MinStemsForFuzzyMatch)
            return false;

        return StemSimilarity(a.Stems, b.Stems, weights) >= StemOverlapThreshold;
    }

    /// <summary>Day-month mentions like "20 жовтня" normalized to "20.10".</summary>
    public static HashSet<string> Dates(string text) =>
        DateRegex().Matches(text)
            .Select(m => $"{int.Parse(m.Groups["d"].Value)}.{Array.IndexOf(Months, m.Groups["m"].Value.ToLowerInvariant()) + 1}")
            .ToHashSet();

    private static readonly string[] Months =
        ["січня", "лютого", "березня", "квітня", "травня", "червня", "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"];

    [GeneratedRegex(@"(?<!\d)(?<d>[1-9]|[12]\d|3[01])\s+(?<m>січня|лютого|березня|квітня|травня|червня|липня|серпня|вересня|жовтня|листопада|грудня)", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    public static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var common = a.Count(b.Contains);
        return common / (double)(a.Count + b.Count - common);
    }

    public static HashSet<string> Shingles(string text)
    {
        var words = Normalize(text);
        if (words.Count < ShingleSize)
            return words.Count == 0 ? [] : [string.Join(' ', words)];

        var result = new HashSet<string>();
        for (var i = 0; i + ShingleSize <= words.Count; i++)
            result.Add(string.Join(' ', words.Skip(i).Take(ShingleSize)));
        return result;
    }

    /// <summary>Declension-tolerant content stems (first 5 letters of words with 4+ letters, plus numbers).</summary>
    public static HashSet<string> Stems(string text) =>
        Normalize(text)
            .Where(w => w.Length >= 4 || w.All(char.IsDigit))
            .Select(w => w.All(char.IsDigit) ? w : w[..Math.Min(5, w.Length)])
            .Where(s => !StopStems.Contains(s))
            .ToHashSet();

    /// <summary>Lowercase words without URLs, punctuation and emoji.</summary>
    public static List<string> Normalize(string text) => TextNormalizer.Words(text).Select(w => w.Replace("'", "")).ToList();

    public static string ContentHash(string text)
    {
        var normalized = string.Join(' ', Normalize(text));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
