using System.Collections.Concurrent;

namespace SystematiaBot.Bot.Jobs;

/// <summary>
/// Runs long operations (collecting, rewriting, crawling) off the update loop.
/// At most one job of a given type per chat; jobs are cancellable by the user and on shutdown.
/// </summary>
public sealed class BackgroundJobRunner(ErrorTracker errors, ILogger<BackgroundJobRunner> logger) : IHostedService
{
    private readonly ConcurrentDictionary<(long ChatId, string Type), (CancellationTokenSource Cts, Task Task)> _jobs = new();
    private readonly CancellationTokenSource _shutdown = new();

    public bool IsRunning(long chatId, string type) => _jobs.ContainsKey((chatId, type));

    public bool TryStart(long chatId, string type, Func<CancellationToken, Task> work)
    {
        var key = (chatId, type);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = Task.Run(async () =>
        {
            if (!await gate.Task)
            {
                cts.Dispose();
                return;
            }

            try
            {
                await work(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                logger.LogInformation("Job {Type} for chat {ChatId} cancelled", type, chatId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Type} for chat {ChatId} failed", type, chatId);
                errors.Record($"задача {type}", ex);
            }
            finally
            {
                _jobs.TryRemove(key, out _);
                cts.Dispose();
            }
        });

        var started = _jobs.TryAdd(key, (cts, task));
        gate.SetResult(started);
        return started;
    }

    /// <summary>Cancels every job of the chat and returns how many were running.</summary>
    public int Cancel(long chatId)
    {
        var count = 0;
        foreach (var (key, job) in _jobs)
        {
            if (key.ChatId != chatId)
                continue;
            try
            {
                job.Cts.Cancel();
                count++;
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return count;
    }

    public int RunningCount => _jobs.Count;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        var running = _jobs.Values.Select(j => j.Task).ToArray();
        if (running.Length == 0)
            return;

        logger.LogInformation("Waiting for {Count} background job(s) to stop", running.Length);
        await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ContinueWith(_ => { }, CancellationToken.None);
    }
}
