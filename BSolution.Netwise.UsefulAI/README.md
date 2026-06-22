# DevOps Impact Analyzer

## 🎯 Purpose

`BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App` is a .NET 10 Azure Functions app that analyzes Azure DevOps work items with a 4-agent pipeline and produces a structured markdown report for developers, analysts and delivery teams.

Its goal is not just to summarize a work item, but to **research surrounding context** and highlight what may matter before implementation or bug fixing starts.

Depending on the work item type, the app produces one of two report styles:

### 🧩 Standard impact analysis for requirements and delivery items
For items such as User Story, Product Backlog Item, Feature, Epic, Requirement, Task or similar work, the pipeline focuses on:
- ⚠️ detecting potential conflicts with existing requirements
- 🔗 identifying dependencies, related work and impacted areas
- 📚 finding relevant WIKI pages, architecture notes and technical decisions
- 💡 recommending concrete follow-up actions before implementation
- 🔍 documenting research coverage, so the team can see which search angles were used

The resulting report is an **impact analysis report** with sections centered on:
- conflicts detected
- dependencies
- related WIKI pages
- recommendations
- research coverage

### 🐛 Bug-oriented diagnosis flow
For `Bug` work items, the pipeline switches to a different research and writing strategy. Instead of focusing mainly on requirement conflicts, it emphasizes:
- 🔁 finding similar bugs, especially resolved ones
- 🧩 locating related PBIs, User Stories or Features that introduced the affected behavior
- 📚 gathering architecture and troubleshooting documentation for the impacted area
- 🔍 proposing evidence-based root-cause hypotheses
- 💡 suggesting concrete investigation or fix approaches

The resulting report is a **bug diagnosis report** with sections centered on:
- similar bugs
- related work items
- relevant architecture and documentation
- possible root causes
- suggested solutions
- research coverage

### 🤖 What the agent pipeline is designed to do
The prompts define a strict role split:
- `Researcher` searches broadly across work items and WIKI using multiple query angles
- `Writer` transforms findings into a structured, linked markdown report
- `Editor` checks completeness, usefulness, language consistency and missing references
- `Sender` is intended to post the approved result back to Azure DevOps

The app cooperates with `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension`, which is the current consumer of its HTTP endpoints.

---

## 📦 Projects in scope

### `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App`
Owns:
- HTTP endpoints for generating and retrieving reports
- the 4-agent analysis pipeline
- work item and WIKI indexing pipelines
- Azure AI Search index bootstrap
- report persistence in Blob Storage

### `BSolution.Netwise.UsefulAI.Core`
Shared infrastructure used by Impact Analyzer:
- Azure DevOps integration
- Azure AI Search runtime queries
- embedding generation
- WIQL work item querying
- claim-check blob storage
- settings store in Azure Tables

### `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension`
Browser extension that calls the backend and shows the generated report in Azure DevOps.

---

## 🧩 Browser extension

`BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension/` is a **Manifest V3 Chrome/Edge browser extension** built with **React + TypeScript**.

It injects an Impact Analyzer panel into the Azure DevOps work item page and calls the Function App backend to generate or retrieve the report.

### What it does
- 🖥️ embeds a dedicated UI directly inside the Azure DevOps work item experience
- 📡 calls the backend HTTP endpoints exposed by the Impact Analyzer app
- 📝 renders the returned markdown report for the user
- ⚙️ stores extension settings such as backend URL and credentials

### High-level structure

```text
BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension/
├── src/
│   ├── core/
│   │   ├── ports/
│   │   ├── services/
│   │   ├── hooks/
│   │   ├── components/
│   │   └── markdown/
│   └── adapters/
│       └── browser/
├── manifest.json
├── package.json
└── vite.config.ts
```

Key browser-side pieces include:
- `ImpactAnalysisClient` — calls the backend
- `content-script.tsx` — injects the panel into `dev.azure.com`
- `BrowserWorkItemHost` — bridges browser/Azure DevOps page context into the app UI
- `options.tsx` — extension configuration page
- `storage.ts` — persistence for extension settings

---

## 🧱 Technology stack

| Area | Technology |
|---|---|
| Language | C# / .NET 10 |
| Hosting | Azure Functions isolated worker |
| Agent runtime | Microsoft.Agents.AI + Azure AI Foundry |
| LLM models | Configured per pipeline stage via `Pipeline:*Model` |
| Embeddings | Azure OpenAI (`text-embedding-3-large` by default) |
| Search | Azure AI Search |
| Messaging | Azure Service Bus |
| Large message handling | Azure Blob Storage claim-check |
| Runtime settings | Azure Tables (`Settings`) |
| DevOps integration | Azure DevOps REST API |
| Auth | `DefaultAzureCredential` for Azure resources, PAT for Azure DevOps |
| IaC | Bicep |

---

## 🔄 Analysis flow

Impact analysis is exposed through two HTTP endpoints:

- `POST /api/workitems/{workItemId}/report`
  - fetches the work item from Azure DevOps
  - runs the analysis pipeline synchronously
  - stores the resulting markdown in Blob Storage via `IReportStore`
  - returns the markdown response
