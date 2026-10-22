using System.Text.Json;
using Shouldly;
using SystematiaBot.Core.News;

namespace SystematiaBot.Tests.News;

public class DedupServiceTests
{
    private static readonly Dictionary<string, string> Texts =
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dedup_texts.json")))!;

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly DedupService _dedup = new();

    [Fact]
    public void Rewrites_of_one_event_by_two_sources_form_one_cluster()
    {
        // Real posts: the city council and a local channel wrote about the same award in their own words.
        var clusters = _dedup.Cluster([
            new(1, Texts["pavlyshyn_rada"], Now),
            new(2, Texts["pavlyshyn_echo"], Now.AddHours(1)),
            new(3, Texts["session_salaries"], Now),
            new(4, Texts["session_tractor"], Now),
            new(5, Texts["session_veterans"], Now)
        ]);

        clusters.ShouldHaveSingleItem().ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public void Paraphrased_short_texts_are_duplicates()
    {
        const string police = "На вулиці Шевченка в Шептицькому зіткнулися два легковики. Двоє дітей потрапили до лікарні, водія Renault затримала поліція. Рух на ділянці обмежили.";
        const string channel = "ДТП у Шептицькому: на Шевченка не розминулися два легковики, двоє дітей у лікарні. Поліція затримала водія Renault, рух на ділянці обмежений.";

        _dedup.AreDuplicates(police, channel).ShouldBeTrue();
    }

    [Fact]
    public void Different_posts_from_one_council_session_are_not_duplicates()
    {
        _dedup.AreDuplicates(Texts["session_salaries"], Texts["session_tractor"]).ShouldBeFalse();
        _dedup.AreDuplicates(Texts["session_veterans"], Texts["pavlyshyn_rada"]).ShouldBeFalse();
    }

    [Fact]
    public void Near_copies_are_duplicates_by_shingles()
    {
        var copy = Texts["session_tractor"] + "\n\nПідписуйтесь на наш канал 👉 https://t.me/example";

        _dedup.AreDuplicates(Texts["session_tractor"], copy).ShouldBeTrue();
    }

    [Fact]
    public void Same_text_far_apart_in_time_is_not_clustered()
    {
        _dedup.Cluster([new(1, Texts["session_tractor"], Now), new(2, Texts["session_tractor"], Now.AddDays(5))]).ShouldBeEmpty();
    }

    [Fact]
    public void Content_hash_ignores_case_punctuation_emoji_and_urls()
    {
        DedupService.ContentHash("Пожежа в Соснівці! 🔥 https://t.me/x/1").ShouldBe(DedupService.ContentHash("пожежа в соснівці"));
    }
}
