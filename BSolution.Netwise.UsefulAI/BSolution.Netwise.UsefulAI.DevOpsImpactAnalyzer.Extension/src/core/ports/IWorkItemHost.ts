import { WorkItemContext } from "../types";

export interface HostCapabilities {
  /** True when the host already provides its own panel chrome (DevOps iframe).
   *  False when the adapter must inject a panel into the page (browser ext). */
  ownsPanelChrome: boolean;
  /** True when the host can mint an auth token (e.g. AAD via DevOps SDK). */
  providesAuthToken: boolean;
}

export type WorkItemChangedHandler = (ctx: WorkItemContext) => void;

/** Port (Hexagonal). All host-specific code lives behind this interface. */
export interface IWorkItemHost {
  readonly capabilities: HostCapabilities;

  /** Initialise the host (SDK.init, DOM injection, etc.). */
  ready(): Promise<void>;

  /** Resolve the work item currently in context, or null if none. */
  getCurrentWorkItem(): Promise<WorkItemContext | null>;

  /** Subscribe to context changes (SPA navigation, refresh). Returns unsubscribe. */
  onWorkItemChanged(handler: WorkItemChangedHandler): () => void;

  /** Optional auth token (Bearer). Return null to fall back to function key. */
  getAuthToken(): Promise<string | null>;

  /** DOM node where the React app should be mounted. */
  getMountPoint(): HTMLElement;
}
