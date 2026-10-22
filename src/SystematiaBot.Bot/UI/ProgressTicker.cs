namespace SystematiaBot.Bot.UI;

/// <summary>
/// Re-renders a progress message every 2 seconds from the latest state, so background work
/// can report progress synchronously without hitting Telegram rate limits.
/// </summary>
public sealed class ProgressTicker : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;

    private ProgressTicker(ProgressMessage message, Func<string?> render, CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
                    if (render() is { } text)
                        await message.UpdateAsync(text, _stop.Token, force: true);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public static ProgressTicker Start(ProgressMessage message, Func<string?> render, CancellationToken ct) => new(message, render, ct);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
    }
}
