namespace SystematiaBot.Bot.Hosting;

public sealed class BotOptions
{
    public const string Section = "Bot";

    public long OwnerId { get; set; }
    public string DefaultCity { get; set; } = "sheptytsky";
    public string DigestTime { get; set; } = "09:00";
    public string TimeZone { get; set; } = "Europe/Kyiv";
}
