using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

/// <summary>"📰 Стаття для сайту": choose one picked news item and generate a long article.</summary>
public sealed class ArticleHandler(BotUi ui, NewsHandler newsHandler, PostsHandler postsHandler, INewsRepository news) : ICallbackHandler
{
    private const int MaxChoices = 12;

    public string Module => "a";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "list":
                await ShowChoicesAsync(ctx, ct);
                return null;
            case "gen" when data.Id is { } id:
                var city = await newsHandler.CurrentCityAsync(ctx, ct);
                return postsHandler.StartRewrite(ctx, city, [id], PostKind.Article) ? "Пишу статтю" : "Рерайт уже йде";
            default:
                return null;
        }
    }

    private async Task ShowChoicesAsync(ChatContext ctx, CancellationToken ct)
    {
        var city = await newsHandler.CurrentCityAsync(ctx, ct);
        var items = (await news.GetByStatusAsync(city.Id, NewsStatus.Picked, ct))
            .Concat(await news.GetByStatusAsync(city.Id, NewsStatus.Done, ct))
            .ToList();
        if (items.Count == 0)
        {
            await ui.ShowAsync(ctx, "Спершу обери новини (✅ Беру) — стаття пишеться з однієї з них.", Rows([Btn("📋 Кандидати", new CallbackData("n", "c", 0))], BackToMenu), ct);
            return;
        }

        var sizes = await news.GetClusterSizesAsync(items.Select(i => i.Id).ToList(), ct);
        var ordered = items.OrderByDescending(i => sizes.GetValueOrDefault(i.Id)).ThenByDescending(i => i.Score ?? 0).Take(MaxChoices);
        var rows = ordered.Select(i =>
        {
            var sources = 1 + sizes.GetValueOrDefault(i.Id);
            return new[] { Btn($"{(sources > 1 ? $"[{sources} дж.] " : "")}{Truncate(NewsView.TitleOf(i), 50)}", new CallbackData("a", "gen", i.Id)) };
        });

        await ui.ShowAsync(ctx, "📰 <b>Стаття для сайту</b>\nОбери новину (краще ту, що підтверджена кількома джерелами):",
            Rows([.. rows, [Btn("⬅️ Новини", new CallbackData("n", "menu"))]]), ct);
    }
}

/// <summary>"📄 Експорт": the .docx package and sending every ready post as a separate message.</summary>
public sealed class ExportHandler(
    BotUi ui,
    NewsHandler newsHandler,
    INewsRepository news,
    IPostRepository posts,
    ISourceRepository sources,
    IDocxExporter docx,
    IClock clock,
    AppPaths paths) : ICallbackHandler
{
    public string Module => "x";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "docx":
                return await ExportAsync(ctx, ct);
            case "all":
                return await SendAllAsync(ctx, ct);
            default:
                return null;
        }
    }

    private async Task<string?> ExportAsync(ChatContext ctx, CancellationToken ct)
    {
        var city = await newsHandler.CurrentCityAsync(ctx, ct);
        var social = await ReadyPostsAsync(city.Id, ct);
        var articles = await posts.GetLatestByCityAsync(city.Id, PostKind.Article, approvedOnly: false, ct);
        var article = articles.Where(a => a.IsApproved).DefaultIfEmpty(articles.LastOrDefault()).Last();
        var rejected = (await news.GetByStatusAsync(city.Id, NewsStatus.Rejected, ct))
            .Where(n => !(n.RejectReason == RejectReason.Duplicate && n.ClusterId is not null && n.ClusterId != n.Id))
            .ToList();
        var citySources = (await sources.GetAllAsync(ct)).Where(s => s.City == city.Id && s.IsEnabled).ToList();

        var export = new NewsExport(
            city.Name,
            clock.UtcNow,
            citySources.Max(s => s.LastFetchedAt),
            citySources.Select(s => (s.Name, s.Url)).ToList(),
            social.Select(p => new ExportPost(p.Title, p.Body, p.NewsItem!.Url)).ToList(),
            article is null ? null : new ExportPost(article.Title, article.Body, article.NewsItem!.Url),
            rejected.Select(n => new ExportRejected(NewsView.TitleOf(n), n.Url, NewsView.RejectReasonText(n.RejectReason, n.RejectNote))).ToList());

        var path = await docx.WriteAsync(export, paths.Exports, ct);
        var caption = $"📄 {Plural(export.Posts.Count, "пост", "пости", "постів")} · стаття: {(article is null ? "немає" : "є")} · не взяті: {export.Rejected.Count}";
        await ui.SendDocumentAsync(ctx.ChatId, path, caption, Rows([Btn("📨 Надіслати всі пости в чат по одному", new CallbackData("x", "all"))], BackToMenu), ct);

        if (export.Rejected.Count < 3)
            await ui.SendAsync(ctx, $"ℹ️ У документі лише {export.Rejected.Count} відкинутих новин, а для тестового потрібно 3+. Відкинь ще кілька кандидатів з причиною.", null, ct);
        return "Готую .docx";
    }

    private async Task<string?> SendAllAsync(ChatContext ctx, CancellationToken ct)
    {
        var city = await newsHandler.CurrentCityAsync(ctx, ct);
        var ready = await ReadyPostsAsync(city.Id, ct);
        if (ready.Count == 0)
            return "Немає готових постів";

        foreach (var post in ready)
            await ui.SendAsync(ctx.ChatId, Escape(PostView.PlainText(post)), null, ct);
        return $"Надіслано {ready.Count}";
    }

    /// <summary>Approved posts; if none are approved yet, the latest drafts so the export is never empty.</summary>
    private async Task<List<Post>> ReadyPostsAsync(string city, CancellationToken ct)
    {
        var approved = await posts.GetLatestByCityAsync(city, PostKind.Social, approvedOnly: true, ct);
        return approved.Count > 0 ? approved : await posts.GetLatestByCityAsync(city, PostKind.Social, approvedOnly: false, ct);
    }
}
