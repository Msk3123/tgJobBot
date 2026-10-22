# Systematia Assistant Bot

Telegram-бот, який автоматизує дві рутини проєкту «Захід Буг»: пошук і підготовку міських новин та щотижневу SEO-перевірку сайту.
**AI робить рутину (збір, фільтр, рерайт, перевірку), рішення приймає людина** — бот нічого не публікує сам.

## Що вміє

- **📰 Новини** — збирає пости з Telegram-каналів і сайтів міста за останні 7 днів, відсіює неміські й дублі, AI оцінює цікавість (1–5). Кандидати показуються картками: ✅ Беру / ❌ Відкинути з причиною / 🔗 Джерело.
- **✍️ Рерайт** — обрані новини AI переписує в пости для соцмереж (заголовок ≤ 100 символів, 100–200 слів, 2–3 абзаци) і в статтю для сайту (заголовок 50–85, 350–600 слів).
- **🔎 Валідатор без AI** — перевіряє ліміти, копіювання джерела (>12 слів поспіль) і **факти**: кожне число, дата, цитата й ім'я мають бути в джерелі. Якщо ні — до 2 автоматичних переписувань з переліком проблем, далі ⚠️ і рішення за людиною.
- **📄 Експорт** — `.docx` з постами, статтею і відкинутими новинами (з причинами) + кожен пост окремим повідомленням для зручного копіювання з телефона.
- **🔍 SEO-аудит** — robots.txt, sitemap (з index і gzip), стратифікована вибірка сторінок, 20+ правил (дублі Title/Description, порожні H1, noindex у sitemap, canonical, hreflang, тонкий контент, тестові сторінки, soft-404, http→https, www). Резюме в чаті + `report.csv` для Excel.
- **⏰ Дайджест** — щодня о 09:00 (Київ) бот сам збирає новини і пише «Знайдено N нових кандидатів».
- **🧪 Демо-режим** — для всіх, крім власника: ті самі екрани на заготовлених даних, без викликів OpenAI і без краулінгу.

## Пайплайн

```mermaid
flowchart LR
    S[Джерела<br/>Telegram · RSS · HTML] --> F[Свіжість ≤ 7 днів<br/>+ мін. довжина]
    F --> L[Локальність<br/>localTerms]
    L --> D[Дедуплікація<br/>шингли + IDF-стеми]
    D --> A[AI-оцінка 1–5<br/>батчі по 10]
    A --> H{{Людина:<br/>✅ / ❌ + причина}}
    H --> R[AI-рерайт]
    R --> V[Валідатор<br/>ліміти + факти]
    V -- помилки, ≤2 рази --> R
    V --> H2{{Людина:<br/>👍 / ✏️ / 🗑}}
    H2 --> E[.docx + пости в чат]
```

## Запуск

