namespace SystematiaBot.Core.Jobs;

public sealed class JobOptions
{
    public const string Section = "Jobs";

    /// <summary>Gmail search for the poller: Djinni sends alerts, messages and rejections; LinkedIn sends job alerts.</summary>
    public string MailQuery { get; set; } = "from:(magic@djinni.co OR jobalerts-noreply@linkedin.com OR jobs-noreply@linkedin.com) newer_than:3d";

    public int PollMinutes { get; set; } = 5;

    /// <summary>At most this many e-mails per sync, so a first run over a full inbox stays cheap.</summary>
    public int MaxMailsPerSync { get; set; } = 25;

    /// <summary>Vacancies scoring below this still get stored but no card is sent.</summary>
    public int MinFitToNotify { get; set; } = 40;

    /// <summary>RSS feeds of boards that publish vacancies without e-mail (DOU).</summary>
    public List<string> DouFeeds { get; set; } = [];

    /// <summary>
    /// DOU sits behind Cloudflare, which challenges datacenter IPs (DigitalOcean, Hetzner…) even for RSS.
    /// The bot does not try to get around that: on such a server set Jobs__DouEnabled=false.
    /// </summary>
    public bool DouEnabled { get; set; } = true;

    public int FeedPollMinutes { get; set; } = 30;

    /// <summary>The prefilter skips vacancies asking for more experience than this, without an LLM call.</summary>
    public int MaxExperienceMonths { get; set; } = 30;

    /// <summary>Remind about a follow-up when an application got no reply for this many days.</summary>
    public int FollowUpDays { get; set; } = 5;
}
