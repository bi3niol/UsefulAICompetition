# DevOps Impact Analyzer — Project Context for GitHub Copilot

## 🎯 Project Goal

Build a **multi-agent AI system** that automatically analyzes newly created Azure DevOps 
work items (User Stories, PBIs, Epics, Features) and produces an **impact analysis report** 
posted as a comment directly on the work item.

The agent detects:
- ⚠️ **Conflicts** — existing requirements that contradict the new work item
- 🔗 **Dependencies** — related items that are affected by or linked to the new requirement
- 📚 **WIKI references** — architecture decisions, ADRs, technical docs relevant to the change

---

## 🧱 Technology Stack

| Layer | Technology |
|---|---|
| Language | **C# / .NET 9** |
| Agent Framework | **Microsoft.Agents.AI.Foundry** (prerelease NuGet) |
| LLM + Embeddings | **Azure OpenAI** — GPT-4o + text-embedding-3-large |
| Vector Store | **Azure AI Search** — hybrid search (vector + BM25 + semantic reranker) |
| Trigger | **Azure Function** — HTTP trigger from Azure DevOps Webhook |
| DevOps Integration | **Azure DevOps REST API v7.1** — work items + WIKI + comments |
| Auth | **Azure Identity** (DefaultAzureCredential) + PAT for DevOps |

### Key NuGet packages
```xml
<PackageReference Include="Microsoft.Agents.AI.Foundry" Version="*-*" />
<PackageReference Include="Azure.AI.Projects" Version="1.*" />
<PackageReference Include="Azure.Search.Documents" Version="11.*" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />
<PackageReference Include="Azure.Identity" Version="1.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.*" />
```

---

## 🤖 Multi-Agent Pipeline

The system uses a **4-agent pipeline** pattern: `Researcher → Writer → Editor → Sender`

```
Azure DevOps Webhook (new Work Item created)
          ↓
   [ORCHESTRATOR]  — ImpactAnalysisPipeline.cs
          ↓
   [RESEARCHER]    — searches DevOps items + WIKI (has search tools)
          ↓ ResearchFindings (JSON)
   [WRITER]        — produces structured markdown report (no tools)
          ↓ draft report
   [EDITOR]        — reviews report quality, approves or sends feedback (no tools)
          ↓ approved report (max 2 retries back to Writer)
   [SENDER]        — posts comment to DevOps work item (has post tool)
          ↓
   Comment posted on Work Item ✅
```

Each agent is created via:
```csharp
AIAgent agent = new AIProjectClient(new Uri(foundryEndpoint), new DefaultAzureCredential())
    .AsAIAgent(model: "gpt-4o", instructions: "...", tools: [...]);
```

---

## 📁 Solution Structure

```
DevOpsImpactAnalyzer/
├── DevOpsImpactAnalyzer.sln
├── src/
│   ├── Agent/
│   │   ├── ImpactAnalysisPipeline.cs     ✅ DONE — orchestrates all 4 agents
│   │   └── Prompts/
│   │       └── AgentPrompts.cs           ✅ DONE — system prompts for all agents
│   ├── Tools/
│   │   ├── Research/
│   │   │   ├── SearchWorkItemsTool.cs    ✅ DONE
│   │   │   ├── SearchWikiTool.cs         ✅ DONE
│   │   │   ├── GetWorkItemDetailsTool.cs ✅ DONE
│   │   │   └── ResearchTools.cs          ✅ DONE — aggregates research tools
│   │   ├── Sender/
│   │   │   ├── PostCommentTool.cs        ✅ DONE
│   │   │   └── SenderTools.cs            ✅ DONE — aggregates sender tools
│   │   └── Shared/
│   │       ├── EmbeddingService.cs       ✅ DONE — text-embedding-3-large
│   │       ├── AzureSearchService.cs     ✅ DONE — hybrid search wrapper
│   │       └── AzureDevOpsService.cs     ✅ DONE — DevOps REST API wrapper
│   ├── Models/
│   │   ├── SearchModels.cs               ✅ DONE — SearchResultItem
│   │   └── DevOpsModels.cs               ✅ DONE — WorkItemDetail, WikiPageDetail
│   ├── Configuration/
│   │   └── ToolsConfiguration.cs         ✅ DONE — DI registration
│   └── Functions/
│       └── WorkItemWebhookFunction.cs    ✅ DONE — Azure Function HTTP trigger
├── appsettings.json                      ✅ DONE (secrets via user-secrets / Key Vault)
└── infra/
    └── main.bicep                        ❌ TODO
```

---

## ⚙️ Configuration (appsettings.json)

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-openai.openai.azure.com/",
    "ApiKey": "-- use secrets --",
    "EmbeddingDeployment": "text-embedding-3-large"
  },
  "AzureSearch": {
    "Endpoint": "https://your-search.search.windows.net",
    "ApiKey": "-- use secrets --"
  },
  "AzureDevOps": {
    "Organization": "your-org",
    "Project": "your-project",
    "PersonalAccessToken": "-- use secrets --"
  },
  "Foundry": {
    "Endpoint": "https://your-foundry.services.ai.azure.com/api/projects/your-project"
  }
}
```

> Use `dotnet user-secrets` locally. Use **Azure Key Vault** in production.

---

## 📋 Key Data Models

```csharp
// Output of Researcher agent (structured JSON)
record ResearchFindings(
    WorkItemRef AnalyzedItem,
    List<RelatedWorkItem> RelatedWorkItems,   // with PotentialRelationType: CONFLICT|DEPENDENCY|RELATED
    List<RelatedWikiPage> RelatedWikiPages,
    List<string> SearchQueriesUsed
);

