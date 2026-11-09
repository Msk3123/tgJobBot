namespace SystematiaBot.Core.Entities;

public class UserSettings
{
    public long ChatId { get; set; }
    public bool DigestEnabled { get; set; } = true;
    public DateOnly? LastDigestDate { get; set; }

    /// <summary>Candidate profile (experience, stack, preferences) used for scoring and drafts. Lives only in the database, never in git.</summary>
    public string? Profile { get; set; }

    public DateTimeOffset? LastMailSyncAt { get; set; }
    public DateTimeOffset? LastFeedSyncAt { get; set; }

    /// <summary>Structured CV (JSON uploaded through the chat) used to build tailored PDFs. Never in git.</summary>
    public string? CvJson { get; set; }
}
