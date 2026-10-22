namespace SystematiaBot.Infrastructure.Seo;

public static class UrlSampler
{
    /// <summary>
    /// Picks up to <paramref name="limit"/> URLs evenly across first path segments (/blog, /shop, …),
    /// so a big catalog section does not crowd out the rest of the site. The home page always goes first.
    /// </summary>
    public static List<string> Stratified(IReadOnlyList<string> urls, int limit)
    {
        if (urls.Count <= limit)
            return urls.ToList();

        var buckets = urls
            .GroupBy(FirstSegment)
            .OrderBy(g => g.Key == "" ? 0 : 1)
            .ThenByDescending(g => g.Count())
            .Select(g => new Queue<string>(g.Select((u, i) => (u, i)).OrderBy(x => Spread(x.i, g.Count())).Select(x => x.u)))
            .ToList();

        var result = new List<string>(limit);
        while (result.Count < limit && buckets.Count > 0)
        {
            foreach (var bucket in buckets.ToList())
            {
                if (result.Count >= limit)
                    break;
                if (bucket.TryDequeue(out var url))
                    result.Add(url);
                else
                    buckets.Remove(bucket);
            }
        }

        return result;
    }

    public static string FirstSegment(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "";

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 || uri.AbsolutePath.EndsWith('/') && segments.Length == 1 ? segments[0].ToLowerInvariant() : "";
    }

    /// <summary>Bit-reversal-like ordering so that a partial take from a bucket is spread across it, not just its head.</summary>
    private static double Spread(int index, int count)
    {
        double result = 0, f = 0.5;
        for (var i = index; i > 0; i >>= 1, f /= 2)
            result += (i & 1) * f;
        return count <= 1 ? 0 : result;
    }
}
