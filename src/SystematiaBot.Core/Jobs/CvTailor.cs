using System.Text;
using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Jobs;

public sealed record CvPlan(string? Headline, string? Summary, List<CvProjectPick>? Projects, List<string>? Skills);

public sealed record CvProjectPick(string? Id, List<string>? Bullets);

public sealed record TailoredCv(CvData Cv, IReadOnlyList<string> UngroundedSkills, bool UsedModel);

/// <summary>
/// Tailors the CV to one vacancy. The model only picks and orders project/bullet ids and skill groups,
/// and rewrites the headline and summary; <see cref="Apply"/> validates every id against the base CV.
/// </summary>
public sealed class CvTailor(ILlmClient llm, IPromptStore prompts, ILogger<CvTailor> logger)
{
    private const int MaxProjects = 4;
    private const int MaxBulletsPerProject = 4;
    private const int MaxHeadline = 90;
    private const int MaxSummary = 650;

    public async Task<TailoredCv> TailorAsync(CvData baseCv, string? profile, Vacancy vacancy, CancellationToken ct)
    {
        var system = prompts.Render("cv_tailor", new Dictionary<string, string> { ["cv"] = Catalog(baseCv) });
        CvPlan? plan = null;
        try
        {
            var response = await llm.CompleteAsync(new LlmRequest("cv_tailor", system, VacancyPrompt.Render(vacancy), MaxOutputTokens: 3000), ct);
            plan = LlmJson.TryParse<CvPlan>(response.Text);
            if (plan is null)
                logger.LogWarning("cv_tailor returned invalid JSON for vacancy {Id}", vacancy.Id);
        }
        catch (LlmUnavailableException ex)
        {
            logger.LogWarning("cv_tailor unavailable: {Reason}", ex.Message);
        }

        var cv = Apply(baseCv, plan);

        // Headline and summary are the only free text; anything not grounded in the CV or profile falls back to the base text.
        var reference = baseCv.ToPlainText() + "\n" + profile;
        var ungrounded = SkillGrounding.FindUngrounded($"{cv.Headline}\n{cv.Summary}", reference);
        if (ungrounded.Count > 0)
        {
            cv.Headline = baseCv.Headline;
            cv.Summary = baseCv.Summary;
        }

        return new TailoredCv(cv, ungrounded, plan is not null);
    }

    /// <summary>Applies a plan to a copy of the base CV, ignoring unknown ids and keeping sane minimums.</summary>
    public static CvData Apply(CvData baseCv, CvPlan? plan)
    {
        var cv = baseCv.Clone();
        if (plan is null)
            return cv;

        if (!string.IsNullOrWhiteSpace(plan.Headline) && plan.Headline.Trim().Length <= MaxHeadline)
            cv.Headline = plan.Headline.Trim();
        if (!string.IsNullOrWhiteSpace(plan.Summary) && plan.Summary.Trim().Length <= MaxSummary)
            cv.Summary = plan.Summary.Trim();

        var byId = cv.Projects.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var picked = new List<CvSection>();
        foreach (var pick in plan.Projects ?? [])
        {
            if (pick.Id is null || !byId.Remove(pick.Id, out var project))
                continue;

            var bullets = project.Bullets.GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var chosen = (pick.Bullets ?? [])
                .Select(id => bullets.GetValueOrDefault(id))
                .OfType<CvBullet>()
                .Distinct()
                .Take(MaxBulletsPerProject)
                .ToList();
            if (chosen.Count < 2)
                chosen = project.Bullets.Take(Math.Min(3, MaxBulletsPerProject)).ToList();

            project.Bullets = chosen;
            picked.Add(project);
            if (picked.Count == MaxProjects)
                break;
        }

        if (picked.Count >= 2)
            cv.Projects = picked;

        if (plan.Skills is { Count: > 0 } order)
        {
            var rank = order.Select((id, i) => (id, i)).GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
            cv.Skills = cv.Skills.OrderBy(g => rank.GetValueOrDefault(g.Id, int.MaxValue)).ToList();
        }

        return cv;
    }

    /// <summary>The CV as the model sees it: ids next to every choosable piece.</summary>
    private static string Catalog(CvData cv)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Headline: {cv.Headline}");
        sb.AppendLine($"Summary: {cv.Summary}");
        sb.AppendLine("Experience (fixed, for context):");
        foreach (var e in cv.Experience)
            sb.AppendLine($"- {e.Title} ({e.Period}): {string.Join(" ", e.Bullets.Select(b => b.Text))}");

        sb.AppendLine("Projects:");
        foreach (var p in cv.Projects)
        {
            sb.AppendLine($"[{p.Id}] {p.Title} — tech: {p.Tech}");
            foreach (var b in p.Bullets)
                sb.AppendLine($"   ({b.Id}) {b.Text}");
        }

        sb.AppendLine("Skill groups:");
        foreach (var g in cv.Skills)
            sb.AppendLine($"[{g.Id}] {g.Label}: {g.Items}");
        sb.AppendLine($"Other: {cv.Other}");
        sb.AppendLine($"Leadership: {cv.Leadership}");
        return sb.ToString();
    }
}
