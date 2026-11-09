using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

public interface IVacancyRepository
{
    void Add(Vacancy vacancy);
    Task<Vacancy?> GetAsync(int id, CancellationToken ct);
    Task<Vacancy?> FindAsync(string source, string externalId, CancellationToken ct);

    /// <summary>A vacancy with the same normalized company and title found since <paramref name="since"/>.</summary>
    Task<Vacancy?> FindByDedupKeyAsync(string dedupKey, DateTimeOffset since, CancellationToken ct);

    /// <summary>Applied vacancies with no reply since <paramref name="appliedBefore"/> and no follow-up reminder yet.</summary>
    Task<List<Vacancy>> GetFollowUpCandidatesAsync(DateTimeOffset appliedBefore, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<List<Vacancy>> GetByStatusAsync(IReadOnlyCollection<VacancyStatus> statuses, int skip, int take, CancellationToken ct);

    Task<int> CountAsync(IReadOnlyCollection<VacancyStatus> statuses, CancellationToken ct);
    Task<Dictionary<VacancyStatus, int>> GetCountsAsync(CancellationToken ct);

    /// <summary>New vacancies the owner has not been told about yet, oldest first.</summary>
    Task<List<Vacancy>> GetUnnotifiedAsync(CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

public interface IMailEventRepository
{
    void Add(MailEvent mailEvent);
    Task<bool> ExistsAsync(string gmailId, CancellationToken ct);
    Task<MailEvent?> GetAsync(int id, CancellationToken ct);

    /// <summary>Recruiter messages and rejections, newest first, with their vacancy.</summary>
    Task<List<MailEvent>> GetConversationsAsync(int skip, int take, CancellationToken ct);

    /// <summary>Events waiting for a Telegram card, oldest first, with their vacancy.</summary>
    Task<List<MailEvent>> GetUnnotifiedAsync(CancellationToken ct);

    /// <summary>Djinni conversation link of the latest message about the vacancy, if any.</summary>
    Task<string?> GetInboxUrlAsync(int vacancyId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
