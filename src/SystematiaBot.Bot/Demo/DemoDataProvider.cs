using System.Collections.Concurrent;
using System.Text.Json;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Seo;

namespace SystematiaBot.Bot.Demo;

/// <summary>Prepared data from demo/*.json for visitors who are not the owner: no OpenAI calls, no crawling.</summary>
public sealed class DemoDataProvider
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<long, DemoSession> _sessions = new();
    private readonly IClock _clock;

    public DemoDataProvider(AppPaths paths, IClock clock)
    {
        _clock = clock;
        var candidates = JsonSerializer.Deserialize<List<DemoCandidate>>(File.ReadAllText(Path.Combine(paths.Demo, "candidates.json")), Options) ?? [];
        var posts = JsonSerializer.Deserialize<DemoPostsFile>(File.ReadAllText(Path.Combine(paths.Demo, "posts.json")), Options)!;
        Seo = JsonSerializer.Deserialize<SeoCrawlResult>(File.ReadAllText(Path.Combine(paths.Demo, "seo_maxidveri.json")), Options)!;

        Source = new Source { Id = -1, City = "sheptytsky", Name = "Демо", Url = "https://t.me/s/sheptytska_mr", Kind = SourceKind.Telegram };
        Candidates = candidates;
        Posts = posts.Posts.ToDictionary(p => int.Parse(p.Key), p => p.Value);
        Article = posts.Article;
    }

    public IReadOnlyList<DemoCandidate> Candidates { get; }
    public IReadOnlyDictionary<int, DemoText> Posts { get; }
    public DemoArticle Article { get; }
    public SeoCrawlResult Seo { get; }
    public Source Source { get; }

    public DemoSession Session(long chatId) => _sessions.GetOrAdd(chatId, _ => new DemoSession());

    /// <summary>Candidate as a transient entity; dates are relative to now so the demo always looks fresh.</summary>
    public NewsItem ToNewsItem(DemoCandidate c)
    {
        var index = Candidates.ToList().IndexOf(c);
        return new NewsItem
        {
            Id = c.Id,
            City = "sheptytsky",
            SourceId = Source.Id,
            Source = new Source { City = "sheptytsky", Name = c.Source, Url = Source.Url },
            Url = c.Url,
            Title = c.Title,
            RawText = c.Text,
            PublishedAt = _clock.UtcNow.AddHours(-(3 * index + 1)),
            ContentHash = "",
            Score = c.Score,
            ScoreReason = c.Reason,
            Category = c.Category
        };
    }

    public DemoCandidate? Find(int id) => Candidates.FirstOrDefault(c => c.Id == id);
}

public sealed record DemoCandidate(int Id, string Source, string Url, string? Title, string Text, int Score, string Reason, string Category, int ExtraSources);

public sealed record DemoText(string Title, string Body);

public sealed record DemoArticle(int CandidateId, string Title, string Body);

public sealed record DemoPostsFile(Dictionary<string, DemoText> Posts, DemoArticle Article);

/// <summary>What a demo visitor has done so far (kept in memory only).</summary>
public sealed class DemoSession
{
    public HashSet<int> Picked { get; } = [];
    public Dictionary<int, string> Rejected { get; } = [];
    public HashSet<int> Approved { get; } = [];
    public HashSet<int> Removed { get; } = [];
    public Dictionary<int, DemoText> Edited { get; } = [];
    public bool Collected { get; set; }
    public bool ArticleWritten { get; set; }
}
