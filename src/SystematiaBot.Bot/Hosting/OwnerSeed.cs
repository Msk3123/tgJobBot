using System.Text.Json;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Bot.Hosting;

/// <summary>
/// Server-side import of the owner's profile and CV: drop data/profile.txt and/or data/cv.json next to the database
/// (never into git) and restart. Each file overwrites the stored value and is renamed to *.imported, so it is
/// applied once. Runs before the first sync, so the very first vacancies are already scored against the profile.
/// </summary>
public static class OwnerSeed
{
    public const string ProfileFile = "profile.txt";
    public const string CvFile = "cv.json";

    public static async Task ImportAsync(IServiceProvider services, long ownerId, string dataDir, ILogger logger, CancellationToken ct = default)
    {
        var profilePath = Path.Combine(dataDir, ProfileFile);
        var cvPath = Path.Combine(dataDir, CvFile);
        if (ownerId == 0 || (!File.Exists(profilePath) && !File.Exists(cvPath)))
            return;

        var repository = services.GetRequiredService<IUserSettingsRepository>();
        var settings = await repository.GetOrCreateAsync(ownerId, ct);
        var changed = false;

        if (File.Exists(profilePath))
        {
            var profile = (await File.ReadAllTextAsync(profilePath, ct)).TrimStart('﻿').Trim();
            if (profile.Length > 0)
            {
                settings.Profile = profile;
                changed = true;
                logger.LogInformation("Owner profile imported from {File} ({Length} chars)", ProfileFile, profile.Length);
            }

            File.Move(profilePath, profilePath + ".imported", overwrite: true);
        }

        if (File.Exists(cvPath))
        {
            var json = (await File.ReadAllTextAsync(cvPath, ct)).TrimStart('﻿');
            if (CvData.TryParse(json, out var error) is { } cv)
            {
                settings.CvJson = JsonSerializer.Serialize(cv, CvData.Json);
                changed = true;
                File.Move(cvPath, cvPath + ".imported", overwrite: true);
                logger.LogInformation("Owner CV imported from {File}: {Projects} projects", CvFile, cv.Projects.Count);
            }
            else
            {
                // Left in place so the owner sees it was not consumed; the reason is in the log.
                logger.LogWarning("{File} ignored: {Error}", CvFile, error);
            }
        }

        if (changed)
            await repository.SaveAsync(settings, ct);
    }
}
