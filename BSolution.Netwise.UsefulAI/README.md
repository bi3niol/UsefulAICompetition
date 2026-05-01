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
| Indexing Backbone | **Azure Service Bus** (Standard) + **Azure Blob Storage** (Claim-Check) |
| Runtime Config Store | **Azure Tables** — generic key-value `Settings` table (e.g. last-sync timestamps) |
| Indexing Triggers | **Azure Function** — Timer + ServiceBus triggers |
| DevOps Integration | **Azure DevOps REST API v7.1** — work items (WIQL + native Work Item Search) + WIKI + comments |
| Auth | **Azure Identity** (DefaultAzureCredential, keyless) + PAT for DevOps |
| IaC | **Bicep** — modular (`main.bicep` + `modules/*`) |

### Key NuGet packages

```xml
<PackageReference Include="Microsoft.Agents.AI.Foundry" Version="*-*" />
<PackageReference Include="Azure.AI.Projects" Version="1.*" />
<PackageReference Include="Azure.Search.Documents" Version="11.*" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />
<PackageReference Include="Azure.Identity" Version="1.*" />
<PackageReference Include="Azure.Storage.Blobs" Version="12.*" />
<PackageReference Include="Azure.Data.Tables" Version="12.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.*" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" Version="5.*" />
```

---

## 🤖 Multi-Agent Pipeline (analysis path)

The system uses a **4-agent pipeline** pattern: `Researcher → Writer → Editor → Sender`

Analysis is exposed via two HTTP functions:

- `GenerateWorkItemReportFunction` — `POST /api/AnalyzeWorkItem/{workItemId}/generate`. Fetches the
  work item from Azure DevOps, runs the pipeline synchronously, persists the resulting markdown
  report to Blob Storage (`reports/{workItemId}.md`) via `IReportStore`, and returns it in the response.
- `GetWorkItemReportFunction` — `GET /api/AnalyzeWorkItem/{workItemId}`. Returns the previously
  generated report from Blob Storage; `404` if it has not been generated yet.

There is currently **no DevOps webhook function** — wiring up an external webhook (e.g. via API
Management or Logic App) that calls the generate endpoint is left to ops.

```
POST /api/AnalyzeWorkItem/{workItemId}/generate
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
          ↓
   IReportStore.SaveAsync → Blob: reports/{workItemId}.md
          ↓
   Markdown returned in HTTP response

GET  /api/AnalyzeWorkItem/{workItemId}
          ↓
   IReportStore.TryGetAsync → Blob: reports/{workItemId}.md
          ↓
   200 OK (markdown) / 404 (not generated yet)
```

Each agent is created via:

```csharp
AIAgent agent = new AIProjectClient(new Uri(foundryEndpoint), new DefaultAzureCredential())
    .AsAIAgent(model: "gpt-4o", instructions: "...", tools: [...]);
```

---

## 🔄 Indexing Pipelines (Service Bus + Blob Storage, staged)

Both indexers use **4-stage Service Bus pipelines** with the **Claim-Check Pattern**:
stages 2–4 store the full payload as a JSON blob in Azure Blob Storage and put only a thin
`BlobRefMessage { BlobUri }` (~100 B) on the queue. This completely removes the 256 KB
Service Bus Standard message size limit — blobs can be any size.

Stages 1 (`workitem-ids`, `wiki-page-refs`) carry small primitive messages and do NOT use Claim-Check.

### Work Item indexing pipeline

