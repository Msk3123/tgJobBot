using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;
using SystematiaBot.Infrastructure.Cv;
using SystematiaBot.Infrastructure.Gmail;
using SystematiaBot.Infrastructure.Jobs;
using SystematiaBot.Infrastructure.Llm;
using SystematiaBot.Infrastructure.Persistence;
using SystematiaBot.Infrastructure.Persistence.Repositories;

namespace SystematiaBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddDbContext<BotDbContext>(o => o.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
        services.AddScoped<IVacancyRepository, VacancyRepository>();
        services.AddScoped<IMailEventRepository, MailEventRepository>();
        services.AddScoped<ILlmUsageRepository, LlmUsageRepository>();
        return services;
    }

    public static IServiceCollection AddJobSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobOptions>(configuration.GetSection(JobOptions.Section));
        services.Configure<GmailOptions>(configuration.GetSection(GmailOptions.Section));

        services.AddHttpClient(GmailMailbox.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IMailbox, GmailMailbox>();

        services.AddHttpClient(DjinniPageFetcher.HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(DjinniPageFetcher.UserAgent);
                c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9,en;q=0.8");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = false });
        services.AddSingleton<IVacancyPageFetcher, DjinniPageFetcher>();

        var jobs = configuration.GetSection(JobOptions.Section).Get<JobOptions>() ?? new JobOptions();
        if (jobs.DjinniFeeds.Count > 0)
            services.AddSingleton<IVacancyFeed, DjinniRssFeed>();

        if (jobs.DouEnabled && jobs.DouFeeds.Count > 0)
        {
            services.AddHttpClient(DouRssFeed.HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(DjinniPageFetcher.UserAgent);
            });
            services.AddSingleton<IVacancyFeed, DouRssFeed>();
        }

        services.AddSingleton<ICvRenderer, QuestPdfCvRenderer>();
        services.AddScoped<VacancyScorer>();
        services.AddScoped<DraftWriter>();
        services.AddScoped<CvTailor>();
        services.AddScoped<VacancyIntake>();
        services.AddScoped<MailSyncService>();
        services.AddScoped<FeedSyncService>();
        return services;
    }

    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration, string promptsDirectory)
    {
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.Section));
        services.AddSingleton<IPromptStore>(new FilePromptStore(promptsDirectory));

        // To switch to Claude: replace OpenAiLlmClient with ClaudeLlmClient in the line below.
        services.AddSingleton<LlmBudgetGuard>(sp => ActivatorUtilities.CreateInstance<LlmBudgetGuard>(sp, ActivatorUtilities.CreateInstance<OpenAiLlmClient>(sp)));
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<LlmBudgetGuard>());
        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
