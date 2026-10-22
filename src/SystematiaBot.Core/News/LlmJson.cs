using System.Text.Json;

namespace SystematiaBot.Core.News;

internal static class LlmJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parses the model output, tolerating ```json fences and text around the object.</summary>
    public static T? TryParse<T>(string text) where T : class
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(text[start..(end + 1)], Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
