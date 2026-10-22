namespace SystematiaBot.Core.Entities;

public class UserSettings
{
    public long ChatId { get; set; }
    public required string City { get; set; }
    public bool DigestEnabled { get; set; }
    public DateOnly? LastDigestDate { get; set; }
    public int? LastMenuMessageId { get; set; }
}
