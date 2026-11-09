# CLAUDE.md — Job Hunter Bot

Personal Telegram assistant for a job search on Djinni: reads Djinni e-mails through the Gmail API,
scores vacancies against the owner's profile, drafts application messages and replies. It never sends anything itself.

## Rules
- .NET 10, nullable enabled, async/await with `CancellationToken`, no `.Result` / `.Wait()`, no God classes.
- `SystematiaBot.Core` has no references to Telegram, EF Core, HTTP or OpenAI. Infrastructure implements Core abstractions.
- Never invent library APIs (Telegram.Bot 22.x, OpenAI 2.x) — check the installed assembly.
- The bot answers only `Bot:OwnerId`. It works with a personal mailbox and CV: no demo mode, no data for strangers.
- Personal data (profile, e-mail fixtures, names, inbox ids) never goes into git. Test fixtures are sanitized copies.
- Third-party text (vacancies, recruiter messages) goes to the LLM in the user turn inside tags, never into the system prompt.
- Drafts are checked by `SkillGrounding`: the model may not claim technologies the profile does not have.
- Secrets only via `dotnet user-secrets` (project `src/SystematiaBot.Bot`) or env vars / `.env`. Never in code, logs or commits.
- Code, comments, commits: English. Bot UI and README: Ukrainian.
- Commits: Conventional Commits (`feat(jobs): ...`, `fix(bot): ...`).
- No new NuGet packages without explaining why (Gmail is plain HttpClient on purpose).

## Layout
- `config/`, `prompts/` — repo root, copied to output by the Bot csproj.
- `data/`, `logs/` — runtime state at the repo root (git-ignored); `/app/data` in Docker.
- `src/SystematiaBot.Cli` — one-time `gmail-auth` (OAuth code + PKCE, loopback redirect).

## Commands
- Build: `dotnet build`
- Tests: `dotnet test`
- Run: `dotnet run --project src/SystematiaBot.Bot`
- New migration: `dotnet ef migrations add <Name> --project src/SystematiaBot.Infrastructure --startup-project src/SystematiaBot.Infrastructure --output-dir Persistence/Migrations`
