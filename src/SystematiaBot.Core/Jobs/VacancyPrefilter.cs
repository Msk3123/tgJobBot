using System.Globalization;
using System.Text.RegularExpressions;

namespace SystematiaBot.Core.Jobs;

public sealed record PrefilterResult(bool Pass, string? Reason);

/// <summary>
/// Cheap rules that run before any LLM call: a Senior Java role or "5 years required" does not need
/// a model to be rejected. Only clear-cut cases are filtered; everything ambiguous goes to scoring.
/// </summary>
public static partial class VacancyPrefilter
{
    public static PrefilterResult Check(string title, string? details, int? experienceMonths, int maxExperienceMonths)
    {
        var lower = title.ToLowerInvariant();
        var juniorFriendly = JuniorRegex().IsMatch(lower);

        if (!juniorFriendly && SeniorRegex().Match(lower) is { Success: true } senior)
            return new PrefilterResult(false, $"рівень {senior.Value.Trim()}");

        if (ForeignStackRegex().Match(lower) is { Success: true } stack && !DotNetRegex().IsMatch(lower))
            return new PrefilterResult(false, $"інший стек ({stack.Value.Trim()})");

        var months = experienceMonths ?? MonthsFromText(details);
        if (months > maxExperienceMonths)
            return new PrefilterResult(false, $"потрібно {Years(months.Value)} досвіду");

        return new PrefilterResult(true, null);
    }

    /// <summary>"2 роки досвіду", "1.5 years" → months; "Без досвіду" → 0.</summary>
    public static int? MonthsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (text.Contains("без досвіду", StringComparison.OrdinalIgnoreCase) || text.Contains("no experience", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (YearsRegex().Match(text) is not { Success: true } m)
            return null;

        var value = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return (int)Math.Round(value * 12);
    }

    private static string Years(int months)
    {
        var years = months / 12.0;
        return years % 1 == 0 ? $"{years:0}+ р." : $"{years:0.#}+ р.";
    }

    [GeneratedRegex(@"\b(junior|trainee|intern|internship|entry|стажер|джун|початків)", RegexOptions.CultureInvariant)]
    private static partial Regex JuniorRegex();

    [GeneratedRegex(@"\b(senior|lead|principal|staff|architect|head|director|cto|vp|сеньйор|лід)\b", RegexOptions.CultureInvariant)]
    private static partial Regex SeniorRegex();

    // Programming stacks that are not .NET. AI/ML and data roles are left to the model on purpose.
    [GeneratedRegex(@"(?<![\w#.+])(java(?!\s*script)|php|python|golang|ruby|node\.?js|ios|android|flutter|kotlin|swift|c\+\+|c language|embedded|aqa|qa|tester|devops|sre|1c|1с|salesforce|sap|odoo|unity|unreal|rust|scala|elixir)(?![\w+#])", RegexOptions.CultureInvariant)]
    private static partial Regex ForeignStackRegex();

    [GeneratedRegex(@"(\.net|c#|с#|asp\.?net|dotnet|blazor|creatio)", RegexOptions.CultureInvariant)]
    private static partial Regex DotNetRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d)?)\s*\+?\s*(?:рік|роки|років|year)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearsRegex();
}
