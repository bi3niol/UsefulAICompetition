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
| Indexing Backbone | **Azure Service Bus** (Standard) — staged indexing pipelines for WI + WIKI |
| Indexing Triggers | **Azure Function** — Timer + ServiceBus triggers |
| DevOps Integration | **Azure DevOps REST API v7.1** — work items + WIKI + comments |
| Auth | **Azure Identity** (DefaultAzureCredential, keyless) + PAT for DevOps |
| IaC | **Bicep** — modular (`main.bicep` + `modules/*`) |

### Key NuGet packages

```xml
<PackageReference Include="Microsoft.Agents.AI.Foundry" Version="*-*" />
<PackageReference Include="Azure.AI.Projects" Version="1.*" />
<PackageReference Include="Azure.Search.Documents" Version="11.*" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />
<PackageReference Include="Azure.Identity" Version="1.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" Version="5.*" />
```

---

## 🤖 Multi-Agent Pipeline (analysis path)

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

## 🔄 Indexing Pipelines (Service Bus, staged)

Both indexers were refactored from monolithic timer-triggered functions into **4-stage
Service Bus pipelines**. This isolates DevOps API calls and Azure OpenAI embedding calls
into separately throttleable stages, prevents API rate-limit exhaustion, and gives each
message its own retry/dead-letter lifecycle.

### Work Item indexing pipeline

```
[WorkItemIndexerFunction]   Timer  0 */15 * * * *  (every 15 min, RunOnStartup)
          ↓ first run  → IWorkItemQueryService.QueryAllIdsAsync()
          ↓ next runs  → IWorkItemQueryService.QueryChangedIdsSinceAsync(ScheduleStatus.Last)
          ↓ batches of 200 IDs → enqueue WorkItemIdsBatchMessage
   Service Bus queue: workitem-ids
          ↓
[WorkItemFetchFunction]     ServiceBusTrigger("workitem-ids")
          ↓ batch GET work items (200/req) via AzureDevOpsService → strip HTML
          ↓ emit one WorkItemDetailMessage per work item
   Service Bus queue: workitem-details
          ↓
[WorkItemBuildDocumentsFunction]  ServiceBusTrigger("workitem-details")
          ↓ IWorkItemDocumentBuilder.BuildAsync(detail)
          ↓ chunk (max 8 000 chars, word boundary) + embedding
          ↓ emit WorkItemIndexDocumentsMessage (typed DTO list)
   Service Bus queue: workitem-documents
          ↓
[WorkItemUploadFunction]    ServiceBusTrigger("workitem-documents")
          ↓ IWorkItemSearchUploader.UploadAsync() → MergeOrUpload (≤500/batch)
   Azure AI Search: work-items-index ✅
```

### WIKI indexing pipeline (analogous)

```
[WikiIndexerFunction]       Timer  0 0 0 * * *  (daily, RunOnStartup)
          ↓ IWikiPageQueryService.QueryAllPageRefsAsync()
          ↓ enumerate wikis + recursive page paths (recursionLevel=full)
          ↓ enqueue one WikiPageRefMessage per page
   Service Bus queue: wiki-page-refs
          ↓
[WikiPageFetchFunction]     ServiceBusTrigger("wiki-page-refs")
          ↓ AzureDevOpsService.GetWikiPageAsync(...) → WikiPageContentMessage
          ↓ returns null on missing/broken pages (no retry storm)
   Service Bus queue: wiki-pages
          ↓
[WikiBuildDocumentsFunction]  ServiceBusTrigger("wiki-pages")
          ↓ IWikiDocumentBuilder.BuildAsync(wikiId, page)
          ↓ Markdown chunking (H1/H2 boundaries, max 6 000 chars) + embedding
          ↓ emit WikiIndexDocumentsMessage
   Service Bus queue: wiki-documents
          ↓
[WikiUploadFunction]        ServiceBusTrigger("wiki-documents")
          ↓ IWikiSearchUploader.UploadAsync() → upsert
   Azure AI Search: wiki-pages-index ✅
```

### Throttling & reliability

- **Concurrency** is capped via `host.json` (NOT via `SemaphoreSlim`):
  `extensions.serviceBus.maxConcurrentCalls = 4`, `prefetchCount = 8`.
