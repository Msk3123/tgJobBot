using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

public enum ApplyOutcome
{
    Applied,
    AlreadyApplied,
    /// <summary>The vacancy is closed or the form is gone.</summary>
    NotAvailable,
    /// <summary>The stored login no longer works; the owner has to refresh it.</summary>
    SessionExpired,
    /// <summary>The board answered with a validation error (required field, etc.).</summary>
    Rejected,
    /// <summary>Network error or a page layout the client does not understand.</summary>
    Failed
}

public sealed record ApplyResult(ApplyOutcome Outcome, string? InboxUrl = null, string? Detail = null);

public sealed record ApplyAttachment(string FileName, byte[] Content);

/// <summary>
/// Submits an application on a job board on the owner's behalf. Called only from an explicit
/// "📨 Подати" tap on a draft the owner has seen; never in bulk, never on a schedule.
/// </summary>
public interface IJobApplicant
{
    /// <summary>The board this applicant works with (a <see cref="Vacancy.Source"/> value).</summary>
    string Source { get; }

    bool IsConfigured { get; }

    Task<ApplyResult> ApplyAsync(Vacancy vacancy, string message, ApplyAttachment? cv, CancellationToken ct);

    /// <summary>True when the stored login works, false when it expired, null when not configured or unreachable.</summary>
    Task<bool?> CheckSessionAsync(CancellationToken ct);
}
