namespace BSolution.Netwise.UsefulAI.Core.Models;

/// <summary>Repozytorium Git w Azure DevOps (wynik z endpoint /git/repositories).</summary>
public class GitRepositoryInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? DefaultBranch { get; set; }
    public string? Url { get; set; }
    public string? WebUrl { get; set; }
}

/// <summary>Skrócony opis pull requesta z DevOps API.</summary>
public class PullRequestDetail
{
    public int PullRequestId { get; set; }
    public string? RepositoryId { get; set; }
    public string? RepositoryName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SourceBranch { get; set; }
    public string? TargetBranch { get; set; }
    public string? Status { get; set; }
    public string? MergeStatus { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreationDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    public string? LastMergeCommitId { get; set; }
    public string? LastMergeSourceCommitId { get; set; }
    public string? LastMergeTargetCommitId { get; set; }
    public string? Url { get; set; }
    public string? WebUrl { get; set; }
}

/// <summary>Pojedyncza zmiana plikowa w obrębie PR/commitu.</summary>
public class PullRequestChange
{
    /// <summary>Ścieżka pliku w repo (np. <c>/src/Foo/Bar.cs</c>).</summary>
    public string? Path { get; set; }

    /// <summary>Typ zmiany: <c>add</c>, <c>edit</c>, <c>delete</c>, <c>rename</c>.</summary>
    public string? ChangeType { get; set; }

    /// <summary>Stara ścieżka przy <c>rename</c>.</summary>
    public string? OriginalPath { get; set; }
}

/// <summary>Item drzewa repozytorium Git (plik lub folder).</summary>
public class GitItem
{
    public string? Path { get; set; }
    public string? GitObjectType { get; set; }
    public bool IsFolder { get; set; }
    public string? ObjectId { get; set; }
    public string? Url { get; set; }

    /// <summary>Rozmiar pliku w bajtach (gdy znany; foldery: 0).</summary>
    public long? Size { get; set; }
}

/// <summary>
/// Pojedyncza zmiana plikowa wynikająca z porównania dwóch commitów
/// (Git diffs API). Używana do inkrementalnego skanu kodu w WikiDocGeneratorze.
/// </summary>
public class RepoFileChange
{
    public string? Path { get; set; }
    public string? OriginalPath { get; set; }

    /// <summary>add / edit / delete / rename / sourceRename — wartości z Azure DevOps Git API.</summary>
    public string? ChangeType { get; set; }
}

/// <summary>Wynik operacji zapisu/aktualizacji strony wiki.</summary>
public class WikiPageWriteResult
{
    /// <summary>Ścieżka strony w wiki (np. <c>/Architecture/Module</c>).</summary>
    public string? Path { get; set; }

    /// <summary>Nowy ETag strony (do incremental sync / kolejnych zapisów).</summary>
    public string? ETag { get; set; }

    /// <summary>URL do strony w portalu DevOps.</summary>
    public string? RemoteUrl { get; set; }

    /// <summary>True jeśli strona została utworzona, false jeśli zaktualizowana.</summary>
    public bool Created { get; set; }
}
