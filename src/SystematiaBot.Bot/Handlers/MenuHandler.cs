using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.News;
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
    IOptions<BotOptions> botOptions) : ICallbackHandler
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
        sb.AppendLine($"Аптайм: {(int)uptime.TotalHours} год {uptime.Minutes} хв");
        sb.AppendLine($"Фонових задач: {jobs.RunningCount}");
        await ui.ShowAsync(ctx, sb.ToString().TrimEnd(), Rows(BackToMenu), ct);
    }
}
