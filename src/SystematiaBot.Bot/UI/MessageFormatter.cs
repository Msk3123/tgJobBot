using System.Globalization;
using System.Net;
using System.Text;

namespace SystematiaBot.Bot.UI;

public static class MessageFormatter
{
    public const int MaxMessageLength = 4096;
    public const string DemoBanner = "🧪 <b>Демо-режим</b>";

    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>HTML-escapes dynamic text for ParseMode.Html.</summary>
    public static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string Number(int value) => value.ToString("N0", Uk).Replace(' ', ' ');

    /// <summary>Ukrainian plural: 1 сторінка, 2 сторінки, 5 сторінок.</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var mod100 = Math.Abs(n) % 100;
        var mod10 = mod100 % 10;
        var word = mod100 is >= 11 and <= 14 ? many : mod10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
        return $"{Number(n)} {word}";
    }

    public static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    /// <summary>Splits a long message by paragraphs (then lines, then hard cuts) into chunks within Telegram's limit.</summary>
    public static IReadOnlyList<string> Split(string text, int max = MaxMessageLength)
    {
        if (text.Length <= max)
            return [text];

        var chunks = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
                chunks.Add(current.ToString().TrimEnd());
            current.Clear();
        }

        foreach (var paragraph in text.Split("\n\n"))
        {
            foreach (var piece in SplitPiece(paragraph, max))
            {
                var separatorLength = current.Length > 0 ? 2 : 0;
                if (current.Length + separatorLength + piece.Length > max)
                    Flush();
                if (current.Length > 0)
                    current.Append("\n\n");
                current.Append(piece);
            }
        }

        Flush();
        return chunks;
    }

    private static IEnumerable<string> SplitPiece(string paragraph, int max)
    {
        if (paragraph.Length <= max)
        {
            yield return paragraph;
            yield break;
        }

        var line = new StringBuilder();
        foreach (var part in paragraph.Split('\n'))
        {
            if (line.Length + part.Length + 1 > max && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }

            var rest = part;
            while (rest.Length > max)
            {
                yield return rest[..max];
                rest = rest[max..];
            }

            if (line.Length > 0)
                line.Append('\n');
            line.Append(rest);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }
}