- **Lock duration** 5 min, **maxDeliveryCount** 5, **dead-lettering on expiration** enabled.
- **Messages are typed POCO DTOs** (`Indexing/Messages/*.cs`) serialized to JSON —
  this avoids Azure SDK `SearchDocument` deserialization issues with `float[]` vectors.
- **Auth is keyless** — `ServiceBus__fullyQualifiedNamespace` app setting + Managed Identity
  with `Azure Service Bus Data Sender` and `Azure Service Bus Data Receiver` roles.

### Search index bootstrap

```
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
├── Program.cs                                ✅ Function host bootstrap + DI
├── ImpactAnalysisPipeline.cs                 ✅ Orchestrates all 4 agents
├── AgentPrompts.cs                           ✅ System prompts for all agents
├── host.json                                 ✅ Service Bus throttling
│
├── Functions/
│   ├── WorkItemWebhookFunction.cs            ✅ HTTP trigger (DevOps webhook)
│   ├── AnalyzeWorkItemFunction.cs            ✅ Background analysis runner
│   │
│   ├── WorkItemIndexerFunction.cs            ✅ Stage 1/4 Timer → workitem-ids
│   ├── WorkItemFetchFunction.cs              ✅ Stage 2/4 SB → workitem-details
│   ├── WorkItemBuildDocumentsFunction.cs     ✅ Stage 3/4 SB → workitem-documents
│   ├── WorkItemUploadFunction.cs             ✅ Stage 4/4 SB → AI Search
│   │
│   ├── WikiIndexerFunction.cs                ✅ Stage 1/4 Timer → wiki-page-refs
│   ├── WikiPageFetchFunction.cs              ✅ Stage 2/4 SB → wiki-pages
│   ├── WikiBuildDocumentsFunction.cs         ✅ Stage 3/4 SB → wiki-documents
│   └── WikiUploadFunction.cs                 ✅ Stage 4/4 SB → AI Search
│
├── Indexing/
│   ├── SearchIndexManager.cs                 ✅ IHostedService — creates both AI Search indexes
│   │
│   ├── WorkItemIndexDocument.cs              ✅ Typed DTO for AI Search upload
│   ├── WorkItemQueryService.cs               ✅ WIQL + change detection
│   ├── WorkItemDocumentBuilder.cs            ✅ Chunk + embed work items
│   ├── WorkItemSearchUploader.cs             ✅ MergeOrUpload to work-items-index
│   │
│   ├── WikiIndexDocument.cs                  ✅ Typed DTO for AI Search upload
│   ├── WikiPageQueryService.cs               ✅ Enumerate wikis + page paths
│   ├── WikiDocumentBuilder.cs                ✅ Chunk Markdown + embed
│   ├── WikiSearchUploader.cs                 ✅ Upsert to wiki-pages-index
│   │
│   └── Messages/
│       ├── WorkItemIdsBatchMessage.cs        ✅ workitem-ids payload
│       ├── WorkItemDetailMessage.cs          ✅ workitem-details payload
│       ├── WorkItemIndexDocumentsMessage.cs  ✅ workitem-documents payload
│       ├── WikiPageRefMessage.cs             ✅ wiki-page-refs payload
│       ├── WikiPageContentMessage.cs         ✅ wiki-pages payload
│       └── WikiIndexDocumentsMessage.cs      ✅ wiki-documents payload
│
├── Tools/
│   ├── AgentToolAttribute.cs                 ✅ [AgentTool(Description = "...")]
│   ├── Research/
│   │   ├── SearchWorkItemsTool.cs            ✅
│   │   ├── SearchWikiTool.cs                 ✅
│   │   ├── GetWorkItemDetailsTool.cs         ✅
│   │   ├── GetWikiPageDetailsTool.cs         ✅
│   │   └── ResearchTools.cs                  ✅
│   ├── Sender/
│   │   ├── PostCommentTool.cs                ✅
│   │   └── SenderTools.cs                    ✅
│   └── Shared/
│       ├── EmbeddingService.cs               ✅ text-embedding-3-large via AIProjectClient
│       ├── AzureSearchService.cs             ✅ Hybrid search wrapper
│       └── AzureDevOpsService.cs             ✅ DevOps REST API v7.1 wrapper
│
├── Models/
│   ├── SearchModels.cs                       ✅ SearchResultItem
│   └── DevOpsModels.cs                       ✅ WorkItemDetail, WikiPageDetail, WikiInfo
│
├── Configs/
│   └── ToolsConfig.cs                        ✅ DI (AddImpactAnalyzerTools)
│
├── appsettings.json                          ✅ Config skeleton
├── local.settings.json                       ⚠️ PARTIAL
│
└── infra/
    ├── main.bicep                            ✅ Orchestrator
    └── modules/
        ├── functionapp.bicep                 ✅ Function App + MI + app settings
        ├── servicebus.bicep                  ✅ Namespace + 6 queues + RBAC
        └── ai.bicep                          ✅ AI Foundry + OpenAI + AI Search
```

