namespace SystematiaBot.Core.Abstractions;

public sealed record MailMessage(string Id, string From, string Subject, string PlainText, DateTimeOffset ReceivedAt);

/// <summary>Read-only access to the owner's mailbox (Gmail API with the gmail.readonly scope).</summary>
public interface IMailbox
{
    bool IsConfigured { get; }

    /// <summary>Message ids matching a Gmail search query, newest first.</summary>
    Task<IReadOnlyList<string>> SearchAsync(string query, int max, CancellationToken ct);

    Task<MailMessage> GetAsync(string id, CancellationToken ct);
}

/// <summary>The mailbox cannot be reached or the token was revoked; the message is safe to show to the owner.</summary>
public sealed class MailboxException(string message, Exception? inner = null) : Exception(message, inner);
