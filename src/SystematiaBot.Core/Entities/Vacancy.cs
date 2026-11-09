namespace SystematiaBot.Core.Entities;

public class Vacancy
{
    public int Id { get; set; }

    /// <summary>Job board: "djinni", "dou" or "linkedin".</summary>
    public required string Source { get; set; }

    /// <summary>Id on the job board (djinni.co/jobs/{ExternalId}-slug).</summary>
    public required string ExternalId { get; set; }

    public required string Url { get; set; }
    public required string Title { get; set; }
    public string? Company { get; set; }
    public string? Salary { get; set; }

    /// <summary>"1 рік досвіду · B1 · Тільки віддалено · Україна".</summary>
    public string? Details { get; set; }

    public string? Description { get; set; }
    public bool IsInactive { get; set; }

    /// <summary>When the board published (or last bumped) the vacancy; drives the freshness part of the priority.</summary>
    public DateTimeOffset? PostedAt { get; set; }

    /// <summary>Required experience in months, when the board states it.</summary>
    public int? ExperienceMonths { get; set; }

    /// <summary>Normalized "company|title" used to spot the same vacancy on several boards.</summary>
    public string? DedupKey { get; set; }

    /// <summary>The same vacancy on other boards, one "source url" per line.</summary>
    public string? AlsoOn { get; set; }

    /// <summary>Why the rule-based prefilter skipped it without spending an LLM call.</summary>
    public string? FilterReason { get; set; }

    public VacancyStatus Status { get; set; }
    public int? FitScore { get; set; }

    /// <summary>apply | maybe | skip.</summary>
    public string? FitVerdict { get; set; }

    /// <summary>Why it fits, one reason per line.</summary>
    public string? FitReasons { get; set; }

    /// <summary>What is missing, one gap per line.</summary>
    public string? Gaps { get; set; }

    public string? Draft { get; set; }

    /// <summary>Technologies the draft mentions that the profile does not, one per line.</summary>
    public string? DraftWarnings { get; set; }

    public DateTimeOffset FoundAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>The owner already got a card about this vacancy.</summary>
    public bool Notified { get; set; }

    public DateTimeOffset? FollowUpRemindedAt { get; set; }
    public DateTimeOffset? FollowUpDoneAt { get; set; }
    public string? FollowUpDraft { get; set; }
}
