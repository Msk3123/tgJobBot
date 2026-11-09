namespace SystematiaBot.Core.Entities;

/// <summary>Lifecycle of one vacancy: New → Drafted → Applied → Replied → Interview → Offer, or Rejected / Skipped.</summary>
public enum VacancyStatus
{
    New,
    Drafted,
    Applied,
    Replied,
    Interview,
    Offer,
    Rejected,
    Skipped
}

public enum MailKind
{
    /// <summary>"Нові вакансії за вашим профілем".</summary>
    VacancyAlert,

    /// <summary>A recruiter wrote in the Djinni inbox.</summary>
    RecruiterMessage,

    /// <summary>A recruiter message that reads as a rejection, or Djinni's "ви не підійшли" notice.</summary>
    Rejection,

    /// <summary>Automatic "thanks for applying" replies.</summary>
    AutoReply,

    /// <summary>Weekly stats, newsletters and everything else.</summary>
    Other
}
