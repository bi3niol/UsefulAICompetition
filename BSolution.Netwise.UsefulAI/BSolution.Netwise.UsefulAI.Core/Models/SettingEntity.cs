using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace BSolution.Netwise.UsefulAI.Core.Models;

/// <summary>
/// Generic row for the <c>Settings</c> configuration table in key-value style.
/// The value is serialized as JSON in the <see cref="Value"/> property, so a
/// single table schema can hold any type (DateTimeOffset, int, config records, etc.).
/// </summary>
/// <remarks>
/// Key convention: <c>PartitionKey = "settings"</c>, <c>RowKey = logical key</c>
/// (e.g. <c>"indexer.workitems.lastSync"</c>).
/// </remarks>
public class SettingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = SettingKeys.Partition;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>JSON-serialized setting value.</summary>
    public string? Value { get; set; }

    /// <summary>Deserializes <see cref="Value"/> to the requested type.</summary>
    public T? As<T>() =>
        string.IsNullOrEmpty(Value) ? default : JsonSerializer.Deserialize<T>(Value);
}

public static class SettingKeys
{
    public const string TableName = "Settings";
    public const string Partition = "settings";

    // Concrete configuration keys used in the application.
    public const string WorkItemsLastSync = "indexer.workitems.lastSync";
    public const string WikiLastSync = "indexer.wiki.lastSync";
    public const string WikiGenLastSync = "wikigen.workitems.lastSync";

    /// <summary>
    /// Builds the watermark key for a code scan per repository+branch.
    /// Format: <c>wikigen.code.{repoId}.{branch}.lastSha</c>. The watermark
    /// stores the SHA of the last scanned commit on that branch.
    /// </summary>
    public static string WikiGenCodeLastSha(string repoId, string branch) =>
        $"wikigen.code.{repoId}.{branch}.lastSha";
}
