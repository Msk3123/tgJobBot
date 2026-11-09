using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystematiaBot.Core.Jobs;

/// <summary>
/// Structured CV. Every bullet has an id, so tailoring is selection and ordering of existing facts:
/// the model returns ids, never new bullet text, and cannot invent experience in the PDF.
/// </summary>
public sealed class CvData
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Name { get; set; } = "";
    public string? Headline { get; set; }
    public List<CvLink> Contacts { get; set; } = [];
    public string? Summary { get; set; }
    public List<CvSection> Experience { get; set; } = [];
    public List<CvSection> Projects { get; set; } = [];
    public string? Other { get; set; }
    public List<CvSkillGroup> Skills { get; set; } = [];
    public List<string> Education { get; set; } = [];
    public string? Languages { get; set; }
    public string? Leadership { get; set; }

    public static CvData? TryParse(string json, out string? error)
    {
        CvData? cv;
        try
        {
            cv = JsonSerializer.Deserialize<CvData>(json, Json);
        }
        catch (JsonException ex)
        {
            error = $"JSON не читається: {ex.Message}";
            return null;
        }

        error = cv switch
        {
            null => "порожній файл",
            { Name.Length: 0 } => "немає поля name",
            { Projects.Count: 0 } => "немає жодного проєкту в projects",
            _ when cv.Projects.Concat(cv.Experience).SelectMany(s => s.Bullets).GroupBy(b => b.Id).FirstOrDefault(g => g.Count() > 1) is { } dup
                => $"id пунктів мають бути унікальні, а «{dup.Key}» повторюється",
            _ => null
        };
        return error is null ? cv : null;
    }

    public CvData Clone() => JsonSerializer.Deserialize<CvData>(JsonSerializer.Serialize(this, Json), Json)!;

    /// <summary>All facts as plain text: the reference for grounding checks.</summary>
    public string ToPlainText()
    {
        var sb = new StringBuilder().AppendLine(Name).AppendLine(Headline).AppendLine(Summary);
        foreach (var section in Experience.Concat(Projects))
        {
            sb.AppendLine(section.Title).AppendLine(section.Tech);
            foreach (var bullet in section.Bullets)
                sb.AppendLine(bullet.Text);
        }

        foreach (var group in Skills)
            sb.AppendLine(group.Items);
        return sb.AppendLine(Other).AppendLine(Leadership).ToString();
    }
}

public sealed class CvLink
{
    public string Label { get; set; } = "";
    public string? Url { get; set; }
}

public sealed class CvSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Period { get; set; }
    public List<CvLink> Links { get; set; } = [];
    public List<CvBullet> Bullets { get; set; } = [];
    public string? Tech { get; set; }
}

public sealed class CvBullet
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class CvSkillGroup
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Items { get; set; } = "";
}
