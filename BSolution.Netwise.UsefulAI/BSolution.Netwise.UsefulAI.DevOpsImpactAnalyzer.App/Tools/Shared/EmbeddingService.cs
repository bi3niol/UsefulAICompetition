using Azure;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}

public class EmbeddingService : IEmbeddingService
{
    // Azure.AI.OpenAI 2.x: EmbeddingClient (singular), deployment przekazywany w konstruktorze
    private readonly EmbeddingClient _client;

    public EmbeddingService(IConfiguration config)
    {
        var endpoint = config["AzureOpenAI:Endpoint"]!;
        var apiKey = config["AzureOpenAI:ApiKey"]!;
        var deployment = config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-large";

        // GetEmbeddingClient (singular) — deployment name jest argumentem, nie częścią żądania
        _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
            .GetEmbeddingClient(deployment);
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var normalizedText = NormalizeText(text);

        var response = await _client.GenerateEmbeddingAsync(normalizedText, cancellationToken: ct);

        // Azure.AI.OpenAI 2.x: ToFloats() zamiast .Data[0].Embedding
        return response.Value.ToFloats().ToArray();
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