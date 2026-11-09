namespace SystematiaBot.Bot.Hosting;

public sealed class BotOptions
{
    public const string Section = "Bot";

    /// <summary>Telegram user id of the only person allowed to use the bot. 0 = nobody (the id is logged on /start).</summary>
    public long OwnerId { get; set; }

    public string DigestTime { get; set; } = "09:00";
    public string TimeZone { get; set; } = "Europe/Kyiv";
}
