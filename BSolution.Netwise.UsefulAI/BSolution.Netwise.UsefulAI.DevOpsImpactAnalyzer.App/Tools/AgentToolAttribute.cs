using System.ComponentModel;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools;

/// <summary>
/// Marks a method as an AI agent tool.
/// Inherits from <see cref="DescriptionAttribute"/> — this way
/// <c>AITool.FromMethod()</c> from Microsoft.Extensions.AI automatically
/// reads the description and passes it to the LLM as the tool description.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentToolAttribute : DescriptionAttribute
{
    public AgentToolAttribute() : base(string.Empty) { }

    // Override as a settable property — required for the [AgentTool(Description = "...")] syntax
    public new string Description
    {
        get => DescriptionValue;
        set => DescriptionValue = value;
    }
}