namespace SystematiaBot.Core.Jobs;

public sealed class JobOptions
{
    public const string Section = "Jobs";

    /// <summary>Gmail search for the poller. Djinni sends every alert, message and rejection from this address.</summary>
    public string MailQuery { get; set; } = "from:magic@djinni.co newer_than:3d";

    public int PollMinutes { get; set; } = 5;

    /// <summary>At most this many e-mails per sync, so a first run over a full inbox stays cheap.</summary>
    public int MaxMailsPerSync { get; set; } = 25;

    /// <summary>Vacancies scoring below this still get stored but no card is sent.</summary>
    public int MinFitToNotify { get; set; } = 40;
}