```
[WorkItemIndexerFunction]   Timer  0 */15 * * * *  (every 15 min, RunOnStartup)
          ↓ first run  → IWorkItemQueryService.QueryAllIdsAsync()
          ↓ next runs  → IWorkItemQueryService.QueryChangedIdsSinceAsync(ScheduleStatus.Last)
          ↓ batches of 200 IDs → enqueue WorkItemIdsBatchMessage  [plain, no Claim-Check]
   Service Bus queue: workitem-ids
          ↓
[WorkItemFetchFunction]     ServiceBusTrigger("workitem-ids")
          ↓ batch GET work items (200/req) + comments via AzureDevOpsService
          ↓ upload blob: workitem-details/{date}/{wiId}_{guid8}.json
          ↓ emit BlobRefMessage[]  (one per WI)
   Service Bus queue: workitem-details
          ↓
[WorkItemBuildDocumentsFunction]  ServiceBusTrigger("workitem-details")
          ↓ download WorkItemDetailMessage blob
          ↓ IWorkItemDocumentBuilder.BuildAsync → chunk (max 8 000 chars) + embed all chunks
          ↓ upload blob: workitem-documents/{date}/{wiId}_{guid8}.json  [List<WorkItemIndexDocument>]
          ↓ emit BlobRefMessage?  (null → no message sent)
   Service Bus queue: workitem-documents
          ↓
[WorkItemUploadFunction]    ServiceBusTrigger("workitem-documents")
          ↓ download List<WorkItemIndexDocument> blob
          ↓ IWorkItemSearchUploader.UploadAsync() → MergeOrUpload (≤500/batch)
   Azure AI Search: work-items-index ✅
```

### WIKI indexing pipeline (analogous)

```
[WikiIndexerFunction]       Timer  0 0 0 * * *  (daily, RunOnStartup)
          ↓ IWikiPageQueryService.QueryAllPageRefsAsync()
          ↓ enumerate wikis + recursive page paths (recursionLevel=full)
          ↓ enqueue WikiPageRefMessage  [plain, no Claim-Check]
   Service Bus queue: wiki-page-refs
          ↓
[WikiPageFetchFunction]     ServiceBusTrigger("wiki-page-refs")
          ↓ AzureDevOpsService.GetWikiPageAsync(...)
          ↓ upload blob: wiki-pages/{date}/{wikiId}-{pathSlug}_{guid8}.json
          ↓ emit BlobRefMessage?  (null on empty/broken page)
   Service Bus queue: wiki-pages
          ↓
[WikiBuildDocumentsFunction]  ServiceBusTrigger("wiki-pages")
          ↓ download WikiPageContentMessage blob
          ↓ IWikiDocumentBuilder.BuildAsync → chunk Markdown (H1/H2, max 6 000 chars) + embed
          ↓ upload blob: wiki-documents/{date}/{wikiId}-{pathSlug}_{guid8}.json  [List<WikiIndexDocument>]
          ↓ emit BlobRefMessage?
   Service Bus queue: wiki-documents
          ↓
[WikiUploadFunction]        ServiceBusTrigger("wiki-documents")
          ↓ download List<WikiIndexDocument> blob
          ↓ IWikiSearchUploader.UploadAsync() → upsert (≤500/batch)
   Azure AI Search: wiki-pages-index ✅
```

### Claim-Check Pattern — blob naming convention

Container: **`messages`** (auto-created by `BlobMessageStore` on first write).

| Subfolder | Payload type | Name pattern |
|---|---|---|
| `workitem-details/` | `WorkItemDetailMessage` | `{date}/{wiId}_{guid8}.json` |
| `workitem-documents/` | `List<WorkItemIndexDocument>` | `{date}/{wiId}_{guid8}.json` |
| `wiki-pages/` | `WikiPageContentMessage` | `{date}/{wikiId}-{pathSlug}_{guid8}.json` |
| `wiki-documents/` | `List<WikiIndexDocument>` | `{date}/{wikiId}-{pathSlug}_{guid8}.json` |

`guid8` = first 8 hex chars of a new `Guid` — guarantees uniqueness across re-indexing runs.
`pathSlug` = wiki path sanitised: `/` → `-`, non-alphanumeric stripped, max 50 chars.

Blobs are **not deleted after processing** — use Azure Blob Storage Lifecycle Management for retention if needed.

### Throttling & reliability