- `GET /api/workitems/{workItemId}/report`
  - returns the previously stored markdown report
  - returns `404` when no report exists yet

### Pipeline

`ImpactAnalysisPipeline` runs a 4-agent flow:

`Researcher → Writer → Editor → Sender`

Current runtime behavior:
- `Researcher` gathers related work items and WIKI context using tools
- `Writer` produces the markdown report
- `Editor` reviews the report and can request up to 2 rewrites
- `Sender` tooling exists, but posting the comment is currently disabled in `RunAsync`
- the approved report is returned to the caller and persisted by `GenerateWorkItemReportFunction`

### Models per stage

Configured in `appsettings.json` under:
- `Pipeline:ResearcherModel`
- `Pipeline:WriterModel`
- `Pipeline:EditorModel`
- `Pipeline:SenderModel`

Current defaults in the repo:
- Researcher: `o4-mini`
- Writer: `o4-mini`
- Editor: `gpt-4o`
- Sender: `gpt-4o`

---

## Agent tools

### Research tools
Located in `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/Tools/Research/`:
- `SearchWorkItemsTool`
- `KeywordSearchWorkItemsTool`
- `SearchWikiTool`
- `GetWorkItemDetailsTool`
- `GetWikiPageDetailsTool`
- `ResearchTools`

### Writer tools
Located in `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/Tools/Writer/`:
- `WriterTools`
  - currently exposes `GetCurrentDate`

### Sender tools
Located in `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/Tools/Sender/`:
- `PostCommentTool`
- `SenderTools`

Tool conventions used by this app:
- tools are registered through DI in `Configs/ToolsConfig.cs`
- reusable infrastructure is taken from `BSolution.Netwise.UsefulAI.Core`
- sender tools exist even though comment posting is currently not invoked by the main pipeline flow

---

## Report storage

Impact reports are stored by `ReportStore` in Blob Storage:

- container: `reports`
- blob name: `{workItemId}.md`
- content type: `text/markdown; charset=utf-8`

The blob name is deterministic, so a newly generated report overwrites the previous one for the same work item.

---

## Indexing pipelines

Impact Analyzer maintains two Azure AI Search indexes:
- `work-items-index`
- `wiki-pages-index`

Both indexing flows use a staged Service Bus pipeline.

### Claim-check design

- stage 1 queues carry small plain messages
- stages 2-4 store larger payloads in Blob Storage via `IBlobMessageStore`
- Service Bus then carries only `BlobRefMessage { BlobUri }`
- claim-check payloads are stored in container `messages`

### Work item indexing

Functions:
- `WorkItemIndexerFunction`
- `WorkItemFetchFunction`
- `WorkItemBuildDocumentsFunction`
- `WorkItemUploadFunction`

Flow:
1. `WorkItemIndexerFunction`
   - timer trigger: `0 0 0 * * *`
   - `RunOnStartup = true`
   - reads watermark `indexer.workitems.lastSync` from `ISettingsStore`
   - queries IDs through `IWorkItemQueryService`
   - emits `WorkItemIdsBatchMessage` batches of up to 100 IDs
2. `WorkItemFetchFunction`
   - fetches work item details and comments from Azure DevOps
   - writes `WorkItemDetailMessage` payloads to blob storage
   - emits `BlobRefMessage`
3. `WorkItemBuildDocumentsFunction`
   - builds `WorkItemIndexDocument` chunks
   - uses shared chunking helper from Core
   - stores document payloads in blob storage
   - emits `BlobRefMessage`
4. `WorkItemUploadFunction`
   - uploads documents to Azure AI Search
   - batch size up to 500 documents

Indexed work item types are configured in `ToolsConfig.cs` and currently include:
- User Story
- Product Backlog Item
- Bug
- Task
- Epic
- Feature
- Requirement

### WIKI indexing

Functions:
- `WikiIndexerFunction`
- `WikiPageFetchFunction`
- `WikiBuildDocumentsFunction`
- `WikiUploadFunction`

Flow:
1. `WikiIndexerFunction`
   - timer trigger: `0 0 */4 * * *`
   - `RunOnStartup = true`
   - reads watermark `indexer.wiki.lastSync`
   - enumerates WIKI pages through `IWikiPageQueryService`
   - emits `WikiPageRefMessage`
2. `WikiPageFetchFunction`
   - downloads WIKI page content from Azure DevOps
   - stores `WikiPageContentMessage` in blob storage
   - emits `BlobRefMessage` or no downstream message for missing/broken pages
3. `WikiBuildDocumentsFunction`
   - builds `WikiIndexDocument` chunks
   - stores document payloads in blob storage
   - emits `BlobRefMessage`
4. `WikiUploadFunction`
   - uploads documents to Azure AI Search

The WIKI pipeline supports incremental sync based on the persisted watermark and can fall back to full sync when incremental metadata is unavailable.

### Blob path conventions

App-specific blob paths are generated in `BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/Stores/BlobPaths.cs`.

