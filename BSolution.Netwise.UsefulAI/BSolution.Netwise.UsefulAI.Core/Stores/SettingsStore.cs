using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using BSolution.Netwise.UsefulAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.Core.Stores;

/// <summary>
/// Generic key-value store backed by Azure Tables (table <c>Settings</c>).
/// Values are serialized as JSON, so any type serializable by <see cref="JsonSerializer"/>
/// is supported.
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