---

## ⚙️ Configuration

### `appsettings.json` (logical settings)

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-openai.openai.azure.com/",
    "EmbeddingDeployment": "text-embedding-3-large"
  },
  "AzureSearch": {
    "Endpoint": "https://your-search.search.windows.net"
  },
  "AzureDevOps": {
    "Organization": "your-org",
    "Project": "your-project",
    "PersonalAccessToken": "-- use secrets / Key Vault --"
  },
  "Foundry": {
    "Endpoint": "https://your-foundry.services.ai.azure.com/api/projects/your-project"
  }
}
```

### Service Bus (keyless via Managed Identity)

```
ServiceBus__fullyQualifiedNamespace = <namespace>.servicebus.windows.net
```

Set on the Function App by `infra/modules/functionapp.bicep`. The
`Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` extension uses this prefix to
auto-resolve via `DefaultAzureCredential`.

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

// DevOps REST API models (Models/DevOpsModels.cs)
// WorkItemDetail  — Id, Title, Type, State, Description, AcceptanceCriteria,
//                   AreaPath, IterationPath, Tags, Priority, AssignedTo,
//                   CreatedDate, ChangedDate, Relations, Url
// WikiPageDetail  — Id, Path, Content, RemoteUrl, GitItemPath, ETag
// WikiInfo        — Id, Name, RemoteUrl

// Service Bus message DTOs (Indexing/Messages/*.cs) — typed POCOs serialized to JSON
// WorkItemIdsBatchMessage         { int[] Ids }
// WorkItemDetailMessage           { WorkItemDetail Detail }
// WorkItemIndexDocumentsMessage   { List<WorkItemIndexDocument> Documents }
// WikiPageRefMessage              { string WikiId, string WikiName, string Path }
// WikiPageContentMessage          { string WikiId, string WikiName, WikiPageDetail Page }
// WikiIndexDocumentsMessage       { List<WikiIndexDocument> Documents }

// AI Search typed upload DTOs (Indexing/*IndexDocument.cs) — replace SearchDocument
// to keep float[] embedding vectors round-tripping cleanly through SB JSON.
```

---

## ✅ What Is Already Implemented

### Core Pipeline
1. **Multi-agent pipeline** (`ImpactAnalysisPipeline.cs`) — Researcher → Writer → Editor (max 2 retries) → Sender
2. **Agent system prompts** (`AgentPrompts.cs`) — defined for all 4 agents
3. **ResearchTools** — `SearchWorkItemsTool`, `SearchWikiTool`, `GetWorkItemDetailsTool`, `GetWikiPageDetailsTool`
4. **SenderTools** — `PostCommentTool`
5. **Webhook handler** (`WorkItemWebhookFunction.cs`) + **background runner** (`AnalyzeWorkItemFunction.cs`)

### Shared Services
6. **EmbeddingService** — `float[]` vectors via text-embedding-3-large (AIProjectClient)
7. **AzureSearchService** — hybrid search (vector HNSW + BM25 + semantic reranker, score threshold)
8. **AzureDevOpsService** — DevOps REST API v7.1 wrapper (work items batch GET, WIQL, comments, wiki list/pages with ETag)

