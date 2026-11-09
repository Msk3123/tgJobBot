using System.Text.RegularExpressions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

/// <summary>
/// LinkedIn job e-mails (alerts and recommendations) list jobs as blocks of "title / company / location / notes"
/// followed by a "View job: https://www.linkedin.com/comm/jobs/view/{id}/?trackingId=…" line.
/// The tracking part of those links carries personal tokens, so only the canonical /jobs/view/{id}/ is stored.
/// Job pages themselves are not fetched (LinkedIn does not allow it), so these vacancies have no description.
/// </summary>
public static partial class LinkedInMailParser
{
    public const string Source = "linkedin";

    private static readonly string[] NoteMarkers =
    [
        "активно наймає", "actively hiring", "контакт", "connection", "подати заявку", "easy apply", "apply with",
        "випускник", "alumni", "applicant", "серед перших", "promoted", "просуває", "рекомендовано"
    ];

    public static ParsedMail Parse(string plainText)
    {
        var vacancies = new List<ParsedVacancy>();
        var seen = new HashSet<string>();
        var block = new List<string>();

        foreach (var raw in plainText.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (JobLinkRegex().Match(line) is { Success: true } link)
            {
                var id = link.Groups["id"].Value;
                if (seen.Add(id) && ToVacancy(id, block) is { } vacancy)
                    vacancies.Add(vacancy);
                block.Clear();
                continue;
            }

            if (line.Contains("http", StringComparison.OrdinalIgnoreCase) || line.StartsWith("---", StringComparison.Ordinal))
            {
                block.Clear();
                continue;
            }

            if (line.Length > 0)
                block.Add(line);
        }

        return new ParsedMail(vacancies.Count > 0 ? MailKind.VacancyAlert : MailKind.Other, null, null, null, null, vacancies);
    }

    private static ParsedVacancy? ToVacancy(string id, List<string> block)
    {
        var notes = block.Where(IsNote).ToList();
        var fields = block.Where(l => !IsNote(l)).TakeLast(3).ToList();
        if (fields.Count == 0)
            return null;

        var title = fields[0];
        var company = fields.Count > 1 ? fields[1] : null;
        var location = fields.Count > 2 ? fields[2] : null;
        var details = string.Join(" · ", new[] { location }.Concat(notes).Where(s => !string.IsNullOrWhiteSpace(s)));

        return new ParsedVacancy(id, $"https://www.linkedin.com/jobs/view/{id}/", title, company, null,
            details.Length == 0 ? null : details, null, Source);
    }

    private static bool IsNote(string line) => NoteMarkers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"linkedin\.com/(?:comm/)?jobs/view/(?<id>\d+)")]
    private static partial Regex JobLinkRegex();
}
