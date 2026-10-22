namespace SystematiaBot.Core.Entities;

public class Post
{
    public int Id { get; set; }
    public int NewsItemId { get; set; }
    public NewsItem? NewsItem { get; set; }
    public PostKind Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public int Version { get; set; } = 1;
    public string? ValidationJson { get; set; }
    public bool IsApproved { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
