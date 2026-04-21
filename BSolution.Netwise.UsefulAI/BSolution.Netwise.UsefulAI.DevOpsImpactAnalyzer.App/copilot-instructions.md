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
| IaC | **Bicep** (subscription scope) + `deploy.ps1` PowerShell wrapper |

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

## 🔄 Indexing Pipeline (background sync) — ✅ verified running

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

> ✅ Both indexer Functions have been launched manually and are confirmed working
> end-to-end against the deployed Azure AI Search instance.

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
│   └── SearchIndexManager.cs                 ✅ DONE — IHostedService, creates both AI Search indexes
│
├── Tools/
│   ├── AgentToolAttribute.cs                 ✅ DONE — [AgentTool(Description = "...")] attribute
│   ├── Research/
│   │   ├── SearchWorkItemsTool.cs            ✅ DONE
│   │   ├── SearchWikiTool.cs                 ✅ DONE
│   │   ├── GetWorkItemDetailsTool.cs         ✅ DONE
│   │   └── ResearchTools.cs                  ✅ DONE — aggregates research tools
│   ├── Sender/
│   │   ├── PostCommentTool.cs                ✅ DONE
│   │   └── SenderTools.cs                    ✅ DONE — aggregates sender tools
│   └── Shared/
│       ├── EmbeddingService.cs               ✅ DONE — text-embedding-3-large via AIProjectClient
│       ├── AzureSearchService.cs             ✅ DONE — hybrid search wrapper
│       └── AzureDevOpsService.cs             ✅ DONE — DevOps REST API v7.1 wrapper
│
├── Models/
│   ├── SearchModels.cs                       ✅ DONE — SearchResultItem
│   └── DevOpsModels.cs                       ✅ DONE — WorkItemDetail, WikiPageDetail, WikiInfo
│
├── Configs/
│   └── ToolsConfig.cs                        ✅ DONE — DI (AddImpactAnalyzerTools)
│
├── appsettings.json                          ✅ DONE — config skeleton (secrets via user-secrets / Key Vault)
├── local.settings.json                       ⚠️  PARTIAL — only AzureWebJobsStorage + FUNCTIONS_WORKER_RUNTIME
│
└── infra/                                    ✅ DONE — Bicep IaC, deployed to Azure
    ├── main.bicep                            ✅ DONE — subscription-scope orchestrator (2 RGs)
    ├── modules/
    │   ├── functionapp.bicep                 ✅ DONE — Function App + ASP + KV + Storage + AppInsights
    │   └── ai.bicep                          ✅ DONE — AI Search + Foundry account + project + deployments
    ├── deploy.ps1                            ✅ DONE — PowerShell deploy wrapper
    └── last-deploy-res.md                    ✅ DONE — last successful deployment outputs
```

---

## ⚙️ Configuration

### Local development (`appsettings.json` + user-secrets)

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-openai.openai.azure.com/",
    "ApiKey": "-- via user-secrets --",
    "EmbeddingDeployment": "text-embedding-3-large",
    "ApiVersion": "2024-10-21"
  },
  "AzureSearch": {
    "Endpoint": "https://your-search.search.windows.net",
    "ApiKey": "-- via user-secrets --"
  },
  "AzureDevOps": {
    "Organization": "your-org",
    "Project": "your-project",
    "PersonalAccessToken": "-- via user-secrets --"
  },
  "Foundry": {
    "Endpoint": "https://your-foundry.services.ai.azure.com/api/projects/your-project"
  }
}
```

> Use `dotnet user-secrets` locally. Use **Azure Key Vault** in production.

### Production — Key Vault (`bsusefulkvwulklaqz5uiow`)

All application secrets are stored in Key Vault and **verified equal to local user-secrets**.
The Function App's system-assigned managed identity has the **Key Vault Secrets User** role
and resolves them at runtime via `@Microsoft.KeyVault(...)` app-setting references defined
in `infra/modules/functionapp.bicep` (resource `functionAppSettings`).

| Key Vault secret name              | App setting (Function App)        | Maps to config key                  |
|------------------------------------|-----------------------------------|-------------------------------------|
| `Foundry--Endpoint`                | `Foundry__Endpoint`               | `Foundry:Endpoint`                  |
| `AzureSearch--Endpoint`            | `AzureSearch__Endpoint`           | `AzureSearch:Endpoint`              |
| `AzureSearch--ApiKey`              | `AzureSearch__ApiKey`             | `AzureSearch:ApiKey`                |
| `AzureOpenAI--Endpoint`            | `AzureOpenAI__Endpoint`           | `AzureOpenAI:Endpoint`              |
| `AzureOpenAI--ApiKey`              | `AzureOpenAI__ApiKey`             | `AzureOpenAI:ApiKey`                |
| `AzureOpenAI--ApiVersion`          | `AzureOpenAI__ApiVersion`         | `AzureOpenAI:ApiVersion`            |
| `AzureOpenAI--EmbeddingDeployment` | `AzureOpenAI__EmbeddingDeployment`| `AzureOpenAI:EmbeddingDeployment`   |
| `AzureDevOps--Organization`        | `AzureDevOps__Organization`       | `AzureDevOps:Organization`          |
| `AzureDevOps--Project`             | `AzureDevOps__Project`            | `AzureDevOps:Project`               |
| `AzureDevOps--PersonalAccessToken` | `AzureDevOps__PersonalAccessToken`| `AzureDevOps:PersonalAccessToken`   |

