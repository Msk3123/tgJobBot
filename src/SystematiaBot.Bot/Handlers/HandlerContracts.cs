using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;

namespace SystematiaBot.Bot.Handlers;

/// <summary>Handles callback queries of one module (the first segment of <see cref="CallbackData"/>).</summary>
public interface ICallbackHandler
{
    string Module { get; }

    /// <summary>Returns an optional toast shown on the pressed button.</summary>
    Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct);
}

/// <summary>Handles a free-text message the bot is waiting for.</summary>
public interface ITextInputHandler
{
    AwaitingInput Awaiting { get; }

    Task HandleAsync(ChatContext ctx, string text, int? targetId, CancellationToken ct);
}
