using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.Core.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace BSolution.Netwise.UsefulAI.Core.Configuration;

/// <summary>
/// Shared dependency registration for all applications
/// (Impact Analyzer, Wiki Doc Generator, ...). Covers Azure clients
/// (Blob, Table), <c>Embedding</c>/<c>Search</c>/<c>DevOps</c> services,
/// and the claim-check store and settings store.
/// </summary>
/// <remarks>
/// Uses <c>AzureWebJobsStorage</c> (same account as the Functions runtime)
/// and <c>DefaultAzureCredential</c> (keyless). Supports fallback for Azurite
/// (<c>UseDevelopmentStorage=true</c>) and ApiKey for AzureSearch/AzureOpenAI
/// in local tests.
/// </remarks>
public static class CoreServicesRegistration
{
    public static IServiceCollection AddUsefulAICoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IAzureSearchService, AzureSearchService>();
        services.AddHttpClient<IAzureDevOpsService, AzureDevOpsService>()
            .AddStandardResilienceHandler();

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

        return services;
    }
}
