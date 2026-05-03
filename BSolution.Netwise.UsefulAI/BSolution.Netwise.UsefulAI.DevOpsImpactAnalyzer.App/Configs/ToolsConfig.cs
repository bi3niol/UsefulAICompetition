using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Writer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Configs;

public static class ToolsConfig
{
    public static IServiceCollection AddImpactAnalyzerTools(
        this IServiceCollection services)
    {
        // Shared services
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IAzureSearchService, AzureSearchService>();
        services.AddHttpClient<IAzureDevOpsService, AzureDevOpsService>()
            .AddStandardResilienceHandler();

        // Blob Storage — wspólny BlobServiceClient (keyless via DefaultAzureCredential)
        // dla całej aplikacji. Korzysta z TEGO SAMEGO storage account co Functions runtime
        // (AzureWebJobsStorage). Wymagana rola dla MI: "Storage Blob Data Owner"
        // (już przypisana w bicep).
        // Każdy store sam pobiera swój kontener — nie wstrzykujemy BlobContainerClient
        // globalnie, bo różne usługi używają różnych kontenerów (messages, reports, ...).
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["AzureWebJobsStorage"];
            if (connectionString == "UseDevelopmentStorage=true")
                return new BlobServiceClient(connectionString);

            var accountName = config["AzureWebJobsStorage:accountName"]
                ?? throw new InvalidOperationException(
                    "AzureWebJobsStorage:accountName (AzureWebJobsStorage__accountName) is not configured.");
            return new BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new DefaultAzureCredential());
        });
        services.AddSingleton<IBlobMessageStore, BlobMessageStore>();
        services.AddSingleton<IReportStore, ReportStore>();

        // Azure Tables — generyczna tabela "Settings" (key-value) trzymająca
        // konfigurację runtime'ową (m.in. znaczniki ostatniej synchronizacji
        // indekserów). Korzysta z TEGO SAMEGO storage account co Functions
        // runtime (AzureWebJobsStorage), keyless via DefaultAzureCredential.
        // Wymagana rola dla MI: "Storage Table Data Contributor".
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["AzureWebJobsStorage"];
            TableServiceClient serviceClient;
            if (connectionString == "UseDevelopmentStorage=true")
            {
                serviceClient = new TableServiceClient(connectionString);
            }
            else
            {
                var accountName = config["AzureWebJobsStorage:accountName"]
                    ?? throw new InvalidOperationException(
                        "AzureWebJobsStorage:accountName (AzureWebJobsStorage__accountName) is not configured.");
                serviceClient = new TableServiceClient(
                    new Uri($"https://{accountName}.table.core.windows.net"),
                    new DefaultAzureCredential());
            }
            var tableClient = serviceClient.GetTableClient(SettingKeys.TableName);
            tableClient.CreateIfNotExists();
            return tableClient;
        });
        services.AddSingleton<ISettingsStore, SettingsStore>();

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
