using System.Text.RegularExpressions;

namespace SystematiaBot.Core.Jobs;

/// <summary>
/// Cheap anti-hallucination check without an LLM: finds technologies a draft claims that the candidate profile never mentions.
/// A cover letter saying "I have experience with Kafka" when the profile has no Kafka is the fastest way to fail a screening call.
/// </summary>
public static class SkillGrounding
{
    // Each entry: display name and the spellings that count as a mention. Matching is case-insensitive with
    // custom word boundaries so "Java" does not match inside "JavaScript" and "C#" is not cut at "#".
    private static readonly (string Name, string[] Aliases)[] Terms =
    [
        ("Java", ["Java"]), ("Kotlin", ["Kotlin"]), ("Python", ["Python"]), ("Golang", ["Golang"]), ("PHP", ["PHP"]),
        ("Ruby", ["Ruby"]), ("Rust", ["Rust"]), ("C++", ["C++"]), ("JavaScript", ["JavaScript"]), ("TypeScript", ["TypeScript"]),
        ("React", ["React", "React.js", "ReactJS"]), ("Angular", ["Angular"]), ("Vue", ["Vue", "Vue.js"]), ("Svelte", ["Svelte"]),
        ("Node.js", ["Node.js", "NodeJS"]), ("Blazor", ["Blazor"]), ("WPF", ["WPF"]), ("WinForms", ["WinForms", "Windows Forms"]),
        ("MAUI", ["MAUI"]), ("Xamarin", ["Xamarin"]), ("Unity", ["Unity"]), ("Delphi", ["Delphi"]), ("Creatio", ["Creatio"]),
        ("MS SQL Server", ["SQL Server", "MS SQL", "MSSQL"]), ("PostgreSQL", ["PostgreSQL", "Postgres"]), ("MySQL", ["MySQL"]),
        ("MongoDB", ["MongoDB", "Mongo"]), ("Redis", ["Redis"]), ("Elasticsearch", ["Elasticsearch"]), ("Kafka", ["Kafka"]),
        ("RabbitMQ", ["RabbitMQ"]), ("Azure", ["Azure"]), ("AWS", ["AWS", "Amazon Web Services"]), ("GCP", ["GCP", "Google Cloud"]),
        ("Kubernetes", ["Kubernetes", "K8s"]), ("Docker", ["Docker"]), ("Terraform", ["Terraform"]), ("Jenkins", ["Jenkins"]),
        ("GitHub Actions", ["GitHub Actions"]), ("GraphQL", ["GraphQL"]), ("gRPC", ["gRPC"]), ("SignalR", ["SignalR"]),
        ("Microservices", ["microservices", "мікросервіс"]), ("Dapper", ["Dapper"]), ("NHibernate", ["NHibernate"]),
        ("Selenium", ["Selenium"]), ("Playwright", ["Playwright"]), ("NUnit", ["NUnit"]), ("xUnit", ["xUnit"])
    ];

    private static readonly Dictionary<string, Regex> Patterns = Terms
        .SelectMany(t => t.Aliases)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(a => a, a => new Regex($@"(?<![\w#+.]){Regex.Escape(a)}(?![\w#+])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), StringComparer.OrdinalIgnoreCase);

    /// <summary>Technologies mentioned in <paramref name="draft"/> but absent from <paramref name="profile"/>.</summary>
    public static IReadOnlyList<string> FindUngrounded(string draft, string profile) =>
        Terms
            .Where(t => t.Aliases.Any(a => Patterns[a].IsMatch(draft)) && !t.Aliases.Any(a => Patterns[a].IsMatch(profile)))
            .Select(t => t.Name)
            .ToList();
}
