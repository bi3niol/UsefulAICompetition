using BSolution.Netwise.UsefulAI.Core.Configuration;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Sender;
using Microsoft.Extensions.DependencyInjection;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Configs;

public static class WikiDocGeneratorToolsConfig
{
    public static IServiceCollection AddWikiDocGeneratorTools(this IServiceCollection services)
    {
        // Wspólne serwisy infrastrukturalne (DevOps REST, Embedding, Search,
        // BlobServiceClient, TableClient + stores) — z projektu Core.
        services.AddUsefulAICoreServices();

        // Research tools
        services.AddSingleton<GetPullRequestDetailsTool>();
        services.AddSingleton<GetPullRequestChangesTool>();
        services.AddSingleton<ReadRepositoryFileTool>();
        services.AddSingleton<ListWikiPagesTool>();
        services.AddSingleton<GetWikiPageTool>();
        services.AddSingleton<GetWorkItemDetailsTool>();
        services.AddSingleton<ResearchTools>();

        // Sender tools
        services.AddSingleton<UpsertWikiPageTool>();
        services.AddSingleton<SenderTools>();

        // Pipeline
        services.AddSingleton<WikiDocGenerationPipeline>();

        return services;
    }
}
