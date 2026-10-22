namespace SystematiaBot.Core.Entities;

public class SeoRun
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public required string StartUrl { get; set; }
    public SeoMode Mode { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int PagesCrawled { get; set; }
    public string? SummaryJson { get; set; }
    public string? CsvPath { get; set; }
}
