namespace SystematiaBot.Bot.Hosting;

/// <summary>
/// Resolves where read-only content (config, prompts, demo) and writable state (data, logs, exports) live.
/// Content ships next to the binaries; state goes to the repo root during development so it survives rebuilds.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string contentRoot, string stateRoot)
    {
        ContentRoot = contentRoot;
        StateRoot = stateRoot;
    }

    public string ContentRoot { get; }
    public string StateRoot { get; }

    public string Config => Path.Combine(ContentRoot, "config");
    public string Prompts => Path.Combine(ContentRoot, "prompts");
    public string Demo => Path.Combine(ContentRoot, "demo");
    public string Data => Ensure(Path.Combine(StateRoot, "data"));
    public string Logs => Ensure(Path.Combine(StateRoot, "logs"));
    public string Exports => Ensure(Path.Combine(StateRoot, "exports"));

    public static AppPaths Detect()
    {
        var contentRoot = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(contentRoot);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SystematiaBot.sln")))
            dir = dir.Parent;

        return new AppPaths(contentRoot, dir?.FullName ?? contentRoot);
    }

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
