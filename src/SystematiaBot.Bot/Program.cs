using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SystematiaBot.Bot;
using SystematiaBot.Bot.Handlers;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.News;
using SystematiaBot.Infrastructure;
using SystematiaBot.Infrastructure.Persistence;
using SystematiaBot.Infrastructure.Sources;
using Telegram.Bot;

Console.OutputEncoding = Encoding.UTF8;

var paths = AppPaths.Detect();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.Sources.Clear();
    builder.Configuration
        .AddJsonFile(Path.Combine(paths.Config, "appsettings.json"), optional: false, reloadOnChange: false)
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    builder.Services.AddSerilog((_, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(Path.Combine(paths.Logs, "bot-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

    var token = builder.Configuration["Telegram:BotToken"];
    if (string.IsNullOrWhiteSpace(token))
    {
        Log.Fatal("Telegram:BotToken is not set. Run: dotnet user-secrets set \"Telegram:BotToken\" \"<token>\" --project src/SystematiaBot.Bot");
        return 1;
    }

    builder.Services.AddSingleton(paths);
    builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.Section));
    builder.Services.AddHttpClient("telegram").ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(100));
    builder.Services.AddSingleton<ITelegramBotClient>(sp =>
        new TelegramBotClient(token, sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram")));

    builder.Services
        .AddCoreServices()
        .AddPersistence(Path.Combine(paths.Data, "bot.db"))
        .AddSeo(builder.Configuration);
    builder.Services.AddSingleton<ICityCatalog>(new JsonCityCatalog(Path.Combine(paths.Config, "sources.json")));

    builder.Services.AddSingleton<BotUi>();
    builder.Services.AddSingleton<UserStateStore>();
    builder.Services.AddSingleton<BackgroundJobRunner>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobRunner>());

    builder.Services.AddScoped<UpdateRouter>();
    builder.Services.AddScoped<MenuHandler>();
    builder.Services.AddScoped<SeoHandler>();
    builder.Services.AddScoped<ICallbackHandler>(sp => sp.GetRequiredService<MenuHandler>());
    builder.Services.AddScoped<ICallbackHandler>(sp => sp.GetRequiredService<SeoHandler>());
    builder.Services.AddScoped<ITextInputHandler>(sp => sp.GetRequiredService<SeoHandler>());

    builder.Services.AddHostedService<BotWorker>();

    var host = builder.Build();

    var botOptions = builder.Configuration.GetSection(BotOptions.Section).Get<BotOptions>() ?? new BotOptions();
    if (botOptions.OwnerId == 0)
        Log.Warning("Bot:OwnerId is not set: everyone gets demo mode. Send /start and look for the user id in the log");

    await using (var scope = host.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.MigrateAsync();

    Log.Information("Systematia Assistant starting, version {Version}", BuildInfo.Version);
    await host.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
