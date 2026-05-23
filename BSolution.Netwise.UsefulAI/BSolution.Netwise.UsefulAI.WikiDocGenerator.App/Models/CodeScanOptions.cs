namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;

public class CodeScanOptions
{
    public List<CodeScanRepository> Repositories { get; set; } = [];

    public List<string> IncludeExtensions { get; set; } =
    [
        ".cs", ".csproj", ".sln", ".slnx",
        ".bicep", ".tf",
        ".md",
        ".yml", ".yaml",
        ".json"
    ];

    public List<string> ExcludeFolders { get; set; } =
    [
        "bin", "obj", "node_modules", ".vs", ".git", ".github",
        "dist", "out", "TestResults", "packages"
    ];

    public int MaxFileBytes { get; set; } = 200_000;
    public int MaxFilesPerPipelineRun { get; set; } = 40;
}

public class CodeScanRepository
{
    public string Name { get; set; } = string.Empty;
    public string? Branch { get; set; }
}
