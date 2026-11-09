using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;
using Telegram.Bot.Types;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

/// <summary>
/// The owner's CV as structured JSON (sent to the bot as a .json file, stored in the DB, never in git).
/// Structured data instead of a .docx: the tailor can address every project and bullet by id,
/// and the renderer controls the layout, so a tailored PDF never breaks ATS parsing.
/// </summary>
public sealed partial class CvHandler(
    BotUi ui,
    IUserSettingsRepository settingsRepository,
    ICvRenderer renderer,
    ILogger<CvHandler> logger) : ICallbackHandler
{
    public const int MaxJsonBytes = 256 * 1024;

    public const string NoCvHint =
        "📄 <b>CV ще не завантажено</b>\n\n" +
        "Надішли мені файл <code>cv.json</code> зі своїм CV. Формат — /cv → «🧩 Шаблон JSON».\n" +
        "Кожен проєкт і пункт має свій id: під вакансію я лише переставляю і відбираю їх, тексти лишаються твої.";

    public string Module => "cv";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "show":
                await ShowAsync(ctx, ct);
                return null;
            case "pdf":
                return await SendBaseAsync(ctx, ct) ? null : "Спершу завантаж CV";
            case "tpl":
                var template = JsonSerializer.SerializeToUtf8Bytes(Template(), new JsonSerializerOptions(CvData.Json) { WriteIndented = true });
                await ui.SendDocumentAsync(ctx.ChatId, template, "cv_template.json",
                    "🧩 Шаблон. Заповни своїми даними й надішли файл назад. id — будь-які короткі латинські слова, головне унікальні.", null, ct);
                return null;
            default:
                return null;
        }
    }

    public async Task ShowAsync(ChatContext ctx, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        var cv = string.IsNullOrWhiteSpace(settings.CvJson) ? null : CvData.TryParse(settings.CvJson, out _);
        if (cv is null)
        {
            await ui.ShowAsync(ctx, NoCvHint, Rows([Btn("🧩 Шаблон JSON", Cb.CvTemplate)], BackToMenu), ct);
            return;
        }

        await ui.ShowAsync(ctx, Summary(cv) + "\n\nЩоб оновити — просто надішли новий <code>.json</code>.",
            Rows([Btn("📄 Базове CV (PDF)", Cb.CvBase), Btn("🧩 Шаблон JSON", Cb.CvTemplate)], BackToMenu), ct);
    }

    /// <summary>The owner sent a document. Only small .json files are accepted; anything else gets a hint.</summary>
    public async Task ImportAsync(ChatContext ctx, Document document, CancellationToken ct)
    {
        var isJson = document.FileName?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true
            || document.MimeType == "application/json";
        if (!isJson)
        {
            await ui.SendAsync(ctx, "Я приймаю тільки CV у форматі <code>.json</code>. Шаблон — /cv.", null, ct);
            return;
        }

        if (document.FileSize is > MaxJsonBytes)
        {
            await ui.SendAsync(ctx, $"Файл завеликий: CV у JSON має бути до {MaxJsonBytes / 1024} КБ.", null, ct);
            return;
        }

        using var buffer = new MemoryStream();
        await ui.DownloadAsync(document.FileId, buffer, ct);
        if (buffer.Length > MaxJsonBytes)
        {
            await ui.SendAsync(ctx, $"Файл завеликий: CV у JSON має бути до {MaxJsonBytes / 1024} КБ.", null, ct);
            return;
        }

        var json = Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('﻿');
        if (CvData.TryParse(json, out var error) is not { } cv)
        {
            await ui.SendAsync(ctx, $"😕 Не вдалося прочитати CV: {Escape(error ?? "невідома помилка")}\n\nПорівняй зі шаблоном: /cv", null, ct);
            return;
        }

        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        // Re-serialized: unknown fields are dropped, so the DB holds exactly what the renderer understands.
        settings.CvJson = JsonSerializer.Serialize(cv, CvData.Json);
        await settingsRepository.SaveAsync(settings, ct);
        logger.LogInformation("CV imported: {Projects} projects, {Experience} experience entries", cv.Projects.Count, cv.Experience.Count);

        await ui.SendAsync(ctx, "✅ CV збережено.\n\n" + Summary(cv), null, ct);
        await SendPdfAsync(ctx.ChatId, cv, "Базова версія. Під конкретну вакансію — кнопка «📄 CV під вакансію» в картці.", ct);
    }

    private async Task<bool> SendBaseAsync(ChatContext ctx, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        if (string.IsNullOrWhiteSpace(settings.CvJson) || CvData.TryParse(settings.CvJson, out _) is not { } cv)
        {
            await ui.SendAsync(ctx, NoCvHint, Rows([Btn("🧩 Шаблон JSON", Cb.CvTemplate)]), ct);
            return false;
        }

        await SendPdfAsync(ctx.ChatId, cv, "Базова версія CV.", ct);
        return true;
    }

    private Task SendPdfAsync(long chatId, CvData cv, string caption, CancellationToken ct) =>
        ui.SendDocumentAsync(chatId, renderer.Render(cv), FileName(cv.Name, null), "📄 " + Escape(caption), null, ct);

    private static string Summary(CvData cv)
    {
        var bullets = cv.Experience.Concat(cv.Projects).Sum(s => s.Bullets.Count);
        var skills = cv.Skills.Sum(g => g.Items.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
        return $"📄 <b>{Escape(cv.Name)}</b>" + (cv.Headline is { } h ? $"\n{Escape(h)}" : "") +
               $"\n\nДосвід: {cv.Experience.Count} · Проєкти: {cv.Projects.Count} · Пунктів: {bullets} · Навичок: {skills}";
    }

    /// <summary>"Maksym Paliy" + "SoftServe Inc." → "Maksym_Paliy_CV_SoftServe_Inc.pdf". Recruiters see this name, so no ids or dates.</summary>
    public static string FileName(string name, string? company)
    {
        static string Clean(string value, int max)
        {
            var cleaned = NonWordRegex().Replace(value, "_").Trim('_');
            return cleaned.Length > max ? cleaned[..max].TrimEnd('_') : cleaned;
        }

        var person = Clean(name, 40);
        var baseName = person.Length == 0 ? "CV" : person + "_CV";
        var target = company is null ? "" : Clean(company, 30);
        return (target.Length == 0 ? baseName : $"{baseName}_{target}") + ".pdf";
    }

    private static CvData Template() => new()
    {
        Name = "Ім'я Прізвище",
        Headline = ".NET Developer · C#, ASP.NET Core, EF Core",
        Contacts =
        [
            new CvLink { Label = "email@example.com", Url = "mailto:email@example.com" },
            new CvLink { Label = "github.com/username", Url = "https://github.com/username" }
        ],
        Summary = "2–3 речення: хто ти, що вмієш, чим підтверджено.",
        Experience =
        [
            new CvSection
            {
                Id = "job1", Title = "Intern .NET Developer — Company", Period = "2025",
                Bullets = [new CvBullet { Id = "job1-a", Text = "Що зробив і який результат (з цифрою)." }],
                Tech = "C#, ASP.NET Core"
            }
        ],
        Projects =
        [
            new CvSection
            {
                Id = "shop", Title = "Online shop", Period = "2025",
                Links = [new CvLink { Label = "demo", Url = "https://example.com" }],
                Bullets =
                [
                    new CvBullet { Id = "shop-api", Text = "REST API на ASP.NET Core, 40+ ендпоінтів." },
                    new CvBullet { Id = "shop-db", Text = "EF Core + PostgreSQL, міграції, індекси." }
                ],
                Tech = "ASP.NET Core, EF Core, PostgreSQL, Docker"
            },
            new CvSection
            {
                Id = "bot", Title = "Telegram bot", Period = "2026",
                Bullets = [new CvBullet { Id = "bot-a", Text = "Фоновий воркер, SQLite, інтеграція з API." }],
                Tech = ".NET Worker, Telegram.Bot"
            }
        ],
        Skills =
        [
            new CvSkillGroup { Id = "backend", Label = "Backend", Items = "C#, .NET, ASP.NET Core, EF Core" },
            new CvSkillGroup { Id = "db", Label = "Databases", Items = "PostgreSQL, SQL Server" }
        ],
        Education = ["Університет, спеціальність, роки"],
        Languages = "English — B2, Ukrainian — native",
        Leadership = "Волонтерство, організації, менторство — одним рядком"
    };

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWordRegex();
}