### Indexing — Service Bus pipelines
9. **Work Item pipeline** — 3 services (`WorkItemQueryService`, `WorkItemDocumentBuilder`, `WorkItemSearchUploader`) + 4 functions across 3 SB queues (`workitem-ids`, `workitem-details`, `workitem-documents`)
10. **WIKI pipeline** — 3 services (`WikiPageQueryService`, `WikiDocumentBuilder`, `WikiSearchUploader`) + 4 functions across 3 SB queues (`wiki-page-refs`, `wiki-pages`, `wiki-documents`)
11. **Typed message DTOs** under `Indexing/Messages/` — avoid `SearchDocument`/`float[]` JSON pitfalls
12. **SearchIndexManager** (`IHostedService`) — creates `work-items-index` + `wiki-pages-index` on startup (HNSW cosine, 3072 dims, semantic config `devops-semantic-config`)
13. **`host.json`** — `extensions.serviceBus.maxConcurrentCalls = 4`, `prefetchCount = 8` (replaces previous `SemaphoreSlim` throttling)

### Infrastructure & Config
14. **DI registration** (`Configs/ToolsConfig.cs`) — `AddImpactAnalyzerTools()` registers all services, indexer stages, tools, pipeline and `SearchIndexManager`
15. **`AgentToolAttribute`** — `[AgentTool(Description = "...")]`
16. **Bicep IaC** (`infra/main.bicep` + `modules/functionapp.bicep`, `servicebus.bicep`, `ai.bicep`) — full deployment incl. namespace, 6 queues, Managed Identity, RBAC, `ServiceBus__fullyQualifiedNamespace` app setting
17. **Windows MAX_PATH workaround** in csproj — `ExtensionsCsProjDirectory=$(LOCALAPPDATA)\AzFuncWorkerExt\$(MSBuildProjectName)\$(Configuration)\$(TargetFramework)` (Windows-only). The Functions Worker SDK auto-generates `obj\<Cfg>\<TFM>\WorkerExtensions\WorkerExtensions.csproj` and **explicitly disables `Directory.Build.props/targets`** for it, so this MSBuild prop is the only supported escape hatch when restored extension paths exceed 260 chars.

---

## 🔜 Next Steps

### Integration Tests (MEDIUM priority)
- `AzureDevOpsService` — test against a real DevOps project
- `AzureSearchService` — hybrid search with sample documents
- End-to-end Service Bus pipeline tests (publish to first queue, assert AI Search contents)
- Full agent pipeline test — mock `WorkItemEvent` → assert comment posted

### Local Development (LOW priority)
- `docker-compose.yml` with Azurite (Functions runtime requires it)
- Populate `local.settings.json` with all connection settings
- Seed script for AI Search indexes for offline pipeline testing

---

## 💡 Coding Guidelines for This Project

### Agents & tools
- All agent tools must have `[AgentTool(Description = "...")]` with a detailed description — the LLM relies on it to decide when to call.
- Tool methods are `async Task<string>` returning **JSON strings** — agents communicate via JSON.
- Use `IConfiguration` for all settings — never hardcode endpoints or keys.
- Register everything via DI in `ToolsConfig.AddImpactAnalyzerTools()`.

### Indexing pipelines
- **Each stage = one Function class.** Do NOT bundle stages or call the next stage directly — always return a message via `[ServiceBusOutput("queue-name")]`.
- **Messages are typed POCO DTOs** under `Indexing/Messages/`. Never put `SearchDocument` on the wire — use the matching `*IndexDocument` DTO so `float[]` vectors survive JSON round-tripping.
- **Throttle via `host.json`**, not `SemaphoreSlim`. The Service Bus extension's `maxConcurrentCalls` controls per-instance parallelism per function.
- Stage 2 (fetch) functions should **return `null` on missing/broken upstream data** — this prevents poison-message retry storms; dead-lettering still catches genuine failures via `maxDeliveryCount = 5`.
- Naming convention for indexer services: `I{Domain}QueryService`, `I{Domain}DocumentBuilder`, `I{Domain}SearchUploader`.

### Sizing
- Chunking limits: max **8 000 chars** for work items (~2 000 tokens), max **6 000 chars** for WIKI (~1 500 tokens).
- Azure Search uploads: batch at max **500 documents** per `IndexDocumentsAsync` call.
- Work item ID batches into the pipeline: **200 IDs per `WorkItemIdsBatchMessage`** (matches DevOps batch GET limit).

### Auth & secrets
- **Keyless everywhere possible** — `DefaultAzureCredential` for OpenAI / AI Search / Service Bus / Foundry.
- DevOps PAT is the only key-based secret — store in Key Vault, reference via app setting.
- `dotnet user-secrets` locally; **Key Vault** in Azure.