- **Concurrency** capped via `host.json` (NOT `SemaphoreSlim`):
  `extensions.serviceBus.maxConcurrentCalls = 4`, `prefetchCount = 8`.
- **Lock duration** 5 min, **maxDeliveryCount** 5, **dead-lettering on expiration** enabled.
- **Stage 2 (Fetch) returns `null`** on missing/broken pages → no message sent, no DLQ storm.
- **BlobRefMessage on SB** is ~100 B → 256 KB limit is never a concern.
- **Blob payloads are unbounded** — handles Epic WI with 100+ comments, multi-MB WIKI pages.
- **Last-sync watermarks** for stage 1 timers are persisted via `ISettingsStore` (Azure Tables
  `Settings` table, keys `indexer.workitems.lastSync` / `indexer.wiki.lastSync`).
- **Auth is keyless** — Blob, Tables and Service Bus all reuse the **same storage account as the
  Functions runtime**. App settings: `ServiceBus__fullyQualifiedNamespace` and
  `AzureWebJobsStorage__accountName` (consumed via `config["AzureWebJobsStorage:accountName"]`).
  Function App MI requires: `Azure Service Bus Data Sender/Receiver`, `Storage Blob Data Owner`,
  `Storage Table Data Contributor`.

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
│   ├── GenerateWorkItemReportFunction.cs     ✅ POST /api/AnalyzeWorkItem/{workItemId}/generate
│   │                                            — runs pipeline, persists report to Blob, returns markdown
│   ├── GetWorkItemReportFunction.cs          ✅ GET  /api/AnalyzeWorkItem/{workItemId}
│   │                                            — returns stored report from Blob (404 if missing)
│   ├── HttpTestFunction.cs                   ✅ Ad-hoc HTTP endpoint for manual tool testing
│   │
│   ├── WorkItemIndexerFunction.cs            ✅ Stage 1/4 Timer → workitem-ids
│   ├── WorkItemFetchFunction.cs              ✅ Stage 2/4 SB + Blob → workitem-details
│   ├── WorkItemBuildDocumentsFunction.cs     ✅ Stage 3/4 SB + Blob → workitem-documents
│   ├── WorkItemUploadFunction.cs             ✅ Stage 4/4 SB + Blob → AI Search
│   │
│   ├── WikiIndexerFunction.cs                ✅ Stage 1/4 Timer → wiki-page-refs
│   ├── WikiPageFetchFunction.cs              ✅ Stage 2/4 SB + Blob → wiki-pages
│   ├── WikiBuildDocumentsFunction.cs         ✅ Stage 3/4 SB + Blob → wiki-documents
│   └── WikiUploadFunction.cs                 ✅ Stage 4/4 SB + Blob → AI Search
│
├── Indexing/
│   ├── SearchIndexManager.cs                 ✅ IHostedService — creates both AI Search indexes
│   │
│   ├── WorkItemIndexDocument.cs              ✅ Typed DTO for AI Search upload
│   ├── WorkItemQueryService.cs               ✅ WIQL + change detection (uses ISettingsStore)
│   ├── WorkItemDocumentBuilder.cs            ✅ Chunk + embed work items
│   ├── WorkItemSearchUploader.cs             ✅ MergeOrUpload to work-items-index
│   │
│   ├── WikiIndexDocument.cs                  ✅ Typed DTO for AI Search upload
│   ├── WikiPageQueryService.cs               ✅ Enumerate wikis + page paths
│   ├── WikiDocumentBuilder.cs                ✅ Chunk Markdown + embed
│   ├── WikiSearchUploader.cs                 ✅ Upsert to wiki-pages-index
│   │
│   └── Messages/
│       ├── BlobRefMessage.cs                 ✅ Thin SB message { BlobUri } — Claim-Check ref
│       ├── WorkItemIdsBatchMessage.cs        ✅ workitem-ids payload  (plain, no Claim-Check)
│       ├── WorkItemDetailMessage.cs          ✅ workitem-details blob payload
│       ├── WorkItemIndexDocumentsMessage.cs  (legacy — blob carries List<> directly)
│       ├── WikiPageRefMessage.cs             ✅ wiki-page-refs payload  (plain, no Claim-Check)
│       ├── WikiPageContentMessage.cs         ✅ wiki-pages blob payload
│       └── WikiIndexDocumentsMessage.cs      (legacy — blob carries List<> directly)
│
├── Stores/
│   ├── BlobMessageStore.cs                   ✅ IBlobMessageStore + BlobPaths (Claim-Check, container `messages`)
│   ├── ReportStore.cs                        ✅ IReportStore — Impact Analysis reports (container `reports`)
│   └── SettingsStore.cs                      ✅ ISettingsStore — generic JSON KV on Azure Tables
│
├── Tools/
│   ├── AgentToolAttribute.cs                 ✅ [AgentTool(Description = "...")]
│   ├── Research/
│   │   ├── SearchWorkItemsTool.cs            ✅ Semantic / hybrid (vector + BM25 + reranker)
│   │   ├── KeywordSearchWorkItemsTool.cs     ✅ Native DevOps Work Item Search (Lucene/BM25)
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
│   ├── DevOpsModels.cs                       ✅ WorkItemDetail, WikiPageDetail, WikiInfo
│   ├── PipelineModels.cs                     ✅ WorkItemEvent, WorkItemRef, ResearchFindings,
│   │                                            RelatedWorkItem, RelatedWikiPage, EditorDecision
│   └── SettingEntity.cs                      ✅ Azure Tables row + SettingKeys constants
│
├── Extensions/
│   └── StringExtensions.cs                   ✅ SplitIntoChunks(maxChars, overlapFraction)
│
├── Configs/
│   └── ToolsConfig.cs                        ✅ DI (AddImpactAnalyzerTools)
│
├── docs/
│   └── work-items-indexing.md                ✅ Pipeline reference docs
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

