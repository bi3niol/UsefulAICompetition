# BSolution.Netwise.UsefulAI.WikiDocGenerator.App

## Overview

Azure Functions app (.NET 10, isolated worker) that **automatically generates and maintains Azure DevOps Wiki documentation** based on source code, work items, and merged pull requests.

Uses a 4-agent LLM pipeline (Researcher -> Writer -> Editor -> Sender) powered by Azure AI Foundry to produce and update wiki pages in a **dedicated generated wiki** (separate from any manually-maintained wiki).

---

## Architecture

### 4-stage processing (Service Bus + Claim-Check)

To minimize API rate-limit risk and avoid Function timeouts, the pipeline is split
into **4 independent stages** connected via Service Bus queues. Large payloads
between stages use **Claim-Check** pattern (Blob Storage), identical to Impact Analyzer.

| Stage | Function | Queue consumed | Queue produced | Responsibility |
|-------|----------|----------------|----------------|----------------|
| 1a | `WikiRefreshTimerFunction` | *(timer)* | `wikigen-pipeline` | Query changed WI IDs, enqueue batches |
| 1b | `PullRequestWebhookFunction` | *(HTTP)* | `wikigen-pipeline` | Parse PR webhook, enqueue message |
| 2 | `WikiDocResearchFunction` | `wikigen-pipeline` | `wikigen-write` | Run Researcher agent, store findings blob |
| 3 | `WikiDocWriteFunction` | `wikigen-write` | `wikigen-send` | Run Writer+Editor loop, store draft blob |
| 4 | `WikiDocSendFunction` | `wikigen-send` | *(none)* | Run Sender agent, upsert wiki pages |

**Benefits:**
- Each stage has its own timeout, retry policy, and DLQ.
- Rate limit (429) on one LLM call doesn't block other stages.
- Between Stage 2→3 and 3→4, payload is stored as blob (`BlobRefMessage` on queue).
- Stage 1 functions complete in seconds; Stage 2/3/4 run under generous lock renewal.

### Service Bus queues

| Queue | Message type | Payload |
|-------|-------------|---------|
| `wikigen-pipeline` | `WikiGenPipelineMessage` | Small (IDs + metadata) |
| `wikigen-write` | `BlobRefMessage` | URI to findings blob |
| `wikigen-send` | `BlobRefMessage` | URI to draft blob |

### Blob paths (container: `messages`)

- `wikigen/findings/{date}/{slug}_{uid}.json` — Researcher output
- `wikigen/drafts/{date}/{slug}_{uid}.json` — Writer+Editor output

### Pipeline agents (per stage)

```
Stage 2: Researcher (tools: code, wiki, work items)
Stage 3: Writer → Editor (retry loop, max 2 iterations)
Stage 4: Sender (tool: UpsertWikiPage)
```

- **Researcher**: Reads code, work items, existing wiki pages; decides update vs create.
- **Writer**: Produces full markdown content for each page.
- **Editor**: Quality gate (approve / reject with feedback).
- **Sender**: Calls `CreateOrUpdateWikiPageAsync` with ETag-based optimistic concurrency.

### Entry points (Stage 1)

| Trigger | Source | Queue message |
|---------|--------|---------------|
| Daily timer (02:00 UTC) | Changed Feature/Epic/User Story/PBI | `WikiGenSource.WorkItems` |
| PR webhook | `git.pullrequest.merged` | `WikiGenSource.PullRequest` |
| *(future)* Code scan timer | Incremental code diff per repo | `WikiGenSource.CodeScan` |

---

## Configuration

### Required settings (local.settings.json / App Settings)

- AzureDevOps:Organization
- AzureDevOps:Project
- AzureDevOps:PersonalAccessToken (PAT with Code+Wiki+WorkItems read/write)
- WikiDocGenerator:TargetWikiId (GUID of the generated wiki)
- Foundry:Endpoint
- AzureWebJobsStorage__accountName
- ServiceBus__fullyQualifiedNamespace
- AzureSearch:Endpoint

