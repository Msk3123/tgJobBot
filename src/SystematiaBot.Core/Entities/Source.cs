namespace SystematiaBot.Core.Entities;

public class Source
{
    public int Id { get; set; }
    public required string City { get; set; }
    public required string Name { get; set; }
    public SourceKind Kind { get; set; }
    public required string Url { get; set; }

    /// <summary>JSON with CSS selectors for <see cref="SourceKind.HtmlList"/> sources.</summary>
    public string? Selectors { get; set; }

    /// <summary>Source publishes only about this city, so the locality heuristic is skipped.</summary>
    public bool IsCityOnly { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastFetchedAt { get; set; }
    public string? LastError { get; set; }
}