Current subfolders:
- `workitem-details/`
- `workitem-documents/`
- `wiki-pages/`
- `wiki-documents/`

Pattern:
- `{subfolder}/{yyyy-MM-dd}/{identity}_{guid8}.json`

---

## Search index bootstrap

`SearchIndexManager` is registered as a hosted service and runs on app startup.

It ensures both indexes exist with:
- HNSW vector search
- cosine distance
- 3072-dimensional vectors
- semantic configuration `devops-semantic-config`

Current implementation detail:
- runtime search access in `AzureSearchService` supports `DefaultAzureCredential` with optional API key fallback
- index creation in `SearchIndexManager` currently uses `AzureSearch:ApiKey`

---

## Shared services from Core used by Impact Analyzer

### `BSolution.Netwise.UsefulAI.Core/Services`
- `AzureDevOpsService`
- `AzureSearchService`
- `EmbeddingService`
- `WorkItemQueryService`

### `BSolution.Netwise.UsefulAI.Core/Stores`
- `BlobMessageStore`
- `SettingsStore`
- `BlobPathHelpers`

### `BSolution.Netwise.UsefulAI.Core/Configuration`
- `CoreServicesRegistration.AddUsefulAICoreServices()`

Impact Analyzer wires these shared services in `AddImpactAnalyzerTools()` and adds its own app-specific registrations on top.

---

## Configuration

### App settings used by Impact Analyzer

#### Foundry
- `Foundry:Endpoint`

#### Pipeline model selection
- `Pipeline:ResearcherModel`
- `Pipeline:WriterModel`
- `Pipeline:EditorModel`
- `Pipeline:SenderModel`

#### Azure OpenAI
- `AzureOpenAI:Endpoint`
- `AzureOpenAI:EmbeddingDeployment`
- optional local fallback: `AzureOpenAI:ApiKey`

#### Azure AI Search
- `AzureSearch:Endpoint`
- currently required for index bootstrap: `AzureSearch:ApiKey`

#### Azure DevOps
- `AzureDevOps:Organization`
- `AzureDevOps:Project`
- `AzureDevOps:PersonalAccessToken`

#### Azure Functions / shared Azure resources
- `AzureWebJobsStorage` or `AzureWebJobsStorage__accountName`
- `ServiceBus__fullyQualifiedNamespace`

### Storage and settings conventions

- claim-check blob container: `messages`
- report blob container: `reports`
- settings table: `Settings`
- work item watermark key: `indexer.workitems.lastSync`
- WIKI watermark key: `indexer.wiki.lastSync`

Watermarks store the run start timestamp so incremental runs do not miss changes that happen during processing.

---

## Solution structure relevant to Impact Analyzer

```text
BSolution.Netwise.UsefulAI.Core/
├── Configuration/
├── Models/
├── Services/
└── Stores/

BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/
├── Configs/
├── Functions/
├── Indexing/
│   └── Messages/
├── Models/
├── Stores/
├── Tools/
│   ├── Research/
│   ├── Sender/
│   └── Writer/
├── AgentPrompts.cs
├── ImpactAnalysisPipeline.cs
├── Program.cs
├── appsettings.json
└── host.json

BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension/
└── src/
```

---

## Important implementation notes

- `Program.cs` creates `AIProjectClient` with `DefaultAzureCredential`
- `ToolsConfig.cs` calls `AddUsefulAICoreServices()` and then registers Impact Analyzer-specific services
- `ImpactAnalysisPipeline.RunAsync()` currently returns the approved report and does not invoke `RunSenderAsync()`
- Service Bus throttling is configured in `host.json` with:
  - `maxConcurrentCalls = 4`
  - `prefetchCount = 8`
  - `maxAutoLockRenewalDuration = 01:05:00`
- `WorkItemQueryService` uses WIQL and day-level date precision for incremental queries
- `ReportStore` and claim-check storage reuse the Functions storage account

---

## Current HTTP entry points

### `GenerateWorkItemReportFunction`
- route: `POST /api/workitems/{workItemId}/report`
- auth level: `Function`
- behavior:
  - validates ID
  - fetches work item details from Azure DevOps
  - maps to `WorkItemEvent`
  - runs the impact analysis pipeline
  - saves the markdown report
  - returns `text/markdown`

### `GetWorkItemReportFunction`
- route: `GET /api/workitems/{workItemId}/report`
- auth level: `Function`
- behavior:
  - validates ID
  - loads the stored report from `IReportStore`
  - returns `404` when missing
  - returns `text/markdown` when found

### `HttpTestFunction`
- ad-hoc development endpoint for manual testing

---

## Notes for future changes

When updating Impact Analyzer:
- keep one Function class per pipeline stage
- do not call downstream stages directly
- keep claim-check for stages 2-4 of indexing pipelines
- prefer extending Core services instead of duplicating logic
- register new services in `Configs/ToolsConfig.cs`
- keep configuration in `IConfiguration`
- do not hardcode endpoints, queue names, container names, or secrets
- keep changes scoped to Impact Analyzer unless a strictly necessary Core extraction is required
