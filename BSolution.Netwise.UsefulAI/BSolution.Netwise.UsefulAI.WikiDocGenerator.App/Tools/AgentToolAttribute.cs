using System.ComponentModel;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools;

/// <summary>
/// Marks a method as an AI agent tool. Inherits from <see cref="DescriptionAttribute"/>
/// — <c>AITool.FromMethod()</c> from Microsoft.Extensions.AI reads the description
/// and passes it to the LLM as the tool description.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentToolAttribute : DescriptionAttribute
{
    public AgentToolAttribute() : base(string.Empty) { }

    public new string Description
    {
        get => DescriptionValue;
        set => DescriptionValue = value;
    }
}