### Function App settings (set by `functionapp.bicep`)

| Setting | Value | Purpose |
|---|---|---|
| `ServiceBus__fullyQualifiedNamespace` | `<ns>.servicebus.windows.net` | Keyless SB auth |
| `AzureWebJobsStorage__accountName` | `<storage-account-name>` | Functions runtime state **AND** shared by `IBlobMessageStore` (container `messages`), `IReportStore` (container `reports`) and `ISettingsStore` (table `Settings`) — single keyless storage account for everything |

> Use `dotnet user-secrets` locally. Use **Azure Key Vault** in production for secrets.

---

## 📋 Key Data Models

```csharp
// All defined in Models/PipelineModels.cs

// Output of Researcher agent (structured JSON)
record ResearchFindings(
    WorkItemRef AnalyzedItem,
    List<RelatedWorkItem> RelatedWorkItems,   // PotentialRelationType: CONFLICT|DEPENDENCY|RELATED
    List<RelatedWikiPage> RelatedWikiPages,
    List<string> SearchQueriesUsed
);

// Editor decision after reviewing Writer's report
record EditorDecision(bool IsApproved, string? Feedback);

// Triggered work item payload (built from DevOps fetch inside GenerateWorkItemReportFunction)
record WorkItemEvent(int Id, string Type, string Title, string Description,
                     string AcceptanceCriteria, string AreaPath, string Tags);

// DevOps REST API models (Models/DevOpsModels.cs)
// WorkItemDetail  — Id, Title, Type, State, Description, AcceptanceCriteria,
//                   AreaPath, IterationPath, Tags, Priority, AssignedTo,
//                   CreatedDate, ChangedDate, Relations, Url
// WikiPageDetail  — Id, Path, Content, RemoteUrl, GitItemPath, ETag
// WikiInfo        — Id, Name, RemoteUrl

// Service Bus messages
// BlobRefMessage                  { string BlobUri }        ← on SB for stages 2-4 (Claim-Check)
// WorkItemIdsBatchMessage         { List<int> Ids }          ← plain (stage 1 WI)
// WikiPageRefMessage              { string WikiId, WikiName, Path } ← plain (stage 1 WIKI)

// Blob payload types (JSON, stored in container "messages")
// WorkItemDetailMessage           { WorkItemDetail WorkItem }
// WikiPageContentMessage          { string WikiId, WikiName, WikiPageDetail Page }
// List<WorkItemIndexDocument>     — all chunks for one WI
// List<WikiIndexDocument>         — all chunks for one WIKI page

// Runtime config (Azure Tables, Models/SettingEntity.cs)
// SettingEntity                    { string PartitionKey="settings", string RowKey, string? Value (JSON) }
// SettingKeys.WorkItemsLastSync   = "indexer.workitems.lastSync"
// SettingKeys.WikiLastSync        = "indexer.wiki.lastSync"
```

