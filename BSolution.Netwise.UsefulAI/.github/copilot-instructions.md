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
| Language | **C# / .NET 10** |
| Agent Framework | **Microsoft.Agents.AI.Foundry** (prerelease NuGet) |
| LLM + Embeddings | **Azure OpenAI** — GPT-4o + text-embedding-3-large |
| Vector Store | **Azure AI Search** — hybrid search (vector + BM25 + semantic reranker) |
| Webhook Trigger | **Azure Function** — HTTP trigger from Azure DevOps Webhook |
| Indexing Triggers | **Azure Function** — Timer triggers (work items every 15 min, WIKI every 1 h) |
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

## 🔄 Indexing Pipeline (background sync)

```
[WorkItemIndexerFunction]  Timer: 0 */15 * * * *  (every 15 min)
          ↓ first run  → WorkItemIndexer.RunFullSyncAsync()
          ↓ next runs  → WorkItemIndexer.RunIncrementalSyncAsync(since: lastRun)
   [WorkItemIndexer]
          ↓ WIQL query → batch GET (200 IDs/req) → StripHtml → chunk (max 8 000 chars)
          ↓ parallel embed (SemaphoreSlim(4)) → MergeOrUpload (500 docs/batch)
   Azure AI Search: work-items-index ✅

[WikiIndexerFunction]  Timer: 0 0 * * * *  (every 1 hour)
          ↓ WikiIndexer.RunSyncAsync()
   [WikiIndexer]
          ↓ GetWikiList → GetWikiPagePaths (recursionLevel=full, single request)
          ↓ parallel page fetch → chunk Markdown (max 6 000 chars) → embed → upsert
   Azure AI Search: wiki-pages-index ✅

[SearchIndexManager]  IHostedService — runs automatically on Function App startup
          ↓ CreateOrUpdateIndex: work-items-index + wiki-pages-index
          ↓ HNSW (cosine, M=4, efConstruction=400, efSearch=500), 3072 vector dims
          ↓ SemanticConfiguration: devops-semantic-config (on both indexes)
```

---

## 📁 Solution Structure

```
BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/
│
├── Program.cs                                ✅ DONE — Function host bootstrap + DI
├── ImpactAnalysisPipeline.cs                 ✅ DONE — orchestrates all 4 agents
├── AgentPrompts.cs                           ✅ DONE — system prompts for all agents
│
├── Functions/
│   ├── WorkItemWebhookFunction.cs            ✅ DONE — HTTP trigger (DevOps webhook)
│   ├── WorkItemIndexerFunction.cs            ✅ DONE — Timer trigger (*/15 min), full/incremental
│   └── WikiIndexerFunction.cs                ✅ DONE — Timer trigger (every 1 h), full sync
│
├── Indexing/
│   ├── WorkItemIndexer.cs                    ✅ DONE — WIQL → batch fetch → chunk → embed → upload
│   ├── WikiIndexer.cs                        ✅ DONE — discover paths → chunk MD → embed → upload
│   └── SearchIndexManager.cs                ✅ DONE — IHostedService, creates both AI Search indexes
│
├── Tools/
│   ├── AgentToolAttribute.cs                 ✅ DONE — [AgentTool(Description = "...")] attribute
│   ├── Research/
│   │   ├── SearchWorkItemsTool.cs            ✅ DONE
│   │   ├── SearchWikiTool.cs                 ✅ DONE
│   │   ├── GetWorkItemDetailsTool.cs         ✅ DONE
│   │   └── ResearchTools.cs                 ✅ DONE — aggregates research tools
│   ├── Sender/
│   │   ├── PostCommentTool.cs                ✅ DONE
│   │   └── SenderTools.cs                   ✅ DONE — aggregates sender tools
│   └── Shared/
│       ├── EmbeddingService.cs               ✅ DONE — text-embedding-3-large via AIProjectClient
│       ├── AzureSearchService.cs             ✅ DONE — hybrid search wrapper
│       └── AzureDevOpsService.cs             ✅ DONE — DevOps REST API v7.1 wrapper
│
├── Models/
│   ├── SearchModels.cs                       ✅ DONE — SearchResultItem
│   └── DevOpsModels.cs                      ✅ DONE — WorkItemDetail, WikiPageDetail, WikiInfo
│
├── Configs/
│   └── ToolsConfig.cs                       ✅ DONE — DI (AddImpactAnalyzerTools)
│
├── appsettings.json                          ✅ DONE — config skeleton (secrets via user-secrets / Key Vault)
├── local.settings.json                       ⚠️  PARTIAL — only AzureWebJobsStorage + FUNCTIONS_WORKER_RUNTIME
│
└── infra/
    └── main.bicep                            ❌ TODO
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
    List<RelatedWorkItem> RelatedWorkItems,   // PotentialRelationType: CONFLICT|DEPENDENCY|RELATED
    List<RelatedWikiPage> RelatedWikiPages,
    List<string> SearchQueriesUsed
);

// Editor decision after reviewing Writer's report
record EditorDecision(bool IsApproved, string? Feedback);

// Azure DevOps Webhook payload trigger
record WorkItemEvent(int Id, string Type, string Title, string Description,
                     string AcceptanceCriteria, string AreaPath, string Tags);

// DevOps REST API models (DevOpsModels.cs)
// WorkItemDetail  — Id, Title, Type, State, Description, AcceptanceCriteria,
//                   AreaPath, IterationPath, Tags, Priority, AssignedTo,
//                   CreatedDate, ChangedDate, Relations, Url
// WikiPageDetail  — Id, Path, Content, RemoteUrl, GitItemPath, ETag
// WikiInfo        — Id, Name, RemoteUrl
```