### Code scan settings (optional, enables code-based documentation)

- WikiDocGenerator:Code:Repositories:0:Name = MyRepo
- WikiDocGenerator:Code:Repositories:0:Branch = main
- WikiDocGenerator:Code:IncludeExtensions (list of extensions like .cs, .csproj, .bicep, .md, .yml, .yaml, .json)
- WikiDocGenerator:Code:ExcludeFolders (list: bin, obj, node_modules, .vs, .git, dist, out, TestResults, packages)
- WikiDocGenerator:Code:MaxFileBytes = 200000
- WikiDocGenerator:Code:MaxFilesPerPipelineRun = 40

### Pipeline model selection (optional)

- Pipeline:ResearcherModel (default: o4-mini)
- Pipeline:WriterModel (default: o4-mini)
- Pipeline:EditorModel (default: gpt-4o)
- Pipeline:SenderModel (default: gpt-4o)

---

## Service Bus queues

- `wikigen-pipeline` — entry point (from timer/webhook), carries `WikiGenPipelineMessage`
- `wikigen-write` — Research→Write handoff, carries `BlobRefMessage`
- `wikigen-send` — Write→Send handoff, carries `BlobRefMessage`
- Throttling configured in `host.json` (maxConcurrentCalls: 4)

---

## Watermarks

Stored in Azure Table Settings (partition: settings):

| Key | Purpose |
|-----|---------|
| wikigen.workitems.lastSync | Last successful timer run (DateTimeOffset) |
| wikigen.code.{repoId}.{branch}.lastSha | Last scanned commit SHA per repo (future) |

Watermarks record the **run start time** (not end), so items changed during processing are not lost.

---

## Key design decisions

1. **Separate generated wiki** — never writes to the manually-maintained wiki consumed by Impact Analyzer.
2. **4-stage pipeline via Service Bus** — each LLM call runs in its own Function invocation with independent timeout, retry, and DLQ. Minimizes risk of hitting API rate limits or Function timeout.
3. **Claim-Check** — large payloads (findings, drafts) stored in Blob Storage; only URI on Service Bus.
4. **ETag optimistic concurrency** — `CreateOrUpdateWikiPageAsync` auto-retries on 412 (stale ETag).
5. **Researcher decides page routing** — callers never specify target wiki paths; the LLM groups topics and picks existing pages or proposes new ones.
6. **Branch from config** — code is always read from a configured branch, never from arbitrary user input.
7. **Filter-first** — only allowed extensions, excluded folders, and max file size reach the LLM context.
8. **Failure isolation** — a failing Service Bus message goes to DLQ; other messages/stages continue.

---

## Project structure

- `Configs/` — DI registration
- `Functions/` — Stage 1 (timer/webhook) + Stages 2-4 (SB consumers)
- `Messages/` — Service Bus message contracts (`WikiGenPipelineMessage`, `BlobRefMessage`)
- `Models/` — Pipeline input/output records + `CodeScanOptions`
- `Services/` — `CodeRepositoryResolver`, `CodeFileFilter`
- `Stores/` — `BlobPaths` (claim-check blob path generation)
- `Tools/Research/` — Agent tools for Researcher
- `Tools/Sender/` — Agent tools for Sender
- `WikiDocGenerationPipeline.cs` — 4-agent orchestrator (public per-stage methods)
- `WikiDocAgentPrompts.cs` — System prompts per agent role

---

## Relationship to other projects

- **BSolution.Netwise.UsefulAI.Core** - shared library (DevOps API, Search, Embedding, Stores, WorkItemQueryService).
- **BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App** - independent app; NOT modified by WikiDocGenerator work.

---

## Next steps (planned)

- CodeRefreshTimerFunction - daily incremental scan of configured repos (diff-based via commit SHA watermark).
- Code summarization stage - LLM converts raw code into descriptive text before indexing.
- Azure AI Search index (wikigen-code) for semantic search over code descriptions.
- Multi-repo orchestration for CodeScan source type.
