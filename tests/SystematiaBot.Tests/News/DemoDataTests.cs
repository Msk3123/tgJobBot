using System.Text.Json;
using Shouldly;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Validation;
using Xunit.Abstractions;

namespace SystematiaBot.Tests.News;

/// <summary>Prepared demo texts must pass the same deterministic validator as generated ones.</summary>
public class DemoDataTests(ITestOutputHelper output)
{
    private static readonly string Root = FindRoot();
    private readonly TextValidator _validator = new(new FactChecker());

    [Fact]
    public void Demo_posts_and_article_pass_validation()
    {
        var candidates = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(Path.Combine(Root, "demo", "candidates.json")))!
            .ToDictionary(c => c.GetProperty("id").GetInt32(), c => c);
        using var posts = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "demo", "posts.json")));

        var failures = new List<string>();
        void Check(PostKind kind, int id, JsonElement post)
        {
            var source = candidates[id];
            var sourceText = $"{(source.GetProperty("title").ValueKind == JsonValueKind.String ? source.GetProperty("title").GetString() : "")}\n{source.GetProperty("text").GetString()}";
            var report = _validator.Validate(kind, post.GetProperty("title").GetString()!, post.GetProperty("body").GetString()!, source.GetProperty("url").GetString(), [sourceText]);
            output.WriteLine($"{kind} {id}: title {report.TitleLength}, words {report.WordCount}, paragraphs {report.ParagraphCount}; {string.Join(" | ", report.Issues.Select(i => $"{i.Level}: {i.Message}"))}");
            if (report.HasErrors)
                failures.Add($"{kind} {id}: {string.Join("; ", report.FixList)}");
        }

        foreach (var post in posts.RootElement.GetProperty("posts").EnumerateObject())
            Check(PostKind.Social, int.Parse(post.Name), post.Value);
        var article = posts.RootElement.GetProperty("article");
        Check(PostKind.Article, article.GetProperty("candidateId").GetInt32(), article);

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Demo_provider_loads_all_files()
    {
        var provider = new SystematiaBot.Bot.Demo.DemoDataProvider(new SystematiaBot.Bot.Hosting.AppPaths(Root, Root), new SystematiaBot.Core.Abstractions.SystemClock());

        provider.Candidates.Count.ShouldBe(8);
        provider.Posts.Keys.ShouldAllBe(id => provider.Find(id) != null);
        provider.Find(provider.Article.CandidateId).ShouldNotBeNull();
        provider.Seo.Pages.Count.ShouldBe(50);
        provider.Seo.SitemapUrlCount.ShouldBeGreaterThan(1000);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SystematiaBot.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