---

## ✅ What Is Already Implemented

### Core Pipeline
1. **Multi-agent pipeline** (`ImpactAnalysisPipeline.cs`) — Researcher → Writer → Editor (max 2 retries) → Sender
2. **Agent system prompts** (`AgentPrompts.cs`) — defined for all 4 agents
3. **ResearchTools** — `SearchWorkItemsTool` (semantic/hybrid), `KeywordSearchWorkItemsTool` (native BM25/Lucene), `SearchWikiTool`, `GetWorkItemDetailsTool`, `GetWikiPageDetailsTool`
4. **SenderTools** — `PostCommentTool`
5. **HTTP entry points** — `GenerateWorkItemReportFunction` (`POST /api/AnalyzeWorkItem/{workItemId}/generate`) runs the pipeline synchronously, persists the markdown report via `IReportStore` and returns it; `GetWorkItemReportFunction` (`GET /api/AnalyzeWorkItem/{workItemId}`) returns the previously stored report from Blob Storage (404 if missing)
6. **HttpTestFunction** — ad-hoc endpoint for manually invoking tools during dev

### Shared Services
7. **EmbeddingService** — `float[]` vectors via text-embedding-3-large (AIProjectClient)
8. **AzureSearchService** — hybrid search (vector HNSW + BM25 + semantic reranker, score threshold)
9. **AzureDevOpsService** — DevOps REST API v7.1 wrapper (batch GET, WIQL, native Work Item Search, comments, wiki list/pages); registered as `HttpClient` with `AddStandardResilienceHandler`
10. **BlobMessageStore** (`IBlobMessageStore` in `Stores/`) — upload/download JSON blobs in container `messages`; `BlobPaths` static class generates all blob paths
10a. **ReportStore** (`IReportStore` in `Stores/`) — saves/loads Impact Analysis markdown reports in container `reports` under deterministic name `{workItemId}.md` (latest report overwrites previous)
11. **SettingsStore** (`ISettingsStore` in `Stores/`) — generic JSON key-value over Azure Tables (`Settings` table); used by indexer timer functions to persist last-sync watermarks
12. **StringExtensions.SplitIntoChunks** — shared chunking helper with optional overlap fraction

### Indexing — Service Bus + Claim-Check pipelines
13. **Work Item pipeline** — 3 services + 4 functions; stages 2–4 use Claim-Check via `IBlobMessageStore`
14. **WIKI pipeline** — 3 services + 4 functions; identical Claim-Check pattern
15. **Blob payloads are unbounded** — eliminates 256 KB SB Standard limit; handles large Epics, multi-MB WIKI pages
16. **SearchIndexManager** (`IHostedService`) — creates `work-items-index` + `wiki-pages-index` on startup (HNSW cosine, 3072 dims, `devops-semantic-config`)
17. **`host.json`** — `maxConcurrentCalls = 4`, `prefetchCount = 8`

