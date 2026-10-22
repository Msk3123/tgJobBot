namespace SystematiaBot.Core.Entities;

public class NewsItem
{
    public int Id { get; set; }
    public required string City { get; set; }
    public int SourceId { get; set; }
    public Source? Source { get; set; }
    public required string Url { get; set; }
    public string? Title { get; set; }
    public required string RawText { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>SHA256 of the normalized text.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Id of the representative item of the duplicate cluster (null when the item is alone).</summary>
    public int? ClusterId { get; set; }

    public NewsStatus Status { get; set; } = NewsStatus.New;
    public RejectReason? RejectReason { get; set; }
    public string? RejectNote { get; set; }
    public int? Score { get; set; }
    public string? ScoreReason { get; set; }
    public string? Category { get; set; }
}
