# CLAUDE.md — Systematia Assistant Bot

Full spec: `SPEC.md`. Work in stages (SPEC §12); after each: build → tests → commit → push.

## Rules
- .NET 10, nullable enabled, async/await with `CancellationToken`, no `.Result` / `.Wait()`, no God classes.
- `SystematiaBot.Core` has no references to Telegram, EF Core or OpenAI. Infrastructure implements Core abstractions.
- Never invent library APIs (Telegram.Bot 22.x, OpenAI 2.x) — check the installed assembly.
- Never invent news sources; `config/sources.json` contains only sources verified to return posts in the last 7 days.
- Test data only in `demo/` and `tests/` — never hard-coded in production code.
- Secrets only via `dotnet user-secrets` (project `src/SystematiaBot.Bot`) or env vars. Never in code, logs or commits.
- Code, comments, commits: English. Bot UI and README: Ukrainian.
- Commits: Conventional Commits (`feat(seo): ...`, `fix(bot): ...`).
- No new NuGet packages outside SPEC §3 without explaining why.

## Layout
- `config/`, `prompts/`, `demo/` — repo root, copied to output by the Bot csproj.
- `data/`, `logs/`, `exports/` — runtime state at the repo root (git-ignored).

## Commands
- Build: `dotnet build`
- Tests: `dotnet test`
- Run: `dotnet run --project src/SystematiaBot.Bot`
