using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace BSolution.Netwise.UsefulAI.Core.Models;

/// <summary>
/// Generyczny wiersz tabeli konfiguracyjnej <c>Settings</c> w stylu key-value.
/// Wartość jest serializowana jako JSON do property <see cref="Value"/>, dzięki
/// czemu jeden schemat tabeli obsługuje dowolne typy (DateTimeOffset, int,
/// rekordy konfiguracyjne itp.).
/// </summary>
/// <remarks>
/// Konwencja kluczy: <c>PartitionKey = "settings"</c>, <c>RowKey = klucz logiczny</c>
/// (np. <c>"indexer.workitems.lastSync"</c>).
/// </remarks>
public class SettingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = SettingKeys.Partition;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>JSON-owana wartość ustawienia.</summary>
    public string? Value { get; set; }

    /// <summary>Deserializuje <see cref="Value"/> do żądanego typu.</summary>
    public T? As<T>() =>
        string.IsNullOrEmpty(Value) ? default : JsonSerializer.Deserialize<T>(Value);
}

public static class SettingKeys
{
    public const string TableName = "Settings";
    public const string Partition = "settings";

    // Konkretne klucze konfiguracyjne używane w aplikacji.
    public const string WorkItemsLastSync = "indexer.workitems.lastSync";
    public const string WikiLastSync = "indexer.wiki.lastSync";
}
