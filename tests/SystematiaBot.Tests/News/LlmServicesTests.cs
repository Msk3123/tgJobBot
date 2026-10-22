using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;
using SystematiaBot.Core.Validation;

namespace SystematiaBot.Tests.News;

public class LlmServicesTests
{
    private static readonly CityConfig City = new() { Id = "sheptytsky", Name = "Шептицький" };

    private const string Source =
        "20 жовтня у Шептицькому на вулиці Шевченка зіткнулися два легковики. Двоє дітей потрапили до лікарні. " +
        "Водія Renault затримала поліція. Рух на ділянці обмежили до вечора, просять обирати інші маршрути.";

    [Fact]
    public async Task Rewrite_retries_with_fix_list_until_text_is_valid()
    {
        var invented = Draft("ДТП у Шептицькому: двоє дітей у лікарні", Body(150, extra: "Водію 48 років."));
        var valid = Draft("ДТП у Шептицькому: двоє дітей у лікарні", Body(150));
        var llm = new FakeLlm(invented, valid);
        var (service, posts) = CreateRewrite(llm);

        var result = await service.RewriteAsync(NewsItem(), City, PostKind.Social, CancellationToken.None);

        result.Attempts.ShouldBe(2);
        result.Report.HasErrors.ShouldBeFalse();
        llm.Requests[1].System.ShouldContain("«48» — немає в джерелі");
        posts.Saved.ShouldHaveSingleItem().Version.ShouldBe(1);
    }

    [Fact]
    public async Task Rewrite_keeps_last_version_with_problems_after_two_retries()
    {
        var bad = Draft("ДТП у Шептицькому", Body(50));
        var llm = new FakeLlm(bad, bad, bad);
        var (service, posts) = CreateRewrite(llm);

        var result = await service.RewriteAsync(NewsItem(), City, PostKind.Social, CancellationToken.None);

        result.Attempts.ShouldBe(1 + RewriteService.MaxRetries);
        result.Report.HasErrors.ShouldBeTrue();
        result.Report.Issues.ShouldContain(i => i.Code == "WORD_COUNT");
        posts.Saved.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Scoring_retries_invalid_json_once_and_applies_scores()
    {
        var items = new List<NewsItem> { NewsItem(1), NewsItem(2) };
        var llm = new FakeLlm("not json", """{"items":[{"id":1,"score":5,"reason":"безпека в місті","category":"incident"},{"id":2,"score":9,"reason":"x"}]}""");
        var scorer = new ScoringService(llm, new FakePrompts(), NullLogger<ScoringService>.Instance);

        await scorer.ScoreAsync(City, items, CancellationToken.None);

        llm.Requests.Count.ShouldBe(2);
        items[0].Score.ShouldBe(5);
        items[0].Category.ShouldBe("incident");
        items[1].Score.ShouldBeNull();
    }

    [Fact]
    public void Body_normalization_makes_blank_line_paragraphs()
    {
        RewriteService.NormalizeBody("Перший абзац.\nДругий абзац.").ShouldBe("Перший абзац.\n\nДругий абзац.");
        RewriteService.NormalizeBody("Рядок один\nще рядок\n\nДругий").ShouldBe("Рядок один ще рядок\n\nДругий");
    }

    private static string Body(int words, string extra = "")
    {
        // Facts only from the source; filler words have no numbers or capitalized names.
        var filler = Enumerable.Range(0, words - 12).Select(i => i % 2 == 0 ? "мешканці" : "просять").ToList();
        var first = "Двоє дітей у лікарні після аварії на вулиці Шевченка. " + string.Join(' ', filler.Take(filler.Count / 2)) + ".";
        var second = "Поліція затримала водія. " + string.Join(' ', filler.Skip(filler.Count / 2)) + ". " + extra;
        return $"{first}\n\n{second}".Trim();
    }

    private static string Draft(string title, string body) =>
        System.Text.Json.JsonSerializer.Serialize(new { title, body });

    private static NewsItem NewsItem(int id = 1) => new()
    {
        Id = id, City = City.Id, Url = $"https://t.me/s/x/{id}", RawText = Source, ContentHash = "h", PublishedAt = DateTimeOffset.UtcNow
    };

    private static (RewriteService, FakePosts) CreateRewrite(FakeLlm llm)
    {
        var posts = new FakePosts();
        var service = new RewriteService(llm, new FakePrompts(), new TextValidator(new FactChecker()), new FakeNews(), posts,
            new FixedClock(), NullLogger<RewriteService>.Instance);
        return (service, posts);
    }

    private sealed class FakeLlm(params string[] responses) : ILlmClient
    {
        public List<LlmRequest> Requests { get; } = [];
        public bool IsConfigured => true;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new LlmResponse(responses[Math.Min(Requests.Count - 1, responses.Length - 1)], 100, 50));
        }
    }

    private sealed class FakePrompts : IPromptStore
    {
        public string Render(string name, IReadOnlyDictionary<string, string> values) =>
            $"{name}\n{string.Join('\n', values.Select(v => $"{v.Key}: {v.Value}"))}";
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakePosts : IPostRepository
    {
        public List<Post> Saved { get; } = [];
        public void Add(Post post) => Saved.Add(post);
        public void Remove(Post post) => Saved.Remove(post);
        public Task<Post?> GetAsync(int id, CancellationToken ct) => Task.FromResult(Saved.FirstOrDefault(p => p.Id == id));
        public Task<Post?> GetLatestAsync(int newsItemId, PostKind kind, CancellationToken ct) => Task.FromResult(Saved.LastOrDefault());
        public Task<int> GetNextVersionAsync(int newsItemId, PostKind kind, CancellationToken ct) => Task.FromResult(Saved.Count + 1);
        public Task<List<Post>> GetLatestByCityAsync(string city, PostKind kind, bool approvedOnly, CancellationToken ct) => Task.FromResult(Saved);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeNews : INewsRepository
    {
        public Task<HashSet<(int SourceId, string Url)>> GetExistingKeysAsync(IReadOnlyCollection<int> sourceIds, CancellationToken ct) => Task.FromResult(new HashSet<(int, string)>());
        public void Add(NewsItem item) { }
        public Task<List<NewsItem>> GetActiveAsync(string city, DateTimeOffset since, CancellationToken ct) => Task.FromResult(new List<NewsItem>());
        public Task<List<NewsItem>> GetByStatusAsync(string city, NewsStatus status, CancellationToken ct) => Task.FromResult(new List<NewsItem>());
        public Task<NewsItem?> GetAsync(int id, CancellationToken ct) => Task.FromResult<NewsItem?>(null);
        public Task<List<NewsItem>> GetClusterMembersAsync(NewsItem primary, CancellationToken ct) => Task.FromResult(new List<NewsItem>());
        public Task<Dictionary<int, int>> GetClusterSizesAsync(IReadOnlyCollection<int> primaryIds, CancellationToken ct) => Task.FromResult(new Dictionary<int, int>());
        public Task<NewsCounts> GetCountsAsync(string city, DateTimeOffset since, int minScore, CancellationToken ct) => Task.FromResult(new NewsCounts(0, 0, 0, 0, 0));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
