using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.News;
using SystematiaBot.Infrastructure.Llm;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

public sealed class MenuHandler(
    BotUi ui,
    UserStateStore state,
    BackgroundJobRunner jobs,
    IUserSettingsRepository settingsRepository,
    ICityCatalog cities,
    INewsRepository news,
    IClock clock,
    IOptions<NewsOptions> newsOptions,
    IOptions<BotOptions> botOptions,
    ISourceRepository sources,
    LlmBudgetGuard llm,
    IOptions<OpenAiOptions> openAiOptions,
    ErrorTracker errors) : ICallbackHandler
{
    public string Module => "m";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "main":
                state.Reset(ctx.ChatId);
                await ShowMainAsync(ctx, ct);
                return null;
            case "status":
                await ShowStatusAsync(ctx, ct);
                return null;
            default:
                return null;
        }
    }

    public async Task ShowMainAsync(ChatContext ctx, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, botOptions.Value.DefaultCity, ct);
        var cityName = cities.Find(settings.City)?.Name ?? settings.City;

        var counts = await news.GetCountsAsync(settings.City, clock.UtcNow.AddDays(-newsOptions.Value.MaxAgeDays), newsOptions.Value.MinScore, ct);

        var text = $"👋 <b>Systematia Assistant</b>\nМісто: {Escape(cityName)} · Дайджест: {(settings.DigestEnabled ? "увімк" : "вимк")} · Кандидатів: {counts.New}";
        await ui.ShowAsync(ctx, text, MainMenu(settings.DigestEnabled), ct);
    }

    public async Task ShowStatusAsync(ChatContext ctx, CancellationToken ct)
    {
        var uptime = DateTimeOffset.UtcNow - BuildInfo.StartedAt;
        var sb = new StringBuilder("ℹ️ <b>Статус</b>\n");
        sb.AppendLine($"Версія: <code>{Escape(BuildInfo.ShortVersion)}</code>");
        sb.AppendLine($"Аптайм: {(int)uptime.TotalDays} д {uptime.Hours} год {uptime.Minutes} хв");
        sb.AppendLine($"Фонових задач: {jobs.RunningCount}");
        sb.AppendLine($"Режим: {(ctx.IsOwner ? "власник" : "демо")}");

        var all = await sources.GetAllAsync(ct);
        var enabled = all.Where(s => s.IsEnabled).ToList();
        if (enabled.Count > 0)
        {
            sb.AppendLine().AppendLine($"<b>Джерела</b> ({enabled.Count(s => s.LastError is null)} ок / {enabled.Count(s => s.LastError is not null)} з помилкою):");
            foreach (var source in enabled)
            {
                var last = source.LastFetchedAt is { } at ? NewsView.Date(at) : "ще не збирали";
                sb.AppendLine(source.LastError is null
                    ? $"✅ {Escape(source.Name)} · {last}"
                    : $"❌ {Escape(source.Name)} · {Escape(Truncate(source.LastError, 80))}");
            }
        }

        sb.AppendLine();
        if (openAiOptions.Value.IsConfigured)
        {
            var spent = await llm.GetSpentAsync(llm.Today(), ct);
            sb.AppendLine($"OpenAI сьогодні: ${spent:0.000} з ${openAiOptions.Value.DailyBudgetUsd:0.00} ({Escape(openAiOptions.Value.Model)})");
        }
        else
        {
            sb.AppendLine("OpenAI: не налаштовано (OpenAI:ApiKey / OpenAI:Model)");
        }

        sb.Append(errors.Last is { } error
            ? $"Остання помилка: {NewsView.Date(error.At)} · {Escape(error.Where)} · {Escape(Truncate(error.Message, 120))}"
            : "Помилок з моменту запуску не було ✅");
        await ui.ShowAsync(ctx, sb.ToString(), Rows(BackToMenu), ct);
    }
}
