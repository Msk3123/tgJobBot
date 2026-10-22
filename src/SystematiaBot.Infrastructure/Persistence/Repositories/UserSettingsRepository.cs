using Microsoft.EntityFrameworkCore;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence.Repositories;

public sealed class UserSettingsRepository(BotDbContext db) : IUserSettingsRepository
{
    public async Task<UserSettings> GetOrCreateAsync(long chatId, string defaultCity, CancellationToken ct)
    {
        var settings = await db.UserSettings.FindAsync([chatId], ct);
        if (settings is not null)
            return settings;

        settings = new UserSettings { ChatId = chatId, City = defaultCity };
        db.UserSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    public async Task<IReadOnlyList<UserSettings>> GetDigestSubscribersAsync(CancellationToken ct) =>
        await db.UserSettings.Where(u => u.DigestEnabled).ToListAsync(ct);

    public async Task SaveAsync(UserSettings settings, CancellationToken ct)
    {
        if (db.Entry(settings).State == EntityState.Detached)
            db.UserSettings.Update(settings);
        await db.SaveChangesAsync(ct);
    }
}
