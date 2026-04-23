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

        // Pipeline indeksacji work itemów (Service Bus) — 3 niezależne usługi po jednej per etap
        services.AddSingleton<IWorkItemQueryService, WorkItemQueryService>();
        services.AddSingleton<IWorkItemDocumentBuilder, WorkItemDocumentBuilder>();
        services.AddSingleton<IWorkItemSearchUploader, WorkItemSearchUploader>();

        // Pipeline indeksacji WIKI (Service Bus) — analogiczny podział na 3 etapy
        services.AddSingleton<IWikiPageQueryService, WikiPageQueryService>();
        services.AddSingleton<IWikiDocumentBuilder, WikiDocumentBuilder>();
        services.AddSingleton<IWikiSearchUploader, WikiSearchUploader>();

        // Research tools
        services.AddSingleton<SearchWorkItemsTool>();
        services.AddSingleton<SearchWikiTool>();
        services.AddSingleton<GetWorkItemDetailsTool>();
        services.AddSingleton<GetWikiPageDetailsTool>();
        services.AddSingleton<ResearchTools>();

        // Sender tools
        services.AddSingleton<PostCommentTool>();
        services.AddSingleton<SenderTools>();

        // Pipeline
        services.AddSingleton<ImpactAnalysisPipeline>();

        return services;
    }
}
