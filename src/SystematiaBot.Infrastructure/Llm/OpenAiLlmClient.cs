using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using SystematiaBot.Core.Abstractions;

namespace SystematiaBot.Infrastructure.Llm;

public sealed class OpenAiOptions
{
    public const string Section = "OpenAI";
    public const string Unset = "SET_ME";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = Unset;
    public int MaxOutputTokens { get; set; } = 1200;
    public double DailyBudgetUsd { get; set; } = 1.0;
    public double InputPricePerMillionUsd { get; set; }
    public double CachedInputPricePerMillionUsd { get; set; }
    public double OutputPricePerMillionUsd { get; set; }

    /// <summary>Optional cheaper model for high-volume calls (vacancy scoring).</summary>
    public string? CheapModel { get; set; }
    public double CheapInputPricePerMillionUsd { get; set; }
    public double CheapCachedInputPricePerMillionUsd { get; set; }
    public double CheapOutputPricePerMillionUsd { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model) && Model != Unset;
}

/// <summary>Official OpenAI SDK behind <see cref="ILlmClient"/>. Never logs request bodies or the key.</summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private readonly OpenAiOptions _options;
    private readonly ChatClient? _client;
    private readonly ChatClient? _cheapClient;
    private readonly ILogger<OpenAiLlmClient> _logger;

    public OpenAiLlmClient(IOptions<OpenAiOptions> options, ILogger<OpenAiLlmClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (!_options.IsConfigured)
            return;

        _client = new ChatClient(_options.Model, new ApiKeyCredential(_options.ApiKey!));
        if (!string.IsNullOrWhiteSpace(_options.CheapModel))
            _cheapClient = new ChatClient(_options.CheapModel, new ApiKeyCredential(_options.ApiKey!));
    }

    public bool IsConfigured => _client is not null;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (_client is null)
            throw new LlmUnavailableException("OpenAI не налаштовано: задай OpenAI:ApiKey і OpenAI:Model");

        var (client, model) = request.Cheap && _cheapClient is not null ? (_cheapClient, _options.CheapModel!) : (_client, _options.Model);
        var options = new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens ?? _options.MaxOutputTokens };
        if (request.Json)
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();

        // Static instructions and the profile first, the variable part last: the provider caches the identical prefix.
        ChatMessage[] messages = [new SystemChatMessage(request.System), new UserChatMessage(request.User)];
        try
        {
            ClientResult<ChatCompletion> result = await client.CompleteChatAsync(messages, options, ct);
            var completion = result.Value;
            var text = string.Concat(completion.Content.Select(c => c.Text));
            var usage = completion.Usage;
            var cached = usage?.InputTokenDetails?.CachedTokenCount ?? 0;
            var reasoning = usage?.OutputTokenDetails?.ReasoningTokenCount ?? 0;
            _logger.LogInformation("LLM {Operation} on {Model}: {In} in ({Cached} cached) / {Out} out ({Reasoning} reasoning), finish {Finish}",
                request.Operation, model, usage?.InputTokenCount, cached, usage?.OutputTokenCount, reasoning, completion.FinishReason);
            return new LlmResponse(text, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0, cached, reasoning, model);
        }
        catch (ClientResultException ex)
        {
            _logger.LogWarning("LLM {Operation} on {Model} failed with HTTP {Status}", request.Operation, model, ex.Status);
            throw new LlmUnavailableException($"OpenAI повернув помилку {ex.Status}");
        }
    }
}

/// <summary>
/// Placeholder for Anthropic Claude. To switch: implement CompleteAsync with the Anthropic SDK
/// and change one line in DependencyInjection.AddLlm.
/// </summary>
public sealed class ClaudeLlmClient : ILlmClient
{
    public bool IsConfigured => false;

    // TODO: call the Messages API (model from config), map usage.input_tokens / output_tokens.
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        throw new NotImplementedException("ClaudeLlmClient is a placeholder");
}
