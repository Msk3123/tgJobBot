using System.Globalization;
using System.Text;

namespace SystematiaBot.Bot.Routing;

/// <summary>
/// Compact callback payload "{module}:{action}[:{id}[:{arg}]]", e.g. "n:pk:123", "n:rj:123:old", "s:mode:q".
/// Telegram limits callback_data to 64 bytes; all parsing and serialization lives here.
/// </summary>
public sealed record CallbackData(string Module, string Action, int? Id = null, string? Arg = null)
{
    public const int MaxBytes = 64;

    public override string ToString()
    {
        var sb = new StringBuilder().Append(Module).Append(':').Append(Action);
        if (Id is not null || Arg is not null)
            sb.Append(':').Append(Id?.ToString(CultureInfo.InvariantCulture) ?? "");
        if (Arg is not null)
            sb.Append(':').Append(Arg);

        var value = sb.ToString();
        if (Encoding.UTF8.GetByteCount(value) > MaxBytes)
            throw new InvalidOperationException($"Callback data exceeds {MaxBytes} bytes: {value}");
        return value;
    }

    public static bool TryParse(string? value, out CallbackData data)
    {
        data = null!;
        if (string.IsNullOrEmpty(value))
            return false;

        var parts = value.Split(':', 4);
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return false;

        int? id = null;
        if (parts.Length > 2 && parts[2].Length > 0)
        {
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return false;
            id = parsed;
        }

        data = new CallbackData(parts[0], parts[1], id, parts.Length > 3 ? parts[3] : null);
        return true;
    }

    public static implicit operator string(CallbackData data) => data.ToString();
}

/// <summary>Callback modules and actions used across handlers.</summary>
public static class Cb
{
    public static CallbackData MainMenu => new("m", "main");
    public static CallbackData Status => new("m", "status");
    public static CallbackData Sync => new("m", "sync");
    public static CallbackData DigestToggle => new("m", "digest");
    public static CallbackData CancelJob => new("j", "cancel");

    /// <summary>Vacancy lists: "new" (to review), "act" (in progress), "arc" (rejected / skipped).</summary>
    public static CallbackData VacancyList(string list, int page = 0) => new("v", "list", page, list);
    public static CallbackData Vacancy(int id) => new("v", "show", id);
    public static CallbackData Draft(int id) => new("v", "draft", id);
    public static CallbackData Redraft(int id) => new("v", "redraft", id);
    public static CallbackData Rescore(int id) => new("v", "score", id);
    public static CallbackData SetStatus(int id, string status) => new("v", "st", id, status);
    public static CallbackData Cv(int id) => new("v", "cv", id);
    public static CallbackData FollowUp(int id) => new("v", "fu", id);
    public static CallbackData FollowUpAgain(int id) => new("v", "fu2", id);
    public static CallbackData FollowUpDone(int id) => new("v", "fud", id);
    public static CallbackData FollowUpMute(int id) => new("v", "fun", id);

    public static CallbackData CvMenu => new("cv", "show");
    public static CallbackData CvBase => new("cv", "pdf");
    public static CallbackData CvTemplate => new("cv", "tpl");

    public static CallbackData Conversations(int page = 0) => new("c", "list", page);
    public static CallbackData Conversation(int id) => new("c", "show", id);
    public static CallbackData ReplyDraft(int id) => new("c", "reply", id);
    public static CallbackData Rereply(int id) => new("c", "rereply", id);

    public static CallbackData Profile => new("p", "show");
    public static CallbackData ProfileEdit => new("p", "edit");
}
