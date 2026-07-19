const offscreenDocument = "offscreen.html";
const bridgeUrl = "http://127.0.0.1:32145";
const registeredTabIds = new Set();

chrome.runtime.onMessage.addListener((message) => {
  if (message.type !== "unregister-tab") return;
  registeredTabIds.delete(message.tabId);
  updateDetectedTabs();
});

async function ensureOffscreenDocument() {
  const existing = await chrome.runtime.getContexts({ contextTypes: ["OFFSCREEN_DOCUMENT"], documentUrls: [chrome.runtime.getURL(offscreenDocument)] });
  if (existing.length === 0) {
    await chrome.offscreen.createDocument({
      url: offscreenDocument,
      reasons: ["USER_MEDIA"],
      justification: "Capture registered tab audio and apply individual volume controls."
    });
  }
}

chrome.action.onClicked.addListener(async (tab) => {
  if (!tab.id) return;

  try {
    await ensureOffscreenDocument();
    const streamId = await chrome.tabCapture.getMediaStreamId({ targetTabId: tab.id });
    await chrome.runtime.sendMessage({ type: "register-tab", tabId: tab.id, title: tab.title ?? "Untitled tab", favIconUrl: tab.favIconUrl ?? null, streamId });
    registeredTabIds.add(tab.id);
    await updateDetectedTabs();
  } catch (error) {
    console.error("Unable to register tab audio", error);
  }
});

async function updateDetectedTabs() {
  const audibleTabs = await chrome.tabs.query({ audible: true });
  const detectedTabs = audibleTabs
    .filter((tab) => tab.id && !registeredTabIds.has(tab.id))
    .map((tab) => ({ tabId: tab.id, title: tab.title ?? "Untitled tab" }));

  await chrome.action.setBadgeBackgroundColor({ color: "#C46B00" });
  await chrome.action.setBadgeText({ text: detectedTabs.length > 0 ? String(detectedTabs.length) : "" });
  await chrome.action.setTitle({ title: detectedTabs.length > 0 ? "音声を検出しました。対象タブでクリックして個別制御を有効化" : "Register this tab with DopaRush Mixer" });

  try {
    await fetch(`${bridgeUrl}/bridge/detected-tabs`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tabs: detectedTabs })
    });
  } catch (error) {
    console.debug("DopaRush Mixer is not running", error);
  }
}

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (Object.hasOwn(changeInfo, "audible")) updateDetectedTabs();
  if (typeof changeInfo.title === "string" || typeof changeInfo.favIconUrl === "string") {
    chrome.runtime.sendMessage({ type: "update-tab-details", tabId, title: changeInfo.title, favIconUrl: changeInfo.favIconUrl });
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  registeredTabIds.delete(tabId);
  updateDetectedTabs();
});

chrome.runtime.onStartup.addListener(updateDetectedTabs);
chrome.runtime.onInstalled.addListener(updateDetectedTabs);
