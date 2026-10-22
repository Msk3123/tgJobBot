using System.Reflection;

namespace SystematiaBot.Bot.Hosting;

public static class BuildInfo
{
    /// <summary>Informational version, e.g. "1.0.0+3f2a9c1" (the SDK appends the git commit hash at build time).</summary>
    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    public static string ShortVersion
    {
        get
        {
            var plus = Version.IndexOf('+');
            return plus < 0 ? Version : $"{Version[..plus]}+{Version[(plus + 1)..].PadRight(7)[..7].Trim()}";
        }
    }

    public static DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}