---

## ✅ What Is Already Implemented

### Core Pipeline
1. **Full multi-agent pipeline** (`ImpactAnalysisPipeline.cs`) — Researcher → Writer → Editor (max 2 retries) → Sender
2. **Agent system prompts** (`AgentPrompts.cs`) — defined for all 4 agents
3. **ResearchTools** — `SearchWorkItemsTool`, `SearchWikiTool`, `GetWorkItemDetailsTool`
4. **SenderTools** — `PostCommentTool` (posts markdown comment to DevOps work item)
5. **Azure Function webhook handler** (`WorkItemWebhookFunction.cs`) — HTTP trigger, filters by item type, runs pipeline async

### Shared Services
6. **EmbeddingService** — generates `float[]` vectors using text-embedding-3-large (via `AIProjectClient`)
7. **AzureSearchService** — hybrid search: vector (HNSW) + BM25 keyword + semantic reranker, score threshold filtering
8. **AzureDevOpsService** — full DevOps REST API v7.1 wrapper:
   - GET work item (with relations + HTML strip), batch GET (200 IDs/req), WIQL query
   - POST comment, GET wiki page (with ETag), GET wiki list, recursive page path discovery

### Indexing
9. **WorkItemIndexer** (`Indexing/WorkItemIndexer.cs`) — full sync + incremental sync:
   - WIQL query → batch fetch (200 IDs/req) → `StripHtml` → chunk (max 8 000 chars, word boundary)
   - Parallel embedding (semaphore, 4 concurrent) → `MergeOrUpload` to Azure Search (500 docs/batch)
   - Chunk 0 ID = `"{workItemId}"`, subsequent chunks = `"{workItemId}-{n}"`
10. **WikiIndexer** (`Indexing/WikiIndexer.cs`) — full wiki sync:
    - Single `recursionLevel=full` call to discover all page paths
    - Parallel page fetch + embed (semaphore, 4 concurrent) → chunk Markdown (max 6 000 chars) → upload
11. **SearchIndexManager** (`Indexing/SearchIndexManager.cs`) — `IHostedService`, runs on startup:
    - Creates/updates `work-items-index` and `wiki-pages-index` via SDK
    - HNSW algorithm (cosine, M=4, efConstruction=400, efSearch=500), 3072 vector dims
    - Semantic configuration `devops-semantic-config` on both indexes
12. **WorkItemIndexerFunction** — Timer `0 */15 * * * *`, detects first run → full sync, subsequent → incremental since `ScheduleStatus.Last`
13. **WikiIndexerFunction** — Timer `0 0 * * * *` (every hour), always full sync

### Infrastructure & Config
14. **DI registration** (`Configs/ToolsConfig.cs`) — `AddImpactAnalyzerTools()` registers all services, indexers, tools, pipeline and `SearchIndexManager` as hosted service
15. **`AgentToolAttribute`** — `[AgentTool(Description = "...")]` — LLM uses description to decide when to call each tool
16. **`local.settings.json`** — base setup for local Azure Functions run (Azurite storage + dotnet-isolated runtime)

---

## 🔜 Next Steps (TODO — implement in this order)

### 1. `infra/main.bicep` — Azure Infrastructure as Code
**Priority: HIGH — required for production deployment**

Resources to provision:
- Azure AI Foundry project
- Azure OpenAI (GPT-4o deployment + text-embedding-3-large deployment)
- Azure AI Search (**Standard tier** — required for semantic search + vector search)
- Azure Function App (.NET 10, Consumption plan)
- Azure Key Vault (for all secrets)
- Managed Identity + RBAC role assignments:
  - `Cognitive Services OpenAI User` → Function App on OpenAI
  - `Search Index Data Contributor` → Function App on AI Search
  - `Key Vault Secrets User` → Function App on Key Vault

### 2. Integration Tests
**Priority: MEDIUM**

- `AzureDevOpsService` — test against a real DevOps project (use dedicated test work items)
- `AzureSearchService` — test hybrid search with sample indexed documents
- `WorkItemIndexer` / `WikiIndexer` — test full sync end-to-end with controlled DevOps data
- Full pipeline test — mock `WorkItemEvent` → verify comment posted on work item

### 3. Local Development Setup — complete
**Priority: LOW — quality of life**

- `docker-compose.yml` with Azurite (local Azure Storage emulator for Function triggers)
- Populate `local.settings.json` with all required keys (AzureSearch, AzureDevOps, Foundry, AzureOpenAI)
- Seed script to populate AI Search indexes with sample data for local pipeline testing

---

## 💡 Coding Guidelines for This Project

- All tools must have `[AgentTool(Description = "...")]` attribute with a detailed description
  — the LLM uses these descriptions to decide when to call each tool
- Tool methods must be `async Task<string>` and return **JSON strings**
  — agents communicate via JSON between pipeline steps
- Use `IConfiguration` for all settings — never hardcode endpoints or keys
- Register everything via DI in `ToolsConfig.AddImpactAnalyzerTools()` (`Configs/ToolsConfig.cs`)
- Follow existing naming: `{Name}Tool.cs` for tools, `{Name}Service.cs` for services, `{Name}Indexer.cs` for indexers
- Secrets: `dotnet user-secrets` locally, Key Vault references in Azure
- Chunking limits: max **8 000 chars** for work items (~2 000 tokens), max **6 000 chars** for WIKI (~1 500 tokens)
- Embedding parallelism: always throttle with `SemaphoreSlim(4)` to avoid Azure OpenAI rate limits
- Azure Search uploads: batch at max **500 documents** per `IndexDocumentsAsync` call