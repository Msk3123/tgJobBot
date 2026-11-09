using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

public sealed record IntakeResult(Vacancy Vacancy, bool Created, bool Duplicate);

/// <summary>
/// The single entry point for new vacancies from any board:
/// dedup → (Djinni page) → rule prefilter → LLM scoring on the cheap model → notification gate.
/// </summary>
public sealed class VacancyIntake(
    IVacancyRepository vacancies,
    IVacancyPageFetcher pages,
    VacancyScorer scorer,
    IClock clock,
    IOptions<JobOptions> options,
    ILogger<VacancyIntake> logger)
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromDays(45);

    public async Task<IntakeResult> IngestAsync(VacancyCandidate candidate, string? profile, bool applyPrefilter, CancellationToken ct)
    {
        var existing = await vacancies.FindAsync(candidate.Source, candidate.ExternalId, ct);
        if (existing is not null)
            return new IntakeResult(existing, false, false);

        var key = VacancyDedup.Key(candidate.Company, candidate.Title);
        if (key is not null && await vacancies.FindByDedupKeyAsync(key, clock.UtcNow - DedupWindow, ct) is { } twin && twin.Source != candidate.Source)
        {
            AddAlsoOn(twin, candidate.Source, candidate.Url);
            await vacancies.SaveChangesAsync(ct);
            logger.LogInformation("Vacancy {Source}:{Id} is a duplicate of #{Twin}", candidate.Source, candidate.ExternalId, twin.Id);
            return new IntakeResult(twin, false, true);
        }

        var vacancy = new Vacancy
        {
            Source = candidate.Source,
            ExternalId = candidate.ExternalId,
            Url = candidate.Url,
            Title = candidate.Title,
            Company = candidate.Company,
            Salary = candidate.Salary,
            Details = candidate.Details,
            Description = candidate.Description,
            PostedAt = candidate.PostedAt,
            ExperienceMonths = candidate.ExperienceMonths,
            DedupKey = key,
            Status = VacancyStatus.New,
            FoundAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        vacancies.Add(vacancy);
        await vacancies.SaveChangesAsync(ct);

        await EnrichAndScoreAsync(vacancy, profile, applyPrefilter, ct);
        return new IntakeResult(vacancy, true, false);
    }

    /// <param name="applyPrefilter">False when the owner asked for this vacancy explicitly (pasted link, "rescore").</param>
    public async Task EnrichAndScoreAsync(Vacancy vacancy, string? profile, bool applyPrefilter, CancellationToken ct)
    {
        if (vacancy.Source == DjinniMailParser.Source && await pages.FetchAsync(vacancy.Url, ct) is { } page)
        {
            vacancy.Title = page.Title ?? vacancy.Title;
            vacancy.Company = page.Company ?? vacancy.Company;
            vacancy.Description = page.Description ?? vacancy.Description;
            vacancy.IsInactive = page.IsInactive;
            vacancy.PostedAt = page.PostedAt ?? vacancy.PostedAt;
            vacancy.ExperienceMonths = page.ExperienceMonths ?? vacancy.ExperienceMonths;
            vacancy.DedupKey = VacancyDedup.Key(vacancy.Company, vacancy.Title) ?? vacancy.DedupKey;
        }

        if (!applyPrefilter && vacancy.FilterReason is not null)
        {
            // The owner asked for this one explicitly: the rule filter is overridden, the vacancy goes back to the inbox.
            vacancy.FilterReason = null;
            if (vacancy.Status == VacancyStatus.Skipped)
                vacancy.Status = VacancyStatus.New;
        }

        if (applyPrefilter && vacancy.Status == VacancyStatus.New)
        {
            var filter = VacancyPrefilter.Check(vacancy.Title, vacancy.Details, vacancy.ExperienceMonths, options.Value.MaxExperienceMonths);
            if (!filter.Pass)
            {
                vacancy.Status = VacancyStatus.Skipped;
                vacancy.FilterReason = filter.Reason;
                vacancy.Notified = true;
                vacancy.UpdatedAt = clock.UtcNow;
                await vacancies.SaveChangesAsync(ct);
                logger.LogInformation("Prefilter skipped #{Id} {Title}: {Reason}", vacancy.Id, vacancy.Title, filter.Reason);
                return;
            }
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

    private static void AddAlsoOn(Vacancy twin, string source, string url)
    {
        var lines = (twin.AlsoOn ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (lines.Any(l => l.EndsWith(url, StringComparison.Ordinal)))
            return;
        lines.Add($"{source} {url}");
        twin.AlsoOn = string.Join('\n', lines);
    }
}
