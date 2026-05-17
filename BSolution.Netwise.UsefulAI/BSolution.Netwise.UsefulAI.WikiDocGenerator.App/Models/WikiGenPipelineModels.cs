namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;

/// <summary>
/// Wejście do pipeline'u uruchamianego z webhooka po zakończonym (merged) PR.
/// Mapowane z Azure DevOps service hook payload — patrz <c>PullRequestWebhookFunction</c>.
/// </summary>
public record PullRequestEvent(
    string RepositoryId,
    string RepositoryName,
    int PullRequestId,
    string Title,
    string? Description,
    string SourceBranch,
    string TargetBranch,
    string? MergeCommitId,
    string? Author,
    IReadOnlyList<int> LinkedWorkItemIds
);

/// <summary>
/// Wejście do pipeline'u uruchamianego ręcznie — bierze listę work itemów
/// (Feature / User Story / PBI) i AKTUALIZUJE istniejące strony wiki lub
/// TWORZY nowe, jeśli żadna istniejąca strona tematycznie nie pasuje.
/// Decyzja "update vs create" należy do Researchera, nie do wywołującego.
/// </summary>
public record WorkItemsWikiRefreshRequest(
    IReadOnlyList<int> WorkItemIds,
    string? RepositoryId
);

/// <summary>Strukturalny output Researchera dla generatora wiki.</summary>
public record WikiResearchFindings(
    string Scope,
    IReadOnlyList<ChangedArtifact> ChangedArtifacts,
    IReadOnlyList<RelatedWorkItemRef> RelatedWorkItems,
    IReadOnlyList<ExistingWikiPageRef> ExistingPagesToUpdate,
    IReadOnlyList<string> SuggestedNewPagePaths,
    IReadOnlyList<string> KeyConceptsCovered,
    IReadOnlyList<string> SearchQueriesUsed
);

public record ChangedArtifact(
    string Path,
    string ChangeType,
    string? Summary
);

public record RelatedWorkItemRef(
    int Id,
    string Type,
    string Title,
    string Url,
    string Relevance
);

public record ExistingWikiPageRef(
    string Path,
    string? CurrentETag,
    string Reason
);

/// <summary>Pojedyncza decyzja Writera o utworzeniu lub aktualizacji strony.</summary>
public record WikiPageEdit(
    string Path,
    string MarkdownContent,
    string? ExistingETag,
    string Rationale
);

/// <summary>Output Writera — paczka edycji do wykonania.</summary>
public record WikiDraft(
    IReadOnlyList<WikiPageEdit> Edits,
    string Summary
);

/// <summary>Decyzja Editora — wspólny kształt z Impact Analyzerem, ale celowo lokalna kopia
/// żeby projekty App były od siebie niezależne.</summary>
public record EditorDecision(
    bool IsApproved,
    string? Feedback
);
