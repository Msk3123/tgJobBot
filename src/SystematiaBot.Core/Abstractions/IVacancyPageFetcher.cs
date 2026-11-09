namespace SystematiaBot.Core.Abstractions;

/// <param name="PostedAt">datePosted from the page's JobPosting JSON-LD (Djinni updates it when a vacancy is bumped).</param>
/// <param name="ExperienceMonths">experienceRequirements.monthsOfExperience from the same JSON-LD.</param>
public sealed record VacancyPage(string? Title, string? Company, string? Description, bool IsInactive, DateTimeOffset? PostedAt = null, int? ExperienceMonths = null);

/// <summary>Loads the public vacancy page: the authoritative title, company and full description.</summary>
public interface IVacancyPageFetcher
{
    /// <summary>Null when the page could not be loaded; the vacancy then keeps what the e-mail said.</summary>
    Task<VacancyPage?> FetchAsync(string url, CancellationToken ct);
}
