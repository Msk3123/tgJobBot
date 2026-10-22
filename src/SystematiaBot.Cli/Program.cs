using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;
using SystematiaBot.Core.Seo;
using SystematiaBot.Infrastructure;
using SystematiaBot.Infrastructure.Persistence;
using SystematiaBot.Infrastructure.Sources;

Console.OutputEncoding = Encoding.UTF8;

const string usage = """
    Usage:
      SystematiaBot.Cli seo <url> [--full]     SEO audit, prints summary and writes exports/*.csv
      SystematiaBot.Cli sources [city]         checks every source: posts in the last 7 days
      SystematiaBot.Cli collect [city]         collects news into data/bot.db (same pipeline as the bot)
    """;

if (args.Length == 0 || args[0] is not ("seo" or "sources" or "collect") || args[0] == "seo" && args.Length < 2)
{
    Console.WriteLine(usage);
    return 2;
}

var stateRoot = Environment.CurrentDirectory;
var builder = Host.CreateApplicationBuilder();
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "config", "appsettings.json"), optional: false)
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables();
builder.Services.AddSerilog(lc => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));
Directory.CreateDirectory(Path.Combine(stateRoot, "data"));
builder.Services
    .AddCoreServices()
    .AddPersistence(Path.Combine(stateRoot, "data", "bot.db"))
    .AddSeo(builder.Configuration)
    .AddNews(builder.Configuration)
    .AddLlm(builder.Configuration, Path.Combine(AppContext.BaseDirectory, "prompts"));
builder.Services.AddSingleton<ICityCatalog>(new JsonCityCatalog(Path.Combine(AppContext.BaseDirectory, "config", "sources.json")));

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var services = host.Services;
return args[0] switch
{
    "seo" => await RunSeoAsync(args[1], args.Contains("--full"), OptionValue("--save-json")),
    "sources" => await CheckSourcesAsync(args.ElementAtOrDefault(1) ?? "sheptytsky"),
    _ => await CollectAsync(args.ElementAtOrDefault(1) ?? "sheptytsky")
};

string? OptionValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

