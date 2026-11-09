namespace SystematiaBot.Core.Entities;

public class UserSettings
{
    public long ChatId { get; set; }
    public bool DigestEnabled { get; set; } = true;
    public DateOnly? LastDigestDate { get; set; }

    /// <summary>Candidate profile (experience, stack, preferences) used for scoring and drafts. Lives only in the database, never in git.</summary>
    public string? Profile { get; set; }

    public DateTimeOffset? LastMailSyncAt { get; set; }
}