> Convention: Key Vault secret names use `--` (KV disallows `:` and `__`); Function App
> setting names use `__`, which the .NET configuration provider maps to `:` in `IConfiguration`.

> The `functionAppSettings` resource (`Microsoft.Web/sites/config@2024-04-01 'appsettings'`)
> is a separate resource (not inline in `siteConfig.appSettings`) so it can declare
> `dependsOn: [kvSecretsUserRole]` without creating a circular dependency on the
> Function App. This guarantees RBAC propagation finishes before the app cold-starts
> and tries to resolve the `@Microsoft.KeyVault(...)` references.

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

### Indexing — verified end-to-end ✅
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
12. **WorkItemIndexerFunction** — Timer `0 */15 * * * *`, detects first run → full sync, subsequent → incremental since `ScheduleStatus.Last` — **manually triggered, confirmed working**
13. **WikiIndexerFunction** — Timer `0 0 * * * *` (every hour), always full sync — **manually triggered, confirmed working**

### Infrastructure & Config
14. **DI registration** (`Configs/ToolsConfig.cs`) — `AddImpactAnalyzerTools()` registers all services, indexers, tools, pipeline and `SearchIndexManager` as hosted service
15. **`AgentToolAttribute`** — `[AgentTool(Description = "...")]` — LLM uses description to decide when to call each tool
16. **`local.settings.json`** — base setup for local Azure Functions run (Azurite storage + dotnet-isolated runtime)
17. **Bicep IaC (`infra/`)** — subscription-scope deployment, **already deployed to Azure**:
    - 2 resource groups: `rg-ntw-usefulai-ai-dev`, `rg-ntw-usefulai-app-dev` (region: `swedencentral`)
    - `bs-useful-search-dev` — Azure AI Search **Standard** tier (semantic + vector)
    - `bs-useful-foundry-dev` — Azure AI Foundry (AIServices) account with `gpt-4o` (2024-11-20) + `text-embedding-3-large` deployments
    - `bs-useful-aiproj-dev` — Foundry project (child of AIServices account)
    - `bs-useful-func-dev` — Function App (.NET 10 isolated, Windows, B1 ASP)
    - `bsusefulkvwulklaqz5uiow` — Key Vault (RBAC mode, soft-delete 7d)
    - Storage account, Application Insights, Log Analytics workspace
    - **Managed Identity + RBAC**: Cognitive Services OpenAI User, Azure AI Developer,
      Search Index Data Contributor, Search Service Contributor, Key Vault Secrets User,
      Storage Blob Data Owner / Queue Data Contributor / Table Data Contributor
18. **`infra/deploy.ps1`** — PowerShell wrapper for `az deployment sub create`, prints all
    output endpoints + post-deploy instructions (Key Vault secrets + Function publish)

---

## 🔜 Next Steps (TODO — implement in this order)

### 1. Re-run `deploy.ps1` to apply the new app-settings resource
**Priority: HIGH — required so the deployed Function App can read configuration**

- The 3 previously-missing secrets (`AzureOpenAI--Endpoint`, `AzureOpenAI--ApiKey`,
  `AzureOpenAI--ApiVersion`) have been added to Key Vault.
- `infra/modules/functionapp.bicep` now provisions the `functionAppSettings` resource
  with Key Vault references for all 10 application secrets.
- Re-run `infra/deploy.ps1` (idempotent) so the new `Microsoft.Web/sites/config`
  resource is materialized on the existing Function App.

### 2. Deploy Function App code to Azure
**Priority: HIGH — Function App resource exists but no code has been published yet**

- `func azure functionapp publish bs-useful-func-dev`
- Verify `SearchIndexManager` runs on cold start (creates/updates both AI Search indexes)
- Manually trigger `WorkItemIndexerFunction` and `WikiIndexerFunction` once via the
  portal to seed the indexes against the production Foundry/Search resources
- Configure Azure DevOps Service Hook (Webhook) → `https://bs-useful-func-dev.azurewebsites.net/api/WorkItemWebhook`

### 3. Integration / End-to-end Tests
**Priority: MEDIUM**

- `AzureDevOpsService` — test against a real DevOps project (use dedicated test work items)
- `AzureSearchService` — test hybrid search with sample indexed documents
- `WorkItemIndexer` / `WikiIndexer` — test full sync end-to-end with controlled DevOps data
- Full pipeline test — mock `WorkItemEvent` → verify comment posted on work item

### 4. Local Development Setup — complete
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
