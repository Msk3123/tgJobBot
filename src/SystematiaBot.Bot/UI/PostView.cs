using System.Text;
using System.Text.Json;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Validation;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.UI;

public static class PostView
{
    private static readonly HashSet<string> LimitCodes = ["TITLE_LENGTH", "WORD_COUNT", "PARAGRAPHS"];

    public static ValidationReport? ReportOf(Post post)
    {
        if (string.IsNullOrEmpty(post.ValidationJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ValidationReport>(post.ValidationJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Post text as it should be published (title, body, source link) plus the validation block.</summary>
    public static string Render(Post post, string header, ValidationReport? report)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header).AppendLine();
        sb.AppendLine($"<b>{Escape(post.Title)}</b>").AppendLine();
        sb.AppendLine(Escape(post.Body)).AppendLine();
        sb.AppendLine($"🔗 Джерело: {Escape(post.NewsItem?.Url)}");
        if (report is not null)
            sb.AppendLine("──────").Append(RenderReport(report));
        return sb.ToString().TrimEnd();
    }

    public static string RenderReport(ValidationReport r)
    {
        static string Mark(bool ok) => ok ? "✅" : "❌";

        var titleOk = r.TitleLength >= r.TitleMin && r.TitleLength <= r.TitleMax;
        var wordsOk = r.WordCount >= r.WordsMin && r.WordCount <= r.WordsMax;
        var parts = new List<string>
        {
            r.TitleMin > 1 ? $"{Mark(titleOk)} Заголовок {r.TitleLength} ({r.TitleMin}–{r.TitleMax})" : $"{Mark(titleOk)} Заголовок {r.TitleLength}/{r.TitleMax}",
            $"{Mark(wordsOk)} {Plural(r.WordCount, "слово", "слова", "слів")}"
        };
        if (r.ParagraphsMin is { } min && r.ParagraphsMax is { } max)
            parts.Add($"{Mark(r.ParagraphCount >= min && r.ParagraphCount <= max)} {Plural(r.ParagraphCount, "абзац", "абзаци", "абзаців")}");

        var sb = new StringBuilder(string.Join(" · ", parts));
        foreach (var issue in r.Issues.Where(i => !LimitCodes.Contains(i.Code)))
            sb.Append('\n').Append(issue.Level == IssueLevel.Error ? "❌ " : "⚠️ ").Append(Escape(issue.Message));

        if (!r.HasErrors && r.Issues.Count == 0)
            sb.Append("\n✅ Факти: усе знайдено в джерелі");
        return sb.ToString();
    }

    /// <summary>Plain text for copying / export: title, body and the source link.</summary>
    public static string PlainText(Post post) => $"{post.Title}\n\n{post.Body}\n\nДжерело: {post.NewsItem?.Url}";
}
