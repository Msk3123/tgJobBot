using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Shouldly;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Infrastructure.Export;

namespace SystematiaBot.Tests.Bot;

public class DocxExporterTests
{
    [Fact]
    public async Task Docx_contains_posts_article_and_rejected_with_builtin_headings()
    {
        var export = new NewsExport(
            "Шептицький",
            new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero),
            [("Шептицька міська рада", "https://t.me/s/sheptytska_mr")],
            Enumerable.Range(1, 10).Select(i => new ExportPost($"Пост {i}", "Перший абзац.\n\nДругий абзац.", $"https://t.me/x/{i}")).ToList(),
            new ExportPost("Стаття про ялинку", "Абзац статті.", "https://zaxid.net/a"),
            [new ExportRejected("Стара новина", "https://t.me/x/99", "застаріла"), new ExportRejected("Львівська", "https://t.me/x/98", "не про місто"), new ExportRejected("Чутки", "https://t.me/x/97", "неперевірена інформація")]);
        var dir = Path.Combine(Path.GetTempPath(), "docx-" + Guid.NewGuid().ToString("N"));

        var path = await new DocxExporter().WriteAsync(export, dir, CancellationToken.None);

        Path.GetFileName(path).ShouldBe("Новини — Шептицький — 24.10.2026.docx");
        using (var doc = WordprocessingDocument.Open(path, false))
        {
            var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();
            string StyleOf(Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";

            paragraphs.Count(p => StyleOf(p) == "Heading2").ShouldBe(10 + 1 + 3);
            paragraphs.Count(p => StyleOf(p) == "Heading1").ShouldBe(3);
            paragraphs.ShouldContain(p => p.InnerText == "Причина: не про місто");
            paragraphs.ShouldContain(p => p.InnerText == "Джерело: https://t.me/x/10");
        }

        Directory.Delete(dir, recursive: true);
    }
}