async Task<int> RunSeoAsync(string url, bool full, string? saveJson)
{
    var options = services.GetRequiredService<IOptions<SeoOptions>>().Value;
    var lastReport = DateTime.MinValue;
    var progress = new Progress<SeoProgress>(p =>
    {
        if ((DateTime.UtcNow - lastReport).TotalSeconds < 2)
            return;
        lastReport = DateTime.UtcNow;
        Console.WriteLine($"  … {p.Stage}: sitemap {p.SitemapUrls} URL · перевірено {p.Checked}/{p.Total} · проблем {p.IssuesFound}");
    });

    var crawl = await services.GetRequiredService<ISeoCrawler>().CrawlAsync(url, full ? options.FullPages : options.QuickPages, progress, cts.Token);
    var issues = services.GetRequiredService<SeoIssueDetector>().Detect(crawl.Pages, crawl.Site);
    var summary = SeoSummary.Build(crawl, issues);
    var csvPath = await services.GetRequiredService<ISeoReportExporter>().WriteCsvAsync(crawl, issues, Path.Combine(stateRoot, "exports"), CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine($"SEO-аудит {summary.StartUrl}");
    Console.WriteLine(summary.SitemapUrlCount > 0
        ? $"Перевірено {summary.PagesChecked} з {summary.SitemapUrlCount} сторінок sitemap"
        : $"Sitemap не знайдено, перевірено {summary.PagesChecked} сторінок за посиланнями");
    foreach (var group in summary.Groups.Take(12))
    {
        var icon = group.Severity switch { SeoSeverity.High => "🔴", SeoSeverity.Medium => "🟠", SeoSeverity.Low => "🟡", _ => "ℹ️" };
        Console.WriteLine($"{icon} {group.Code} — {group.Count} ({SeoSummary.Describe(group.Code)}), напр. {group.ExampleUrl}{(group.ExampleDetail is null ? "" : $" [{group.ExampleDetail}]")}");
    }

    Console.WriteLine($"CSV: {csvPath}");

    if (saveJson is not null)
    {
        // Snapshot for demo mode: pages without outgoing links to keep the file small.
        var snapshot = new SeoCrawlResult
        {
            StartUrl = crawl.StartUrl,
            Site = crawl.Site,
            SitemapUrlCount = crawl.SitemapUrlCount,
            Pages = crawl.Pages.Select(p => new SeoPage
            {
                Url = p.Url, Status = p.Status, RedirectTo = p.RedirectTo, Error = p.Error, FromSitemap = p.FromSitemap, IsHtml = p.IsHtml,
                Title = p.Title, Description = p.Description, H1 = p.H1, Canonical = p.Canonical, Robots = p.Robots, Hreflangs = p.Hreflangs,
                Images = p.Images, ImagesWithoutAlt = p.ImagesWithoutAlt, WordCount = p.WordCount
            }).ToList()
        };
        await File.WriteAllTextAsync(saveJson, System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        Console.WriteLine($"JSON: {saveJson}");
    }

    return 0;
}

async Task<int> CheckSourcesAsync(string cityId)
{
    var city = services.GetRequiredService<ICityCatalog>().Find(cityId) ?? throw new ArgumentException($"Unknown city {cityId}");
    var factory = services.GetRequiredService<INewsSourceFactory>();
    var since = DateTimeOffset.UtcNow.AddDays(-7);

    Console.WriteLine("| Назва | Тип | URL | Постів за 7 днів | Про місто |");
    Console.WriteLine("|---|---|---|---|---|");
    foreach (var config in city.Sources)
    {
        var source = new Source
        {
            City = city.Id, Name = config.Name, Kind = config.Kind, Url = config.Url, IsCityOnly = config.IsCityOnly,
            Selectors = config.Selectors is null ? null : System.Text.Json.JsonSerializer.Serialize(config.Selectors)
        };
        try
        {
            var items = (await factory.For(config.Kind).FetchAsync(source, since, cts.Token)).Where(i => i.PublishedAt >= since).ToList();
            var local = config.IsCityOnly ? items.Count : items.Count(i => LocalityFilter.MentionsCity(i.Title + "\n" + i.Text, city));
            Console.WriteLine($"| {config.Name} | {config.Kind} | {config.Url} | {items.Count} | {(config.IsCityOnly ? "усі (міське джерело)" : local.ToString())} |");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"| {config.Name} | {config.Kind} | {config.Url} | ПОМИЛКА: {ex.Message} | |");
        }
    }

    return 0;
}

async Task<int> CollectAsync(string cityId)
{
    var city = services.GetRequiredService<ICityCatalog>().Find(cityId) ?? throw new ArgumentException($"Unknown city {cityId}");
    await using var scope = services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.MigrateAsync(cts.Token);

    var progress = new Progress<CollectProgress>(p => Console.WriteLine($"  … {p.Stage}: {p.SourcesDone}/{p.SourcesTotal} джерел · {p.Posts} постів{(p.ToScore > 0 ? $" · AI оцінює {p.ToScore}" : "")}"));
    var result = await scope.ServiceProvider.GetRequiredService<NewsCollectionService>().CollectAsync(city, progress, cts.Token);
    Console.WriteLine(result);

    var news = scope.ServiceProvider.GetRequiredService<INewsRepository>();
    var candidates = (await news.GetByStatusAsync(city.Id, NewsStatus.New, cts.Token))
        .OrderByDescending(n => n.Score ?? 0).ThenByDescending(n => n.PublishedAt).Take(15);
    foreach (var item in candidates)
        Console.WriteLine($"[{item.Score?.ToString() ?? "-"}] {item.PublishedAt:dd.MM HH:mm} {item.Source?.Name}: {Truncate(item.Title ?? item.RawText, 100)}");
    return 0;
}

static string Truncate(string s, int max)
{
    s = s.ReplaceLineEndings(" ");
    return s.Length <= max ? s : s[..max] + "…";
}
