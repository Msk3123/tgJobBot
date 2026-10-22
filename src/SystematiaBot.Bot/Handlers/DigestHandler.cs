using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Core.Abstractions;

namespace SystematiaBot.Bot.Handlers;

/// <summary>"⏰ Дайджест" toggle on the main menu.</summary>
public sealed class DigestHandler(MenuHandler menu, IUserSettingsRepository settingsRepository, IClock clock, IOptions<BotOptions> options) : ICallbackHandler
{
    public string Module => "d";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        if (data.Action != "toggle")
            return null;

        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, options.Value.DefaultCity, ct);
        settings.DigestEnabled = !settings.DigestEnabled;
        await settingsRepository.SaveAsync(settings, ct);
        await menu.ShowMainAsync(ctx, ct);

        if (!settings.DigestEnabled)
            return "Дайджест вимкнено";

        var next = DigestScheduler.NextRun(clock.UtcNow, options.Value.DigestTime);
        return $"Дайджест увімкнено. Наступний запуск: {next:dd.MM} о {next:HH:mm}";
    }
}
