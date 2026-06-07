using BSolution.Netwise.UsefulAI.Core.Services;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class AnalyzeRepositoryFileTool(IAzureDevOpsService devOps)
{
    private const int MaxAnalyzedLength = 50_000;

    [AgentTool(Description = """
        Analyzes a single repository file and returns a compact JSON summary:
        declared types/functions, relevant references and invoked members,
        plus a short potential impact summary.
        Supported files: .cs, .js, .ts, .jsx, .tsx, .html.
        Use this first to understand file intent without loading full file content
        into the conversation history.
        """)]
    public async Task<string> AnalyzeRepositoryFileAsync(
        [Description("Repository ID (GUID)")] string repositoryId,
        [Description("File path inside repository, e.g. /src/Foo/Bar.cs")] string path,
        [Description("Commit SHA to read at. Pass empty string for default branch HEAD.")]
        string commitId)
    {
        var effectiveCommit = string.IsNullOrWhiteSpace(commitId) ? null : commitId;
        var content = await devOps.GetFileContentAsync(repositoryId, path, effectiveCommit);

        if (content is null)
            return JsonSerializer.Serialize(new { error = "File not found.", path });

        var truncated = content.Length > MaxAnalyzedLength;
        var analyzedContent = truncated ? content[..MaxAnalyzedLength] : content;

        var language = GetLanguageHint(path);
        var analysis = language switch
        {
            "csharp" => AnalyzeCSharp(analyzedContent),
            "javascript" or "typescript" or "javascriptreact" or "typescriptreact" => AnalyzeJsTs(analyzedContent),
            "html" => AnalyzeHtml(analyzedContent),
            _ => AnalyzeGeneric(analyzedContent)
        };

        var area = GetArea(path);

        return JsonSerializer.Serialize(new
        {
            path,
            analyzedChars = analyzedContent.Length,
            truncated,
            languageHint = language,
            declarations = analysis.Declarations,
            references = analysis.References,
            impactSummary = BuildImpactSummary(area, language, analysis)
        });
    }

    private static FileAnalysis AnalyzeCSharp(string content)
    {
        var walker = CSharpFileAnalyzer.Analyze(content);

        var types = walker.Types
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                name      = t.Name,
                kind      = t.Kind,
                baseTypes = t.BaseTypes.Count > 0 ? t.BaseTypes : null,
                methods   = t.Methods
                    .OrderBy(m => m.Name)
                    .Select(m => new
                    {
                        name           = m.Name,
                        returnType     = m.ReturnType,
                        modifiers      = m.Modifiers,
                        parameterNames = m.ParameterNames
                    })
                    .ToList()
            })
            .ToList();

        var interfaces = walker.Interfaces
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                name      = t.Name,
                baseTypes = t.BaseTypes.Count > 0 ? t.BaseTypes : null,
                methods   = t.Methods.Select(m => m.Name).Order().ToList()
            })
            .ToList();

        var totalMethods = walker.Types.Sum(t => t.Methods.Count);

        return new FileAnalysis(
            new
            {
                types,
                interfaces,
                enums = walker.Enums.Order().ToList()
            },
            new
            {
                namespaces     = walker.Namespaces.Order().ToList(),
                invokedMembers = walker.InvokedMembers.Order().Take(160).ToList()
            },
            walker.Types.Count + walker.Enums.Count,
            totalMethods,
            walker.Namespaces.Count + walker.InvokedMembers.Count);
    }

    private static FileAnalysis AnalyzeJsTs(string content)
    {
        var imports = Regex.Matches(content, "^\\s*import\\s+.*?from\\s+['\\\"]([^'\\\"]+)['\\\"]", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();

        var classes = Regex.Matches(content, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();

        var interfaces = Regex.Matches(content, @"\binterface\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();

        var typeAliases = Regex.Matches(content, @"\btype\s+([A-Za-z_][A-Za-z0-9_]*)\s*=")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();

        var functionDecl = Regex.Matches(content, @"\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(m => m.Groups[1].Value);

        var arrowFunctions = Regex.Matches(content, @"\b(?:const|let|var)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\([^\)]*\)\s*=>")
            .Select(m => m.Groups[1].Value);

        var classMethods = Regex.Matches(content, @"^\s*(?:async\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*\([^\)]*\)\s*\{", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Where(IsNotKeyword);

        var functions = functionDecl
            .Concat(arrowFunctions)
            .Concat(classMethods)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(160)
            .ToList();

        var components = functions
            .Where(n => n.Length > 0 && char.IsUpper(n[0]))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();

        var invokedMembers = Regex.Matches(content, @"\.([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(180)
            .ToList();

        return new FileAnalysis(
            new
            {
                classes,
                interfaces,
                typeAliases,
                functions,
                components
            },
            new
            {
                imports,
                invokedMembers
            },
            classes.Count + interfaces.Count + typeAliases.Count,
            functions.Count,
            imports.Count + invokedMembers.Count);
    }

    private static FileAnalysis AnalyzeHtml(string content)
    {
        var tags = Regex.Matches(content, @"<\s*([a-zA-Z][a-zA-Z0-9\-]*)\b")
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(120)
            .ToList();

        var ids = Regex.Matches(content, "\\bid\\s*=\\s*['\\\"]([^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(120)
            .ToList();

        var classes = Regex.Matches(content, "\\bclass\\s*=\\s*['\\\"]([^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase)
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(180)
            .ToList();

        var eventHandlers = Regex.Matches(content, "\\bon([a-zA-Z]+)\\s*=\\s*['\\\"][^'\\\"]*['\\\"]")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .Take(120)
            .ToList();

        var scriptSources = Regex.Matches(content, "<script[^>]*\\bsrc\\s*=\\s*['\\\"]([^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(120)
            .ToList();

        var inlineFunctions = Regex.Matches(content, @"\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(120)
            .ToList();

        return new FileAnalysis(
            new
            {
                tags,
                ids,
                classes,
                functions = inlineFunctions
            },
            new
            {
                scriptSources,
                eventHandlers
            },
            tags.Count,
            inlineFunctions.Count,
            scriptSources.Count + eventHandlers.Count);
    }

    private static FileAnalysis AnalyzeGeneric(string content)
    {
        var invokedMembers = Regex.Matches(content, @"\.([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .Take(80)
            .ToList();

        return new FileAnalysis(
            new { },
            new { invokedMembers },
            0,
            0,
            invokedMembers.Count);
    }

    private static bool IsNotKeyword(string name) =>
        name is not ("if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock" or "return" or "new");

    private static string GetArea(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "unknown area";

        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2)
            return $"{segments[0]}/{segments[1]}";

        return segments.Length == 1 ? segments[0] : "unknown area";
    }

    private static string GetLanguageHint(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".jsx" => "javascriptreact",
            ".tsx" => "typescriptreact",
            ".html" or ".htm" => "html",
            ".json" => "json",
            ".md" => "markdown",
            ".yml" or ".yaml" => "yaml",
            _ => "unknown"
        };
    }

    private static string BuildImpactSummary(string area, string language, FileAnalysis analysis)
    {
        return $"File in {area} ({language}). Found {analysis.TypeCount} type-like declaration(s), {analysis.FunctionCount} function/method declaration(s), and {analysis.ReferenceCount} reference signal(s). Changes here may impact related integrations, runtime behavior, and documentation sections for this area.";
    }

    private sealed record FileAnalysis(object Declarations, object References, int TypeCount, int FunctionCount, int ReferenceCount);
}
