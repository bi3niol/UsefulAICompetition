# Copilot Instructions for DevOps Impact Analyzer

## Purpose

Work in this repository as a **code agent**, not as a documentation writer.

This project is a **.NET 10 Azure Functions app** that analyzes Azure DevOps work items using a
**4-agent pipeline** and produces an impact analysis report.

Primary flow:

- `GenerateWorkItemReportFunction` fetches a work item, runs `ImpactAnalysisPipeline`, saves the markdown report through `IReportStore`, and returns it.
- `GetWorkItemReportFunction` returns the stored report from Blob Storage.

Secondary flow:

- Two **4-stage indexing pipelines** push Azure DevOps work items and WIKI pages into Azure AI Search.
- Stages 2–4 use **Claim-Check** with Blob Storage and send only `BlobRefMessage` through Service Bus.

---

## Repository priorities

When changing code, optimize for:

1. **Correctness**
2. **Project consistency**
3. **Reuse of existing abstractions**
4. **Operational reliability**
5. **Small, targeted diffs**

Do not redesign working areas without a clear reason.

---

## Architecture summary

### Analysis pipeline
`Researcher -> Writer -> Editor -> Sender`

Main files:

- `ImpactAnalysisPipeline.cs`
- `AgentPrompts.cs`
- `Functions/GenerateWorkItemReportFunction.cs`
- `Functions/GetWorkItemReportFunction.cs`

### Indexing pipelines
Work item pipeline:

1. `WorkItemIndexerFunction`
2. `WorkItemFetchFunction`
3. `WorkItemBuildDocumentsFunction`
4. `WorkItemUploadFunction`

WIKI pipeline:

1. `WikiIndexerFunction`
2. `WikiPageFetchFunction`
3. `WikiBuildDocumentsFunction`
4. `WikiUploadFunction`

### Shared infrastructure

- Azure Functions isolated worker
- Azure Service Bus
- Azure Blob Storage
- Azure Tables
- Azure AI Search
- Azure OpenAI / AI Foundry
- Azure DevOps REST API

---

## Non-negotiable project rules

### 1. Follow the existing pipeline model
- Keep **one Function class per pipeline stage**.
- Do **not** call downstream stages directly.
- Communicate between stages through **Service Bus output bindings**.

### 2. Preserve Claim-Check design
- Stage 1 queues carry small plain messages.
- Stages 2–4 must store large payloads in Blob Storage through `IBlobMessageStore`.
- Service Bus payload for those stages should remain `BlobRefMessage`.

### 3. Reuse existing services
Prefer extending existing services before adding new ones:

- `AzureDevOpsService`
- `AzureSearchService`
- `EmbeddingService`
- `BlobMessageStore`
- `ReportStore`
- `SettingsStore`

### 4. Use DI and configuration only
- Register new services in `ToolsConfig.AddImpactAnalyzerTools()` or existing DI setup.
- Read settings from `IConfiguration`.
- Never hardcode endpoints, queue names, model names, container names, or secrets.

### 5. Keep auth model unchanged
- Use `DefaultAzureCredential` for Azure resources.
- Do not introduce connection strings for Blob, Tables, Search, Service Bus, OpenAI, or Foundry.
- Azure DevOps PAT is the only expected secret-based auth.

### 6. Respect persistence conventions
- Claim-check blobs go to container: `messages`
- Reports go to container: `reports`
- Runtime KV config goes to table: `Settings`
- New blob path patterns must be added in `BlobPaths`

### 7. Respect existing throttling approach
- Throttling belongs in `host.json`
- Do **not** introduce `SemaphoreSlim`-based concurrency control for Service Bus handlers unless there is a very specific reason

---

## Agent and tool rules

### Tool implementation
All agent tools must:

- use `[AgentTool(Description = "...")]`
- be `async`
- return `Task<string>`
- return **JSON strings**
- be registered through DI

### Tool placement
- Research tools go under `Tools/Research/`
- Sender tools go under `Tools/Sender/`
- Shared helper services go under `Tools/Shared/`

### Prompt changes
If changing prompts in `AgentPrompts.cs`:

- preserve the role separation between Researcher, Writer, Editor, and Sender
- avoid making Writer or Editor depend on tools if they currently do not
- keep output expectations deterministic and easy to parse

---

