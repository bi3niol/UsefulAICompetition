# Copilot Instructions for UsefulAI Solution

## Purpose

Work in this repository as a **code agent**, not as a documentation writer.

The solution contains **two independent .NET 10 Azure Functions apps** that share a common class library:

1. **`BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App`** — analyzes Azure DevOps work items via a 4-agent pipeline and produces impact analysis reports. Cooperates with the browser extension **`BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension`**, which is the only consumer of its HTTP endpoints.
2. **`BSolution.Netwise.UsefulAI.WikiDocGenerator.App`** — generates and updates Azure DevOps WIKI pages based on code, work items and merged pull requests. Currently under active development.
3. **`BSolution.Netwise.UsefulAI.Core`** — shared class library hosting models, services, stores and DI registration reused by both apps.

---

## Working scope rule (CRITICAL)

When the active task is about **Impact Analyzer**:

- You MAY freely modify:
  - `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/**`
  - `BSolution.Netwise.UsefulAI.Core/**` (only when needed for Impact Analyzer)
- You MUST NOT modify:
  - `BSolution.Netwise.UsefulAI.WikiDocGenerator.App/**`
- You MAY READ the WikiDocGenerator code to:
  - understand established patterns (pipelines, prompts, retry, indexing),
  - identify reusable services/tools that should be **extracted to Core**.
- If shared code is needed, the only allowed path is:
  1. Move/extract the code into `BSolution.Netwise.UsefulAI.Core`.
  2. Update Impact Analyzer references to the Core types **only as a strictly mechanical follow-up** (using-imports + DI registrations). No behavior changes, no refactoring of unrelated code.
  3. Wire the Core type into WikiDocGenerator.

When the task is explicitly about extracting/refactoring shared code into Core, both apps may be touched — but each change to an unaffected app must remain mechanical (namespace/DI), never behavioral.

---

## Repository priorities

When changing code, optimize for:

