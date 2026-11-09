namespace SystematiaBot.Core.Abstractions;

public sealed record VacancyPage(string? Title, string? Company, string? Description, bool IsInactive);

/// <summary>Loads the public vacancy page: the authoritative title, company and full description.</summary>
public interface IVacancyPageFetcher
{
    /// <summary>Null when the page could not be loaded; the vacancy then keeps what the e-mail said.</summary>
    Task<VacancyPage?> FetchAsync(string url, CancellationToken ct);
}
