using System.Text.RegularExpressions;

namespace SystematiaBot.Infrastructure.Seo;

/// <summary>Minimal robots.txt model: Allow/Disallow rules for "User-agent: *" (longest match wins) and Sitemap lines.</summary>
public sealed class RobotsTxt
{
    private readonly List<(Regex Pattern, int Length, bool Allow)> _rules;

    private RobotsTxt(List<(Regex, int, bool)> rules, List<string> sitemaps)
    {
        _rules = rules;
        Sitemaps = sitemaps;
    }

    public static RobotsTxt Empty { get; } = new([], []);

    public IReadOnlyList<string> Sitemaps { get; }

    public static RobotsTxt Parse(string content)
    {
        var rules = new List<(Regex, int, bool)>();
        var sitemaps = new List<string>();
        var groupAgents = new List<string>();
        var inRules = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "user-agent":
                    if (inRules)
                    {
                        groupAgents.Clear();
                        inRules = false;
                    }

                    groupAgents.Add(value);
                    break;
                case "allow" or "disallow":
                    inRules = true;
                    if (value.Length > 0 && groupAgents.Contains("*"))
                        rules.Add((ToRegex(value), value.Length, key == "allow"));
                    break;
                case "sitemap":
                    if (value.Length > 0)
                        sitemaps.Add(value);
                    break;
            }
        }

        return new RobotsTxt(rules, sitemaps);
    }

    public bool IsAllowed(string pathAndQuery)
    {
        var best = _rules.Where(r => r.Pattern.IsMatch(pathAndQuery)).OrderByDescending(r => r.Length).ThenByDescending(r => r.Allow).FirstOrDefault();
        return best.Pattern is null || best.Allow;
    }

    private static Regex ToRegex(string rule)
    {
        var anchored = rule.EndsWith('$');
        var body = Regex.Escape(anchored ? rule[..^1] : rule).Replace(@"\*", ".*");
        return new Regex("^" + body + (anchored ? "$" : ""), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
