using BSolution.Netwise.UsefulAI.Core.Configuration;
using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Sender;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Configs;

public static class WikiDocGeneratorToolsConfig
{
    // Tylko feature-level typy — Tasks/Bugs nie reprezentują tematów dokumentowanych w wiki.
    private static readonly string[] DocumentableWorkItemTypes =
    [
        "Feature", "Epic", "User Story", "Product Backlog Item"
    ];

    public static IServiceCollection AddWikiDocGeneratorTools(this IServiceCollection services)
    {
        // Wspólne serwisy infrastrukturalne (DevOps REST, Embedding, Search,
        // BlobServiceClient, TableClient + stores) — z projektu Core.
        services.AddUsefulAICoreServices();

        // Source query — feature-level work items dla cyklicznego odświeżania wiki.
        // Używamy wspólnego WorkItemQueryService z Core, sparametryzowanego listą typów.
        services.AddSingleton<IWorkItemQueryService>(sp => new WorkItemQueryService(
            sp.GetRequiredService<IAzureDevOpsService>(),
            sp.GetRequiredService<ILogger<WorkItemQueryService>>(),
            DocumentableWorkItemTypes,
            logTag: "WIKIGEN-QUERY"));

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
