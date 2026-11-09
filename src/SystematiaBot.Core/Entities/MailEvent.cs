namespace SystematiaBot.Core.Entities;

/// <summary>One processed e-mail. Its Gmail id makes the poller idempotent.</summary>
public class MailEvent
{
    public int Id { get; set; }
    public required string GmailId { get; set; }
    public MailKind Kind { get; set; }
    public required string Subject { get; set; }
    public string? Contact { get; set; }
    public string? Company { get; set; }

    /// <summary>The recruiter's message, unwrapped to normal paragraphs.</summary>
    public string? Text { get; set; }

    /// <summary>Link to the conversation in the Djinni inbox.</summary>
    public string? InboxUrl { get; set; }

    public int? VacancyId { get; set; }
    public Vacancy? Vacancy { get; set; }

    public string? ReplyDraft { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool Notified { get; set; }
}
