using Microsoft.EntityFrameworkCore;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence.Repositories;

public sealed class UserSettingsRepository(BotDbContext db) : IUserSettingsRepository
{
    public async Task<UserSettings> GetOrCreateAsync(long chatId, CancellationToken ct)
    {
        var settings = await db.UserSettings.FindAsync([chatId], ct);
        if (settings is not null)
            return settings;

        settings = new UserSettings { ChatId = chatId };
        db.UserSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken ct)
    {
        if (db.Entry(settings).State == EntityState.Detached)
            db.UserSettings.Update(settings);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class LlmUsageRepository(BotDbContext db) : ILlmUsageRepository
{
    public async Task AddAsync(LlmUsage usage, CancellationToken ct)
    {
        db.LlmUsages.Add(usage);
        await db.SaveChangesAsync(ct);
    }

    public async Task<double> GetCostAsync(DateOnly day, CancellationToken ct) =>
        await db.LlmUsages.Where(u => u.Day == day).SumAsync(u => u.CostUsd, ct);
}
