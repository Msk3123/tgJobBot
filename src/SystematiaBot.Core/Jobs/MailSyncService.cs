using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

public sealed record SyncResult(int MailsRead, int NewVacancies, int NewEvents);

/// <summary>
/// Reads Djinni e-mails, turns them into vacancies and conversation events, enriches vacancies from their page
/// and scores them. Idempotent: every Gmail id is stored, so a re-run or a crash mid-way never duplicates anything.
/// </summary>
public sealed class MailSyncService(
    IMailbox mailbox,
    IMailEventRepository mails,
    IVacancyRepository vacancies,
    IVacancyPageFetcher pages,
    VacancyScorer scorer,
    IClock clock,
    IOptions<JobOptions> options,
    ILogger<MailSyncService> logger)
{
    public bool IsConfigured => mailbox.IsConfigured;

    public async Task<SyncResult> SyncAsync(string? profile, CancellationToken ct)
    {
        var ids = await mailbox.SearchAsync(options.Value.MailQuery, options.Value.MaxMailsPerSync, ct);
        var read = 0;
        var newEvents = 0;
        var toEnrich = new List<Vacancy>();

        // Oldest first, so an alert is processed before a later reply about the same vacancy.
        foreach (var id in ids.Reverse())
        {
            if (await mails.ExistsAsync(id, ct))
                continue;

            var message = await mailbox.GetAsync(id, ct);
            read++;
            var parsed = DjinniMailParser.Parse(message.Subject, message.PlainText);
            var mailEvent = new MailEvent
            {
                GmailId = id,
                Kind = parsed.Kind,
                Subject = message.Subject,
                Contact = parsed.Contact,
                Company = parsed.Company,
                Text = parsed.Text,
                InboxUrl = parsed.InboxUrl,
                ReceivedAt = message.ReceivedAt,
                Notified = parsed.Kind is MailKind.VacancyAlert or MailKind.Other
            };

            foreach (var item in parsed.Vacancies)
            {
                var (vacancy, created) = await GetOrCreateAsync(item, parsed.Kind, ct);
                if (created && parsed.Kind == MailKind.VacancyAlert)
                    toEnrich.Add(vacancy);
                if (parsed.Kind != MailKind.VacancyAlert)
                {
                    mailEvent.Vacancy = vacancy;
                    ApplyConversation(vacancy, parsed.Kind);
                }
            }

            if (parsed.Kind is MailKind.RecruiterMessage or MailKind.Rejection)
                newEvents++;

            mails.Add(mailEvent);
            await mails.SaveChangesAsync(ct);
        }

        foreach (var vacancy in toEnrich)
            await EnrichAndScoreAsync(vacancy, profile, ct);

        logger.LogInformation("Mail sync: {Read} new e-mails, {Vacancies} new vacancies, {Events} conversation events", read, toEnrich.Count, newEvents);
        return new SyncResult(read, toEnrich.Count, newEvents);
    }

    /// <summary>The owner pasted a djinni.co/jobs link into the chat.</summary>
    public async Task<Vacancy?> AddFromUrlAsync(string url, string? profile, CancellationToken ct)
    {
        if (!DjinniMailParser.TryParseJobUrl(url, out var externalId, out var canonical))
            return null;

        var existing = await vacancies.FindAsync(DjinniMailParser.Source, externalId, ct);
        if (existing is not null)
        {
            if (existing.FitScore is null)
                await EnrichAndScoreAsync(existing, profile, ct);
            return existing;
        }

        var vacancy = NewVacancy(new ParsedVacancy(externalId, canonical, $"Вакансія {externalId}", null, null, null, null), VacancyStatus.New);
        vacancy.Notified = true;
        vacancies.Add(vacancy);
        await vacancies.SaveChangesAsync(ct);
        await EnrichAndScoreAsync(vacancy, profile, ct);
        return vacancy;
    }

    public async Task EnrichAndScoreAsync(Vacancy vacancy, string? profile, CancellationToken ct)
    {
        var page = await pages.FetchAsync(vacancy.Url, ct);
        if (page is not null)
        {
            vacancy.Title = page.Title ?? vacancy.Title;
            vacancy.Company = page.Company ?? vacancy.Company;
            vacancy.Description = page.Description ?? vacancy.Description;
            vacancy.IsInactive = page.IsInactive;
        }

        if (!string.IsNullOrWhiteSpace(profile) && scorer.IsConfigured && !vacancy.IsInactive)
        {
            try
            {
                if (await scorer.ScoreAsync(profile, vacancy, ct) is { } fit)
                {
                    vacancy.FitScore = fit.Score;
                    vacancy.FitVerdict = fit.Verdict;
                    vacancy.FitReasons = string.Join('\n', fit.Reasons);
                    vacancy.Gaps = string.Join('\n', fit.Gaps);
                }
            }
            catch (LlmUnavailableException ex)
            {
                // Budget exhausted or provider down: keep the vacancy unscored, the owner still gets the card.
                logger.LogWarning("Vacancy {Id} left unscored: {Reason}", vacancy.Id, ex.Message);
            }
        }

        // Low-fit and inactive vacancies are kept for history but never pushed to the phone.
        if (vacancy.IsInactive || vacancy.FitScore < options.Value.MinFitToNotify)
            vacancy.Notified = true;

        vacancy.UpdatedAt = clock.UtcNow;
        await vacancies.SaveChangesAsync(ct);
    }

    private async Task<(Vacancy Vacancy, bool Created)> GetOrCreateAsync(ParsedVacancy item, MailKind kind, CancellationToken ct)
    {
        var existing = await vacancies.FindAsync(DjinniMailParser.Source, item.ExternalId, ct);
        if (existing is not null)
            return (existing, false);

        // A vacancy first seen in a reply means the owner already applied to it outside the bot.
        var vacancy = NewVacancy(item, kind == MailKind.VacancyAlert ? VacancyStatus.New : VacancyStatus.Applied);
        vacancy.Notified = kind != MailKind.VacancyAlert;
        vacancies.Add(vacancy);
        return (vacancy, true);
    }

    private Vacancy NewVacancy(ParsedVacancy item, VacancyStatus status) => new()
    {
        Source = DjinniMailParser.Source,
        ExternalId = item.ExternalId,
        Url = item.Url,
        Title = item.Title,
        Company = item.Company,
        Salary = item.Salary,
        Details = item.Details,
        Description = item.Snippet,
        Status = status,
        FoundAt = clock.UtcNow,
        UpdatedAt = clock.UtcNow
    };

    /// <summary>Moves the vacancy forward on a reply; never moves it back (an interview stays an interview after an auto-reply).</summary>
    private void ApplyConversation(Vacancy vacancy, MailKind kind)
    {
        var next = kind switch
        {
            MailKind.Rejection => VacancyStatus.Rejected,
            MailKind.RecruiterMessage => VacancyStatus.Replied,
            MailKind.AutoReply => VacancyStatus.Applied,
            _ => vacancy.Status
        };

        var isProgress = next == VacancyStatus.Rejected
            ? vacancy.Status is not (VacancyStatus.Offer or VacancyStatus.Rejected)
            : Rank(next) > Rank(vacancy.Status);

        if (!isProgress)
            return;

        vacancy.Status = next;
        vacancy.AppliedAt ??= clock.UtcNow;
        vacancy.UpdatedAt = clock.UtcNow;
    }

    private static int Rank(VacancyStatus status) => status switch
    {
        VacancyStatus.New or VacancyStatus.Skipped => 0,
        VacancyStatus.Drafted => 1,
        VacancyStatus.Applied => 2,
        VacancyStatus.Replied => 3,
        VacancyStatus.Interview => 4,
        VacancyStatus.Offer => 5,
        _ => 0
    };
}
