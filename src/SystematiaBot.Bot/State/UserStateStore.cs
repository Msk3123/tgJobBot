using System.Collections.Concurrent;

namespace SystematiaBot.Bot.State;

public enum AwaitingInput
{
    None,
    SeoUrl,
    RejectNote,
    PostEdit
}

/// <summary>What the bot expects from the next text message of a chat.</summary>
public sealed record UserState(AwaitingInput Awaiting, int? TargetId = null);

/// <summary>In-memory conversation state; losing it on restart only resets "waiting for input" prompts.</summary>
public sealed class UserStateStore
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();
    private readonly ConcurrentDictionary<long, string> _pendingSeoUrls = new();

    public UserState Get(long chatId) => _states.GetValueOrDefault(chatId, new UserState(AwaitingInput.None));

    public void Set(long chatId, AwaitingInput awaiting, int? targetId = null) => _states[chatId] = new UserState(awaiting, targetId);

    public void Reset(long chatId) => _states.TryRemove(chatId, out _);

    public void SetSeoUrl(long chatId, string url) => _pendingSeoUrls[chatId] = url;

    public string? GetSeoUrl(long chatId) => _pendingSeoUrls.GetValueOrDefault(chatId);
}
