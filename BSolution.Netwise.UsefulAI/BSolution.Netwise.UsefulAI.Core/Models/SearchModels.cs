using System;
using System.Collections.Generic;
using System.Text;

namespace BSolution.Netwise.UsefulAI.Core.Models;

public class SearchResultItem
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? AreaPath { get; set; }
    public string? Tags { get; set; }
    public string? Path { get; set; }  // WIKI path
    public string? WikiId { get; set; }  // WIKI id
    public string? ContentExcerpt { get; set; }  // WIKI excerpt
    public string? Url { get; set; }
    public double Score { get; set; }
}