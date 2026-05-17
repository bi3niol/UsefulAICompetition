using Microsoft.Extensions.AI;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Sender;

public class SenderTools(UpsertWikiPageTool upsert)
{
    public IList<AITool> GetAll() =>
    [
        AIFunctionFactory.Create(upsert.UpsertWikiPageAsync)
    ];
}
