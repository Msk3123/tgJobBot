namespace SystematiaBot.Core.Validation;

public enum IssueLevel
{
    Warning,
    Error
}

public enum FactKind
{
    Number,
    Date,
    Quote,
    Name
}

public sealed record ValidationIssue(string Code, IssueLevel Level, string Message);

public sealed record UnsupportedFact(FactKind Kind, string Text, IssueLevel Level);

/// <summary>Deterministic result of checking a generated text against limits and the source.</summary>
public sealed class ValidationReport
{
    public int TitleLength { get; init; }
    public int TitleMin { get; init; }
    public int TitleMax { get; init; }
    public int WordCount { get; init; }
    public int WordsMin { get; init; }
    public int WordsMax { get; init; }
    public int ParagraphCount { get; init; }
    public int? ParagraphsMin { get; init; }
    public int? ParagraphsMax { get; init; }
    public bool HasSourceLink { get; init; }
    public string? CopiedFragment { get; init; }
    public List<ValidationIssue> Issues { get; init; } = [];
    public List<UnsupportedFact> UnsupportedFacts { get; init; } = [];

    public bool HasErrors => Issues.Any(i => i.Level == IssueLevel.Error);

    /// <summary>Error messages to feed back into the rewrite prompt.</summary>
    public IReadOnlyList<string> FixList => Issues.Where(i => i.Level == IssueLevel.Error).Select(i => i.Message).ToList();
}
