using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

/// <summary>
/// The candidate profile is sent through the chat and stored in the bot's database on the server,
/// so personal data never ends up in the (public) repository.
/// </summary>
public sealed class ProfileHandler(BotUi ui, UserStateStore state, IUserSettingsRepository settingsRepository) : ICallbackHandler, ITextInputHandler
{
    private const int MinLength = 200;
    private const int PreviewLength = 1500;

    public string Module => "p";

    public AwaitingInput Awaiting => AwaitingInput.Profile;

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "show":
                await ShowAsync(ctx, ct);
                return null;
            case "edit":
                state.Set(ctx.ChatId, AwaitingInput.Profile);
                await ui.ShowAsync(ctx,
                    "✏️ <b>Надішли профіль одним повідомленням</b>\n\n" +
                    "Що варто включити: досвід і проєкти (з цифрами), стек, рівень англійської, які ролі шукаєш, зарплата, формат роботи, місто. " +
                    "На цьому тексті будуються оцінка вакансій і всі чернетки, тож пиши тільки правду: модель не має права додавати того, чого тут немає.\n\n" +
                    "Ліміт Telegram — 4096 символів. /cancel — скасувати.",
                    Rows(BackToMenu), ct);
                return null;
            default:
                return null;
        }
    }

    public async Task ShowAsync(ChatContext ctx, CancellationToken ct)
    {
        var profile = (await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct)).Profile;
        var text = string.IsNullOrWhiteSpace(profile)
            ? "👤 <b>Профіль порожній</b>\n\nБез нього бот не може оцінювати вакансії і писати відгуки."
            : $"👤 <b>Профіль</b> ({Plural(profile.Length, "символ", "символи", "символів")})\n\n<blockquote expandable>{Escape(Truncate(profile, PreviewLength))}</blockquote>";
        await ui.ShowAsync(ctx, text, Rows([Btn("✏️ Замінити профіль", Cb.ProfileEdit)], BackToMenu), ct);
    }

    public async Task HandleAsync(ChatContext ctx, string text, UserState? current, CancellationToken ct)
    {
        if (text.Trim().Length < MinLength)
        {
            await ui.SendAsync(ctx, $"Закоротко ({text.Trim().Length} символів). Для доброї оцінки потрібно хоча б {MinLength}: досвід, проєкти, стек, що шукаєш.", Rows(BackToMenu), ct);
            return;
        }

        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        settings.Profile = text.Trim();
        await settingsRepository.SaveAsync(settings, ct);
        state.Reset(ctx.ChatId);

        await ui.SendAsync(ctx, $"✅ Профіль збережено ({Plural(settings.Profile.Length, "символ", "символи", "символів")}). Нові вакансії оцінюватимуться по ньому; старі можна переоцінити кнопкою «🎯 Оцінити».",
            Rows(BackToMenu), ct);
    }
}
