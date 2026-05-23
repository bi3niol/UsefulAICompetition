namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;

/// <summary>
/// Konfiguracja skanu kodu źródłowego dla WikiDocGeneratora.
/// Czytana z sekcji <c>WikiDocGenerator:Code</c> w <c>IConfiguration</c>.
/// </summary>
/// <remarks>
/// Świadomie modelowane jako lista repozytoriów: realny projekt zwykle
/// żyje w kilku repo (np. backend + frontend + infra). Każde repo ma własną
/// gałąź, z której zawsze czytamy kod (Researcher nigdy nie wybiera gałęzi sam).
/// </remarks>
public class CodeScanOptions
{
    /// <summary>Lista repozytoriów do skanu. Pusta = funkcjonalność wyłączona.</summary>
    public List<CodeScanRepository> Repositories { get; set; } = [];

    /// <summary>
    /// Rozszerzenia plików dopuszczone do skanu (z kropką, np. <c>.cs</c>).
    /// Domyślne pokrywają C#, projekty, IaC, dokumentację i konfigurację.
    /// </summary>
    public List<string> IncludeExtensions { get; set; } =
    [
        ".cs", ".csproj", ".sln", ".slnx",
        ".bicep", ".tf",
        ".md",
        ".yml", ".yaml",
        ".json"
    ];

    /// <summary>
    /// Nazwy katalogów (case-insensitive) pomijane przy skanie. Wyłącznie
    /// techniczne: build outputy, paczki, lokalne katalogi narzędziowe.
    /// </summary>
    public List<string> ExcludeFolders { get; set; } =
    [
        "bin", "obj", "node_modules", ".vs", ".git", ".github",
        "dist", "out", "TestResults", "packages"
    ];

    /// <summary>Maks. rozmiar pliku branego do kontekstu (większe pomijamy).</summary>
    public int MaxFileBytes { get; set; } = 200_000;

    /// <summary>
    /// Maks. liczba plików zmienionych przekazywana do jednego biegu pipeline'u.
    /// Większy diff dzielony na paczki, żeby kontekst LLM był strawny.
    /// </summary>
    public int MaxFilesPerPipelineRun { get; set; } = 40;
}

/// <summary>Pojedyncze repozytorium objęte skanem.</summary>
public class CodeScanRepository
{
    /// <summary>Nazwa lub ID repo w Azure DevOps (preferowana nazwa — łatwiej czyta).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gałąź na której operuje skan (np. <c>main</c>). Jeśli puste — używany
    /// jest domyślny branch repo zwrócony przez DevOps API.
    /// </summary>
    public string? Branch { get; set; }
}
