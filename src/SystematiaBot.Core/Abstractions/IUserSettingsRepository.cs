using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

public interface IUserSettingsRepository
{
    Task<UserSettings> GetOrCreateAsync(long chatId, CancellationToken ct);
    Task SaveAsync(UserSettings settings, CancellationToken ct);
}
