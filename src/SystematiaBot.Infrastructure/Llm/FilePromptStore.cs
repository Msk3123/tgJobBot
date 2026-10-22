using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SystematiaBot.Core.Abstractions;

namespace SystematiaBot.Infrastructure.Llm;

/// <summary>Loads prompts/{name}.md once and fills {{placeholders}}; unknown placeholders become empty.</summary>
public sealed partial class FilePromptStore(string directory) : IPromptStore
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public string Render(string name, IReadOnlyDictionary<string, string> values)
    {
        var template = _cache.GetOrAdd(name, n => File.ReadAllText(Path.Combine(directory, $"{n}.md")));
        return PlaceholderRegex().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
