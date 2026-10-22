using SystematiaBot.Bot.Routing;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SystematiaBot.Bot;

/// <summary>Long polling loop. Network errors are retried with exponential backoff; each update gets its own DI scope.</summary>
public sealed class BotWorker(ITelegramBotClient bot, IServiceScopeFactory scopeFactory, ILogger<BotWorker> logger) : BackgroundService
{
    private static readonly BotCommand[] Commands =
    [
        new() { Command = "start", Description = "Головне меню" },
        new() { Command = "news", Description = "Новини" },
        new() { Command = "seo", Description = "SEO-аудит сайту" },
        new() { Command = "status", Description = "Статус бота" },
        new() { Command = "cancel", Description = "Скасувати поточну задачу" }
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeAsync(stoppingToken);

        int? offset = null;
        var backoff = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            Update[] updates;
            try
            {
                updates = await bot.GetUpdates(offset, limit: 50, timeout: 30, allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken: stoppingToken);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Polling failed ({Error}), retrying in {Delay}s", ex.Message, backoff.TotalSeconds);
                await Task.Delay(backoff, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
                continue;
            }

            foreach (var update in updates)
            {
                offset = update.Id + 1;
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<UpdateRouter>().RouteAsync(update, stoppingToken);
            }
        }

        logger.LogInformation("Polling stopped");
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var me = await bot.GetMe(ct);
                await bot.DeleteWebhook(cancellationToken: ct);
                await bot.SetMyCommands(Commands, cancellationToken: ct);
                logger.LogInformation("Connected to Telegram as @{Username}", me.Username);
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 401)
            {
                throw new InvalidOperationException("Telegram rejected the bot token (401 Unauthorized). Check Telegram:BotToken in user-secrets.", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 10)
            {
                logger.LogWarning("Telegram init failed ({Error}), attempt {Attempt}", ex.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * attempt, 30)), ct);
            }
        }
    }
}
