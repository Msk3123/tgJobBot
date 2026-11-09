using System.Text;
using System.Text.RegularExpressions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

public sealed record ParsedVacancy(string ExternalId, string Url, string Title, string? Company, string? Salary, string? Details, string? Snippet, string Source = DjinniMailParser.Source);

public sealed record ParsedMail(
    MailKind Kind,
    string? Company,
    string? Contact,
    string? Text,
    string? InboxUrl,
    IReadOnlyList<ParsedVacancy> Vacancies);

/// <summary>
/// Parses the text/plain part of Djinni e-mails. The plain part carries direct djinni.co links
/// (the HTML part only has Mandrill click-tracking redirects), and its layout is stable:
/// links are written as "&lt;https://…&gt;", indented lines start a block, unindented lines are word-wrap continuations.
/// Everything here is best effort: the vacancy page fetched afterwards is the source of truth for title and company.
/// </summary>
public static partial class DjinniMailParser
{
    public const string Source = "djinni";

    private static readonly string[] RejectionMarkers =
    [
        "на жаль", "нажаль", "не відповідає", "не підходить", "не підійш", "іншого кандидата", "з іншим досвідом",
        "вакансію закрито", "вакансія закрита", "к сожалению", "unfortunately", "not a fit", "not the right fit",
        "don't meet", "do not meet", "other candidates", "another candidate", "decided not to", "moving forward with other",
        "position has been filled"
    ];

    private static readonly string[] AutoReplyMarkers = ["автоматична відповідь", "автоматический ответ", "automatic reply", "auto-reply", "autoreply"];

    public static ParsedMail Parse(string subject, string plainText)
    {
        var text = plainText.Replace("\r\n", "\n");

        if (subject.StartsWith("Нові вакансії", StringComparison.OrdinalIgnoreCase))
            return new ParsedMail(MailKind.VacancyAlert, null, null, null, null, ExtractVacancies(text, withCompany: true));

        var isRejectionNotice = subject.Contains("не підійшли", StringComparison.OrdinalIgnoreCase);
        if (!isRejectionNotice && !subject.StartsWith("Повідомлення від", StringComparison.OrdinalIgnoreCase))
            return new ParsedMail(MailKind.Other, null, null, null, null, []);

        var (company, contact, body) = ExtractConversation(text);
        if (contact is null && SubjectSenderRegex().Match(subject) is { Success: true } m)
        {
            contact = m.Groups["contact"].Value.Trim();
            company ??= m.Groups["company"].Value.Trim();
        }

        var kind = isRejectionNotice || LooksLikeRejection(body) ? MailKind.Rejection
            : ContainsAny(body, AutoReplyMarkers) ? MailKind.AutoReply
            : MailKind.RecruiterMessage;

        var inbox = InboxRegex().Match(text) is { Success: true } inboxMatch ? $"https://djinni.co/my/inbox/{inboxMatch.Groups[1].Value}/" : null;
        var vacancies = ExtractVacancies(text, withCompany: false).Take(1).Select(v => v with { Company = company }).ToList();
        return new ParsedMail(kind, company, contact, body, inbox, vacancies);
    }

    public static bool LooksLikeRejection(string? text) => ContainsAny(text, RejectionMarkers);

    /// <summary>"https://djinni.co/jobs/816423-junior-net-developer/?ref=…" → ("816423", canonical url).</summary>
    public static bool TryParseJobUrl(string? value, out string externalId, out string url)
    {
        externalId = url = "";
        if (value is null || JobUrlRegex().Match(value) is not { Success: true } m)
            return false;

        externalId = m.Groups["id"].Value;
        url = $"https://djinni.co/jobs/{externalId}-{m.Groups["slug"].Value}/";
        return true;
    }

    private static List<ParsedVacancy> ExtractVacancies(string text, bool withCompany)
    {
        var result = new List<ParsedVacancy>();
        var seen = new HashSet<string>();

        foreach (Match link in JobLinkRegex().Matches(text))
        {
            var id = link.Groups["id"].Value;
            if (!seen.Add(id))
                continue;

            var slug = link.Groups["slug"].Value;
            var (title, company) = ReadTitleBackwards(text[..link.Index], withCompany);
            var (salary, details, snippet) = ReadDetailsForward(text, link.Index + link.Length);

            result.Add(new ParsedVacancy(
                id,
                $"https://djinni.co/jobs/{id}-{slug}/",
                string.IsNullOrWhiteSpace(title) ? TitleFromSlug(slug) : title,
                company,
                salary,
                details,
                snippet));
        }

        return result;
    }

    /// <summary>
    /// Walks back from a job link: unindented lines are wrapped continuations of the title,
    /// the first indented line starts it. In alerts the indented line before the title is the company.
    /// </summary>
    private static (string? Title, string? Company) ReadTitleBackwards(string before, bool withCompany)
    {
        var lines = before.Split('\n');
        var i = lines.Length - 1;
        var titleLines = new List<string>();

        if (string.IsNullOrWhiteSpace(lines[i]))
            i--;

        for (; i >= 0; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.Contains("<http", StringComparison.Ordinal) || line.Contains('>'))
                break;

            titleLines.Insert(0, line.Trim());
            if (line.StartsWith(' '))
            {
                i--;
                break;
            }
        }

