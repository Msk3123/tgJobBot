using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

public sealed record FitResult(int Score, string Verdict, IReadOnlyList<string> Reasons, IReadOnlyList<string> Gaps);

/// <summary>Rates how well a vacancy fits the candidate profile (0–100) and explains why.</summary>
public sealed class VacancyScorer(ILlmClient llm, IPromptStore prompts, ILogger<VacancyScorer> logger)
{
    public bool IsConfigured => llm.IsConfigured;

    public async Task<FitResult?> ScoreAsync(string profile, Vacancy vacancy, CancellationToken ct)
    {
        // The profile is trusted and goes into the system prompt; the vacancy is third-party text and
        // goes into the user turn inside a tag, so instructions hidden in a posting stay data.
        // The system prompt is identical for every vacancy, so the provider serves it from the prompt cache.
        // The output cap is generous because reasoning tokens count against it; the visible JSON is ~200 tokens.
        var system = prompts.Render("vacancy_score", new Dictionary<string, string> { ["profile"] = profile });
        var request = new LlmRequest("score", system, VacancyPrompt.Render(vacancy), MaxOutputTokens: 2500);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await llm.CompleteAsync(request, ct);
            if (LlmJson.TryParse<ScoreDto>(response.Text) is { Fit: { } fit } dto)
            {
                var verdict = dto.Verdict?.Trim().ToLowerInvariant() is "apply" or "maybe" or "skip" ? dto.Verdict.Trim().ToLowerInvariant() : "maybe";
                return new FitResult(Math.Clamp(fit, 0, 100), verdict, Clean(dto.Reasons), Clean(dto.Gaps));
            }

            logger.LogWarning("Scoring of vacancy {Id} returned invalid JSON (attempt {Attempt})", vacancy.Id, attempt);
        }

        return null;
    }

    private static List<string> Clean(List<string>? items) =>
        (items ?? []).Select(i => i.Trim()).Where(i => i.Length > 0).Take(3).ToList();

    private sealed record ScoreDto(int? Fit, string? Verdict, List<string>? Reasons, List<string>? Gaps);
}

internal static class VacancyPrompt
{
    private const int MaxDescription = 4000;

    public static string Render(Vacancy v)
    {
        var description = v.Description ?? "";
        if (description.Length > MaxDescription)
            description = description[..MaxDescription] + "…";

        return $"""
            <vacancy>
            Title: {v.Title}
            Company: {v.Company ?? "unknown"}
            Salary: {v.Salary ?? "not stated"}
            Details: {v.Details ?? "-"}
            Description:
            {description}
            </vacancy>
            """;
    }
}
