using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
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

/// <summary>News screen, collecting and reviewing candidates (pick / reject with a reason).</summary>
public sealed class NewsHandler(
    BotUi ui,
    UserStateStore state,
    BackgroundJobRunner jobs,
    IServiceScopeFactory scopeFactory,
    INewsRepository news,
    IUserSettingsRepository settingsRepository,
    ICityCatalog cities,
    IClock clock,
    IOptions<NewsOptions> newsOptions,
    IOptions<BotOptions> botOptions) : ICallbackHandler, ITextInputHandler
{
    public const string CollectJob = "collect";
    private const int PreviewLength = 300;

    private static readonly (string Code, RejectReason Reason, string Label)[] Reasons =
    [
        ("old", RejectReason.Old, "🕐 Застаріла"),
        ("loc", RejectReason.NotLocal, "😐 Не про місто"),
        ("unv", RejectReason.Unverified, "❓ Неперевірено"),
        ("dup", RejectReason.Duplicate, "🔁 Дубль"),
        ("cus", RejectReason.Custom, "✏️ Своя причина")
    ];

    private NewsOptions Options => newsOptions.Value;

    public string Module => "n";
    public AwaitingInput Awaiting => AwaitingInput.RejectNote;

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        var position = ListPosition.Parse(data.Arg);
        switch (data.Action)
        {
            case "menu":
                await ShowMenuAsync(ctx, ct);
                return null;
            case "col":
                return await StartCollectAsync(ctx, ct);
            case "c":
                await ShowCandidateAsync(ctx, position, ct);
                return null;
            case "pk" when data.Id is { } id:
                return await PickAsync(ctx, id, position, ct);
            case "rj" when data.Id is { } id:
                await ShowRejectReasonsAsync(ctx, id, position, ct);
                return null;
            case "rr" when data.Id is { } id:
                return await RejectAsync(ctx, id, data.Arg, ct);
            case "city":
                await ShowCitiesAsync(ctx, ct);
                return null;
            case "cs" when data.Id is { } index:
                return await SelectCityAsync(ctx, index, ct);
            default:
                return null;
        }
    }

    public async Task ShowMenuAsync(ChatContext ctx, CancellationToken ct)
    {
        var city = await CurrentCityAsync(ctx, ct);
        var counts = await news.GetCountsAsync(city.Id, Since, Options.MinScore, ct);
        var text = $"📰 <b>Новини — {Escape(city.Name)}</b>\n" +
                   $"Нових: {counts.New} · Обрано: {counts.Picked}/{Options.TargetPicks} · Відкинуто: {counts.Rejected} · Готових постів: {counts.ReadyPosts}";
        if (counts.Weak > 0)
            text += $"\nСлабких (оцінка &lt; {Options.MinScore}): {counts.Weak}";

        await ui.ShowAsync(ctx, text, Rows(
            [Btn("🔎 Зібрати новини", new CallbackData("n", "col")), Btn($"📋 Кандидати ({counts.New})", new CallbackData("n", "c", 0))],
            [Btn($"✍️ Переписати обрані ({counts.Picked})", new CallbackData("p", "rw")), Btn($"✅ Готові пости ({counts.ReadyPosts})", new CallbackData("p", "list", 0))],
            [Btn("📰 Стаття для сайту", new CallbackData("a", "list")), Btn("📄 Експорт .docx", new CallbackData("x", "docx"))],
            [Btn("🏙 Змінити місто", new CallbackData("n", "city")), Btn("⬅️ Назад", Cb.MainMenu)]), ct);
    }

    private async Task<string?> StartCollectAsync(ChatContext ctx, CancellationToken ct)
    {
        var city = await CurrentCityAsync(ctx, ct);
        var jobCtx = new ChatContext { ChatId = ctx.ChatId, UserId = ctx.UserId, IsOwner = ctx.IsOwner };
        return jobs.TryStart(ctx.ChatId, CollectJob, token => CollectAsync(jobCtx, city, token))
            ? "Збираю новини"
            : "Збір уже йде";
    }

    private async Task CollectAsync(ChatContext ctx, CityConfig city, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<NewsCollectionService>();
        var enabled = city.Sources.Count(s => s.IsEnabled);
        var progressMessage = await ui.StartProgressAsync(ctx, $"⏳ Збираю з {Plural(enabled, "джерела", "джерел", "джерел")}…", ct);

        CollectProgress? latest = null;
        CollectResult result;
        await using (ProgressTicker.Start(progressMessage, () => latest is null ? null : RenderProgress(latest), ct))
            result = await service.CollectAsync(city, new SyncProgress<CollectProgress>(p => latest = p), ct);

        var sb = new StringBuilder($"✅ Знайдено {Plural(result.Candidates, "кандидат", "кандидати", "кандидатів")}");
        if (result.Weak > 0)
            sb.Append($" (ще {result.Weak} слабких)");
        sb.Append($"\n\nДжерел: {result.SourcesOk} ок").Append(result.SourcesFailed > 0 ? $", {result.SourcesFailed} з помилкою" : "");
        sb.Append($"\nПостів за {Options.MaxAgeDays} днів: {result.Fresh} · не про місто: {result.NotLocal} · нових: {result.Added} · дублів: {result.Duplicates}");

        await progressMessage.CompleteAsync(sb.ToString(), Rows(
            [Btn("📋 Переглянути", new CallbackData("n", "c", 0))],
            [Btn("📰 Новини", new CallbackData("n", "menu")), Btn("⬅️ Меню", Cb.MainMenu)]), CancellationToken.None);
    }

    private static string RenderProgress(CollectProgress p) => p.Stage switch
    {
        "fetch" => $"⏳ {p.SourcesDone}/{p.SourcesTotal} джерел · {p.Posts} постів…",
        "filter" => $"⏳ {p.SourcesTotal}/{p.SourcesTotal} джерел · {p.Posts} постів · фільтр і пошук дублів…",
        "score" => $"🤖 AI оцінює {Plural(p.ToScore, "новину", "новини", "новин")}…",
        _ => "⏳ …"
    };

    public async Task ShowCandidateAsync(ChatContext ctx, ListPosition position, CancellationToken ct)
    {
        var city = await CurrentCityAsync(ctx, ct);
        var all = await news.GetActiveAsync(city.Id, Since, ct);
        var list = Candidates(all, position.Weak);
        var weakCount = Candidates(all, weak: true).Count;

        if (list.Count == 0)
        {
            var empty = position.Weak ? "Слабких кандидатів немає." : "Кандидатів немає. Натисни «🔎 Зібрати новини».";
            await ui.ShowAsync(ctx, empty, Rows([Btn("🔎 Зібрати новини", new CallbackData("n", "col"))], [Btn("📰 Новини", new CallbackData("n", "menu")), .. BackToMenu]), ct);
            return;
        }

        var index = Math.Clamp(position.Index, 0, list.Count - 1);
        var item = list[index];
        var clusterSizes = await news.GetClusterSizesAsync([item.Id], ct);
        var others = clusterSizes.GetValueOrDefault(item.Id);
        var picked = all.Count(n => n.Status == NewsStatus.Picked);

        var header = new List<string> { $"{index + 1}/{list.Count}" };
        if (item.Score is { } score)
            header.Add($"⭐ {score}/5");
        if (NewsView.Category(item.Category) is { Length: > 0 } category)
            header.Add(category);
        if (position.Weak)
            header.Add("слабкі");

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(" · ", header));
        sb.AppendLine($"<b>{Escape(NewsView.TitleOf(item))}</b>");
        sb.Append($"📅 {NewsView.Date(item.PublishedAt)} · {Escape(item.Source?.Name)}");
        sb.AppendLine(others > 0 ? $" (+{Plural(others, "джерело", "джерела", "джерел")})" : "");
        sb.AppendLine($"Коротко: {Escape(Truncate(item.RawText.ReplaceLineEndings(" "), PreviewLength))}");
        if (!string.IsNullOrWhiteSpace(item.ScoreReason))
            sb.AppendLine($"💬 AI: «{Escape(item.ScoreReason)}»");
        sb.Append($"\nОбрано: {picked}/{Options.TargetPicks}");

        var pos = position with { Index = index };
        var rows = new List<IEnumerable<InlineKeyboardButton>>
        {
            new[] { Btn("✅ Беру", new CallbackData("n", "pk", item.Id, pos.ToString())), Btn("❌ Відкинути", new CallbackData("n", "rj", item.Id, pos.ToString())), Url("🔗 Джерело", item.Url) },
            new[]
            {
                Btn("◀️", new CallbackData("n", "c", null, (pos with { Index = (index - 1 + list.Count) % list.Count }).ToString())),
                Btn($"{index + 1}/{list.Count}", Cb.Noop),
                Btn("▶️", new CallbackData("n", "c", null, (pos with { Index = (index + 1) % list.Count }).ToString())),
                Btn("⬅️ Меню", new CallbackData("n", "menu"))
            }
        };
        if (!position.Weak && weakCount > 0)
            rows.Add([Btn($"Показати слабкі ({weakCount})", new CallbackData("n", "c", null, new ListPosition(0, true).ToString()))]);
        else if (position.Weak)
            rows.Add([Btn("⬅️ До основних", new CallbackData("n", "c", 0))]);

        await ui.ShowAsync(ctx, sb.ToString(), Rows(rows), ct);
    }

    private List<NewsItem> Candidates(IEnumerable<NewsItem> active, bool weak) =>
        active.Where(n => n.Status == NewsStatus.New)
            .Where(n => weak ? n.Score < Options.MinScore : n.Score is null || n.Score >= Options.MinScore)
            .OrderByDescending(n => n.Score ?? 0)
            .ThenByDescending(n => n.PublishedAt)
            .ToList();

    private async Task<string?> PickAsync(ChatContext ctx, int id, ListPosition position, CancellationToken ct)
    {
        var item = await news.GetAsync(id, ct);
        if (item is null)
            return "Новину не знайдено";

        item.Status = NewsStatus.Picked;
        item.RejectReason = null;
        item.RejectNote = null;
        await news.SaveChangesAsync(ct);

        var counts = await news.GetCountsAsync(item.City, Since, Options.MinScore, ct);
        await ShowCandidateAsync(ctx, position, ct);
        return counts.Picked > Options.TargetPicks
            ? $"⚠️ Обрано вже {counts.Picked} — більше, ніж потрібно ({Options.TargetPicks})"
            : $"Обрано {counts.Picked}/{Options.TargetPicks}";
    }

    private async Task ShowRejectReasonsAsync(ChatContext ctx, int id, ListPosition position, CancellationToken ct)
    {
        var item = await news.GetAsync(id, ct);
        if (item is null)
            return;

        var buttons = Reasons.Select(r => Btn(r.Label, new CallbackData("n", "rr", id, $"{r.Code}.{position}"))).ToList();
        await ui.ShowAsync(ctx, $"❌ Чому відкидаємо?\n<b>{Escape(NewsView.TitleOf(item))}</b>", Rows(
            buttons.Take(3), buttons.Skip(3),
            [Btn("↩️ Назад до картки", new CallbackData("n", "c", null, position.ToString()))]), ct);
    }

    private async Task<string?> RejectAsync(ChatContext ctx, int id, string? arg, CancellationToken ct)
    {
        var dot = arg?.IndexOf('.') ?? -1;
        var code = dot > 0 ? arg![..dot] : arg;
        var position = ListPosition.Parse(dot > 0 ? arg![(dot + 1)..] : null);
        var reason = Reasons.FirstOrDefault(r => r.Code == code);
        if (reason.Code is null)
            return null;

        if (reason.Reason == RejectReason.Custom)
        {
            state.Set(ctx.ChatId, AwaitingInput.RejectNote, id, position.ToString());
            await ui.ShowAsync(ctx, "✏️ Напиши причину одним повідомленням:", Rows([Btn("↩️ Скасувати", new CallbackData("n", "c", null, position.ToString()))]), ct);
            return null;
        }

        await SaveRejectAsync(id, reason.Reason, null, ct);
        await ShowCandidateAsync(ctx, position, ct);
        return "Відкинуто";
    }

    /// <summary>The custom reject reason typed by the user.</summary>
    public async Task HandleAsync(ChatContext ctx, string text, UserState? userState, CancellationToken ct)
    {
        state.Reset(ctx.ChatId);
        if (userState?.TargetId is not { } id)
            return;

        await SaveRejectAsync(id, RejectReason.Custom, Truncate(text.Trim(), 300), ct);
        ctx.MessageId = null;
        await ShowCandidateAsync(ctx, ListPosition.Parse(userState.Extra), ct);
    }

    private async Task SaveRejectAsync(int id, RejectReason reason, string? note, CancellationToken ct)
    {
        var item = await news.GetAsync(id, ct);
        if (item is null)
            return;

        item.Status = NewsStatus.Rejected;
        item.RejectReason = reason;
        item.RejectNote = note;
        await news.SaveChangesAsync(ct);
    }

    private async Task ShowCitiesAsync(ChatContext ctx, CancellationToken ct)
    {
        var buttons = cities.Cities.Select((c, i) => new[] { Btn($"🏙 {c.Name}", new CallbackData("n", "cs", i)) });
        await ui.ShowAsync(ctx, "🏙 Обери місто:", Rows([.. buttons, [Btn("⬅️ Назад", new CallbackData("n", "menu"))]]), ct);
    }

    private async Task<string?> SelectCityAsync(ChatContext ctx, int index, CancellationToken ct)
    {
        if (index < 0 || index >= cities.Cities.Count)
            return null;

        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, botOptions.Value.DefaultCity, ct);
        settings.City = cities.Cities[index].Id;
        await settingsRepository.SaveAsync(settings, ct);
        await ShowMenuAsync(ctx, ct);
        return $"Місто: {cities.Cities[index].Name}";
    }

    public async Task<CityConfig> CurrentCityAsync(ChatContext ctx, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, botOptions.Value.DefaultCity, ct);
        return cities.Find(settings.City) ?? cities.Find(botOptions.Value.DefaultCity) ?? cities.Cities[0];
    }

    private DateTimeOffset Since => clock.UtcNow.AddDays(-Options.MaxAgeDays);
}

/// <summary>Position in the candidate list, serialized into callback args as "5" or "5w" (weak list).</summary>
public sealed record ListPosition(int Index, bool Weak)
{
    public override string ToString() => Weak ? $"{Index}w" : Index.ToString();

    public static ListPosition Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return new ListPosition(0, false);

        var weak = value.EndsWith('w');
        return new ListPosition(int.TryParse(weak ? value[..^1] : value, out var index) ? index : 0, weak);
    }
}
