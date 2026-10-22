using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Seo;
using SystematiaBot.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2 || args[0] != "seo")
{
    Console.WriteLine("Usage: SystematiaBot.Cli seo <url> [--full]");
    return 2;
}

var builder = Host.CreateApplicationBuilder();
builder.Configuration.Sources.Clear();
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "config", "appsettings.json"), optional: false);
builder.Services.AddSerilog(lc => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));
builder.Services.AddCoreServices().AddSeo(builder.Configuration);

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var services = host.Services;
var options = services.GetRequiredService<IOptions<SeoOptions>>().Value;
var limit = args.Contains("--full") ? options.FullPages : options.QuickPages;

var lastReport = DateTime.MinValue;
var progress = new Progress<SeoProgress>(p =>
{
    if ((DateTime.UtcNow - lastReport).TotalSeconds < 2)
        return;
    lastReport = DateTime.UtcNow;
    Console.WriteLine($"  … {p.Stage}: sitemap {p.SitemapUrls} URL · перевірено {p.Checked}/{p.Total} · проблем {p.IssuesFound}");
});

var crawl = await services.GetRequiredService<ISeoCrawler>().CrawlAsync(args[1], limit, progress, cts.Token);
var issues = services.GetRequiredService<SeoIssueDetector>().Detect(crawl.Pages, crawl.Site);
var summary = SeoSummary.Build(crawl, issues);
var csvPath = await services.GetRequiredService<ISeoReportExporter>().WriteCsvAsync(crawl, issues, Path.Combine(Environment.CurrentDirectory, "exports"), CancellationToken.None);

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
return 0;
