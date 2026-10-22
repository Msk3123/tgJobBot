using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Infrastructure.Sources;

namespace SystematiaBot.Infrastructure.Export;

/// <summary>
/// The news package as .docx: header, posts, site article, rejected news.
/// Only built-in Title/Heading styles and the default font, so it pastes cleanly into Google Docs.
/// </summary>
public sealed class DocxExporter : IDocxExporter
{
    public Task<string> WriteAsync(NewsExport export, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var date = KyivTime.ToKyiv(export.Date);
        var path = Path.Combine(directory, $"Новини — {export.CityName} — {date:dd.MM.yyyy}.docx");

        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            AddStyles(main);
            var body = new Body();
            main.Document = new Document(body);

            body.Append(Para($"Новини — {export.CityName} — {date:dd.MM.yyyy}", "Title"));
            if (export.CollectedAt is { } collected)
                body.Append(Para($"Дата збору: {KyivTime.ToKyiv(collected):dd.MM.yyyy HH:mm}"));
            body.Append(Para("Джерела:"));
            foreach (var (name, url) in export.Sources)
                body.Append(Para($"• {name} — {url}"));

            body.Append(Para($"Пости для соцмереж ({export.Posts.Count})", "Heading1"));
            var n = 1;
            foreach (var post in export.Posts)
            {
                ct.ThrowIfCancellationRequested();
                body.Append(Para($"{n++}. {post.Title}", "Heading2"));
                AppendBody(body, post.Body);
                body.Append(Para($"Джерело: {post.SourceUrl}"));
            }

            body.Append(Para("Стаття для сайту", "Heading1"));
            if (export.Article is { } article)
            {
                body.Append(Para(article.Title, "Heading2"));
                AppendBody(body, article.Body);
                body.Append(Para($"Джерело: {article.SourceUrl}"));
            }
            else
            {
                body.Append(Para("Статтю ще не написано."));
            }

            body.Append(Para($"Знайдені, але не взяті ({export.Rejected.Count})", "Heading1"));
            foreach (var rejected in export.Rejected)
            {
                body.Append(Para(rejected.Title, "Heading2"));
                body.Append(Para($"Посилання: {rejected.Url}"));
                body.Append(Para($"Причина: {rejected.Reason}"));
            }

            main.Document.Save();
        }

        return Task.FromResult(path);
    }

    private static void AppendBody(Body body, string text)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            body.Append(Para(paragraph));
    }

    private static Paragraph Para(string text, string? style = null)
    {
        var paragraph = new Paragraph();
        if (style is not null)
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = style }));

        var run = new Run();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        paragraph.Append(run);
        return paragraph;
    }

    /// <summary>Built-in style ids/names that Word and Google Docs map to their own Title / Heading 1 / Heading 2.</summary>
    private static void AddStyles(MainDocumentPart main)
    {
        var styles = new Styles();
        styles.Append(BuiltIn("Normal", "Normal", null, isDefault: true));
        styles.Append(BuiltIn("Title", "Title", 0));
        styles.Append(BuiltIn("Heading1", "heading 1", 0));
        styles.Append(BuiltIn("Heading2", "heading 2", 1));
        main.AddNewPart<StyleDefinitionsPart>().Styles = styles;
    }

    private static Style BuiltIn(string id, string name, int? outlineLevel, bool isDefault = false)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id, Default = isDefault ? OnOffValue.FromBoolean(true) : null };
        style.Append(new StyleName { Val = name });
        if (!isDefault)
            style.Append(new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" });
        if (outlineLevel is { } level)
        {
            style.Append(new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "120" }, new OutlineLevel { Val = level }));
            style.Append(new StyleRunProperties(new Bold(), new FontSize { Val = id == "Title" ? "40" : level == 0 ? "32" : "26" }));
        }

        return style;
    }
}
