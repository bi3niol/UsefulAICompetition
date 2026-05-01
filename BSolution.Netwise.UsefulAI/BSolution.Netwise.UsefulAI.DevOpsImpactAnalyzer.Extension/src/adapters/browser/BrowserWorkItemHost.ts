import {
  HostCapabilities,
  IWorkItemHost,
  WorkItemChangedHandler
} from "../../core/ports/IWorkItemHost";
import { WorkItemContext } from "../../core/types";

/**
 * Browser-extension host. Detects the work item ID from the URL and (optionally)
 * fetches its fields via Azure DevOps REST API using a PAT stored in chrome.storage.
 *
 * The DevOps single-page app changes URL via History API without reloading, so we
 * patch pushState/replaceState and listen to popstate to detect navigation.
 */
export class BrowserWorkItemHost implements IWorkItemHost {
  readonly capabilities: HostCapabilities = {
    ownsPanelChrome: false,
    providesAuthToken: false
  };

  private mount: HTMLElement | null = null;
  private currentId: number | null = null;
  private navListeners: Array<() => void> = [];

  async ready(): Promise<void> {
    if (this.mount) return;
    const container = document.createElement("div");
    container.id = "impact-analyzer-root";
    container.style.cssText = [
      "position:fixed",
      "top:64px",
      "right:0",
      "width:480px",
      "height:calc(100vh - 80px)",
      "background:#fff",
      "border-left:1px solid #e1e4e8",
      "box-shadow:-4px 0 12px rgba(0,0,0,0.08)",
      "z-index:2147483646",
      "overflow:hidden",
      "font-family:'Segoe UI', system-ui, sans-serif"
    ].join(";");
    document.body.appendChild(container);
    this.mount = container;
  }

  async getCurrentWorkItem(): Promise<WorkItemContext | null> {
    const id = this.parseWorkItemIdFromUrl(location.href);
    this.currentId = id;
    if (id == null) return null;
    return { id, url: location.href };
  }

  onWorkItemChanged(handler: WorkItemChangedHandler): () => void {
    const fire = async () => {
      const id = this.parseWorkItemIdFromUrl(location.href);
      if (id === this.currentId) return;
      const ctx = await this.getCurrentWorkItem();
      if (ctx) handler(ctx);
    };

    // Patch History API once.
    const wnd = window as unknown as { __impactPatched?: boolean };
    if (!wnd.__impactPatched) {
      const originalPush = history.pushState;
      const originalReplace = history.replaceState;
      history.pushState = function (...args) {
        const r = originalPush.apply(this, args as never);
        window.dispatchEvent(new Event("impact:locationchange"));
        return r;
      };
      history.replaceState = function (...args) {
        const r = originalReplace.apply(this, args as never);
        window.dispatchEvent(new Event("impact:locationchange"));
        return r;
      };
      window.addEventListener("popstate", () =>
        window.dispatchEvent(new Event("impact:locationchange"))
      );
      wnd.__impactPatched = true;
    }

    window.addEventListener("impact:locationchange", fire);
    const off = () => window.removeEventListener("impact:locationchange", fire);
    this.navListeners.push(off);
    return off;
  }

  async getAuthToken(): Promise<string | null> {
    return null; // Backend auth is via function key (see ImpactAnalysisClient).
  }

  getMountPoint(): HTMLElement {
    if (!this.mount) throw new Error("Host not ready. Call ready() first.");
    return this.mount;
  }

  /** Detach listeners and remove the mount point from the DOM. */
  dispose(): void {
    for (const off of this.navListeners) off();
    this.navListeners = [];
    if (this.mount && this.mount.parentNode) {
      this.mount.parentNode.removeChild(this.mount);
    }
    this.mount = null;
    this.currentId = null;
  }

  private parseWorkItemIdFromUrl(url: string): number | null {
    // Matches both /_workitems/edit/123 and /_workitems/?id=123
    const editMatch = url.match(/_workitems\/edit\/(\d+)/);
    if (editMatch) return Number(editMatch[1]);
    const queryMatch = url.match(/[?&]id=(\d+)/);
    if (queryMatch) return Number(queryMatch[1]);
    return null;
  }
}
