using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Handlers;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;
using SystematiaBot.Core.Seo;
using SystematiaBot.Core.Validation;
using Telegram.Bot.Types.ReplyMarkups;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Demo;

/// <summary>
/// The same screens as the owner gets (news, posts, article, export, SEO), driven by demo/*.json.
/// Generation is simulated with a 1–2 s delay; validation runs for real because it is deterministic.
/// </summary>
public sealed class DemoHandler(
    BotUi ui,
    DemoDataProvider demo,
    BackgroundJobRunner jobs,
    UserStateStore state,
    TextValidator validator,
    SeoIssueDetector seoDetector,
    ISeoReportExporter csv,
    IDocxExporter docx,
    IClock clock,
    AppPaths paths,
    IOptions<NewsOptions> newsOptions)
{
    private const int ArticleId = 100_000;
    private static readonly HashSet<string> Modules = ["n", "p", "a", "x", "s"];

    private int TargetPicks => newsOptions.Value.TargetPicks;
    private int MinScore => newsOptions.Value.MinScore;

    public static bool Handles(string module) => Modules.Contains(module);

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        var session = demo.Session(ctx.ChatId);
        switch (data.Module, data.Action)
        {
            case ("n", "menu"):
                await ShowNewsMenuAsync(ctx, session, ct);
                return null;
            case ("n", "col"):
                return Background(ctx, c => CollectAsync(c, session, CancellationToken.None));
            case ("n", "c"):
                await ShowCandidateAsync(ctx, session, ListPosition.Parse(data.Arg), ct);
                return null;
            case ("n", "pk") when data.Id is { } id:
                session.Picked.Add(id);
                session.Rejected.Remove(id);
                await ShowCandidateAsync(ctx, session, ListPosition.Parse(data.Arg), ct);
                return $"Обрано {session.Picked.Count}/{TargetPicks}";
            case ("n", "rj") when data.Id is { } id:
                await ShowRejectReasonsAsync(ctx, id, ListPosition.Parse(data.Arg), ct);
                return null;
            case ("n", "rr") when data.Id is { } id:
                return await RejectAsync(ctx, session, id, data.Arg, ct);
            case ("n", "city"):
                return "У демо доступне лише місто Шептицький";
            case ("p", "rw"):
                return Background(ctx, c => RewriteAsync(c, session, CancellationToken.None));
            case ("p", "list"):
                await ShowReadyAsync(ctx, session, data.Id ?? 0, ct);
                return null;
            case ("p", "ok") when data.Id is { } id:
                session.Approved.Add(id);
                if (ctx.MessageId is { } messageId && BuildPost(session, id) is { } approved)
                    await ui.TryEditAsync(ctx.ChatId, messageId, BotUi.WithBanner(ctx, PostView.Render(approved, "📝 Пост · ✅ схвалено", Validate(approved))), PostsHandler.CardKeyboard(approved), ct);
                return "Схвалено 👍";
            case ("p", "re") when data.Id is { } id:
                return Background(ctx, async c =>
                {
                    await Task.Delay(1500);
                    session.Edited.Remove(id);
                    if (BuildPost(session, id) is { } again)
                        await SendCardAsync(c, again, "📝 Переписано (демо: та сама заготовка)", CancellationToken.None);
                });
            case ("p", "ed") when data.Id is { } id:
                state.Set(ctx.ChatId, AwaitingInput.PostEdit, id);
                await ui.SendAsync(ctx, "✏️ Надішли свій текст: перший рядок — заголовок, далі — текст. Перевірка фактів спрацює по-справжньому.", null, ct);
                return null;
            case ("p", "rm") when data.Id is { } id:
                session.Removed.Add(id);
                session.Approved.Remove(id);
                session.Picked.Remove(id);
                if (ctx.MessageId is { } removedMessage)
                    await ui.TryEditAsync(ctx.ChatId, removedMessage, BotUi.WithBanner(ctx, "🗑 Прибрано"), null, ct);
                return "Прибрано";
            case ("a", "list"):
                await ShowArticleChoicesAsync(ctx, session, ct);
                return null;
            case ("a", "gen"):
                return Background(ctx, c => WriteArticleAsync(c, session, CancellationToken.None));
            case ("x", "docx"):
                await ExportAsync(ctx, session, ct);
                return "Готую .docx";
            case ("x", "all"):
                foreach (var post in ReadyPosts(session))
                    await ui.SendAsync(ctx.ChatId, Escape(PostView.PlainText(post)), null, ct);
                return "Надіслано";
            case ("s", "open"):
                state.Set(ctx.ChatId, AwaitingInput.SeoUrl);
                await ui.ShowAsync(ctx, "🔍 <b>SEO-аудит</b>\nНадішли URL сайту (у демо повернеться заготовлений звіт для https://maxidveri.com)", Rows(BackToMenu), ct);
                return null;
            case ("s", "mode"):
                return Background(ctx, c => SeoAsync(c, CancellationToken.None));
            default:
                return null;
        }
    }

    /// <summary>Simulated long operations run off the update loop, one at a time per chat.</summary>
    private string? Background(ChatContext ctx, Func<ChatContext, Task> work)
    {
        var detached = new ChatContext { ChatId = ctx.ChatId, UserId = ctx.UserId, IsOwner = false };
        return jobs.TryStart(ctx.ChatId, "demo", _ => work(detached)) ? null : "Зачекай, попередня дія ще виконується";
    }

    public async Task HandleTextAsync(ChatContext ctx, string text, UserState current, CancellationToken ct)
    {
        state.Reset(ctx.ChatId);
        var session = demo.Session(ctx.ChatId);
        switch (current.Awaiting)
        {
            case AwaitingInput.SeoUrl:
                await ui.SendAsync(ctx, "🔍 Обери режим перевірки:", Rows(
                    [Btn("⚡ Швидко (50)", Cb.SeoMode(false)), Btn("🔬 Повно (до 500)", Cb.SeoMode(true))], BackToMenu), ct);
                break;
            case AwaitingInput.RejectNote when current.TargetId is { } id:
                session.Rejected[id] = Truncate(text.Trim(), 300);
                session.Picked.Remove(id);
                await ShowCandidateAsync(ctx, session, ListPosition.Parse(current.Extra), ct);
                break;
            case AwaitingInput.PostEdit when current.TargetId is { } postId:
                var lines = text.Replace("\r\n", "\n").Trim().Split('\n', 2);
                session.Edited[postId] = new DemoText(lines[0].Trim(), RewriteService.NormalizeBody(lines.Length > 1 ? lines[1] : ""));
                if (BuildPost(session, postId) is { } edited)
                    await SendCardAsync(ctx, edited, "✏️ Твоя версія", ct);
                break;
        }
    }

    private List<DemoCandidate> Visible(DemoSession session, bool weak) =>
        demo.Candidates
            .Where(c => !session.Picked.Contains(c.Id) && !session.Rejected.ContainsKey(c.Id))
            .Where(c => weak ? c.Score < MinScore : c.Score >= MinScore)
            .ToList();

    private async Task ShowNewsMenuAsync(ChatContext ctx, DemoSession session, CancellationToken ct)
    {
        var fresh = session.Collected ? Visible(session, false).Count : 0;
        var ready = ReadyPosts(session).Count(p => p.IsApproved);
        var text = $"📰 <b>Новини — Шептицький</b>\nНових: {fresh} · Обрано: {session.Picked.Count}/{TargetPicks} · Відкинуто: {session.Rejected.Count} · Готових постів: {ready}";
        if (!session.Collected)
            text += "\n\nПочни з «🔎 Зібрати новини».";

        await ui.ShowAsync(ctx, text, Rows(
            [Btn("🔎 Зібрати новини", new CallbackData("n", "col")), Btn($"📋 Кандидати ({fresh})", new CallbackData("n", "c", 0))],
            [Btn($"✍️ Переписати обрані ({session.Picked.Count})", new CallbackData("p", "rw")), Btn($"✅ Готові пости ({ready})", new CallbackData("p", "list", 0))],
            [Btn("📰 Стаття для сайту", new CallbackData("a", "list")), Btn("📄 Експорт .docx", new CallbackData("x", "docx"))],
            [Btn("🏙 Змінити місто", new CallbackData("n", "city")), Btn("⬅️ Назад", Cb.MainMenu)]), ct);
    }

    private async Task CollectAsync(ChatContext ctx, DemoSession session, CancellationToken ct)
    {
        var progress = await ui.StartProgressAsync(ctx, "⏳ Збираю з 5 джерел…", ct);
        await Task.Delay(1000, ct);
        await progress.UpdateAsync("⏳ 5/5 джерел · 292 постів · фільтр і пошук дублів…", ct, force: true);
        await Task.Delay(1000, ct);
        await progress.UpdateAsync("🤖 AI оцінює 8 новин…", ct, force: true);
        await Task.Delay(1000, ct);
        session.Collected = true;

        var strong = Visible(session, false).Count;
        var weak = Visible(session, true).Count;
        await progress.CompleteAsync($"✅ Знайдено {Plural(strong, "кандидат", "кандидати", "кандидатів")} (ще {weak} слабких)\n\n<i>Демо: заготовлені новини з реальних джерел Шептицького.</i>",
            Rows([Btn("📋 Переглянути", new CallbackData("n", "c", 0))], [Btn("📰 Новини", new CallbackData("n", "menu")), .. BackToMenu]), ct);
    }

    private async Task ShowCandidateAsync(ChatContext ctx, DemoSession session, ListPosition position, CancellationToken ct)
    {
        var list = session.Collected ? Visible(session, position.Weak) : [];
        if (list.Count == 0)
        {
            await ui.ShowAsync(ctx, session.Collected ? "Кандидатів більше немає." : "Спершу натисни «🔎 Зібрати новини».",
                Rows([Btn("🔎 Зібрати новини", new CallbackData("n", "col"))], [Btn("📰 Новини", new CallbackData("n", "menu"))]), ct);
            return;
        }

        var index = Math.Clamp(position.Index, 0, list.Count - 1);
        var candidate = list[index];
        var item = demo.ToNewsItem(candidate);
        var pos = position with { Index = index };

        var sb = new StringBuilder();
        sb.AppendLine($"{index + 1}/{list.Count} · ⭐ {candidate.Score}/5 · {NewsView.Category(candidate.Category)}");
        sb.AppendLine($"<b>{Escape(NewsView.TitleOf(item))}</b>");
        sb.Append($"📅 {NewsView.Date(item.PublishedAt)} · {Escape(candidate.Source)}");
        sb.AppendLine(candidate.ExtraSources > 0 ? $" (+{Plural(candidate.ExtraSources, "джерело", "джерела", "джерел")})" : "");
        sb.AppendLine($"Коротко: {Escape(Truncate(item.RawText.ReplaceLineEndings(" "), 300))}");
        sb.AppendLine($"💬 AI: «{Escape(candidate.Reason)}»");
        sb.Append($"\nОбрано: {session.Picked.Count}/{TargetPicks}");

        var rows = new List<IEnumerable<InlineKeyboardButton>>
        {
            new[] { Btn("✅ Беру", new CallbackData("n", "pk", candidate.Id, pos.ToString())), Btn("❌ Відкинути", new CallbackData("n", "rj", candidate.Id, pos.ToString())), Url("🔗 Джерело", candidate.Url) },
            new[]
            {
                Btn("◀️", new CallbackData("n", "c", null, (pos with { Index = (index - 1 + list.Count) % list.Count }).ToString())),
                Btn($"{index + 1}/{list.Count}", Cb.Noop),
                Btn("▶️", new CallbackData("n", "c", null, (pos with { Index = (index + 1) % list.Count }).ToString())),
                Btn("⬅️ Меню", new CallbackData("n", "menu"))
            }
        };
        var weakCount = Visible(session, true).Count;
        if (!position.Weak && weakCount > 0)
            rows.Add([Btn($"Показати слабкі ({weakCount})", new CallbackData("n", "c", null, new ListPosition(0, true).ToString()))]);
        else if (position.Weak)
            rows.Add([Btn("⬅️ До основних", new CallbackData("n", "c", 0))]);

        await ui.ShowAsync(ctx, sb.ToString(), Rows(rows), ct);
    }

    private async Task ShowRejectReasonsAsync(ChatContext ctx, int id, ListPosition position, CancellationToken ct)
    {
        var reasons = new[] { ("old", "🕐 Застаріла"), ("loc", "😐 Не про місто"), ("unv", "❓ Неперевірено"), ("dup", "🔁 Дубль"), ("cus", "✏️ Своя причина") }
            .Select(r => Btn(r.Item2, new CallbackData("n", "rr", id, $"{r.Item1}.{position}"))).ToList();
        await ui.ShowAsync(ctx, "❌ Чому відкидаємо?", Rows(reasons.Take(3), reasons.Skip(3),
            [Btn("↩️ Назад до картки", new CallbackData("n", "c", null, position.ToString()))]), ct);
    }

    private async Task<string?> RejectAsync(ChatContext ctx, DemoSession session, int id, string? arg, CancellationToken ct)
    {
        var dot = arg?.IndexOf('.') ?? -1;
        var code = dot > 0 ? arg![..dot] : arg;
        var position = ListPosition.Parse(dot > 0 ? arg![(dot + 1)..] : null);
        if (code == "cus")
        {
            state.Set(ctx.ChatId, AwaitingInput.RejectNote, id, position.ToString());
            await ui.ShowAsync(ctx, "✏️ Напиши причину одним повідомленням:", null, ct);
            return null;
        }

        session.Rejected[id] = code switch
        {
            "old" => "застаріла",
            "loc" => "не про місто",
            "unv" => "неперевірена інформація",
            _ => "дубль"
        };
        session.Picked.Remove(id);
        await ShowCandidateAsync(ctx, session, position, ct);
        return "Відкинуто";
    }

    private async Task RewriteAsync(ChatContext ctx, DemoSession session, CancellationToken ct)
    {
        var picked = demo.Candidates.Where(c => session.Picked.Contains(c.Id)).ToList();
        if (picked.Count == 0)
        {
            await ui.SendAsync(ctx, "Спершу обери новини (✅ Беру).", Rows([Btn("📋 Кандидати", new CallbackData("n", "c", 0))]), ct);
            return;
        }

        var progress = await ui.StartProgressAsync(ctx, $"✍️ Пишу пост 1/{picked.Count}…", ct);
        for (var i = 0; i < picked.Count; i++)
        {
            await progress.UpdateAsync($"✍️ Пишу пост {i + 1}/{picked.Count}…", ct, force: true);
            await Task.Delay(Random.Shared.Next(1000, 2000), ct);
            if (BuildPost(session, picked[i].Id) is { } post)
                await SendCardAsync(ctx, post, $"📝 Пост {i + 1}/{picked.Count}", ct);
            else
                await ui.SendAsync(ctx, $"ℹ️ Для «{Escape(Truncate(picked[i].Title ?? picked[i].Text, 60))}» у демо немає заготовленого поста.", null, ct);
        }

        await progress.CompleteAsync("✅ Готово. Перевір кожен текст і натисни 👍 Готово.\n<i>Демо: пости заготовлені, а перевірка лімітів і фактів — справжня.</i>",
            Rows([Btn("✅ Готові пости", new CallbackData("p", "list", 0)), Btn("📰 Новини", new CallbackData("n", "menu"))]), ct);
    }

    private async Task ShowReadyAsync(ChatContext ctx, DemoSession session, int index, CancellationToken ct)
    {
        var ready = ReadyPosts(session).Where(p => p.IsApproved).ToList();
        if (ready.Count == 0)
        {
            await ui.ShowAsync(ctx, "Готових постів ще немає: обери новини, натисни «✍️ Переписати обрані» і схвали пости 👍.", Rows([Btn("📰 Новини", new CallbackData("n", "menu"))]), ct);
            return;
        }

        index = Math.Clamp(index, 0, ready.Count - 1);
        var post = ready[index];
        await ui.ShowAsync(ctx, PostView.Render(post, $"✅ Готовий пост {index + 1}/{ready.Count}", Validate(post)), Rows(
            [Btn("✏️ Свій текст", new CallbackData("p", "ed", post.Id)), Btn("🗑 Прибрати", new CallbackData("p", "rm", post.Id))],
            [
                Btn("◀️", new CallbackData("p", "list", (index - 1 + ready.Count) % ready.Count)),
                Btn($"{index + 1}/{ready.Count}", Cb.Noop),
                Btn("▶️", new CallbackData("p", "list", (index + 1) % ready.Count)),
                Btn("📰 Новини", new CallbackData("n", "menu"))
            ]), ct);
    }

    private async Task ShowArticleChoicesAsync(ChatContext ctx, DemoSession session, CancellationToken ct)
    {
        var target = demo.Find(demo.Article.CandidateId)!;
        var note = session.Picked.Contains(target.Id) ? "" : "\n<i>У демо стаття заготовлена для новини про ялинку.</i>";
        await ui.ShowAsync(ctx, $"📰 <b>Стаття для сайту</b>\nОбери новину:{note}", Rows(
            [Btn($"[3 дж.] {Truncate(target.Title ?? target.Text, 50)}", new CallbackData("a", "gen", target.Id))],
            [Btn("⬅️ Новини", new CallbackData("n", "menu"))]), ct);
    }

    private async Task WriteArticleAsync(ChatContext ctx, DemoSession session, CancellationToken ct)
    {
        var progress = await ui.StartProgressAsync(ctx, "✍️ Пишу статтю 1/1…", ct);
        await Task.Delay(2000, ct);
        session.ArticleWritten = true;
        var article = BuildArticle();
        await SendCardAsync(ctx, article, "📰 <b>Стаття для сайту</b>", ct);
        await progress.CompleteAsync("✅ Статтю написано.", Rows([Btn("📄 Експорт .docx", new CallbackData("x", "docx")), Btn("📰 Новини", new CallbackData("n", "menu"))]), ct);
    }

    private async Task ExportAsync(ChatContext ctx, DemoSession session, CancellationToken ct)
    {
        var posts = ReadyPosts(session);
        if (posts.Count == 0)
            posts = demo.Posts.Keys.Select(id => BuildPost(session, id)!).ToList();
        var rejected = session.Rejected.Count >= 3
            ? session.Rejected.Select(r => (demo.Find(r.Key)!, r.Value))
            : demo.Candidates.Where(c => c.Score < MinScore).Select(c => (c, c.Reason)).Concat(session.Rejected.Select(r => (demo.Find(r.Key)!, r.Value)));

        var export = new NewsExport("Шептицький", clock.UtcNow, clock.UtcNow,
            [("Шептицька міська рада", "https://t.me/s/sheptytska_mr"), ("Ехо 80100", "https://t.me/s/echo80100"), ("ZAXID.NET — Шептицький", "https://zaxid.net/chervonograd_tag48789/")],
            posts.Select(p => new ExportPost(p.Title, p.Body, p.NewsItem!.Url)).ToList(),
            new ExportPost(demo.Article.Title, demo.Article.Body, demo.Find(demo.Article.CandidateId)!.Url),
            rejected.Select(r => new ExportRejected(NewsView.TitleOf(demo.ToNewsItem(r.Item1)), r.Item1.Url, r.Item2)).ToList());

        var path = await docx.WriteAsync(export, Path.Combine(paths.Exports, "demo", ctx.ChatId.ToString()), ct);
        await ui.SendDocumentAsync(ctx.ChatId, path, $"🧪 Демо · {Plural(export.Posts.Count, "пост", "пости", "постів")} · стаття · не взяті: {export.Rejected.Count}",
            Rows([Btn("📨 Надіслати всі пости в чат по одному", new CallbackData("x", "all"))], BackToMenu), ct);
    }

    private async Task SeoAsync(ChatContext ctx, CancellationToken ct)
    {
        var progress = await ui.StartProgressAsync(ctx, "⏳ SEO-аудит https://maxidveri.com\nЧитаю robots.txt і sitemap…", ct);
        await Task.Delay(1000, ct);
        await progress.UpdateAsync($"⏳ Sitemap: {Number(demo.Seo.SitemapUrlCount)} URL · перевірено 25/50 · знайдено проблем: 31", ct, force: true);
        await Task.Delay(1000, ct);

        var issues = seoDetector.Detect(demo.Seo.Pages, demo.Seo.Site);
        var summary = SeoSummary.Build(demo.Seo, issues);
        var csvPath = await csv.WriteCsvAsync(demo.Seo, issues, Path.Combine(paths.Exports, "demo", ctx.ChatId.ToString()), ct);
        await progress.CompleteAsync(SeoHandler.RenderSummary(summary) + "\n\n<i>Демо: заготовлений звіт реального прогону maxidveri.com.</i>",
            Rows([Btn("🔁 Ще раз", Cb.SeoOpen)], BackToMenu), ct);
        await ui.SendDocumentAsync(ctx.ChatId, csvPath, "📎 Повний звіт по сторінках (відкривається в Excel)", null, ct);
    }

    private List<Post> ReadyPosts(DemoSession session) =>
        demo.Posts.Keys
            .Where(id => session.Picked.Contains(id) && !session.Removed.Contains(id))
            .Select(id => BuildPost(session, id)!)
            .ToList();

    private Post? BuildPost(DemoSession session, int id)
    {
        if (!demo.Posts.TryGetValue(id, out var text) || demo.Find(id) is not { } candidate)
            return null;

        var edited = session.Edited.GetValueOrDefault(id) ?? text;
        return new Post
        {
            Id = id,
            NewsItemId = id,
            NewsItem = demo.ToNewsItem(candidate),
            Kind = PostKind.Social,
            Title = edited.Title,
            Body = edited.Body,
            IsApproved = session.Approved.Contains(id),
            CreatedAt = clock.UtcNow
        };
    }

    private Post BuildArticle()
    {
        var candidate = demo.Find(demo.Article.CandidateId)!;
        return new Post
        {
            Id = ArticleId + candidate.Id,
            NewsItemId = candidate.Id,
            NewsItem = demo.ToNewsItem(candidate),
            Kind = PostKind.Article,
            Title = demo.Article.Title,
            Body = demo.Article.Body,
            CreatedAt = clock.UtcNow
        };
    }

    private ValidationReport Validate(Post post) =>
        validator.Validate(post.Kind, post.Title, post.Body, post.NewsItem!.Url, [$"{post.NewsItem.Title}\n{post.NewsItem.RawText}".Trim()]);

    private async Task SendCardAsync(ChatContext ctx, Post post, string header, CancellationToken ct)
    {
        var keyboard = post.Kind == PostKind.Article ? Rows(BackToMenu) : PostsHandler.CardKeyboard(post);
        await ui.SendAsync(ctx, PostView.Render(post, header, Validate(post)), keyboard, ct);
    }
}
