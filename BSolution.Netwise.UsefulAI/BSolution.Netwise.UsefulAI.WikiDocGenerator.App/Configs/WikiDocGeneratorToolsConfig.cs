using BSolution.Netwise.UsefulAI.Core.Configuration;
using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Sender;
using Microsoft.Extensions.Configuration;
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

        // Wiki store — feature flag WikiDocGenerator:UseBlobStorage (default: true).
        // true  = tymczasowy Blob Storage backend (nie wymaga uprawnień do DevOps Wiki)
        // false = docelowy DevOps Wiki API
        services.AddSingleton<BlobWikiStore>();
        services.AddSingleton<DevOpsWikiStore>();
        services.AddSingleton<IWikiStore>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var useBlobStorage = config.GetValue("WikiDocGenerator:UseBlobStorage", defaultValue: true);
            return useBlobStorage
                ? sp.GetRequiredService<BlobWikiStore>()
                : sp.GetRequiredService<DevOpsWikiStore>();
        });

        // Source query — feature-level work items dla cyklicznego odświeżania wiki.
        services.AddSingleton<IWorkItemQueryService>(sp => new WorkItemQueryService(
            sp.GetRequiredService<IAzureDevOpsService>(),
            sp.GetRequiredService<ILogger<WorkItemQueryService>>(),
            DocumentableWorkItemTypes,
            logTag: "WIKIGEN-QUERY"));

        // Code scan options (bound from config section WikiDocGenerator:Code)
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var opts = new CodeScanOptions();
            config.GetSection("WikiDocGenerator:Code").Bind(opts);
            return Microsoft.Extensions.Options.Options.Create(opts);
        });
        services.AddSingleton<CodeRepositoryResolver>();

        // Research tools
        services.AddSingleton<GetPullRequestDetailsTool>();
        services.AddSingleton<GetPullRequestChangesTool>();
        services.AddSingleton<ReadRepositoryFileTool>();
        services.AddSingleton<ListWikiPagesTool>();
        services.AddSingleton<GetWikiPageTool>();
        services.AddSingleton<GetWorkItemDetailsTool>();
        services.AddSingleton<ListCodeRepositoriesTool>();
        services.AddSingleton<ListRepositoryFilesTool>();
        services.AddSingleton<ResearchTools>();

        // Sender tools
        services.AddSingleton<UpsertWikiPageTool>();
        services.AddSingleton<SenderTools>();

        // Pipeline
        services.AddSingleton<WikiDocGenerationPipeline>();

        return services;
    }
}
