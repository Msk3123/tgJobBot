using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

public interface ISeoRunRepository
{
    Task AddAsync(SeoRun run, CancellationToken ct);
    Task UpdateAsync(SeoRun run, CancellationToken ct);
}