        var title = Collapse(string.Join(' ', titleLines));
        if (!withCompany)
            return (title, null);

        for (; i >= 0; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            return (title, line.Contains('<') || line.Contains('>') ? null : Collapse(line));
        }

        return (title, null);
    }

    /// <summary>After the link: "$$ 1 рік досвіду · B1 · …" until an indented line, then the snippet until "Детальніше".</summary>
    private static (string? Salary, string? Details, string? Snippet) ReadDetailsForward(string text, int start)
    {
        var end = text.IndexOf("Детальніше", start, StringComparison.Ordinal);
        var nextLink = text.IndexOf("<https://djinni.co/jobs/", start, StringComparison.Ordinal);
        if (end < 0 || nextLink >= 0 && nextLink < end)
            end = nextLink >= 0 ? nextLink : Math.Min(text.Length, start + 600);

        var lines = text[start..end].Split('\n');
        var details = new StringBuilder(lines[0]);
        var i = 1;
        for (; i < lines.Length && !lines[i].StartsWith("    ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(lines[i]); i++)
            details.Append(' ').Append(lines[i]);

        var snippet = Collapse(string.Join(' ', lines.Skip(i)));
        var detailsText = Collapse(details.ToString()).Trim('·', ' ');
        if (detailsText.Length == 0)
            return (null, null, NullIfEmpty(snippet));

        var salary = SalaryRegex().Match(detailsText) is { Success: true } s ? s.Groups["salary"].Value.Trim() : null;
        if (salary is not null)
            detailsText = detailsText[salary.Length..].Trim();

        return (salary, NullIfEmpty(detailsText), NullIfEmpty(snippet));
    }

    /// <summary>
    /// Message layout (links removed): "…{Company}\n · {Contact}\n{body}\nВідповісти". The plain-text renderer places
    /// line breaks around the "·" differently from mail to mail, so we anchor on the separator itself.
    /// </summary>
    private static (string? Company, string? Contact, string? Body) ExtractConversation(string text)
    {
        var stripped = LinkRegex().Replace(text, "");
        var dot = stripped.IndexOf('·');
        if (dot < 0)
            return (null, null, null);

        var company = stripped[..dot].Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);

        var rest = stripped[(dot + 1)..];
        var restLines = rest.Split('\n');
        var contactIndex = Array.FindIndex(restLines, l => l.Trim().Length > 0);
        if (contactIndex < 0)
            return (company, null, null);

        var contact = restLines[contactIndex].Trim();
        var bodyLines = new List<string>();
        foreach (var line in restLines.Skip(contactIndex + 1))
        {
            if (line.TrimStart().StartsWith("Відповісти", StringComparison.Ordinal) || line.Contains("Вакансія:", StringComparison.Ordinal))
                break;
            bodyLines.Add(line);
        }

        return (NullIfEmpty(company), NullIfEmpty(contact), NullIfEmpty(Unwrap(bodyLines)));
    }

    /// <summary>Joins word-wrapped lines back into paragraphs; blank lines stay paragraph breaks.</summary>
    public static string Unwrap(IEnumerable<string> lines)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (current.Length > 0)
                    paragraphs.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(line);
        }

        if (current.Length > 0)
            paragraphs.Add(current.ToString());
        return string.Join("\n\n", paragraphs);
    }

    private static string TitleFromSlug(string slug)
    {
        var words = slug.Replace('-', ' ');
        return words.Length == 0 ? "Вакансія" : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static bool ContainsAny(string? text, IEnumerable<string> markers) =>
        text is not null && markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static string Collapse(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"<https://djinni\.co/jobs/(?<id>\d+)-(?<slug>[^/?>\s]+)/?[^>]*>")]
    private static partial Regex JobLinkRegex();

    [GeneratedRegex(@"djinni\.co/jobs/(?<id>\d+)-(?<slug>[^/?#>\s]+)")]
    private static partial Regex JobUrlRegex();

    [GeneratedRegex(@"<https?://[^>\s]+>")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"https://djinni\.co/my/inbox/(\d+)/")]
    private static partial Regex InboxRegex();

    [GeneratedRegex(@"^Повідомлення від\s+(?<contact>.+?),\s*(?<company>[^,]+)$")]
    private static partial Regex SubjectSenderRegex();

    // "$$", "до $1000", "від $800", "$1000-1500", "$1 500–2 000"
    [GeneratedRegex(@"^(?<salary>(?:(?:до|від)\s+)?\$\d+(?:[ ,]\d{3})*(?:\s*[–—-]\s*\$?\d+(?:[ ,]\d{3})*)?|\${1,4})(?=\s|$)")]
    private static partial Regex SalaryRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
