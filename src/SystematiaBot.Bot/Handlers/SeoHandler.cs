using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Seo;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

public sealed class SeoHandler(
    BotUi ui,
    UserStateStore state,
    BackgroundJobRunner jobs,
    IServiceScopeFactory scopeFactory,
    IOptions<SeoOptions> seoOptions,
    AppPaths paths,
    IClock clock,
    ILogger<SeoHandler> logger) : ICallbackHandler, ITextInputHandler
{
    public const string JobType = "seo";
    private const int TopIssues = 8;

    public string Module => "s";
    public AwaitingInput Awaiting => AwaitingInput.SeoUrl;

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "open":
                await OpenAsync(ctx, ct);
                return null;
            case "mode":
                return await StartAsync(ctx, full: data.Arg == "f", ct);
            default:
                return null;
        }
    }

    public async Task OpenAsync(ChatContext ctx, CancellationToken ct)
    {
        state.Set(ctx.ChatId, AwaitingInput.SeoUrl);
        await ui.ShowAsync(ctx, "🔍 <b>SEO-аудит</b>\nНадішли URL сайту (напр. https://maxidveri.com)", Rows(BackToMenu), ct);
    }

    public async Task HandleAsync(ChatContext ctx, string text, UserState? _, CancellationToken ct)
    {
        if (!TryNormalizeUrl(text, out var url))
        {
            await ui.SendAsync(ctx, "Це не схоже на адресу сайту. Надішли, наприклад, <code>https://maxidveri.com</code>", Rows(BackToMenu), ct);
            return;
        }

        state.Reset(ctx.ChatId);
        state.SetSeoUrl(ctx.ChatId, url);
        var options = seoOptions.Value;
        await ui.SendAsync(ctx, $"🔍 Сайт: <b>{Escape(url)}</b>\nОбери режим перевірки:", Rows(
            [Btn($"⚡ Швидко ({options.QuickPages})", Cb.SeoMode(false)), Btn($"🔬 Повно (до {options.FullPages})", Cb.SeoMode(true))],
            BackToMenu), ct);
    }

    private async Task<string?> StartAsync(ChatContext ctx, bool full, CancellationToken ct)
    {
        var url = state.GetSeoUrl(ctx.ChatId);
        if (url is null)
        {
            await OpenAsync(ctx, ct);
            return "Спершу надішли URL";
        }

        var limit = full ? seoOptions.Value.FullPages : seoOptions.Value.QuickPages;
        var jobCtx = new ChatContext { ChatId = ctx.ChatId, UserId = ctx.UserId, IsOwner = ctx.IsOwner };
        var started = jobs.TryStart(ctx.ChatId, JobType, token => RunAsync(jobCtx, url, full, limit, token));
        return started ? "Запускаю аудит" : "Аудит уже йде — дочекайся або натисни ⛔ Скасувати";
    }

    private async Task RunAsync(ChatContext ctx, string url, bool full, int limit, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var crawler = scope.ServiceProvider.GetRequiredService<ISeoCrawler>();
        var detector = scope.ServiceProvider.GetRequiredService<SeoIssueDetector>();
        var exporter = scope.ServiceProvider.GetRequiredService<ISeoReportExporter>();
        var runs = scope.ServiceProvider.GetRequiredService<ISeoRunRepository>();

        var run = new SeoRun { ChatId = ctx.ChatId, StartUrl = url, Mode = full ? SeoMode.Full : SeoMode.Quick, StartedAt = clock.UtcNow };
        await runs.AddAsync(run, ct);

        var progressMessage = await ui.StartProgressAsync(ctx, $"⏳ SEO-аудит {Escape(url)}\nЧитаю robots.txt і sitemap…", ct);
        SeoProgress? latest = null;
        var progress = new SyncProgress<SeoProgress>(p => latest = p);

        SeoCrawlResult crawl;
        try
        {
            await using (ProgressTicker.Start(progressMessage, () => latest is null ? null : RenderProgress(url, latest), ct))
                crawl = await crawler.CrawlAsync(url, limit, progress, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
        {
            logger.LogWarning(ex, "SEO audit of {Url} failed", url);
            await progressMessage.CompleteAsync($"❌ Не вдалося відкрити {Escape(url)}: {Escape(ex.Message)}", Rows([Btn("🔁 Ще раз", Cb.SeoOpen)], BackToMenu), CancellationToken.None);
            return;
        }

        // The crawl may have been cancelled; the report of what was checked is still worth sending.
        var issues = detector.Detect(crawl.Pages, crawl.Site);
        var summary = SeoSummary.Build(crawl, issues);
        var csvPath = await exporter.WriteCsvAsync(crawl, issues, paths.Exports, CancellationToken.None);

        run.FinishedAt = clock.UtcNow;
        run.PagesCrawled = crawl.Pages.Count;
        run.CsvPath = csvPath;
        run.SummaryJson = JsonSerializer.Serialize(summary.Groups);
        await runs.UpdateAsync(run, CancellationToken.None);

        await progressMessage.CompleteAsync(RenderSummary(summary), Rows([Btn("🔁 Ще раз", Cb.SeoOpen)], BackToMenu), CancellationToken.None);
        await ui.SendDocumentAsync(ctx.ChatId, csvPath, "📎 Повний звіт по сторінках (відкривається в Excel)", null, CancellationToken.None);
    }

    private static string RenderProgress(string url, SeoProgress p) => p.Stage switch
    {
        "robots.txt" or "sitemap" => $"⏳ SEO-аудит {Escape(url)}\nЧитаю {p.Stage}…",
        "links" => $"⏳ SEO-аудит {Escape(url)}\nSitemap не знайдено, йду за посиланнями · перевірено {p.Checked}/{p.Total} · знайдено проблем: {p.IssuesFound}",
        _ => $"⏳ SEO-аудит {Escape(url)}\nSitemap: {Number(p.SitemapUrls)} URL · перевірено {p.Checked}/{p.Total} · знайдено проблем: {p.IssuesFound}"
    };

    public static string RenderSummary(SeoSummary summary)
    {
        var host = Uri.TryCreate(summary.StartUrl, UriKind.Absolute, out var uri) ? uri.Host : summary.StartUrl;
        var sb = new StringBuilder();
        sb.AppendLine(summary.Cancelled ? $"⛔ <b>SEO-аудит {Escape(host)} — скасовано</b>" : $"✅ <b>SEO-аудит {Escape(host)}</b>");
        sb.AppendLine(summary.SitemapUrlCount > 0
            ? $"Перевірено {Number(summary.PagesChecked)} з {Plural(summary.SitemapUrlCount, "сторінки", "сторінок", "сторінок")} sitemap"
            : $"Sitemap не знайдено · перевірено {Plural(summary.PagesChecked, "сторінку", "сторінки", "сторінок")} за посиланнями");

        if (summary.Groups.Count == 0)
        {
            sb.AppendLine().Append("Проблем не знайдено 🎉");
            return sb.ToString();
        }

        sb.AppendLine($"Знайдено проблем: {Number(summary.TotalIssues)}").AppendLine();
        foreach (var group in summary.Groups.Take(TopIssues))
        {
            var icon = group.Severity switch { SeoSeverity.High => "🔴", SeoSeverity.Medium => "🟠", SeoSeverity.Low => "🟡", _ => "ℹ️" };
            sb.Append($"{icon} <b>{group.Code}</b> — {Plural(group.Count, "сторінка", "сторінки", "сторінок")}");
            sb.AppendLine($" ({Escape(SeoSummary.Describe(group.Code))})");
            var path = Uri.TryCreate(group.ExampleUrl, UriKind.Absolute, out var example) ? example.PathAndQuery : group.ExampleUrl;
            var detail = group.ExampleDetail is null ? "" : $" · {Escape(Truncate(group.ExampleDetail, 60))}";
            sb.AppendLine($"   напр. {Escape(Truncate(path, 70))}{detail}");
        }

        if (summary.Groups.Count > TopIssues)
            sb.AppendLine().Append($"…і ще {summary.Groups.Count - TopIssues} типів — дивись CSV");
        return sb.ToString().TrimEnd();
    }

    public static bool TryNormalizeUrl(string text, out string url)
    {
        url = "";
        var candidate = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!candidate.Contains("://"))
            candidate = "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
            return false;

        url = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}

/// <summary>IProgress that invokes the callback synchronously (Progress&lt;T&gt; posts to the thread pool).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
