import { BackendConfig } from "../../core/types";

export interface ExtensionSettings extends BackendConfig {
  /** Personal Access Token for Azure DevOps REST API (used to read work item fields). */
  devopsPat?: string;
}

const DEFAULTS: ExtensionSettings = {
  functionUrl: "",
  functionKey: "",
  devopsPat: ""
};

export async function getSettings(): Promise<ExtensionSettings> {
  const stored = await chrome.storage.sync.get(DEFAULTS);
  return { ...DEFAULTS, ...stored } as ExtensionSettings;
}

export async function saveSettings(settings: ExtensionSettings): Promise<void> {
  await chrome.storage.sync.set(settings);
}

export function onSettingsChanged(handler: (s: ExtensionSettings) => void): () => void {
  const listener = (_changes: object, area: string) => {
    if (area !== "sync") return;
    void getSettings().then(handler);
  };
  chrome.storage.onChanged.addListener(listener);
  return () => chrome.storage.onChanged.removeListener(listener);
}