// Editor decision after reviewing Writer's report
record EditorDecision(bool IsApproved, string? Feedback);

// Azure DevOps Webhook payload trigger
record WorkItemEvent(int Id, string Type, string Title, string Description,
                     string AcceptanceCriteria, string AreaPath, string Tags);
```

---

## ✅ What Is Already Implemented

1. **Full multi-agent pipeline** with orchestration, Writer/Editor retry loop (max 2x)
2. **ResearchTools** — `SearchWorkItems`, `SearchWiki`, `GetWorkItemDetails`
3. **SenderTools** — `PostCommentToWorkItem`
4. **EmbeddingService** — generates vectors using text-embedding-3-large
5. **AzureSearchService** — hybrid search (vector + BM25 + semantic reranker) on two indexes
6. **AzureDevOpsService** — GET work item (with relations), POST comment, GET wiki page
7. **Azure Function webhook handler** — filters by relevant item types, runs pipeline async
8. **DI registration** — `ToolsConfiguration.AddImpactAnalyzerTools()`
9. **Agent system prompts** — defined for all 4 agents (Researcher, Writer, Editor, Sender)

---

## 🔜 Next Steps (TODO — implement in this order)

### 1. `WorkItemIndexer.cs` — sync DevOps → Azure AI Search
**Priority: HIGH — without this, semantic search has no data**
- Fetch all work items from DevOps REST API using WIQL queries
- Chunk long descriptions/acceptance criteria (max ~2000 tokens per chunk)
- Generate embeddings via `IEmbeddingService`
- Upsert documents into `work-items-index` in Azure AI Search
- Support incremental sync (only changed items since last run, using `changedDate`)
- Should run as a scheduled Azure Function (timer trigger, e.g. every 15 minutes)

### 2. `WikiIndexer.cs` — sync WIKI → Azure AI Search  
**Priority: HIGH — needed for WIKI semantic search**
- Fetch all WIKI pages recursively via DevOps REST API (`/_apis/wiki/wikis/{id}/pages`)
- Parse and chunk Markdown content (by heading sections, max ~1500 tokens per chunk)
- Generate embeddings via `IEmbeddingService`
- Upsert documents into `wiki-pages-index` in Azure AI Search
- Support incremental sync (detect changed pages via `gitItemPath` / ETag)
- Run as scheduled Azure Function (timer trigger)

### 3. Azure AI Search Index Schemas (Bicep or SDK)
**Priority: HIGH — indexes must exist before indexers run**

Two indexes to create:
```
work-items-index:
  id (string, key), title (string, searchable), type (string, filterable),
  state (string, filterable), description (string, searchable),
  acceptanceCriteria (string, searchable), areaPath (string, filterable),
  tags (string, searchable), url (string), changedDate (DateTimeOffset, sortable),
  contentVector (Collection(Single), 3072 dims for text-embedding-3-large)

wiki-pages-index:
  id (string, key), title (string, searchable), path (string, filterable),
  wikiId (string, filterable), contentExcerpt (string, searchable),
  content (string, searchable), url (string),
  contentVector (Collection(Single), 3072 dims)
```
Both indexes need a **semantic configuration** named `devops-semantic-config`.

### 4. `main.bicep` — Azure Infrastructure as Code
**Priority: MEDIUM — needed for production deployment**
Resources to provision:
- Azure AI Foundry project
- Azure OpenAI (GPT-4o deployment + text-embedding-3-large deployment)
- Azure AI Search (Standard tier — required for semantic search)
- Azure Function App (.NET 9, Consumption plan)
- Azure Key Vault (for secrets)
- Managed Identity + RBAC role assignments

### 5. Integration Tests
**Priority: MEDIUM**
- Test `AzureDevOpsService` against a real DevOps project (use test work items)
- Test `AzureSearchService` with sample indexed documents
- Test full pipeline end-to-end with a mock work item event

### 6. Local Development Setup
**Priority: LOW — quality of life**
- `docker-compose.yml` with Azurite (local Azure Storage emulator)
- `local.settings.json` for Azure Functions local run
- Seed script to populate AI Search indexes with sample data for local testing

---

## 💡 Coding Guidelines for This Project

- All tools must have `[AgentTool(Description = "...")]` attribute with detailed description
  — the LLM uses these descriptions to decide when to call each tool
- Tool methods must be `async Task<string>` and return **JSON strings**
  — agents communicate via JSON between pipeline steps
- Use `IConfiguration` for all settings — never hardcode endpoints or keys
- Register everything via DI in `ToolsConfiguration.AddImpactAnalyzerTools()`
- Follow existing naming: `{Name}Tool.cs` for tools, `{Name}Service.cs` for shared services
- Secrets: `dotnet user-secrets` locally, Key Vault references in Azure