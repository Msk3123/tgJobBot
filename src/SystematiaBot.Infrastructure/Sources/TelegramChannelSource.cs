using System.Globalization;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Sources;

/// <summary>Public Telegram channels through the web preview https://t.me/s/{channel} (no API keys needed).</summary>
public sealed class TelegramChannelSource(IHttpClientFactory httpFactory, ILogger<TelegramChannelSource> logger) : INewsSource
{
    public const int MaxPages = 5;

    public SourceKind Kind => SourceKind.Telegram;

    public async Task<IReadOnlyList<FetchedNews>> FetchAsync(Source source, DateTimeOffset since, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(NewsHttp.ClientName);
        var baseUrl = PreviewUrl(source.Url);
        var result = new List<FetchedNews>();
        var seen = new HashSet<string>();
        string? before = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = before is null ? baseUrl : $"{baseUrl}?before={before}";
            var html = await http.GetStringAsync(url, ct);
            var parsed = Parse(html);
            if (parsed.Messages.Count == 0)
                break;

            result.AddRange(parsed.Messages.Where(m => seen.Add(m.Url)));
            if (parsed.OldestDate < since || parsed.OldestId is null)
                break;

            before = parsed.OldestId;
            logger.LogDebug("{Channel}: page {Page}, oldest {Date}", source.Name, page + 1, parsed.OldestDate);
        }

        return result;
    }

    public sealed record ParsedPage(List<FetchedNews> Messages, string? OldestId, DateTimeOffset OldestDate);

    /// <summary>Parses one t.me/s page. Media-only posts (no text) are skipped; forwarded posts keep their text.</summary>
    public static ParsedPage Parse(string html)
    {
        using var doc = new HtmlParser().ParseDocument(html);
        var messages = new List<FetchedNews>();
        string? oldestId = null;
        var oldestDate = DateTimeOffset.MaxValue;

        foreach (var message in doc.QuerySelectorAll(".tgme_widget_message[data-post]"))
        {
            var dateLink = message.QuerySelector("a.tgme_widget_message_date");
            var timeAttr = dateLink?.QuerySelector("time[datetime]")?.GetAttribute("datetime")
                           ?? message.QuerySelector("time[datetime]")?.GetAttribute("datetime");
            if (!DateTimeOffset.TryParse(timeAttr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                continue;

            var post = message.GetAttribute("data-post")!;
            var id = post[(post.LastIndexOf('/') + 1)..];
            if (date < oldestDate)
            {
                oldestDate = date;
                oldestId = id;
            }

            var textElement = message.QuerySelector(".tgme_widget_message_text");
            if (textElement is null)
                continue;

            var text = HtmlText.ToText(textElement);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var url = dateLink?.GetAttribute("href") ?? $"https://t.me/{post}";
            messages.Add(new FetchedNews(url, null, text, date.ToUniversalTime()));
        }

        return new ParsedPage(messages, oldestId, oldestDate);
    }

    /// <summary>Accepts "https://t.me/s/name", "https://t.me/name" or "@name".</summary>
    public static string PreviewUrl(string url)
    {
        var name = url.Trim().TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..].TrimStart('@');
        return $"https://t.me/s/{name}";
    }
}
