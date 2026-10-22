using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<SeoRun> SeoRuns => Set<SeoRun>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<LlmUsage> LlmUsages => Set<LlmUsage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite cannot compare or sort DateTimeOffset natively; all values are stored in UTC as sortable longs.
        builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        builder.Properties<SourceKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<NewsStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<RejectReason>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<PostKind>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<SeoMode>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Source>(e =>
        {
            e.HasIndex(s => new { s.City, s.Url }).IsUnique();
            e.Property(s => s.City).HasMaxLength(64);
        });

        model.Entity<NewsItem>(e =>
        {
            e.HasIndex(n => new { n.SourceId, n.Url }).IsUnique();
            e.HasIndex(n => new { n.City, n.Status });
            e.HasIndex(n => n.ContentHash);
            e.HasOne(n => n.Source).WithMany().HasForeignKey(n => n.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Post>(e =>
        {
            e.HasIndex(p => new { p.NewsItemId, p.Kind });
            e.HasOne(p => p.NewsItem).WithMany().HasForeignKey(p => p.NewsItemId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SeoRun>(e => e.HasIndex(r => r.ChatId));
        model.Entity<UserSettings>(e =>
        {
            e.HasKey(u => u.ChatId);
            e.Property(u => u.ChatId).ValueGeneratedNever();
        });
        model.Entity<LlmUsage>(e => e.HasIndex(u => u.Day));
    }
}
