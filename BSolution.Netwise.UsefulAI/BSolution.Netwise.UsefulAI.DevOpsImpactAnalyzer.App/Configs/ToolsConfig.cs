using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
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

        // Blob Storage — kontener "messages" dla Claim-Check Pattern.
        // Korzysta z TEGO SAMEGO storage account co Functions runtime
        // (AzureWebJobsStorage), keyless via DefaultAzureCredential.
        // Wymagana rola dla MI: "Storage Blob Data Owner" (już przypisana w bicep).
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var accountName = config["AzureWebJobsStorage:accountName"]
                ?? throw new InvalidOperationException(
                    "AzureWebJobsStorage:accountName (AzureWebJobsStorage__accountName) is not configured.");
            var serviceClient = new BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new DefaultAzureCredential());
            return serviceClient.GetBlobContainerClient("messages");
        });
        services.AddSingleton<IBlobMessageStore, BlobMessageStore>();

        // Azure Tables — generyczna tabela "Settings" (key-value) trzymająca
        // konfigurację runtime'ową (m.in. znaczniki ostatniej synchronizacji
        // indekserów). Korzysta z TEGO SAMEGO storage account co Functions
        // runtime (AzureWebJobsStorage), keyless via DefaultAzureCredential.
        // Wymagana rola dla MI: "Storage Table Data Contributor".
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            // AzureWebJobsStorage__accountName  →  config["AzureWebJobsStorage:accountName"]
            var accountName = config["AzureWebJobsStorage:accountName"]
                ?? throw new InvalidOperationException(
                    "AzureWebJobsStorage:accountName (AzureWebJobsStorage__accountName) is not configured.");
            var serviceClient = new TableServiceClient(
                new Uri($"https://{accountName}.table.core.windows.net"),
                new DefaultAzureCredential());
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

        // Sender tools
        services.AddSingleton<PostCommentTool>();
        services.AddSingleton<SenderTools>();

        // Pipeline
        services.AddSingleton<ImpactAnalysisPipeline>();

        return services;
    }
}
