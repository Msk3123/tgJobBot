using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

public sealed record Draft(string Text, IReadOnlyList<string> UngroundedSkills);

/// <summary>Writes application messages and replies to recruiters. Never sends anything: the owner copies and sends.</summary>
public sealed class DraftWriter(ILlmClient llm, IPromptStore prompts, ILogger<DraftWriter> logger)
{
    public bool IsConfigured => llm.IsConfigured;

    public Task<Draft?> WriteApplicationAsync(string profile, Vacancy vacancy, CancellationToken ct) =>
        WriteAsync("vacancy_draft", profile, VacancyPrompt.Render(vacancy), ct);

    public Task<Draft?> WriteReplyAsync(string profile, MailEvent message, CancellationToken ct)
    {
        var user = $"""
            {(message.Vacancy is { } v ? VacancyPrompt.Render(v) : "<vacancy>unknown</vacancy>")}
            <recruiter_message from="{message.Contact} ({message.Company})">
            {message.Text}
            </recruiter_message>
            """;
        return WriteAsync("recruiter_reply", profile, user, ct);
    }

    /// <summary>
    /// One retry when the draft claims technologies missing from the profile; the second attempt is told exactly which ones.
    /// If it still claims them, the draft is returned with the list so the UI can warn instead of silently trusting it.
    /// </summary>
    private async Task<Draft?> WriteAsync(string promptName, string profile, string user, CancellationToken ct)
    {
        var system = prompts.Render(promptName, new Dictionary<string, string> { ["profile"] = profile });
        IReadOnlyList<string> ungrounded = [];
        string? text = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var extra = ungrounded.Count == 0 ? "" :
                $"\n\nYour previous draft mentioned {string.Join(", ", ungrounded)}. These are NOT in the candidate profile. Do not mention them.";
            var response = await llm.CompleteAsync(new LlmRequest(promptName, system + extra, user, MaxOutputTokens: 900), ct);
            var dto = LlmJson.TryParse<DraftDto>(response.Text);
            if (string.IsNullOrWhiteSpace(dto?.Message))
            {
                logger.LogWarning("{Prompt} returned no message (attempt {Attempt})", promptName, attempt);
                continue;
            }

            text = dto.Message.Trim();
            ungrounded = SkillGrounding.FindUngrounded(text, profile);
            if (ungrounded.Count == 0)
                break;

            logger.LogInformation("{Prompt} draft mentions skills not in profile: {Skills}", promptName, string.Join(", ", ungrounded));
        }

        return text is null ? null : new Draft(text, ungrounded);
    }

    private sealed record DraftDto(string? Message);
}
