using System.Text;
using System.Text.RegularExpressions;

namespace SystematiaBot.Core.Validation;

/// <summary>Shared text normalization for validation, fact checking and deduplication.</summary>
public static partial class TextNormalizer
{
    public static string StripUrls(string text) => UrlRegex().Replace(text, " ");

    /// <summary>Unifies apostrophes, dashes, spaces and digit grouping ("1 200" → "1200"); keeps case and punctuation.</summary>
    public static string Canonicalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '’' or 'ʼ' or '`' or '‘' => '\'',
                ' ' or ' ' or ' ' => ' ',
                '–' or '—' or '−' => '-',
                _ => ch
            });
        }

        return DigitGroupRegex().Replace(sb.ToString(), "");
    }

    /// <summary>Lowercase words (letters, digits, apostrophes, inner hyphens) without URLs, punctuation and emoji.</summary>
    public static List<string> Words(string text)
    {
        var canonical = Canonicalize(StripUrls(text)).ToLowerInvariant();
        return WordRegex().Matches(canonical).Select(m => m.Value.Trim('\'', '-')).Where(w => w.Length > 0).ToList();
    }

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?<=\d) (?=\d{3}(?!\d))")]
    private static partial Regex DigitGroupRegex();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'\-]*")]
    private static partial Regex WordRegex();
}
