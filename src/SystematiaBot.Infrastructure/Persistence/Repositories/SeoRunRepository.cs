using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence.Repositories;

public sealed class SeoRunRepository(BotDbContext db) : ISeoRunRepository
{
    public async Task AddAsync(SeoRun run, CancellationToken ct)
    {
        db.SeoRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SeoRun run, CancellationToken ct)
    {
        db.SeoRuns.Update(run);
        await db.SaveChangesAsync(ct);
    }
}
