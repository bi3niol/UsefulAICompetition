using System.ComponentModel;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools;

/// <summary>
/// Oznacza metodę jako narzędzie agenta AI. Dziedziczy po <see cref="DescriptionAttribute"/>
/// — <c>AITool.FromMethod()</c> z Microsoft.Extensions.AI odczytuje opis i przekazuje
/// go do LLM jako tool description.
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
