namespace SystematiaBot.Core.Abstractions;

public sealed record ExportPost(string Title, string Body, string SourceUrl);

public sealed record ExportRejected(string Title, string Url, string Reason);

public sealed record NewsExport(
    string CityName,
    DateTimeOffset Date,
    DateTimeOffset? CollectedAt,
    IReadOnlyList<(string Name, string Url)> Sources,
    IReadOnlyList<ExportPost> Posts,
    ExportPost? Article,
    IReadOnlyList<ExportRejected> Rejected);

public interface IDocxExporter
{
    /// <summary>Writes "Новини — {Місто} — {дата}.docx" and returns the file path.</summary>
    Task<string> WriteAsync(NewsExport export, string directory, CancellationToken ct);
}
