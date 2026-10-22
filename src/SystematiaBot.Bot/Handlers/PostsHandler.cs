using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;
using Telegram.Bot.Types.ReplyMarkups;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

/// <summary>Rewriting picked news into social posts, reviewing them, manual edits and the ready list.</summary>
public sealed class PostsHandler(
    BotUi ui,
    UserStateStore state,
    BackgroundJobRunner jobs,
    IServiceScopeFactory scopeFactory,
    NewsHandler newsHandler,
    INewsRepository news,
    IPostRepository posts,
    ILogger<PostsHandler> logger) : ICallbackHandler, ITextInputHandler
{
    public const string RewriteJob = "rewrite";

    public string Module => "p";
    public AwaitingInput Awaiting => AwaitingInput.PostEdit;

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "rw":
                return await StartRewritePickedAsync(ctx, ct);
            case "list":
                await ShowReadyAsync(ctx, data.Id ?? 0, ct);
                return null;
            case "ok" when data.Id is { } id:
                return await ApproveAsync(ctx, id, ct);
            case "re" when data.Id is { } id:
                return await StartRewriteOneAsync(ctx, id, ct);
            case "ed" when data.Id is { } id:
                state.Set(ctx.ChatId, AwaitingInput.PostEdit, id);
                await ui.SendAsync(ctx, "✏️ Надішли свій текст одним повідомленням.\nПерший рядок — заголовок, далі — текст (абзаци через порожній рядок).", null, ct);
                return null;
            case "rm" when data.Id is { } id:
                return await RemoveAsync(ctx, id, ct);
            default:
                return null;
        }
    }

    private async Task<string?> StartRewritePickedAsync(ChatContext ctx, CancellationToken ct)
    {
        var city = await newsHandler.CurrentCityAsync(ctx, ct);
        var picked = await news.GetByStatusAsync(city.Id, NewsStatus.Picked, ct);
        var pending = new List<NewsItem>();
        foreach (var item in picked.OrderBy(n => n.PublishedAt))
        {
            if (await posts.GetLatestAsync(item.Id, PostKind.Social, ct) is null)
                pending.Add(item);
        }

        if (picked.Count == 0)
            return "Спершу обери новини (✅ Беру)";
        if (pending.Count == 0)
        {
            await ui.SendAsync(ctx, "Для всіх обраних новин пости вже є. Відкрий «✅ Готові пости» або натисни «🔁 Переписати» під постом.", Rows([Btn("📰 Новини", new CallbackData("n", "menu"))]), ct);
            return null;
        }

        return StartRewrite(ctx, city, pending.Select(p => p.Id).ToList(), PostKind.Social)
            ? $"Пишу {Plural(pending.Count, "пост", "пости", "постів")}"
            : "Рерайт уже йде";
    }

    private async Task<string?> StartRewriteOneAsync(ChatContext ctx, int postId, CancellationToken ct)
    {
        var post = await posts.GetAsync(postId, ct);
        if (post?.NewsItem is null)
            return "Пост не знайдено";

        var city = await newsHandler.CurrentCityAsync(ctx, ct);
        return StartRewrite(ctx, city, [post.NewsItemId], post.Kind) ? "Переписую" : "Рерайт уже йде";
    }

    public bool StartRewrite(ChatContext ctx, CityConfig city, IReadOnlyList<int> newsIds, PostKind kind)
    {
        var jobCtx = Detached(ctx);
        return jobs.TryStart(ctx.ChatId, RewriteJob, token => RewriteManyAsync(jobCtx, city, newsIds, token, kind));
    }

    private async Task RewriteManyAsync(ChatContext ctx, CityConfig city, IReadOnlyList<int> newsIds, CancellationToken ct, PostKind kind = PostKind.Social)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rewriter = scope.ServiceProvider.GetRequiredService<RewriteService>();
        var repo = scope.ServiceProvider.GetRequiredService<INewsRepository>();
        var noun = kind == PostKind.Article ? "статтю" : "пост";
        var progress = await ui.StartProgressAsync(ctx, $"✍️ Пишу {noun} 1/{newsIds.Count}…", ct);
        int ok = 0, failed = 0;

        for (var i = 0; i < newsIds.Count; i++)
        {
            await progress.UpdateAsync($"✍️ Пишу {noun} {i + 1}/{newsIds.Count}…", ct, force: true);
            var item = await repo.GetAsync(newsIds[i], ct);
            if (item is null)
                continue;

            try
            {
                var result = await rewriter.RewriteAsync(item, city, kind, ct);
                var header = kind == PostKind.Article ? "📰 <b>Стаття для сайту</b>" : $"📝 Пост {i + 1}/{newsIds.Count}";
                if (result.Report.HasErrors)
                    header += $" · ⚠️ не пройшов перевірку після {Plural(result.Attempts, "спроби", "спроб", "спроб")}";
                await SendCardAsync(ctx, result.Post, header, CancellationToken.None);
                ok++;
            }
            catch (LlmUnavailableException ex)
            {
                await progress.CompleteAsync($"⛔ {Escape(ex.Message)}", Rows(BackToMenu), CancellationToken.None);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Rewrite of news {Id} failed", item.Id);
                await ui.SendAsync(ctx, $"😕 Не вдалося написати текст для «{Escape(NewsView.TitleOf(item))}». Деталі в лозі.", null, CancellationToken.None);
            }
        }

        var summary = $"✅ Готово: {ok} з {newsIds.Count}" + (failed > 0 ? $", з помилкою: {failed}" : "") +
                      "\nПеревір кожен текст і натисни 👍 Готово.";
        await progress.CompleteAsync(summary, Rows([Btn("✅ Готові пости", new CallbackData("p", "list", 0)), Btn("📰 Новини", new CallbackData("n", "menu"))]), CancellationToken.None);
    }

    public async Task SendCardAsync(ChatContext ctx, Post post, string header, CancellationToken ct)
    {
        var text = PostView.Render(post, header, PostView.ReportOf(post));
        await ui.SendAsync(ctx, text, CardKeyboard(post), ct);
    }

    public static InlineKeyboardMarkup CardKeyboard(Post post) => Rows(
        post.IsApproved
            ? [Btn("✅ Схвалено", Cb.Noop)]
            : [Btn("👍 Готово", new CallbackData("p", "ok", post.Id))],
        [Btn("🔁 Переписати", new CallbackData("p", "re", post.Id)), Btn("✏️ Свій текст", new CallbackData("p", "ed", post.Id)), Btn("🗑 Прибрати", new CallbackData("p", "rm", post.Id))]);

    private async Task<string?> ApproveAsync(ChatContext ctx, int postId, CancellationToken ct)
    {
        var post = await posts.GetAsync(postId, ct);
        if (post?.NewsItem is null)
            return "Пост не знайдено";

        post.IsApproved = true;
        if (post.Kind == PostKind.Social)
            post.NewsItem.Status = NewsStatus.Done;
        await posts.SaveChangesAsync(ct);

        if (ctx.MessageId is { } messageId)
        {
            var header = post.Kind == PostKind.Article ? "📰 <b>Стаття для сайту</b> · ✅ схвалено" : "📝 Пост · ✅ схвалено";
            await ui.TryEditAsync(ctx.ChatId, messageId, BotUi.WithBanner(ctx, PostView.Render(post, header, PostView.ReportOf(post))), CardKeyboard(post), ct);
        }

        return "Схвалено 👍";
    }

    private async Task<string?> RemoveAsync(ChatContext ctx, int postId, CancellationToken ct)
    {
        var post = await posts.GetAsync(postId, ct);
        if (post?.NewsItem is null)
            return "Пост не знайдено";

        var item = post.NewsItem;
        var title = post.Title;
        // Remove all versions of this text and return the news to the candidates.
        for (var latest = post; latest is not null; latest = await posts.GetLatestAsync(item.Id, post.Kind, ct))
        {
            posts.Remove(latest);
            await posts.SaveChangesAsync(ct);
        }

        if (post.Kind == PostKind.Social)
        {
            item.Status = NewsStatus.New;
            await news.SaveChangesAsync(ct);
        }

        if (ctx.MessageId is { } messageId)
            await ui.TryEditAsync(ctx.ChatId, messageId, $"🗑 Прибрано: {Escape(title)}", null, ct);
        return "Прибрано, новина повернулась у кандидати";
    }

    /// <summary>Manual text for a post (first line = title).</summary>
    public async Task HandleAsync(ChatContext ctx, string text, UserState? userState, CancellationToken ct)
    {
        state.Reset(ctx.ChatId);
        if (userState?.TargetId is not { } postId || await posts.GetAsync(postId, ct) is not { } previous)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<RewriteService>().SaveManualAsync(previous, text, ct);
        var post = await posts.GetAsync(result.Post.Id, ct) ?? result.Post;
        await SendCardAsync(ctx, post, $"✏️ Твоя версія (v{post.Version})", ct);
    }

    private async Task ShowReadyAsync(ChatContext ctx, int index, CancellationToken ct)
    {
        var city = await newsHandler.CurrentCityAsync(ctx, ct);
        var ready = await posts.GetLatestByCityAsync(city.Id, PostKind.Social, approvedOnly: true, ct);
        if (ready.Count == 0)
        {
            await ui.ShowAsync(ctx, "Готових постів ще немає. Обери новини і натисни «✍️ Переписати обрані».", Rows([Btn("📰 Новини", new CallbackData("n", "menu"))]), ct);
            return;
        }

        index = Math.Clamp(index, 0, ready.Count - 1);
        var post = ready[index];
        var text = PostView.Render(post, $"✅ Готовий пост {index + 1}/{ready.Count}", PostView.ReportOf(post));
        if (text.Length > MaxMessageLength)
            text = Truncate(text, MaxMessageLength - 10);

        await ui.ShowAsync(ctx, text, Rows(
            [Btn("🔁 Переписати", new CallbackData("p", "re", post.Id)), Btn("✏️ Свій текст", new CallbackData("p", "ed", post.Id)), Btn("🗑 Прибрати", new CallbackData("p", "rm", post.Id))],
            [
                Btn("◀️", new CallbackData("p", "list", (index - 1 + ready.Count) % ready.Count)),
                Btn($"{index + 1}/{ready.Count}", Cb.Noop),
                Btn("▶️", new CallbackData("p", "list", (index + 1) % ready.Count)),
                Btn("📰 Новини", new CallbackData("n", "menu"))
            ]), ct);
    }

    private static ChatContext Detached(ChatContext ctx) => new() { ChatId = ctx.ChatId, UserId = ctx.UserId, IsOwner = ctx.IsOwner };
}
