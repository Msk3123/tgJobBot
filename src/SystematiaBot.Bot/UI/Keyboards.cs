using SystematiaBot.Bot.Routing;
using Telegram.Bot.Types.ReplyMarkups;

namespace SystematiaBot.Bot.UI;

public static class Keyboards
{
    public static InlineKeyboardButton Btn(string text, CallbackData data) => InlineKeyboardButton.WithCallbackData(text, data.ToString());

    public static InlineKeyboardButton Url(string text, string url) => InlineKeyboardButton.WithUrl(text, url);

    public static InlineKeyboardMarkup Rows(params IEnumerable<InlineKeyboardButton>[] rows) =>
        new(rows.Select(r => r.ToArray()).Where(r => r.Length > 0).ToArray());

    public static InlineKeyboardMarkup Rows(IEnumerable<IEnumerable<InlineKeyboardButton>> rows) =>
        new(rows.Select(r => r.ToArray()).Where(r => r.Length > 0).ToArray());

    public static InlineKeyboardButton[] BackToMenu => [Btn("⬅️ Меню", Cb.MainMenu)];

    public static InlineKeyboardMarkup MainMenu(int newCount, int conversations, bool digestEnabled) => Rows(
        [Btn($"🆕 Нові ({newCount})", Cb.VacancyList("new")), Btn("📋 Мої заявки", Cb.VacancyList("act"))],
        [Btn($"📩 Відповіді ({conversations})", Cb.Conversations()), Btn("🔄 Перевірити пошту", Cb.Sync)],
        [Btn("👤 Профіль", Cb.Profile), Btn($"⏰ Дайджест: {(digestEnabled ? "увімк" : "вимк")}", Cb.DigestToggle)],
        [Btn("🗄 Архів", Cb.VacancyList("arc")), Btn("ℹ️ Статус", Cb.Status)]);

    public static InlineKeyboardMarkup CancelJob => Rows([Btn("⛔ Скасувати", Cb.CancelJob)]);
}
