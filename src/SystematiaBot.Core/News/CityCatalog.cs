using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.News;

/// <summary>A city from config/sources.json.</summary>
public sealed class CityConfig
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<string> Aliases { get; init; } = [];
    public List<string> LocalTerms { get; init; } = [];
    public List<SourceConfig> Sources { get; init; } = [];
}

public sealed class SourceConfig
{
    public required string Name { get; init; }
    public SourceKind Kind { get; init; }
    public required string Url { get; init; }
    public bool IsCityOnly { get; init; }
    public bool IsEnabled { get; init; } = true;

    /// <summary>CSS selectors for HtmlList sources: item, title, link, date, dateFormat, content.</summary>
    public Dictionary<string, string>? Selectors { get; init; }
}

public interface ICityCatalog
{
    IReadOnlyList<CityConfig> Cities { get; }
    CityConfig? Find(string id);
}
