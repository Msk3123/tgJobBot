using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Infrastructure.Cv;

/// <summary>
/// One-page A4 CV with QuestPDF. The Inter font is embedded in the assembly, so the PDF looks the same on Windows
/// and in the Linux container and Cyrillic never falls back to a missing system font.
/// </summary>
public sealed class QuestPdfCvRenderer : ICvRenderer
{
    private const string Font = "Inter";
    private const string Accent = "#0B5CAD";
    private const string Muted = "#57606A";
    private const string Ink = "#1F2328";
    private const string Rule = "#D0D7DE";

    private static readonly Lazy<bool> Setup = new(Initialize);

    public byte[] Render(CvData cv)
    {
        _ = Setup.Value;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(34);
            page.MarginVertical(28);
            page.DefaultTextStyle(t => t.FontFamily(Font).FontSize(8.8f).FontColor(Ink).LineHeight(1.3f));
            page.Content().Column(column => Compose(column, cv));
        })).GeneratePdf();
    }

    private static void Compose(ColumnDescriptor col, CvData cv)
    {
        col.Item().Text(cv.Name).FontSize(19).Bold();
        if (!string.IsNullOrWhiteSpace(cv.Headline))
            col.Item().PaddingTop(1).Text(cv.Headline).FontSize(10.5f).SemiBold().FontColor(Accent);

        if (cv.Contacts.Count > 0)
        {
            col.Item().PaddingTop(4).Text(text =>
            {
                text.DefaultTextStyle(s => s.FontSize(8.4f).FontColor(Muted));
                for (var i = 0; i < cv.Contacts.Count; i++)
                {
                    if (i > 0)
                        text.Span("  ·  ");
                    Link(text, cv.Contacts[i]);
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(cv.Summary))
        {
            Section(col, "Summary");
            col.Item().Text(cv.Summary);
        }

        if (cv.Experience.Count > 0)
        {
            Section(col, "Experience");
            foreach (var entry in cv.Experience)
                Entry(col, entry);
        }

        Section(col, "Projects");
        foreach (var project in cv.Projects)
            Entry(col, project);
        if (!string.IsNullOrWhiteSpace(cv.Other))
        {
            col.Item().PaddingTop(3).Text(t =>
            {
                t.Span("Other: ").SemiBold();
                t.Span(cv.Other);
            });
        }

        if (cv.Skills.Count > 0)
        {
            Section(col, "Technical skills");
            foreach (var group in cv.Skills)
            {
                col.Item().Text(t =>
                {
                    t.Span(group.Label + ": ").SemiBold();
                    t.Span(group.Items);
                });
            }
        }

        if (cv.Education.Count > 0 || cv.Languages is not null || cv.Leadership is not null)
        {
            Section(col, "Education, languages & leadership");
            foreach (var line in cv.Education)
                col.Item().Text(line);
            if (!string.IsNullOrWhiteSpace(cv.Languages))
                col.Item().Text(cv.Languages);
            if (!string.IsNullOrWhiteSpace(cv.Leadership))
                col.Item().Text(cv.Leadership);
        }
    }

    private static void Section(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(8).PaddingBottom(2).BorderBottom(0.6f).BorderColor(Rule).PaddingBottom(1.5f)
            .Text(title.ToUpperInvariant()).FontSize(8.6f).Bold().FontColor(Accent).LetterSpacing(0.06f);

    private static void Entry(ColumnDescriptor col, CvSection section)
    {
        col.Item().PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span(section.Title).SemiBold();
                foreach (var link in section.Links)
                {
                    t.Span("  ·  ").FontColor(Muted);
                    Link(t, link);
                }
            });
            if (!string.IsNullOrWhiteSpace(section.Period))
                row.AutoItem().PaddingLeft(8).Text(section.Period).FontColor(Muted);
        });

        foreach (var bullet in section.Bullets)
        {
            col.Item().PaddingLeft(2).Row(row =>
            {
                row.ConstantItem(9).Text("•").FontColor(Muted);
                row.RelativeItem().Text(bullet.Text);
            });
        }

        if (!string.IsNullOrWhiteSpace(section.Tech))
        {
            col.Item().PaddingLeft(11).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8.2f).FontColor(Muted));
                t.Span("Tech: ").SemiBold();
                t.Span(section.Tech);
            });
        }
    }

    private static void Link(TextDescriptor text, CvLink link)
    {
        if (string.IsNullOrWhiteSpace(link.Url))
            text.Span(link.Label);
        else
            text.Hyperlink(link.Label, link.Url).FontColor(Accent);
    }

    private static bool Initialize()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var assembly = typeof(QuestPdfCvRenderer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }
}