1. **Correctness**
2. **Project consistency** (match the touched app's style)
3. **Reuse of existing abstractions** (especially anything in Core)
4. **Operational reliability**
5. **Small, targeted diffs**

Do not redesign working areas without a clear reason.

---

## Architecture summary

### Shared infrastructure (used by both apps)

- Azure Functions isolated worker (.NET 10)
- Azure Service Bus
- Azure Blob Storage
- Azure Tables
- Azure AI Search
- Azure OpenAI / AI Foundry
- Azure DevOps REST API
- Authentication: `DefaultAzureCredential` for Azure resources; PAT only for Azure DevOps.

### `Core` library — key contents

- `Models/` — `DevOpsModels`, `WikiDocGenModels`, `SearchModels`, `SettingEntity` + `SettingKeys`.
- `Services/` — `AzureDevOpsService`, `AzureSearchService`, `EmbeddingService`, `WorkItemQueryService` (generic WIQL with injected work item types).
- `Stores/` — `BlobMessageStore` (claim-check), `SettingsStore` (KV), `BlobPathHelpers`.
- `Extensions/` — `StringExtensions.SplitIntoChunks`.
- `Configuration/CoreServicesRegistration.AddUsefulAICoreServices()` — single DI entry both apps call from their own `*ToolsConfig`.

### Impact Analyzer (`DevOpsImpactAnalyzer.App`)

Analysis pipeline `Researcher → Writer → Editor → Sender`, driven from HTTP by the browser extension:

- `Functions/GenerateWorkItemReportFunction.cs` (entry point)
- `Functions/GetWorkItemReportFunction.cs` (stored report retrieval)
- `ImpactAnalysisPipeline.cs`, `AgentPrompts.cs`
- 4-stage indexing pipelines (work items + WIKI) using Service Bus + Blob claim-check (`Functions/WorkItem*`, `Functions/Wiki*`, `Indexing/*`).
- Per-app DI in `Configs/ToolsConfig.AddImpactAnalyzerTools()`.

### WikiDocGenerator (`WikiDocGenerator.App`)

Pipeline `Researcher → Writer → Editor → Sender` that **writes to a separate Azure DevOps WIKI** (`WikiDocGenerator:TargetWikiId`) configured independently from the WIKI consumed by Impact Analyzer.

Entry points:

- `Functions/PullRequestWebhookFunction` — DevOps service hook `git.pullrequest.merged`; updates affected pages right after merge.
- `Functions/WikiRefreshTimerFunction` — daily timer trigger; reads Feature/Epic/User Story/PBI changed since last run (watermark `SettingKeys.WikiGenLastSync`), batches them, calls `WikiDocGenerationPipeline.RunForWorkItemsAsync`.

The Researcher decides **update existing wiki page vs create new** — callers never pass a target path.

Per-app DI in `Configs/WikiDocGeneratorToolsConfig.AddWikiDocGeneratorTools()`.

---

## Non-negotiable project rules

### 0. File placement — use correct project directories (CRITICAL)
The solution root is the directory containing the `.sln` file. Each project lives in its own folder **directly** under the solution root:
<solution-root>/
├── BSolution.Netwise.UsefulAI.Core/
├── BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/
├── BSolution.Netwise.UsefulAI.WikiDocGenerator.App/
└── BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension/
When creating or moving files, **always** place them relative to the correct project folder listed above. Never nest a project folder inside another project folder (e.g. do NOT create `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/BSolution.Netwise.UsefulAI.WikiDocGenerator.App/`).

Before writing a new file, verify the target path starts with one of the known project directories shown above. If unsure, use `get_projects_in_solution` to confirm actual project paths.

### 1. Follow the existing pipeline model
- Keep **one Function class per pipeline stage**.
- Do **not** call downstream stages directly.
- Communicate between Service Bus stages through **output bindings**.

### 2. Preserve Claim-Check design (Impact Analyzer indexing)
- Stage 1 queues carry small plain messages.
- Stages 2–4 must store large payloads in Blob Storage via `IBlobMessageStore`.
- Service Bus payload for those stages remains `BlobRefMessage`.

### 3. Reuse existing services
Always check Core before introducing a new abstraction. Prefer extending:

- `AzureDevOpsService` (REST surface)
- `AzureSearchService` (search/index ops)
- `EmbeddingService`
- `WorkItemQueryService` (WIQL — inject your own type list, do not fork it)
- `BlobMessageStore`, `SettingsStore`
- `ReportStore` (Impact Analyzer-specific store, lives in its App project)

### 4. Use DI and configuration only
- Register new services in the **app's own** `*ToolsConfig` extension.
- Read settings from `IConfiguration`.
- Never hardcode endpoints, queue names, model names, container names, wiki IDs, or secrets.

### 5. Keep auth model unchanged
- `DefaultAzureCredential` for Azure resources.
- No connection strings for Blob, Tables, Search, Service Bus, OpenAI, Foundry.
- Azure DevOps PAT is the only expected secret-based auth.

### 6. Respect persistence conventions
- Claim-check blobs: container `messages`.
- Impact Analyzer reports: container `reports`.
- Runtime KV config: table `Settings`, key conventions:
  - `indexer.*` — Impact Analyzer indexing watermarks
  - `wikigen.*` — WikiDocGenerator watermarks
- New blob path patterns must be added in the App's `BlobPaths` (compose them from `Core.Stores.BlobPathHelpers`).

### 7. Respect existing throttling approach
- Throttling lives in `host.json`.
- Do **not** introduce `SemaphoreSlim`-based concurrency control for Service Bus handlers unless there is a very specific reason.

### 8. Keep the two apps independent at runtime
- Each app has its own `Program.cs`, `host.json`, `local.settings.json`.
- WikiDocGenerator and Impact Analyzer must be deployable independently.
- Pipeline-specific models (e.g. `WikiGenPipelineModels`) stay in that app unless explicitly needed by Core or the other app.

---

## Agent and tool rules (both apps)

### Tool implementation
All agent tools must:

- be marked with `[AgentTool(Description = "...")]`
- be `async`
- return `Task<string>`
- return **JSON strings**
- be registered through DI

`AgentToolAttribute` is currently defined per-app under `Tools/`. If a tool becomes useful for both apps, extract it (and the attribute) into Core.

### Tool placement (per app)
- Research tools → `Tools/Research/`
- Sender tools → `Tools/Sender/`
- Shared helper services → `Tools/Shared/` (or Core if reused by both apps)

### Prompt changes
Prompts live next to the pipeline they belong to (`AgentPrompts.cs` for Impact Analyzer, `WikiDocAgentPrompts.cs` for WikiDocGenerator). When editing:

- preserve role separation between Researcher, Writer, Editor, Sender
- avoid making Writer or Editor depend on tools if they currently do not
- keep output expectations deterministic and easy to parse (pipelines use `RunAsync<T>` with strongly-typed deserialization)

---

## Data and indexing rules

### Chunking
Reuse `StringExtensions.SplitIntoChunks(...)` from Core.

Current limits (Impact Analyzer indexing):

- Work items: max `6000` chars per chunk
- WIKI pages: max `4500` chars per chunk

Do not invent a second chunking implementation.

### Upload sizing
- Azure AI Search uploads: max `500` documents per batch
- Work item ID batches: max `100` IDs (Impact Analyzer indexing)
- WikiDocGenerator timer: process work items in batches of `~20` per pipeline run so the LLM context stays manageable

### Missing/broken data handling
For fetch stages:
- prefer returning `null` / no downstream message for missing or broken external data
- avoid poison-message loops and DLQ storms

### Watermarks
Persist watermarks via `ISettingsStore` using stable keys (`indexer.*` for Impact Analyzer, `wikigen.*` for WikiDocGenerator).

Watermarks store the **time the run STARTED** (snapshot taken before the WIQL query), so changes that happen during the run are not lost.

Do not create ad hoc blobs or tables for tiny config values.

---

## Important domain assumptions

### Impact Analyzer
Analyzes newly created/changed Azure DevOps work items to find:
- Conflicts
- Dependencies
- Related work
- Relevant WIKI / architecture references

Result is a markdown report stored in Blob Storage and posted as a work item comment.

### WikiDocGenerator
Maintains a **generated** wiki, separate from the manually authored wiki consumed by Impact Analyzer. For each input (merged PR or batch of changed Feature-level work items), the pipeline:
- groups work items by topic,
- decides for each topic whether to update an existing page or create a new one,
- writes through `IAzureDevOpsService.CreateOrUpdateWikiPageAsync` with ETag-based optimistic concurrency (auto-retry on 412).

---

## File map for common tasks

### Impact Analyzer
- HTTP/API changes: `Functions/GenerateWorkItemReportFunction.cs`, `Functions/GetWorkItemReportFunction.cs`, `Program.cs`
- Agent behavior: `ImpactAnalysisPipeline.cs`, `AgentPrompts.cs`, `Tools/Research/*`, `Tools/Sender/*`
- Indexing pipelines: `Functions/WorkItem*`, `Functions/Wiki*`, `Indexing/*`, `Indexing/Messages/*`
- Reports/storage: `Stores/ReportStore.cs`, `Stores/BlobPaths.cs`

### WikiDocGenerator
- HTTP/webhook: `Functions/PullRequestWebhookFunction.cs`, `Program.cs`
- Scheduled refresh: `Functions/WikiRefreshTimerFunction.cs`
- Agent behavior: `WikiDocGenerationPipeline.cs`, `WikiDocAgentPrompts.cs`, `Tools/Research/*`, `Tools/Sender/*`
- App-specific models: `Models/WikiGenPipelineModels.cs`
- DI: `Configs/WikiDocGeneratorToolsConfig.cs`

### Core (shared)
- DevOps integration: `Services/AzureDevOpsService.cs`
- Search: `Services/AzureSearchService.cs`
- WIQL: `Services/WorkItemQueryService.cs` (parameterized by work item types)
- Embeddings: `Services/EmbeddingService.cs`
- Storage: `Stores/BlobMessageStore.cs`, `Stores/SettingsStore.cs`, `Stores/BlobPathHelpers.cs`
- DI bootstrap: `Configuration/CoreServicesRegistration.cs`
- Cross-app models: `Models/DevOpsModels.cs`, `Models/WikiDocGenModels.cs`, `Models/SearchModels.cs`, `Models/SettingEntity.cs`

### Infrastructure
- `infra/main.bicep`, `infra/modules/*.bicep`

---

## Preferred change patterns

### When adding a new indexing stage (Impact Analyzer)
- add a new message type only if the existing contract is insufficient
- add blob path generation to the app's `BlobPaths`
- keep the stage isolated in its own Function class
- keep payloads serializable and storage-friendly

### When adding a new tool
- place it in the correct folder of the **owning app**
- annotate with `[AgentTool]`, return JSON
- register it in DI within that app's `*ToolsConfig`
- wire it only into the agents that should use it
- if both apps need it, extract to Core first

### When adding a new setting
- pick a stable key under the right prefix (`indexer.*`, `wikigen.*`, or a new app-specific prefix)
- read it through `IConfiguration` or `ISettingsStore`
- do not invent a new top-level config section unless necessary

### When adding a new Azure resource dependency
- first check whether an existing service can absorb the capability
- if a new resource is truly required, reflect it in Bicep and app configuration consistently

### When extracting code into Core
- move concrete classes/interfaces under `BSolution.Netwise.UsefulAI.Core/<area>/`
- parameterize anything that differed between the two call sites (e.g. type lists, container names, log tags)
- update the originating app to consume the Core type via DI
- keep the rest of that app untouched

---

## Avoid these mistakes

Do not:

- modify WikiDocGenerator code while working on Impact Analyzer (and vice versa) beyond mechanical follow-ups required by a Core extraction
- turn the instruction file into a README
- add broad architecture rewrites unless requested
- bypass `IReportStore`, `IBlobMessageStore`, or `ISettingsStore`
- hardcode queue names, container names, wiki IDs, or endpoints
- duplicate chunking, search, DevOps access, or WIQL logic (use Core)
- introduce a second storage account pattern
- replace Claim-Check with large Service Bus messages
- mix unrelated refactoring with a small feature request
- write to the manually-maintained wiki from WikiDocGenerator — it always writes to `WikiDocGenerator:TargetWikiId`

---

## Testing guidance

When adding or changing behavior, prefer tests around:

- Azure DevOps service integration boundaries (Core)
- Azure AI Search upload/search behavior (Core)
- pipeline stage message flow (apps)
- report/wiki generation flow (apps)
- error handling for missing external data
- ETag conflict and retry paths in `CreateOrUpdateWikiPageAsync`

If no tests exist nearby, still keep the implementation testable: small methods, clear interfaces, minimal hidden side effects.

---

## Output style for code changes

When making changes:

- produce the **smallest reasonable diff**
- preserve naming and folder conventions of the touched project
- match the existing C# style in the touched file
- keep comments minimal and useful; prefer them when explaining a non-obvious decision (e.g. why a watermark uses run-start time)
- do not add explanatory documentation unless requested

If requirements are ambiguous, choose the option that best fits the existing architecture and patterns in this repository, and confirm the working-scope rule above before touching cross-app code.

---

## Tool-Specific Guidelines

### AnalyzeRepositoryFileTool
- Prefer support for files `.cs`, `.js`, `.ts`, `.jsx`, `.tsx`, and `.html`.
- Focus on documentation for Dynamics 365 projects (C# plugins/applications and web resources/PCF in JS/TS/HTML).
