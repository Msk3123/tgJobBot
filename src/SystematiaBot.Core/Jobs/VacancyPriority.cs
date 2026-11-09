using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

/// <summary>
/// Priority = fit × freshness. Djinni shows applicant counts only to logged-in users and the bot stays anonymous,
/// so freshness is the competition proxy: applications pile up over the first days, and early applicants get read first.
/// </summary>
public static class VacancyPriority
{
    public static double Freshness(TimeSpan age) => age.TotalDays switch
    {
        <= 1 => 1.0,
        <= 3 => 0.92,
        <= 7 => 0.82,
        <= 14 => 0.7,
        _ => 0.6
    };

    public static int? Score(Vacancy vacancy, DateTimeOffset now) =>
        vacancy.FitScore is { } fit ? (int)Math.Round(fit * Freshness(Age(vacancy, now))) : null;

    public static TimeSpan Age(Vacancy vacancy, DateTimeOffset now)
    {
        var age = now - (vacancy.PostedAt ?? vacancy.FoundAt);
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    public static string FormatAge(TimeSpan age) => age.TotalHours switch
    {
        < 1 => "щойно",
        < 24 => $"{(int)age.TotalHours} год тому",
        _ => $"{(int)age.TotalDays} дн тому"
    };
}
