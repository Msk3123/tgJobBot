using System.Text;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Core.Entities;
using SystematiaBot.Infrastructure.Time;
using Telegram.Bot.Types.ReplyMarkups;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.UI;

/// <summary>Texts and keyboards for vacancies and recruiter conversations.</summary>
public static class JobViews
{
    public static readonly VacancyStatus[] NewList = [VacancyStatus.New];
    public static readonly VacancyStatus[] ActiveList = [VacancyStatus.Drafted, VacancyStatus.Applied, VacancyStatus.Replied, VacancyStatus.Interview, VacancyStatus.Offer];
    public static readonly VacancyStatus[] ArchiveList = [VacancyStatus.Rejected, VacancyStatus.Skipped];

    public static VacancyStatus[] ListStatuses(string? list) => list switch
    {
        "act" => ActiveList,
        "arc" => ArchiveList,
        _ => NewList
    };

    public static string ListTitle(string? list) => list switch
    {
        "act" => "📋 Мої заявки",
        "arc" => "🗄 Архів (відмови і пропущені)",
        _ => "🆕 Нові вакансії"
    };

    public static string StatusLabel(VacancyStatus status) => status switch
    {
        VacancyStatus.New => "🆕 нова",
        VacancyStatus.Drafted => "✍️ є чернетка",
        VacancyStatus.Applied => "📤 подано",
        VacancyStatus.Replied => "📩 є відповідь",
        VacancyStatus.Interview => "🗓 співбесіда",
        VacancyStatus.Offer => "🎉 офер",
        VacancyStatus.Rejected => "❌ відмова",
        VacancyStatus.Skipped => "⏭ пропущено",
        _ => status.ToString()
    };

    public static string Verdict(string? verdict) => verdict switch
    {
        "apply" => "варто подаватись",
        "skip" => "краще пропустити",
        _ => "можна спробувати"
    };

    public static VacancyStatus? ParseStatus(string? code) => code switch
    {
        "nw" => VacancyStatus.New,
        "ap" => VacancyStatus.Applied,
        "iv" => VacancyStatus.Interview,
        "of" => VacancyStatus.Offer,
        "rj" => VacancyStatus.Rejected,
        "sk" => VacancyStatus.Skipped,
        _ => null
    };

