using SystematiaBot.Core.Entities;
using SystematiaBot.Infrastructure.Sources;

namespace SystematiaBot.Bot.UI;

/// <summary>Presentation helpers for news items shared by the news, post and export screens.</summary>
public static class NewsView
{
    public static string TitleOf(NewsItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Title))
            return item.Title.Trim();

        var firstLine = item.RawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var sentenceEnd = firstLine.IndexOfAny(['.', '!', '?']);
        var title = sentenceEnd is > 20 and < 140 ? firstLine[..(sentenceEnd + 1)] : firstLine;
        return MessageFormatter.Truncate(title, 120);
    }

    public static string Category(string? category) => category switch
    {
        "incident" => "🚨 інцидент",
        "utilities" => "🔧 комуналка",
        "infrastructure" => "🏗 інфраструктура",
        "social" => "🤝 соціальне",
        "culture" => "🎭 культура",
        "sport" => "⚽ спорт",
        "war" => "🪖 війна",
        "other" => "📌 інше",
        _ => ""
    };

    public static string Date(DateTimeOffset utc) => KyivTime.ToKyiv(utc).ToString("dd.MM, HH:mm");

    public static string RejectReasonText(RejectReason? reason, string? note) => reason switch
    {
        RejectReason.Old => "застаріла",
        RejectReason.NotLocal => "не про місто",
        RejectReason.Unverified => "неперевірена інформація",
        RejectReason.Duplicate => "дубль",
        RejectReason.Custom => note ?? "інша причина",
        _ => note ?? "—"
    };
}
