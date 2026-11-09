using Microsoft.EntityFrameworkCore;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence.Repositories;

public sealed class VacancyRepository(BotDbContext db) : IVacancyRepository
{
    public void Add(Vacancy vacancy) => db.Vacancies.Add(vacancy);

    public Task<Vacancy?> GetAsync(int id, CancellationToken ct) => db.Vacancies.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<Vacancy?> FindAsync(string source, string externalId, CancellationToken ct) =>
        db.Vacancies.Local.FirstOrDefault(v => v.Source == source && v.ExternalId == externalId)
        ?? await db.Vacancies.FirstOrDefaultAsync(v => v.Source == source && v.ExternalId == externalId, ct);

    public Task<Vacancy?> FindByDedupKeyAsync(string dedupKey, DateTimeOffset since, CancellationToken ct) =>
        db.Vacancies.Where(v => v.DedupKey == dedupKey && v.FoundAt >= since).OrderBy(v => v.FoundAt).FirstOrDefaultAsync(ct);

    public Task<List<Vacancy>> GetFollowUpCandidatesAsync(DateTimeOffset appliedBefore, CancellationToken ct) =>
        db.Vacancies.Where(v => v.Status == VacancyStatus.Applied && v.AppliedAt != null && v.AppliedAt <= appliedBefore
                && v.FollowUpRemindedAt == null && v.FollowUpDoneAt == null)
            .OrderBy(v => v.AppliedAt)
            .ToListAsync(ct);

    // SQLite stores DateTimeOffset as a long (see BotDbContext), so ordering by it is a plain integer sort.
    public Task<List<Vacancy>> GetByStatusAsync(IReadOnlyCollection<VacancyStatus> statuses, int skip, int take, CancellationToken ct) =>
        db.Vacancies.Where(v => statuses.Contains(v.Status))
            .OrderByDescending(v => v.UpdatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public Task<int> CountAsync(IReadOnlyCollection<VacancyStatus> statuses, CancellationToken ct) =>
        db.Vacancies.CountAsync(v => statuses.Contains(v.Status), ct);

    public async Task<Dictionary<VacancyStatus, int>> GetCountsAsync(CancellationToken ct) =>
        await db.Vacancies.GroupBy(v => v.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public Task<List<Vacancy>> GetUnnotifiedAsync(CancellationToken ct) =>
        db.Vacancies.Where(v => !v.Notified && v.Status == VacancyStatus.New).OrderBy(v => v.FoundAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class MailEventRepository(BotDbContext db) : IMailEventRepository
{
    private static readonly MailKind[] ConversationKinds = [MailKind.RecruiterMessage, MailKind.Rejection];

    public void Add(MailEvent mailEvent) => db.MailEvents.Add(mailEvent);

    public Task<bool> ExistsAsync(string gmailId, CancellationToken ct) => db.MailEvents.AnyAsync(m => m.GmailId == gmailId, ct);

    public Task<MailEvent?> GetAsync(int id, CancellationToken ct) =>
        db.MailEvents.Include(m => m.Vacancy).FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<List<MailEvent>> GetConversationsAsync(int skip, int take, CancellationToken ct) =>
        db.MailEvents.Include(m => m.Vacancy)
            .Where(m => ConversationKinds.Contains(m.Kind))
            .OrderByDescending(m => m.ReceivedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public Task<List<MailEvent>> GetUnnotifiedAsync(CancellationToken ct) =>
        db.MailEvents.Include(m => m.Vacancy)
            .Where(m => !m.Notified && ConversationKinds.Contains(m.Kind))
            .OrderBy(m => m.ReceivedAt)
            .ToListAsync(ct);

    public Task<string?> GetInboxUrlAsync(int vacancyId, CancellationToken ct) =>
        db.MailEvents.Where(m => m.VacancyId == vacancyId && m.InboxUrl != null)
            .OrderByDescending(m => m.ReceivedAt)
            .Select(m => m.InboxUrl)
            .FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
