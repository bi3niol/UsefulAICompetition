using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 3/4 — buduje listę chunkowanych dokumentów Azure Search (z embeddingami) dla
/// pojedynczego work itemu i publikuje wynik na kolejce <c>workitem-documents</c>.
///
/// Limity wywołań Embedding API kontroluje
/// <c>host.json → extensions.serviceBus.maxConcurrentCalls</c>.
/// </summary>
public class WorkItemBuildDocumentsFunction(
    IWorkItemDocumentBuilder builder,
    ILogger<WorkItemBuildDocumentsFunction> logger)
{
    [Function(nameof(WorkItemBuildDocumentsFunction))]
    [ServiceBusOutput("workitem-documents", Connection = "ServiceBus")]
    public async Task<WorkItemIndexDocumentsMessage?> Run(
        [ServiceBusTrigger("workitem-details", Connection = "ServiceBus")] WorkItemDetailMessage message,
        CancellationToken ct)
    {
        var wi = message.WorkItem;
        logger.LogInformation("[WI-BUILD] Building documents for WI#{Id}...", wi.Id);

        var documents = await builder.BuildAsync(wi, ct);

        if (documents.Count == 0)
        {
            logger.LogInformation("[WI-BUILD] WI#{Id} produced no documents.", wi.Id);
            return null;
        }

        return new WorkItemIndexDocumentsMessage { Documents = documents };
    }
}
