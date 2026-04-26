using System;
using System.Collections.Generic;
using System.Text;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;

public class WorkItemDetail
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? AreaPath { get; set; }
    public string? IterationPath { get; set; }
    public string? Tags { get; set; }
    public int? Priority { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ChangedDate { get; set; }
    public List<WorkItemRelation> Relations { get; set; } = [];
    public List<WorkItemComment> Comments { get; set; } = [];
    public string? Url { get; set; }
}

public class WorkItemRelation
{
    public string? RelationType { get; set; }
    public string? Url { get; set; }
    public int? RelatedId { get; set; }
}

public class WorkItemComment
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

public class WikiPageDetail
{
    public string? Id { get; set; }
    public string? Path { get; set; }
    public string? Content { get; set; }
    public string? RemoteUrl { get; set; }
    public string? GitItemPath { get; set; }

    /// <summary>ETag z nagłówka HTTP — używany do wykrywania zmian podczas incremental sync.</summary>
    public string? ETag { get; set; }
}

/// <summary>Metadane wiki z Azure DevOps (wynik z endpoint /wiki/wikis).</summary>
public class WikiInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? RemoteUrl { get; set; }
}

/// <summary>
/// Wynik z DevOps Work Item Search API (Lucene/BM25 keyword search).
/// Endpoint: POST https://almsearch.dev.azure.com/{org}/{project}/_apis/search/workitemsearchresults
/// </summary>
public class WorkItemSearchHit
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public string? AssignedTo { get; set; }
    public string? AreaPath { get; set; }
    public string? Tags { get; set; }

    /// <summary>
    /// Snippety z dopasowaniami per pole (klucz = nazwa pola, wartość = fragmenty z tagami
    /// &lt;highlighthit&gt;...&lt;/highlighthit&gt; wokół znalezionych słów).
    /// </summary>
    public Dictionary<string, List<string>> Highlights { get; set; } = [];

    public string? Url { get; set; }
}