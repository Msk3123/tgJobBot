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
    VacancyIntake intake,
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
        var created = 0;

        // Oldest first, so an alert is processed before a later reply about the same vacancy.
        foreach (var id in ids.Reverse())
        {
            if (await mails.ExistsAsync(id, ct))
                continue;

            var message = await mailbox.GetAsync(id, ct);
            read++;
            var parsed = message.From.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase)
                ? LinkedInMailParser.Parse(message.PlainText)
                : DjinniMailParser.Parse(message.Subject, message.PlainText);

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

            // The mail is marked as processed only after its vacancies are in: a restart halfway through
            // (deploys, OOM) used to leave an alert "done" with half of its vacancies never ingested.
            foreach (var item in parsed.Vacancies.DistinctBy(v => v.ExternalId))
            {
                if (parsed.Kind == MailKind.VacancyAlert)
                {
                    // E-mails carry no publication date; for alerts the delivery time is a close proxy.
                    var candidate = VacancyCandidate.From(item) with { PostedAt = message.ReceivedAt };
                    if ((await intake.IngestAsync(candidate, profile, applyPrefilter: true, ct)).Created)
                        created++;
                    continue;
                }

                var vacancy = await GetOrCreateForConversationAsync(item, ct);
                mailEvent.Vacancy = vacancy;
                ApplyConversation(vacancy, parsed.Kind);
            }

            if (parsed.Kind is MailKind.RecruiterMessage or MailKind.Rejection)
                newEvents++;

            mails.Add(mailEvent);
            await mails.SaveChangesAsync(ct);
        }

        logger.LogInformation("Mail sync: {Read} new e-mails, {Vacancies} new vacancies, {Events} conversation events", read, created, newEvents);
        return new SyncResult(read, created, newEvents);
    }

    /// <summary>The owner pasted a djinni.co/jobs link into the chat: no prefilter, they asked for this one.</summary>
    public async Task<Vacancy?> AddFromUrlAsync(string url, string? profile, CancellationToken ct)
    {
        if (!DjinniMailParser.TryParseJobUrl(url, out var externalId, out var canonical))
            return null;

        var existing = await vacancies.FindAsync(DjinniMailParser.Source, externalId, ct);
        if (existing is not null)
        {
            if (existing.FitScore is null)
                await intake.EnrichAndScoreAsync(existing, profile, applyPrefilter: false, ct);
            return existing;
        }

        var result = await intake.IngestAsync(new VacancyCandidate(DjinniMailParser.Source, externalId, canonical, $"Вакансія {externalId}"), profile, applyPrefilter: false, ct);
        result.Vacancy.Notified = true;
        await vacancies.SaveChangesAsync(ct);
        return result.Vacancy;
    }

    public Task EnrichAndScoreAsync(Vacancy vacancy, string? profile, CancellationToken ct) =>
        intake.EnrichAndScoreAsync(vacancy, profile, applyPrefilter: false, ct);

    private async Task<Vacancy> GetOrCreateForConversationAsync(ParsedVacancy item, CancellationToken ct)
    {
        var existing = await vacancies.FindAsync(item.Source, item.ExternalId, ct);
        if (existing is not null)
            return existing;

        // A vacancy first seen in a reply means the owner already applied to it outside the bot.
        var vacancy = new Vacancy
        {
            Source = item.Source,
            ExternalId = item.ExternalId,
            Url = item.Url,
            Title = item.Title,
            Company = item.Company,
            Salary = item.Salary,
            Details = item.Details,
            Description = item.Snippet,
            DedupKey = VacancyDedup.Key(item.Company, item.Title),
            Status = VacancyStatus.Applied,
            FoundAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            Notified = true
        };
        vacancies.Add(vacancy);
        return vacancy;
    }

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
