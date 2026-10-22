using System.Reflection;
using System.Text;
using Serilog;
using SystematiaBot.Bot.Hosting;

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

    builder.Services.AddSingleton(paths);

    var host = builder.Build();
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
