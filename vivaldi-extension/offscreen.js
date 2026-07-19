const bridgeUrl = "http://127.0.0.1:32145";
const tabAudio = new Map();
let masterVolume = 100;
let masterMuted = false;

chrome.runtime.onMessage.addListener((message) => {
  if (message.type === "register-tab") registerTab(message);
  if (message.type === "update-tab-details" && tabAudio.has(message.tabId)) {
    const tab = tabAudio.get(message.tabId);
    if (typeof message.title === "string") tab.title = message.title;
    if (typeof message.favIconUrl === "string") tab.favIconUrl = message.favIconUrl;
  }
});

async function registerTab({ tabId, title, favIconUrl, streamId }) {
  if (tabAudio.has(tabId)) return;

  const stream = await navigator.mediaDevices.getUserMedia({
    audio: {
      mandatory: {
        chromeMediaSource: "tab",
        chromeMediaSourceId: streamId
      }
    },
    video: false
  });
  const context = new AudioContext();
  const source = context.createMediaStreamSource(stream);
  const gain = context.createGain();
  source.connect(gain).connect(context.destination);
  tabAudio.set(tabId, { title, favIconUrl, context, stream, gain, volume: 100, muted: false });
  applyTabGain(tabAudio.get(tabId));
}

async function reportTabs() {
  const tabs = [...tabAudio.entries()].map(([tabId, tab]) => ({
    tabId,
    title: tab.title,
    favIconUrl: tab.favIconUrl,
    isMuted: tab.muted,
    volume: tab.volume,
    isAudible: !tab.muted && tab.volume > 0
  }));

  try {
    await fetch(`${bridgeUrl}/bridge/tabs`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tabs, masterVolume, masterMuted })
    });
  } catch (error) {
    console.debug("DopaRush Mixer is not running", error);
  }
}

async function pollCommands() {
  try {
    const response = await fetch(`${bridgeUrl}/bridge/commands`);
    const { commands } = await response.json();
    for (const command of commands) applyCommand(command);
  } catch (error) {
    console.debug("DopaRush Mixer command polling is unavailable", error);
  }
}

function removeTab(tabId) {
  const tab = tabAudio.get(tabId);
  if (!tab) return;
  tab.stream.getTracks().forEach((track) => track.stop());
  tab.context.close();
  tabAudio.delete(tabId);
  chrome.runtime.sendMessage({ type: "unregister-tab", tabId });
}

function applyCommand(command) {
  if (command.type === "set-master-volume" && typeof command.volume === "number") {
    masterVolume = Math.max(0, Math.min(100, command.volume));
    for (const tab of tabAudio.values()) applyTabGain(tab);
    return;
  }
  if (command.type === "set-master-muted" && typeof command.isMuted === "boolean") {
    masterMuted = command.isMuted;
    for (const tab of tabAudio.values()) applyTabGain(tab);
    return;
  }
  if (command.type === "remove-tab") {
    removeTab(command.tabId);
    return;
  }

  const tab = tabAudio.get(command.tabId);
  if (!tab) return;

  if (command.type === "set-volume" && typeof command.volume === "number") {
    tab.volume = Math.max(0, Math.min(100, command.volume));
  }
  if (command.type === "set-muted" && typeof command.isMuted === "boolean") {
    tab.muted = command.isMuted;
  }
  applyTabGain(tab);
}

function applyTabGain(tab) {
  tab.gain.gain.value = tab.muted || masterMuted ? 0 : (tab.volume / 100) * (masterVolume / 100);
}

setInterval(reportTabs, 1000);
setInterval(pollCommands, 100);
