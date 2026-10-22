using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Sources;

public static class NewsHttp
{
    public const string ClientName = "news";

    /// <summary>Some government and media sites answer 403 to non-browser user agents.</summary>
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
}

public static class KyivTime
{
    /// <summary>Windows id first ("FLE Standard Time"), IANA "Europe/Kyiv" as fallback.</summary>
    public static TimeZoneInfo Zone { get; } = Find("FLE Standard Time") ?? Find("Europe/Kyiv") ?? Find("Europe/Kiev") ?? TimeZoneInfo.Utc;

    public static DateTimeOffset ToKyiv(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    private static TimeZoneInfo? Find(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}

public sealed class NewsSourceFactory(IEnumerable<INewsSource> sources) : INewsSourceFactory
{
    private readonly Dictionary<SourceKind, INewsSource> _byKind = sources.ToDictionary(s => s.Kind);

    public INewsSource For(SourceKind kind) =>
        _byKind.TryGetValue(kind, out var source) ? source : throw new NotSupportedException($"No news source for {kind}");
}
