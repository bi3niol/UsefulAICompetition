

using Microsoft.Extensions.AI;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Writer;

public class WriterTools
{
    public IList<AITool> GetAll() =>
    [
        AIFunctionFactory.Create(GetCurrentDate)
    ];

    [AgentTool(Description = "Returns the current date in YYYY-MM-DD format. Useful for timestamping analysis results or reports.")]
    public string GetCurrentDate() => DateTime.UtcNow.ToString("yyyy-MM-dd");
}