## Data and indexing rules

### Chunking
Reuse `StringExtensions.SplitIntoChunks(...)`.

Current limits:

- Work items: max `8000` chars per chunk
- WIKI pages: max `6000` chars per chunk

Do not invent a second chunking implementation unless necessary.

### Upload sizing
- Azure AI Search uploads: max `500` documents per batch
- Work item ID batches: max `200` IDs

### Missing/broken data handling
For fetch stages:
- prefer returning `null` / no downstream message for missing or broken external data
- avoid poison-message loops and DLQ storms

### Watermarks
Persist indexing watermarks via `ISettingsStore` using stable `indexer.*` keys.

Do not create ad hoc blobs or tables for tiny config values.

---

## Important domain assumptions

The system analyzes newly created or changed Azure DevOps work items and tries to find:

- **Conflicts**
- **Dependencies**
- **Related work**
- **Relevant WIKI / architecture references**

Relevant models are primarily in:

- `Models/PipelineModels.cs`
- `Models/DevOpsModels.cs`
- `Models/SearchModels.cs`
- `Models/SettingEntity.cs`

If adding fields, prefer extending existing models rather than creating duplicate DTO shapes.

---

## File map for common tasks

### HTTP/API changes
Look at:

- `Functions/GenerateWorkItemReportFunction.cs`
- `Functions/GetWorkItemReportFunction.cs`
- `Program.cs`

### Agent behavior changes
Look at:

- `ImpactAnalysisPipeline.cs`
- `AgentPrompts.cs`
- `Tools/Research/*`
- `Tools/Sender/*`

### Azure DevOps integration
Look at:

- `Tools/Shared/AzureDevOpsService.cs`

### Search behavior
Look at:

- `Tools/Shared/AzureSearchService.cs`
- `Indexing/SearchIndexManager.cs`
- `Indexing/WorkItemSearchUploader.cs`
- `Indexing/WikiSearchUploader.cs`

### Indexing pipeline changes
Look at:

- `Functions/WorkItem*`
- `Functions/Wiki*`
- `Indexing/*`
- `Indexing/Messages/*`
- `Stores/BlobMessageStore.cs`

### Storage changes
Look at:

- `Stores/BlobMessageStore.cs`
- `Stores/ReportStore.cs`
- `Stores/SettingsStore.cs`

### Infrastructure changes
Look at:

- `infra/main.bicep`
- `infra/modules/functionapp.bicep`
- `infra/modules/servicebus.bicep`
- `infra/modules/ai.bicep`

---

## Preferred change patterns

### When adding a new indexing stage
- add a new message type only if the existing message contract is insufficient
- add blob path generation to `BlobPaths`
- keep the stage isolated in its own Function class
- keep payloads serializable and storage-friendly

### When adding a new tool
- place it in the correct tool folder
- annotate with `[AgentTool]`
- return JSON, not prose
- register it in DI
- wire it only into the agents that should use it

### When adding a new setting
- add it to configuration classes / access paths consistently
- document expected config shape if needed
- prefer existing config sections before inventing new top-level sections

### When adding a new Azure resource dependency
- first check whether an existing service can absorb the capability
- if a new resource is truly required, reflect it in Bicep and app configuration consistently

---

## Avoid these mistakes

Do not:

- turn the instruction file into a README
- add broad architecture rewrites unless requested
- bypass `IReportStore`, `IBlobMessageStore`, or `ISettingsStore`
- hardcode queue names, container names, or endpoints in random files
- duplicate chunking, search, or DevOps access logic
- introduce a second storage account pattern
- replace Claim-Check with large Service Bus messages
- mix unrelated refactoring with a small feature request

---

## Testing guidance

When adding or changing behavior, prefer tests around:

- Azure DevOps service integration boundaries
- Azure AI Search upload/search behavior
- pipeline stage message flow
- report generation flow
- error handling for missing external data

If no tests exist nearby, still keep the implementation testable:
- small methods
- clear interfaces
- minimal hidden side effects

---

## Output style for code changes

When making changes:

- produce the **smallest reasonable diff**
- preserve naming and folder conventions
- match the existing C# style in the touched file
- keep comments minimal and useful
- do not add explanatory documentation unless requested

If requirements are ambiguous, choose the option that best fits the existing architecture and patterns in this repository.