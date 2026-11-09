namespace SystematiaBot.Infrastructure.Time;

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
