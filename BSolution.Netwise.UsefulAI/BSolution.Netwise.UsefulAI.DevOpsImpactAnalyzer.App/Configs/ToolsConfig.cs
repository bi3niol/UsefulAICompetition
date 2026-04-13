using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Configs;

public static class ToolsConfig
{
    public static IServiceCollection AddImpactAnalyzerTools(
        this IServiceCollection services)
    {
        // Shared services
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IAzureSearchService, AzureSearchService>();
        services.AddHttpClient<IAzureDevOpsService, AzureDevOpsService>();

        // Indexing — SearchIndexManager uruchamia się jako hosted service przy starcie
        services.AddHostedService<SearchIndexManager>();
        services.AddSingleton<IWorkItemIndexer, WorkItemIndexer>();
        services.AddSingleton<IWikiIndexer, WikiIndexer>();

        // Research tools
        services.AddSingleton<SearchWorkItemsTool>();
        services.AddSingleton<SearchWikiTool>();
        services.AddSingleton<GetWorkItemDetailsTool>();
        services.AddSingleton<ResearchTools>();

        // Sender tools
        services.AddSingleton<PostCommentTool>();
        services.AddSingleton<SenderTools>();

        // Pipeline
        services.AddSingleton<ImpactAnalysisPipeline>();

        return services;
    }
}
