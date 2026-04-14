using Azure.AI.Projects;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public EmbeddingService(AIProjectClient projectClient, IConfiguration config)
    {
        var deployment = config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-large";
        _client = projectClient.GetProjectOpenAIClient().GetEmbeddingClient(deployment);
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var normalizedText = NormalizeText(text);

        var response = await _client.GenerateEmbeddingAsync(normalizedText, cancellationToken: ct);

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