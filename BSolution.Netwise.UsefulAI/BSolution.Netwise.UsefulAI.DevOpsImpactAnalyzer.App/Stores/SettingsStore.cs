using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;

/// <summary>
/// Generyczny key-value store oparty o Azure Tables (tabela <c>Settings</c>).
/// Wartości są serializowane jako JSON, więc obsługiwany jest dowolny typ
/// serializowalny przez <see cref="JsonSerializer"/>.
/// </summary>
public interface ISettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task UpsertAsync<T>(string key, T value, CancellationToken ct = default);
}

public class SettingsStore(TableClient tableClient, ILogger<SettingsStore> logger) : ISettingsStore
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await tableClient.GetEntityAsync<SettingEntity>(
                SettingKeys.Partition, key, cancellationToken: ct);
            return response.Value.As<T>();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return default;
        }
    }

    public async Task UpsertAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var entity = new SettingEntity
        {
            PartitionKey = SettingKeys.Partition,
            RowKey = key,
            Value = JsonSerializer.Serialize(value),
        };

        await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);

        logger.LogInformation("[SETTINGS] Upserted '{Key}'.", key);
    }
}