### Infrastructure & Config
18. **DI registration** (`Configs/ToolsConfig.cs`) — registers all services incl. `IBlobMessageStore`, `IReportStore`, `ISettingsStore`, a shared `BlobServiceClient` (each store picks its own container: `messages`, `reports`) and a `TableClient` (`Settings`), all bound to the Functions runtime storage account via `DefaultAzureCredential`
19. **`AgentToolAttribute`** — `[AgentTool(Description = "...")]`
20. **Bicep IaC** — Function App, Service Bus (6 queues), AI stack, RBAC; app settings include `ServiceBus__fullyQualifiedNamespace` and `AzureWebJobsStorage__accountName`. MI roles: `Azure Service Bus Data Sender/Receiver`, `Storage Blob Data Owner`, `Storage Table Data Contributor`
21. **Windows MAX_PATH workaround** — `ExtensionsCsProjDirectory=$(LOCALAPPDATA)\AzFuncWorkerExt\...` in csproj (Windows-only). Worker SDK disables `Directory.Build.props` for its generated `WorkerExtensions.csproj` — this is the only escape hatch.

---

## 🔜 Next Steps

### Integration Tests (MEDIUM priority)
- `AzureDevOpsService` — test against a real DevOps project
- `AzureSearchService` — hybrid search with sample documents
- End-to-end pipeline test: publish to `workitem-ids` → assert blob created → assert AI Search indexed
- Full agent pipeline test — mock `WorkItemEvent` → assert comment posted

### Local Development (LOW priority)
- `docker-compose.yml` with Azurite (Functions runtime + local Blob Storage emulation)
- Populate `local.settings.json` with all connection settings
- Seed script for AI Search indexes for offline pipeline testing

---

## 💡 Coding Guidelines for This Project

### Agents & tools
- All agent tools must have `[AgentTool(Description = "...")]` — LLM uses it to decide when to call.
- Tool methods are `async Task<string>` returning **JSON strings**.
- Use `IConfiguration` for all settings — never hardcode endpoints or keys.
- Register everything via DI in `ToolsConfig.AddImpactAnalyzerTools()`.

### Indexing pipelines
- **Each stage = one Function class.** Never call the next stage directly — always emit via `[ServiceBusOutput]`.
- **Stages 2–4 use Claim-Check:** upload payload blob via `IBlobMessageStore`, put `BlobRefMessage { BlobUri }` on SB. Stage 1 queues (`workitem-ids`, `wiki-page-refs`) are small and skip Claim-Check.
- **Blob paths** are generated by `BlobPaths` static class in `Stores/BlobMessageStore.cs`. Add new methods there for any new pipeline stage.
- **Persist watermarks via `ISettingsStore`** (Azure Tables). Use a stable key under the `indexer.*` namespace; do NOT invent ad-hoc tables or blobs for tiny config values.
- **Throttle via `host.json`** (`maxConcurrentCalls`), not `SemaphoreSlim`.
- **Stage 2 (Fetch) returns `null`** on missing/broken data — prevents poison-message DLQ storms.
- **Blobs are never deleted** by functions — use Azure Blob Lifecycle Management for cleanup.
- **Reuse `StringExtensions.SplitIntoChunks`** for any new chunking logic instead of rewriting it.
- Naming: `I{Domain}QueryService`, `I{Domain}DocumentBuilder`, `I{Domain}SearchUploader`.

### Sizing
- Chunking: max **8 000 chars** for WI (~2 000 tokens), max **6 000 chars** for WIKI (~1 500 tokens).
- AI Search uploads: batch at max **500 documents** per `IndexDocumentsAsync` call.
- WI ID batches: **200 IDs per `WorkItemIdsBatchMessage`** (DevOps batch GET limit).

### Auth & secrets
- **Keyless everywhere** — `DefaultAzureCredential` for OpenAI / AI Search / Service Bus / Blob / Tables / Foundry.
- **One storage account for everything** — Functions runtime, Claim-Check blobs (`messages` container) and `Settings` table all share `AzureWebJobsStorage__accountName`. Do not introduce a second account.
- DevOps PAT is the only key-based secret — Key Vault in Azure, `dotnet user-secrets` locally.