    public static string Card(Vacancy v)
    {
        var sb = new StringBuilder();
        sb.Append(v.Status == VacancyStatus.New ? "🆕 " : "💼 ").Append("<b>").Append(Escape(v.Title)).AppendLine("</b>");

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.Company))
            meta.Add("🏢 " + Escape(v.Company));
        if (!string.IsNullOrWhiteSpace(v.Salary))
            meta.Add("💰 " + Escape(v.Salary));
        if (meta.Count > 0)
            sb.AppendLine(string.Join(" · ", meta));
        if (!string.IsNullOrWhiteSpace(v.Details))
            sb.AppendLine("<i>" + Escape(v.Details) + "</i>");

        sb.AppendLine();
        if (v.FitScore is { } fit)
        {
            sb.AppendLine($"🎯 <b>Збіг {fit}%</b> — {Verdict(v.FitVerdict)}");
            foreach (var reason in Lines(v.FitReasons))
                sb.AppendLine("✅ " + Escape(reason));
            foreach (var gap in Lines(v.Gaps))
                sb.AppendLine("⚠️ " + Escape(gap));
        }
        else
        {
            sb.AppendLine("🎯 Ще не оцінено");
        }

        if (v.IsInactive)
            sb.AppendLine("⛔ <b>Вакансія вже неактивна</b>");

        sb.AppendLine().Append("Статус: ").Append(StatusLabel(v.Status));
        if (v.AppliedAt is { } applied)
            sb.Append(" · подано ").Append(Date(applied));
        return sb.ToString();
    }

    public static InlineKeyboardMarkup CardKeyboard(Vacancy v)
    {
        var rows = new List<InlineKeyboardButton[]>();
        switch (v.Status)
        {
            case VacancyStatus.New:
                rows.Add([Btn("✍️ Написати відгук", Cb.Draft(v.Id)), Btn("⏭ Пропустити", Cb.SetStatus(v.Id, "sk"))]);
                break;
            case VacancyStatus.Drafted:
                rows.Add([Btn("📄 Чернетка", Cb.Draft(v.Id)), Btn("✅ Я подався", Cb.SetStatus(v.Id, "ap"))]);
                break;
            case VacancyStatus.Applied or VacancyStatus.Replied:
                rows.Add([Btn("🗓 Співбесіда", Cb.SetStatus(v.Id, "iv")), Btn("❌ Відмова", Cb.SetStatus(v.Id, "rj"))]);
                break;
            case VacancyStatus.Interview:
                rows.Add([Btn("🎉 Офер", Cb.SetStatus(v.Id, "of")), Btn("❌ Відмова", Cb.SetStatus(v.Id, "rj"))]);
                break;
            case VacancyStatus.Rejected or VacancyStatus.Skipped:
                rows.Add([Btn("↩️ Повернути в нові", Cb.SetStatus(v.Id, "nw"))]);
                break;
        }

        var links = new List<InlineKeyboardButton> { Url("🔗 Djinni", v.Url) };
        if (v.FitScore is null && !v.IsInactive)
            links.Add(Btn("🎯 Оцінити", Cb.Rescore(v.Id)));
        rows.Add([.. links]);
        rows.Add(BackToMenu);
        return Rows(rows);
    }

    public static string DraftView(Vacancy v)
    {
        var sb = new StringBuilder();
        sb.Append("✍️ <b>Відгук:</b> ").Append(Escape(v.Title));
        if (!string.IsNullOrWhiteSpace(v.Company))
            sb.Append(" · ").Append(Escape(v.Company));
        sb.AppendLine().AppendLine();
        sb.Append("<pre>").Append(Escape(v.Draft)).AppendLine("</pre>");

        var warnings = Lines(v.DraftWarnings).ToList();
        if (warnings.Count > 0)
            sb.AppendLine().AppendLine($"⚠️ У тексті є навички, яких немає в профілі: <b>{Escape(string.Join(", ", warnings))}</b>. Прибери або перевір перед відправкою.");

        sb.AppendLine().Append("Як подати: торкнись тексту, щоб скопіювати → «🔗 Відкрити вакансію» → «Відгукнутися» → встав → надішли. Потім натисни «✅ Я подався».");
        return sb.ToString();
    }

    public static InlineKeyboardMarkup DraftKeyboard(Vacancy v) => Rows(
        [Url("🔗 Відкрити вакансію", v.Url)],
        [Btn("✅ Я подався", Cb.SetStatus(v.Id, "ap")), Btn("🔄 Інший варіант", Cb.Redraft(v.Id))],
        [Btn("⬅️ До вакансії", Cb.Vacancy(v.Id))]);

    public static string ListItem(Vacancy v)
    {
        var score = v.FitScore is { } fit ? $"{fit}% · " : "";
        var company = string.IsNullOrWhiteSpace(v.Company) ? "" : $" — {v.Company}";
        var status = v.Status == VacancyStatus.New ? "" : $"{StatusIcon(v.Status)} ";
        return Truncate($"{status}{score}{v.Title}{company}", 60);
    }

    public static string EventCard(MailEvent e)
    {
        var sb = new StringBuilder();
        if (e.Kind == MailKind.Rejection)
            sb.Append("❌ <b>Схоже на відмову</b> · ").Append(Escape(e.Company ?? "компанія"));
        else
            sb.Append("📩 <b>").Append(Escape(e.Contact ?? "Рекрутер")).Append("</b>").Append(e.Company is null ? "" : " · " + Escape(e.Company));
        sb.AppendLine();

        if (e.Vacancy is { } v)
            sb.Append("Вакансія: ").AppendLine(Escape(v.Title));
        sb.Append("🕐 ").AppendLine(Date(e.ReceivedAt)).AppendLine();

        sb.Append("<blockquote>").Append(Escape(Truncate(e.Text ?? "(без тексту)", 2500))).AppendLine("</blockquote>");
        if (e.Kind == MailKind.Rejection)
            sb.AppendLine().Append("Статус вакансії → «відмова». Якщо це помилка, відкрий вакансію і поверни статус.");
        return sb.ToString();
    }

    public static InlineKeyboardMarkup EventKeyboard(MailEvent e)
    {
        var rows = new List<InlineKeyboardButton[]>();
        if (e.Kind == MailKind.RecruiterMessage)
            rows.Add([Btn("💬 Чернетка відповіді", Cb.ReplyDraft(e.Id))]);

        var links = new List<InlineKeyboardButton>();
        if (e.InboxUrl is not null)
            links.Add(Url("🔗 Розмова в Djinni", e.InboxUrl));
        if (e.VacancyId is { } vacancyId)
            links.Add(Btn("📄 Вакансія", Cb.Vacancy(vacancyId)));
        rows.Add([.. links]);
        rows.Add(BackToMenu);
        return Rows(rows);
    }

    public static string ReplyView(MailEvent e)
    {
        var sb = new StringBuilder();
        sb.Append("💬 <b>Відповідь для ").Append(Escape(e.Contact ?? "рекрутера")).AppendLine("</b>").AppendLine();
        sb.Append("<pre>").Append(Escape(e.ReplyDraft)).AppendLine("</pre>").AppendLine();
        sb.Append("Скопіюй текст і надішли в розмові на Djinni. Бот сам нічого не відправляє.");
        return sb.ToString();
    }

    public static InlineKeyboardMarkup ReplyKeyboard(MailEvent e)
    {
        var rows = new List<InlineKeyboardButton[]>();
        if (e.InboxUrl is not null)
            rows.Add([Url("🔗 Розмова в Djinni", e.InboxUrl)]);
        rows.Add([Btn("🔄 Інший варіант", Cb.Rereply(e.Id)), Btn("⬅️ Назад", Cb.Conversation(e.Id))]);
        return Rows(rows);
    }

    public static string EventListItem(MailEvent e)
    {
        var icon = e.Kind == MailKind.Rejection ? "❌" : "📩";
        var who = e.Company ?? e.Contact ?? "Рекрутер";
        var title = e.Vacancy?.Title is { } t ? $" — {t}" : "";
        return Truncate($"{icon} {KyivDate(e.ReceivedAt)} {who}{title}", 60);
    }

    public static string Date(DateTimeOffset utc) => KyivTime.ToKyiv(utc).ToString("dd.MM HH:mm");

    private static string KyivDate(DateTimeOffset utc) => KyivTime.ToKyiv(utc).ToString("dd.MM");

    private static string StatusIcon(VacancyStatus status) => StatusLabel(status).Split(' ')[0];

    private static IEnumerable<string> Lines(string? value) =>
        (value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
