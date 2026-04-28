# DevOps Impact Analyzer — Browser Extension

Browser extension (Manifest V3, Chrome / Edge) that injects a side panel into the
Azure DevOps work item page and calls the `AnalyzeWorkItemFunction` endpoint of the
backend Function App to display the impact analysis report.

## Architecture

The code is split into a **framework-agnostic core** and **adapters**:

```
src/
├── core/                          ← React + TS, no chrome.* / no DevOps SDK
│   ├── ports/IWorkItemHost.ts     ← port for any host
│   ├── services/                  ← ImpactAnalysisClient (fetch backend)
│   ├── hooks/                     ← useWorkItemContext, useImpactAnalysis
│   ├── components/                ← ImpactPanel, AnalysisReport, …
│   └── markdown/                  ← markdown-it + DOMPurify
└── adapters/
    └── browser/                   ← chrome.* + DOM injection
        ├── BrowserWorkItemHost.ts ← implements IWorkItemHost
        ├── content-script.tsx     ← injects panel into dev.azure.com
        ├── options.html / .tsx    ← settings page (URL, key, PAT)
        └── storage.ts             ← chrome.storage.sync wrapper
```

Adding a DevOps Extension adapter later = new folder under `adapters/`,
zero changes in `core/`.

## Prerequisites

- Node.js 18+
- npm

## Develop

```powershell
npm install
npm run dev      # Vite dev server + HMR (CRXJS)
```

Then in Chrome / Edge:

1. `chrome://extensions` → enable **Developer mode**.
2. **Load unpacked** → select the `dist/` folder (created by `npm run dev`).
3. Open Extension **Options** and set:
   - **Function App URL** — e.g. `http://localhost:7071` for local Functions.
   - **Function Key** — leave empty for local, set in production.
   - **PAT** (optional) — Azure DevOps Personal Access Token with `Work Items: Read`.
     Without it, only the work item ID is forwarded to the backend.
4. Open any work item, e.g. `https://dev.azure.com/{org}/{project}/_workitems/edit/123`.

## Build for distribution

```powershell
npm run build    # → dist/
```

Zip `dist/` and distribute, or load unpacked.

## Backend CORS

The Function App must allow requests from extension origins. Add to
`local.settings.json` for local dev:

```json
"Host": {
  "CORS": "*"
}
```

In Azure: Function App → CORS → add `chrome-extension://*` or your extension ID.
