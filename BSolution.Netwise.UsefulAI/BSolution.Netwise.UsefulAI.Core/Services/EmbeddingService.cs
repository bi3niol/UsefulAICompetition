using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;
using System.ClientModel;

namespace BSolution.Netwise.UsefulAI.Core.Services;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Embeds multiple texts in a single API request (batch).
    /// The order of results matches the order of <paramref name="texts"/>.
    /// </summary>
    Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public EmbeddingService(IConfiguration config)
    {
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Missing config: AzureOpenAI:Endpoint");

        var deployment = config["AzureOpenAI:EmbeddingDeployment"]
            ?? "text-embedding-3-large";

        var apiKey = config["AzureOpenAI:ApiKey"];

        AzureOpenAIClient azureClient = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

        _client = azureClient.GetEmbeddingClient(deployment);
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var normalizedText = NormalizeText(text);
        return WithRetryAsync(async () =>
        {
            var response = await _client.GenerateEmbeddingAsync(normalizedText, cancellationToken: ct);
            return response.Value.ToFloats().ToArray();
        }, ct);
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var normalized = texts.Select(NormalizeText).ToList();
        var results = new float[normalized.Count][];

        // Sub-batching: max EmbeddingBatchSize inputs per request to avoid burning the TPM limit
        // in a single shot. Azure OpenAI limit is 2048 inputs/request, but TPM-wise
        // it is safer to stay at ~16 chunks × 8k tokens = ~128k tokens/request.
        for (var offset = 0; offset < normalized.Count; offset += EmbeddingBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = normalized.Skip(offset).Take(EmbeddingBatchSize).ToList();
            var embeddings = await WithRetryAsync(async () =>
            {
                var response = await _client.GenerateEmbeddingsAsync(batch, cancellationToken: ct);
                return response.Value;
            }, ct);

            for (var i = 0; i < embeddings.Count; i++)
                results[offset + i] = embeddings[i].ToFloats().ToArray();
        }

        return results;
    }

    // ~16 chunks × max 8 000 tokens ≈ 128 000 tokens/request — safe margin for TPM
    private const int EmbeddingBatchSize = 16;
    private const int MaxRetryAttempts = 5;
    private static readonly Random _jitter = Random.Shared;

    /// <summary>
    /// Executes <paramref name="action"/> with retry on 429 (TooManyRequests).
    /// Respects the Retry-After header; fallback: exponential backoff with ±20% jitter, max 120s.
    /// Jitter prevents thundering herd when multiple instances receive 429 simultaneously.
    /// </summary>
    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (ClientResultException ex) when (ex.Status == 429 && attempt < MaxRetryAttempts)
            {
                var retryAfter = ex.GetRawResponse()?.Headers
                    .TryGetValue("Retry-After", out var value) == true
                    && int.TryParse(value, out var seconds)
                        ? TimeSpan.FromSeconds(seconds)
                        : delay;

                var baseWait = retryAfter > TimeSpan.FromSeconds(120)
                    ? TimeSpan.FromSeconds(120)
                    : retryAfter;

                // ±20% jitter — breaks thundering herd when multiple instances receive 429 at once
                var jitterFactor = 0.8 + _jitter.NextDouble() * 0.4;
                var wait = TimeSpan.FromMilliseconds(baseWait.TotalMilliseconds * jitterFactor);

                await Task.Delay(wait, ct);

                // exponential backoff as fallback when Retry-After heather is missing
                delay = delay * 2 < TimeSpan.FromSeconds(120) ? delay * 2 : TimeSpan.FromSeconds(120);
            }
        }
    }

    private static string NormalizeText(string text)
    {
        text = string.Join(" ", text.Split([' ', '\n', '\r', '\t'],
            StringSplitOptions.RemoveEmptyEntries));

        return text.Length > 32000
            ? text[..32000]
            : text;
    }
}