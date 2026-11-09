using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using static SystematiaBot.Bot.UI.Keyboards;

namespace SystematiaBot.Bot.Jobs;

/// <summary>
/// Sends cards for everything the owner has not seen yet. Each card is marked as sent right after it goes out,
/// so a crash can at worst repeat one card, never drop one.
/// </summary>
public sealed class JobNotifier(BotUi ui, ILogger<JobNotifier> logger)
{
    private const int MaxVacancyCards = 8;

    public async Task<int> SendPendingAsync(long chatId, IServiceProvider services, CancellationToken ct)
    {
        var mails = services.GetRequiredService<IMailEventRepository>();
        var vacancies = services.GetRequiredService<IVacancyRepository>();
        var sent = 0;

        // Recruiter replies first: they are time-sensitive, new vacancies can wait a minute.
        foreach (var mailEvent in await mails.GetUnnotifiedAsync(ct))
        {
            await ui.SendAsync(chatId, JobViews.EventCard(mailEvent), JobViews.EventKeyboard(mailEvent), ct);
            mailEvent.Notified = true;
            await mails.SaveChangesAsync(ct);
            sent++;
        }

        var pending = (await vacancies.GetUnnotifiedAsync(ct)).OrderByDescending(v => v.FitScore ?? -1).ToList();
        foreach (var vacancy in pending.Take(MaxVacancyCards))
        {
            await ui.SendAsync(chatId, JobViews.Card(vacancy), JobViews.CardKeyboard(vacancy), ct);
            vacancy.Notified = true;
            await vacancies.SaveChangesAsync(ct);
            sent++;
        }

        var rest = pending.Skip(MaxVacancyCards).ToList();
        if (rest.Count > 0)
        {
            foreach (var vacancy in rest)
                vacancy.Notified = true;
            await vacancies.SaveChangesAsync(ct);
            await ui.SendAsync(chatId, $"…і ще {MessageFormatter.Plural(rest.Count, "вакансія", "вакансії", "вакансій")} у списку нових.",
                Rows([Btn("🆕 Відкрити список", Cb.VacancyList("new"))]), ct);
        }

        if (sent > 0)
            logger.LogInformation("Sent {Count} cards to chat {ChatId}", sent, chatId);
        return sent;
    }
}
