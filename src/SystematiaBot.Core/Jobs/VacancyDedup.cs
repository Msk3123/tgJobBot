using System.Text;

namespace SystematiaBot.Core.Jobs;

/// <summary>
/// Spots the same vacancy published on several boards. The key is "company|title" reduced to letters and digits,
/// with Cyrillic look-alikes folded to Latin: DOU titles often contain a Cyrillic "С#" that looks identical to "C#".
/// </summary>
public static class VacancyDedup
{
    private static readonly Dictionary<char, char> Homoglyphs = new()
    {
        ['а'] = 'a', ['в'] = 'b', ['е'] = 'e', ['к'] = 'k', ['м'] = 'm', ['н'] = 'h', ['о'] = 'o',
        ['р'] = 'p', ['с'] = 'c', ['т'] = 't', ['у'] = 'y', ['х'] = 'x', ['і'] = 'i'
    };

    public static string? Key(string? company, string title)
    {
        var c = Normalize(company);
        var t = Normalize(title);
        return c.Length == 0 || t.Length == 0 ? null : $"{c}|{t}";
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var raw in value.ToLowerInvariant())
        {
            var ch = Homoglyphs.GetValueOrDefault(raw, raw);
            if (char.IsLetterOrDigit(ch) || ch == '#' || ch == '+')
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
