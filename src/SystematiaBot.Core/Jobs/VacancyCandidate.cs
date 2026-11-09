namespace SystematiaBot.Core.Jobs;

/// <summary>A vacancy as a source saw it, before it becomes a stored <see cref="Entities.Vacancy"/>.</summary>
public sealed record VacancyCandidate(
    string Source,
    string ExternalId,
    string Url,
    string Title,
    string? Company = null,
    string? Salary = null,
    string? Details = null,
    string? Description = null,
    DateTimeOffset? PostedAt = null,
    int? ExperienceMonths = null)
{
    public static VacancyCandidate From(ParsedVacancy p) =>
        new(p.Source, p.ExternalId, p.Url, p.Title, p.Company, p.Salary, p.Details, p.Snippet);
}
