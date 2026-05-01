/// <reference types="chrome" />

// Service worker: forward icon-clicks to the content script as a TOGGLE_PANEL message.
chrome.action.onClicked.addListener(async (tab) => {
  if (!tab.id || !tab.url) return;
  if (!/^https:\/\/(dev\.azure\.com|[^/]+\.visualstudio\.com)\//.test(tab.url)) return;

  try {
    await chrome.tabs.sendMessage(tab.id, { type: "TOGGLE_PANEL" });
  } catch {
    // Content script not ready (e.g. page not yet loaded). Silently ignore.
  }
});
