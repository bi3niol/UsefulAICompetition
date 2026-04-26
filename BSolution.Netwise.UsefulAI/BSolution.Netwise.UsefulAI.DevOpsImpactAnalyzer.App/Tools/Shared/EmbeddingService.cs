using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;
using System.ClientModel;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Embeduje wiele tekstów w jednym żądaniu do API (batch).
    /// Kolejność wyników odpowiada kolejności <paramref name="texts"/>.
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

        // Sub-batching: max EmbeddingBatchSize inputów na request, żeby nie spalić limitu TPM
        // w jednym strzale. Azure OpenAI limit to 2048 inputów/request, ale TPM-wise
        // bezpieczniej trzymać się ~16 chunków × 8k tokenów = ~128k tokenów/request.
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

    // ~16 chunków × max 8 000 tokenów ≈ 128 000 tokenów/request — bezpieczny margines dla TPM
    private const int EmbeddingBatchSize = 16;
    private const int MaxRetryAttempts = 5;
    private static readonly Random _jitter = Random.Shared;

    /// <summary>
    /// Wykonuje <paramref name="action"/> z retry na 429 (TooManyRequests).
    /// Respektuje Retry-After header; fallback: exponential backoff z jitterem (±20%) max 120s.
    /// Jitter zapobiega thundering herd gdy wiele instancji dostaje 429 jednocześnie.
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

                // ±20% jitter — rozbija thundering herd gdy wiele instancji dostaje 429 razem
                var jitterFactor = 0.8 + _jitter.NextDouble() * 0.4;
                var wait = TimeSpan.FromMilliseconds(baseWait.TotalMilliseconds * jitterFactor);

                await Task.Delay(wait, ct);

                // exponential backoff jako fallback gdy brak Retry-After
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