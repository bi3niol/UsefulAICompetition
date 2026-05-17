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
/// Wspólna rejestracja zależności współdzielonych przez wszystkie aplikacje
/// (Impact Analyzer, Wiki Doc Generator, ...). Obejmuje klientów Azure
/// (Blob, Table), serwisy <c>Embedding</c>/<c>Search</c>/<c>DevOps</c>
/// oraz claim-check store i settings store.
/// </summary>
/// <remarks>
/// Korzysta z <c>AzureWebJobsStorage</c> (to samo konto co Functions runtime)
/// i <c>DefaultAzureCredential</c> (keyless). Obsługuje fallback dla Azurite
/// (<c>UseDevelopmentStorage=true</c>) i ApiKey dla AzureSearch/AzureOpenAI
/// w testach lokalnych.
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
