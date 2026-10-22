using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Infrastructure.Sources;

namespace SystematiaBot.Infrastructure.Llm;

/// <summary>
/// Decorator that records token usage of every call and refuses new calls once today's
/// (Europe/Kyiv) spend reaches OpenAI:DailyBudgetUsd. Prices per token come from config.
/// </summary>
public sealed class LlmBudgetGuard(
    OpenAiLlmClient inner,
    IServiceScopeFactory scopeFactory,
    IOptions<OpenAiOptions> options,
    IClock clock) : ILlmClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsConfigured => inner.IsConfigured;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var today = Today();
        var spent = await GetSpentAsync(today, ct);
        if (spent >= options.Value.DailyBudgetUsd)
            throw new LlmUnavailableException($"Денний бюджет OpenAI вичерпано: ${spent:0.000} з ${options.Value.DailyBudgetUsd:0.00}");

        var response = await inner.CompleteAsync(request, ct);

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILlmUsageRepository>().AddAsync(new LlmUsage
            {
                Day = today,
                At = clock.UtcNow,
                Operation = request.Operation,
                InputTokens = response.InputTokens,
                OutputTokens = response.OutputTokens,
                CostUsd = Cost(response.InputTokens, response.OutputTokens)
            }, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }

        return response;
    }

    public async Task<double> GetSpentAsync(DateOnly day, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILlmUsageRepository>().GetCostAsync(day, ct);
    }

    public DateOnly Today() => DateOnly.FromDateTime(KyivTime.ToKyiv(clock.UtcNow).DateTime);

    public double Cost(int inputTokens, int outputTokens) =>
        inputTokens * options.Value.InputPricePerMillionUsd / 1_000_000 + outputTokens * options.Value.OutputPricePerMillionUsd / 1_000_000;
}
