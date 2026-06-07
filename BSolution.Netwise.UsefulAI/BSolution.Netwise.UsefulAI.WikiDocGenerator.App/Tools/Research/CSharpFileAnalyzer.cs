using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

internal sealed class CSharpFileAnalyzer : CSharpSyntaxWalker
{
    private readonly List<CSharpTypeInfo> _types = [];
    private readonly List<string> _enums = [];
    private readonly HashSet<string> _namespaces = [];
    private readonly HashSet<string> _invokedMembers = [];
    private readonly Stack<TypeBuilder> _typeStack = new();

    public IReadOnlyList<CSharpTypeInfo> Types => _types;
    public IReadOnlyList<CSharpTypeInfo> Interfaces => _types.Where(t => t.Kind == "interface").ToList();
    public IReadOnlyList<string> Enums => _enums;
    public IReadOnlySet<string> Namespaces => _namespaces;
    public IReadOnlySet<string> InvokedMembers => _invokedMembers;

    public static CSharpFileAnalyzer Analyze(string sourceCode)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var walker = new CSharpFileAnalyzer();
        walker.Visit(tree.GetRoot());
        return walker;
    }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name is not null)
            _namespaces.Add(node.Name.ToString());
        base.VisitUsingDirective(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        PushType(node.Identifier.Text, "class", node.BaseList);
        base.VisitClassDeclaration(node);
        _types.Add(_typeStack.Pop().Build());
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        PushType(node.Identifier.Text, "record", node.BaseList);
        base.VisitRecordDeclaration(node);
        _types.Add(_typeStack.Pop().Build());
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        PushType(node.Identifier.Text, "interface", node.BaseList);
        base.VisitInterfaceDeclaration(node);
        _types.Add(_typeStack.Pop().Build());
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        PushType(node.Identifier.Text, "struct", node.BaseList);
        base.VisitStructDeclaration(node);
        _types.Add(_typeStack.Pop().Build());
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        _enums.Add(node.Identifier.Text);
        base.VisitEnumDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_typeStack.TryPeek(out var owner))
        {
            owner.Methods.Add(new CSharpMethodInfo(
                node.Identifier.Text,
                node.ReturnType.ToString(),
                node.Modifiers.Select(m => m.Text).ToList(),
                node.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList()));
        }

        base.VisitMethodDeclaration(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax memberAccess)
            _invokedMembers.Add(memberAccess.Name.Identifier.Text);
        base.VisitInvocationExpression(node);
    }

    private void PushType(string name, string kind, BaseListSyntax? baseList) =>
        _typeStack.Push(new TypeBuilder(name, kind, ExtractBaseTypes(baseList)));

    private static List<string> ExtractBaseTypes(BaseListSyntax? baseList) =>
        baseList?.Types.Select(t => t.Type.ToString()).ToList() ?? [];

    private sealed class TypeBuilder(string name, string kind, List<string> baseTypes)
    {
        public List<CSharpMethodInfo> Methods { get; } = [];
        public CSharpTypeInfo Build() => new(name, kind, baseTypes, Methods);
    }
}

internal sealed record CSharpTypeInfo(
    string Name,
    string Kind,
    List<string> BaseTypes,
    List<CSharpMethodInfo> Methods);

internal sealed record CSharpMethodInfo(
    string Name,
    string ReturnType,
    List<string> Modifiers,
    List<string> ParameterNames);
