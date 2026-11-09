using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

public interface IVacancyRepository
{
    void Add(Vacancy vacancy);
    Task<Vacancy?> GetAsync(int id, CancellationToken ct);
    Task<Vacancy?> FindAsync(string source, string externalId, CancellationToken ct);

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

    Task SaveChangesAsync(CancellationToken ct);
}