Потрібно: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download), токен бота від [@BotFather](https://t.me/BotFather), ключ OpenAI.

```powershell
git clone https://github.com/Msk3123/tgJobBot.git
cd tgJobBot

dotnet user-secrets set "Telegram:BotToken" "<токен>"       --project src/SystematiaBot.Bot
dotnet user-secrets set "OpenAI:ApiKey"     "<ключ>"         --project src/SystematiaBot.Bot
dotnet user-secrets set "Bot:OwnerId"       "<твій Telegram ID>" --project src/SystematiaBot.Bot

dotnet run --project src/SystematiaBot.Bot
```

- Модель OpenAI задається в `config/appsettings.json` → `OpenAI:Model` (там стоїть `SET_ME`, без моделі AI-кроки вимкнені, решта працює). Там само — денний бюджет і ціни за мільйон токенів для `LlmBudgetGuard`.
- Свій Telegram ID видно в лозі після першого `/start` (`/start from chat …, user …`).
- Секрети — тільки в user-secrets або змінних середовища (`Telegram__BotToken` тощо), у репозиторії їх немає.
- БД (`data/bot.db`), логи (`logs/`) і звіти (`exports/`) створюються в корені репозиторію.

Консольні команди (ті самі сервіси без Telegram):

```powershell
dotnet run --project src/SystematiaBot.Cli -- seo https://maxidveri.com [--full]
dotnet run --project src/SystematiaBot.Cli -- sources     # перевірка джерел: постів за 7 днів
dotnet run --project src/SystematiaBot.Cli -- collect     # збір новин у data/bot.db
```

Тести: `dotnet test`.

## Джерела новин — Шептицький

Перевірено 22.10.2026 командою `sources` (постів за останні 7 днів). Список — у `config/sources.json`.

| Назва | Тип | URL | Постів за 7 днів | Про місто |
|---|---|---|---|---|
| Шептицька міська рада | Telegram | https://t.me/s/sheptytska_mr | 71 | усі (міське джерело) |
| Ехо 80100 | Telegram | https://t.me/s/echo80100 | 95 | усі (міське джерело) |
| ДСНС Львівщини | Telegram | https://t.me/s/dsns_lviv | 33 | фільтр localTerms (0 за тиждень) |
| Львівська ОВА | Telegram | https://t.me/s/kozytskyy_maksym_official | 85 | фільтр localTerms (22) |
| ZAXID.NET — тег «Шептицький» | HtmlList | https://zaxid.net/chervonograd_tag48789/ | 3 | усі (сторінка тегу) |

Перевірені, але **не додані**:

| Джерело | Причина |
|---|---|
| lv.npu.gov.ua (поліція Львівщини) | стрічка новин рендериться JavaScript-ом, HTML без новин; Telegram-каналу поліції області з постами за тиждень не знайдено |
| lv.dsns.gov.ua | 403 для запитів без браузера; Telegram ДСНС додано |
| chervonograd.net (RSS) | останній пост 2017 року |
| echo80100.info (RSS) | останній пост 2021 року (канал «Ехо 80100» додано) |
| chervonograd.fm | останній пост 29.09 — немає новин за 7 днів |
| t.me/chervonograd_mr | старий канал ради, останній пост у лютому 2026 |
| zaxidbug.com | наш власний сайт — як джерело не використовується |

Шептицька РВА публікує новини у Facebook — без API туди не дістатись, але її повідомлення передруковують канали ради, «Ехо 80100» і ZAXID.NET.

## Архітектура

```
src/
  SystematiaBot.Core/            сутності, абстракції, пайплайн новин, валідатор, SEO-правила (без Telegram/EF/OpenAI)
  SystematiaBot.Infrastructure/  EF Core + SQLite, джерела (t.me/s, RSS, HTML), OpenAI, SEO-краулер, DOCX/CSV
  SystematiaBot.Bot/             Generic Host, long polling, роутер, хендлери, демо-режим, дайджест
  SystematiaBot.Cli/             консольні команди
tests/SystematiaBot.Tests/       xUnit + Shouldly, фікстури без мережі
config/  prompts/  demo/         налаштування, промпти (.md з {{плейсхолдерами}}), демо-дані
```

Довгі операції (збір, рерайт, краулінг) ідуть у `BackgroundJobRunner`: одна задача типу на чат, кнопка «⛔ Скасувати», прогрес редагує одне повідомлення не частіше разу на 2 с.

## Як замінити OpenAI на Claude

Уся логіка працює через `ILlmClient` (`Core/Abstractions/ILlmClient.cs`).

1. Реалізувати `CompleteAsync` у `src/SystematiaBot.Infrastructure/Llm/OpenAiLlmClient.cs` → клас `ClaudeLlmClient` (Messages API, `usage.input_tokens` / `output_tokens`).
2. У `DependencyInjection.AddLlm` змінити один рядок — зареєструвати `ClaudeLlmClient` замість `OpenAiLlmClient` (облік бюджету `LlmBudgetGuard` лишається).

## Скріни

<!-- TODO: скріни / GIF з телефона -->
_Скоро._
