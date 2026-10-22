using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SystematiaBot.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystematiaBot.Core.News;
using SystematiaBot.Core.Seo;
using SystematiaBot.Core.Validation;
using SystematiaBot.Infrastructure.Export;
using SystematiaBot.Infrastructure.Persistence;
using SystematiaBot.Infrastructure.Persistence.Repositories;
using SystematiaBot.Infrastructure.Seo;
using SystematiaBot.Infrastructure.Sources;

namespace SystematiaBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSeo(this IServiceCollection services, IConfiguration configuration)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var seo = configuration.GetSection(SeoOptions.Section).Get<SeoOptions>() ?? new SeoOptions();
        services.Configure<SeoOptions>(configuration.GetSection(SeoOptions.Section));

        services.AddHttpClient(SeoCrawler.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(seo.TimeoutSec);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(seo.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Some shops set a "viewed products" cookie on every page; carrying it across the crawl
                // grows the Cookie header until the server answers 400 "Request Header Or Cookie Too Large".
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<SitemapReader>();
        services.AddSingleton<SeoIssueDetector>();
        services.AddSingleton<ISeoCrawler, SeoCrawler>();
        services.AddSingleton<ISeoReportExporter, CsvExporter>();
        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddDbContext<BotDbContext>(o => o.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
        services.AddScoped<ISeoRunRepository, SeoRunRepository>();
        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<INewsRepository, NewsRepository>();
        return services;
    }

    public static IServiceCollection AddNews(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NewsOptions>(configuration.GetSection(NewsOptions.Section));
        services.AddHttpClient(NewsHttp.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(NewsHttp.UserAgent);
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9");
        });

        services.AddSingleton<INewsSource, TelegramChannelSource>();
        services.AddSingleton<INewsSource, RssSource>();
        services.AddSingleton<INewsSource, HtmlListSource>();
        services.AddSingleton<INewsSourceFactory, NewsSourceFactory>();
        services.AddSingleton<DedupService>();
        services.AddScoped<NewsCollectionService>();
        services.TryAddScoped<INewsScorer, NullNewsScorer>();
        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<FactChecker>();
        services.AddSingleton<TextValidator>();
        return services;
    }
}
