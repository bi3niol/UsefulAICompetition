import * as React from "react";
import * as ReactDOM from "react-dom";

import "azure-devops-ui/Core/override.css";
import "../../styles/panel.scss";

import { ImpactPanel } from "../../core/components/ImpactPanel";
import { ImpactAnalysisClient } from "../../core/services/ImpactAnalysisClient";
import { BrowserWorkItemHost } from "./BrowserWorkItemHost";
import { ExtensionSettings, getSettings, onSettingsChanged } from "./storage";

const WORK_ITEM_URL_RE = /_workitems\/edit\/\d+|[?&]id=\d+/;

let host: BrowserWorkItemHost | null = null;
let settings: ExtensionSettings | null = null;
let mounted = false;
let userClosed = false;

function buildClient(s: ExtensionSettings): ImpactAnalysisClient {
  return new ImpactAnalysisClient({
    functionUrl: s.functionUrl,
    functionKey: s.functionKey
  });
}

async function showPanel(): Promise<void> {
  if (mounted) return;
  if (!host) host = new BrowserWorkItemHost();
  await host.ready();
  if (!settings) settings = await getSettings();

  const mount = host.getMountPoint();
  ReactDOM.render(
    <ImpactPanel host={host} client={buildClient(settings)} onClose={hidePanel} />,
    mount
  );
  mounted = true;
}

function hidePanel(): void {
  if (!mounted || !host) return;
  const mount = host.getMountPoint();
  ReactDOM.unmountComponentAtNode(mount);
  host.dispose();
  host = null;
  mounted = false;
  userClosed = true;
}

async function togglePanel(): Promise<void> {
  if (mounted) {
    hidePanel();
  } else {
    userClosed = false;
    await showPanel();
  }
}

async function maybeAutoShow(): Promise<void> {
  if (userClosed || mounted) return;
  if (!WORK_ITEM_URL_RE.test(location.href)) return;
  await showPanel();
}

// Re-render existing panel when settings change.
onSettingsChanged((updated) => {
  settings = updated;
  if (!mounted || !host) return;
  ReactDOM.render(
    <ImpactPanel host={host} client={buildClient(updated)} onClose={hidePanel} />,
    host.getMountPoint()
  );
});

// Listen for clicks on the toolbar icon (forwarded by background.ts).
chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (msg?.type === "TOGGLE_PANEL") {
    void togglePanel().then(() => sendResponse({ ok: true }));
    return true; // async response
  }
  return undefined;
});

// Auto-show on initial load if we are on a work item page.
void maybeAutoShow();

// React to SPA navigation inside Azure DevOps.
const fireNav = () => window.dispatchEvent(new Event("impact:navigation"));
const origPush = history.pushState;
const origReplace = history.replaceState;
history.pushState = function (...args) {
  const r = origPush.apply(this, args as never);
  fireNav();
  return r;
};
history.replaceState = function (...args) {
  const r = origReplace.apply(this, args as never);
  fireNav();
  return r;
};
window.addEventListener("popstate", fireNav);
window.addEventListener("impact:navigation", () => void maybeAutoShow());
