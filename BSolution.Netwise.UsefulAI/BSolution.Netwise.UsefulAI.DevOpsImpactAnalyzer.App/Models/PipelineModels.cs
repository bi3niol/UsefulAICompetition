using System;
using System.Collections.Generic;
using System.Text;

namespace BSolution.Netwise.UsefulAI.Core.Models;

public record WorkItemEvent(
    int Id,
    string Type,
    string Title,
    string Description,
    string AcceptanceCriteria,
    string AreaPath,
    string Tags
);

/// <summary>
/// Referencja do analizowanego work itemu — czêœæ strukturalnego outputu Researchera.
/// </summary>
public record WorkItemRef(
    int Id,
    string Type,
    string Title,
    string State,
    string AreaPath,
    string Url
);

public record ResearchFindings(
    WorkItemRef AnalyzedItem,
    List<RelatedWorkItem> RelatedWorkItems,
    List<RelatedWikiPage> RelatedWikiPages,
    List<string> SearchQueriesUsed
);

public record RelatedWorkItem(
    int Id,
    string Title,
    string Type,
    string State,
    double Similarity,
    string Url,
    string PotentialRelationType,  // "CONFLICT" | "DEPENDENCY" | "RELATED"
    string Reason
);

public record RelatedWikiPage(
    string Title,
    string Path,
    string Url,
    string Relevance
);

public record EditorDecision(
    bool IsApproved,
    string? Feedback
);
