using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

public interface IUserSettingsRepository
{
    Task<UserSettings> GetOrCreateAsync(long chatId, string defaultCity, CancellationToken ct);
    Task<IReadOnlyList<UserSettings>> GetDigestSubscribersAsync(CancellationToken ct);
    Task SaveAsync(UserSettings settings, CancellationToken ct);
}
