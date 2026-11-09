using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Core.Abstractions;

/// <summary>A board that publishes vacancies as a feed (DOU RSS).</summary>
public interface IVacancyFeed
{
    string Source { get; }

    Task<IReadOnlyList<VacancyCandidate>> FetchAsync(CancellationToken ct);
}
