using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SystematiaBot.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BotDbContext>().UseSqlite("Data Source=data/bot.db").Options);
}
