using BSolution.Netwise.UsefulAI.Core.Configuration;
using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Writer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Configs;

public static class ToolsConfig
{
    // Pełny zbiór typów WI do indeksacji semantycznej — od Epica po Task'a.
    private static readonly string[] IndexableWorkItemTypes =
    [
        "User Story", "Product Backlog Item", "Bug",
        "Task", "Epic", "Feature", "Requirement"
    ];

    public static IServiceCollection AddImpactAnalyzerTools(
        this IServiceCollection services)
    {
        // Wspólne serwisy infrastrukturalne (Embedding, Search, DevOps,
        // BlobServiceClient, TableClient + Blob/Settings stores) — zarejestrowane
        // raz w Core. App dokłada tylko składniki specyficzne dla Impact Analyzera.
        services.AddUsefulAICoreServices();

        // Store raportów — specyficzny dla Impact Analyzera (kontener "reports").
        services.AddSingleton<IReportStore, ReportStore>();

        // Indexing — SearchIndexManager uruchamia się jako hosted service przy starcie
        services.AddHostedService<SearchIndexManager>();

        // Pipeline indeksacji work itemów (Service Bus) — 3 niezależne usługi po jednej per etap.
        // WorkItemQueryService jest wspólny w Core; tu wstrzykujemy listę typów do filtrowania.
        services.AddSingleton<IWorkItemQueryService>(sp => new WorkItemQueryService(
            sp.GetRequiredService<IAzureDevOpsService>(),
            sp.GetRequiredService<ILogger<WorkItemQueryService>>(),
            IndexableWorkItemTypes,
            logTag: "WI-INDEX-QUERY"));
        services.AddSingleton<IWorkItemDocumentBuilder, WorkItemDocumentBuilder>();
        services.AddSingleton<IWorkItemSearchUploader, WorkItemSearchUploader>();

        // Pipeline indeksacji WIKI (Service Bus) — analogiczny podział na 3 etapy
        services.AddSingleton<IWikiPageQueryService, WikiPageQueryService>();
        services.AddSingleton<IWikiDocumentBuilder, WikiDocumentBuilder>();
        services.AddSingleton<IWikiSearchUploader, WikiSearchUploader>();

        // Research tools
        services.AddSingleton<SearchWorkItemsTool>();
        services.AddSingleton<KeywordSearchWorkItemsTool>();
        services.AddSingleton<SearchWikiTool>();
        services.AddSingleton<GetWorkItemDetailsTool>();
        services.AddSingleton<GetWikiPageDetailsTool>();
        services.AddSingleton<ResearchTools>();
        services.AddSingleton<WriterTools>();

        // Sender tools
        services.AddSingleton<PostCommentTool>();
        services.AddSingleton<SenderTools>();

        // Pipeline
        services.AddSingleton<ImpactAnalysisPipeline>();

        return services;
    }
}
