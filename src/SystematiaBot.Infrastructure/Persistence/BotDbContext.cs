using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();
    public DbSet<MailEvent> MailEvents => Set<MailEvent>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<LlmUsage> LlmUsages => Set<LlmUsage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite cannot compare or sort DateTimeOffset natively; all values are stored in UTC as sortable longs.
        builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        builder.Properties<VacancyStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<MailKind>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Vacancy>(e =>
        {
            e.HasIndex(v => new { v.Source, v.ExternalId }).IsUnique();
            e.HasIndex(v => v.Status);
            e.HasIndex(v => v.DedupKey);
            e.Property(v => v.Source).HasMaxLength(32);
            e.Property(v => v.ExternalId).HasMaxLength(64);
        });

        model.Entity<MailEvent>(e =>
        {
            e.HasIndex(m => m.GmailId).IsUnique();
            e.HasIndex(m => m.Kind);
            e.Property(m => m.GmailId).HasMaxLength(64);
            e.HasOne(m => m.Vacancy).WithMany().HasForeignKey(m => m.VacancyId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<UserSettings>(e =>
        {
            e.HasKey(u => u.ChatId);
            e.Property(u => u.ChatId).ValueGeneratedNever();
        });

        model.Entity<LlmUsage>(e => e.HasIndex(u => u.Day));
    }
}
