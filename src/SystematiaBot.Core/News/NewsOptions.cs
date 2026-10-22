namespace SystematiaBot.Core.News;

public sealed class NewsOptions
{
    public const string Section = "News";

    public int MaxAgeDays { get; set; } = 7;
    public int MinScore { get; set; } = 3;
    public int TargetPicks { get; set; } = 10;
    public int SourceConcurrency { get; set; } = 4;

    /// <summary>Shorter posts are service messages (air raid alerts etc.), not news.</summary>
    public int MinWords { get; set; } = 12;
}
