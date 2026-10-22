using System.Text.Json;
using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.News;

/// <summary>AI rates how interesting each item is for residents (1–5), in batches of 10 per request.</summary>
public sealed class ScoringService(ILlmClient llm, IPromptStore prompts, ILogger<ScoringService> logger) : INewsScorer
{
    public const int BatchSize = 10;
    private const int MaxTextPerItem = 1500;

    private static readonly HashSet<string> Categories = ["incident", "utilities", "infrastructure", "social", "culture", "sport", "war", "other"];

    public async Task ScoreAsync(CityConfig city, IReadOnlyList<NewsItem> items, CancellationToken ct)
    {
        if (!llm.IsConfigured)
        {
            logger.LogInformation("LLM is not configured, {Count} items left unscored", items.Count);
            return;
        }

        foreach (var batch in items.Chunk(BatchSize))
        {
            var input = JsonSerializer.Serialize(
                batch.Select(n => new { id = n.Id, text = Truncate($"{n.Title}\n{n.RawText}".Trim(), MaxTextPerItem) }),
                new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var prompt = prompts.Render("score", new Dictionary<string, string> { ["city"] = city.Name, ["items"] = input });

            var parsed = await RequestAsync(prompt, ct) ?? await RequestAsync(prompt, ct);
            if (parsed?.Items is null)
            {
                logger.LogWarning("Scoring batch of {Count} returned invalid JSON twice, leaving unscored", batch.Length);
                continue;
            }

            foreach (var result in parsed.Items)
            {
                var item = batch.FirstOrDefault(n => n.Id == result.Id);
                if (item is null || result.Score is not (>= 1 and <= 5))
                    continue;

                item.Score = result.Score;
                item.ScoreReason = Truncate(result.Reason?.Trim() ?? "", 300);
                item.Category = Categories.Contains(result.Category ?? "") ? result.Category : "other";
            }
        }
    }

    private async Task<ScoreBatch?> RequestAsync(string prompt, CancellationToken ct)
    {
        var response = await llm.CompleteAsync(new LlmRequest("score", prompt, "Оціни новини. Відповідь — тільки JSON."), ct);
        return LlmJson.TryParse<ScoreBatch>(response.Text);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record ScoreBatch(List<ScoreItem>? Items);

    private sealed record ScoreItem(int Id, int? Score, string? Reason, string? Category);
}
