using System.Text.Json;
using System.Text.Json.Serialization;
using SystematiaBot.Core.News;

namespace SystematiaBot.Infrastructure.Sources;

/// <summary>Loads cities and their news sources from config/sources.json.</summary>
public sealed class JsonCityCatalog : ICityCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonCityCatalog(string path)
    {
        var file = JsonSerializer.Deserialize<SourcesFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidOperationException($"Empty sources file: {path}");
        Cities = file.Cities;
    }

    public IReadOnlyList<CityConfig> Cities { get; }

    public CityConfig? Find(string id) => Cities.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    private sealed class SourcesFile
    {
        public List<CityConfig> Cities { get; init; } = [];
    }
}
