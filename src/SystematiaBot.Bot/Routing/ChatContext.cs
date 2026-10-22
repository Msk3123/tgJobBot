namespace SystematiaBot.Bot.Routing;

/// <summary>Who is talking to the bot and which message (if any) the current screen should replace.</summary>
public sealed class ChatContext
{
    public required long ChatId { get; init; }
    public required long UserId { get; init; }
    public string? Username { get; init; }
    public required bool IsOwner { get; init; }

    /// <summary>Message to edit in place (set for callback queries).</summary>
    public int? MessageId { get; set; }

    public bool IsDemo => !IsOwner;
}
