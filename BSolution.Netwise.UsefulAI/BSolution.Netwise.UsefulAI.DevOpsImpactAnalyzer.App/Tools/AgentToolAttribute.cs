using System.ComponentModel;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools;

/// <summary>
/// Oznacza metodę jako narzędzie agenta AI.
/// Dziedziczy po <see cref="DescriptionAttribute"/> — dzięki temu
/// <c>AITool.FromMethod()</c> z Microsoft.Extensions.AI automatycznie
/// odczytuje opis i przekazuje go do LLM jako tool description.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentToolAttribute : DescriptionAttribute
{
    public AgentToolAttribute() : base(string.Empty) { }

    // Nadpisujemy jako settable property — wymagane dla składni [AgentTool(Description = "...")]
    public new string Description
    {
        get => DescriptionValue;
        set => DescriptionValue = value;
    }
}