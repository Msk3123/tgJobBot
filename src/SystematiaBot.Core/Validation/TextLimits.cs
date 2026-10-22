using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Validation;

public sealed record TextLimits(int TitleMin, int TitleMax, int WordsMin, int WordsMax, int? ParagraphsMin, int? ParagraphsMax)
{
    /// <summary>Max run of consecutive words shared with the source before the text counts as copied.</summary>
    public const int MaxCopiedRun = 12;

    public static TextLimits Social { get; } = new(1, 100, 100, 200, 2, 3);
    public static TextLimits Article { get; } = new(50, 85, 350, 600, null, null);

    public static TextLimits For(PostKind kind) => kind == PostKind.Article ? Article : Social;
}
