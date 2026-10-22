using System.Text.Json;
using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Validation;

namespace SystematiaBot.Core.News;

public sealed record RewriteResult(Post Post, ValidationReport Report, int Attempts);

/// <summary>
/// Generates a social post or a site article from the source text(s), validates it deterministically
/// and retries with the list of problems (at most <see cref="MaxRetries"/> times). The last version is saved either way.
/// </summary>
public sealed class RewriteService(
    ILlmClient llm,
    IPromptStore prompts,
    TextValidator validator,
    INewsRepository news,
    IPostRepository posts,
    IClock clock,
    ILogger<RewriteService> logger)
{
    public const int MaxRetries = 2;
    private const int MaxSourceChars = 6000;

    public async Task<RewriteResult> RewriteAsync(NewsItem item, CityConfig city, PostKind kind, CancellationToken ct)
    {
        if (!llm.IsConfigured)
            throw new LlmUnavailableException("OpenAI не налаштовано: задай OpenAI:ApiKey і OpenAI:Model");

        var sources = await SourceTextsAsync(item, ct);
        var promptName = kind == PostKind.Article ? "site_article" : "social_post";
        var operation = kind == PostKind.Article ? "rewrite-article" : "rewrite-social";

        Draft? draft = null;
        ValidationReport? report = null;
        var fixList = "";
        var attempts = 0;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            attempts++;
            var system = prompts.Render(promptName, new Dictionary<string, string>
            {
                ["city"] = city.Name,
                ["source_text"] = Truncate(string.Join("\n\n---\n\n", sources), MaxSourceChars),
                ["source_url"] = item.Url,
                ["fix_list"] = fixList
            });

            var user = draft is null
                ? "Напиши текст. Відповідь — тільки JSON {\"title\":\"…\",\"body\":\"…\"}."
                : $"Попередня версія:\n{JsonSerializer.Serialize(draft, LlmJson.Options)}\n\nВиправ усі проблеми зі списку і поверни новий JSON.";

            var response = await llm.CompleteAsync(new LlmRequest(operation, system, user), ct);
            var parsed = LlmJson.TryParse<Draft>(response.Text);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Title) || string.IsNullOrWhiteSpace(parsed.Body))
            {
                logger.LogWarning("Rewrite of news {Id}: invalid JSON on attempt {Attempt}", item.Id, attempts);
                fixList = "- Відповідь має бути валідним JSON з полями title і body.";
                continue;
            }

            draft = parsed with { Body = NormalizeBody(parsed.Body) };
            report = validator.Validate(kind, draft.Title, draft.Body, item.Url, sources);
            if (!report.HasErrors)
                break;

            fixList = string.Join('\n', report.FixList.Select(f => $"- {f}"));
            logger.LogInformation("Rewrite of news {Id}, attempt {Attempt}: {Errors}", item.Id, attempts, string.Join("; ", report.FixList));
        }

        if (draft is null || report is null)
            throw new InvalidOperationException("Модель не повернула валідний JSON після кількох спроб");

        var post = new Post
        {
            NewsItemId = item.Id,
            Kind = kind,
            Title = draft.Title.Trim(),
            Body = draft.Body,
            Version = await posts.GetNextVersionAsync(item.Id, kind, ct),
            ValidationJson = JsonSerializer.Serialize(report),
            CreatedAt = clock.UtcNow
        };
        posts.Add(post);
        await posts.SaveChangesAsync(ct);
        return new RewriteResult(post, report, attempts);
    }

    /// <summary>Validates a text the user typed by hand ("first line = title") and saves it as a new version.</summary>
    public async Task<RewriteResult> SaveManualAsync(Post previous, string text, CancellationToken ct)
    {
        var item = previous.NewsItem ?? await news.GetAsync(previous.NewsItemId, ct) ?? throw new InvalidOperationException("News item not found");
        var lines = text.Replace("\r\n", "\n").Trim().Split('\n', 2);
        var title = lines[0].Trim();
        var body = NormalizeBody(lines.Length > 1 ? lines[1] : "");

        var sources = await SourceTextsAsync(item, ct);
        var report = validator.Validate(previous.Kind, title, body, item.Url, sources);
        var post = new Post
        {
            NewsItemId = item.Id,
            Kind = previous.Kind,
            Title = title,
            Body = body,
            Version = await posts.GetNextVersionAsync(item.Id, previous.Kind, ct),
            ValidationJson = JsonSerializer.Serialize(report),
            CreatedAt = clock.UtcNow
        };
        posts.Add(post);
        await posts.SaveChangesAsync(ct);
        return new RewriteResult(post, report, 0);
    }

    /// <summary>The item's text plus texts of the same event from other sources (cluster members).</summary>
    public async Task<List<string>> SourceTextsAsync(NewsItem item, CancellationToken ct)
    {
        var members = await news.GetClusterMembersAsync(item, ct);
        return [$"{item.Title}\n{item.RawText}".Trim(), .. members.Select(m => $"{m.Title}\n{m.RawText}".Trim())];
    }

    /// <summary>Paragraphs separated by exactly one blank line; models sometimes use single newlines.</summary>
    public static string NormalizeBody(string body)
    {
        var text = body.Replace("\r\n", "\n").Replace("\\n", "\n").Trim();
        var paragraphs = text.Contains("\n\n")
            ? text.Split("\n\n").Select(p => string.Join(' ', p.Split('\n', StringSplitOptions.TrimEntries)))
            : text.Split('\n');
        return string.Join("\n\n", paragraphs.Select(p => p.Trim()).Where(p => p.Length > 0));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record Draft(string Title, string Body);
}
