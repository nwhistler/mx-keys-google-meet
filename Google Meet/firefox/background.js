// Bridges the MX Keys plugin (a local Node process) and whichever Google Meet
// tab is currently active. The plugin has no way to see into the browser on its
// own, so this is the piece that closes the loop:
//
//   MX Keys key -> Actions SDK plugin -> ws://127.0.0.1:47624 -> this service worker
//                                                                     |
//                                                                     v
//                                                          content.js in the Meet tab

// Chrome/Arc/Dia only define `chrome`; Firefox defines `browser` (Promise-based,
// same shape) and also aliases `chrome` in callback style. Preferring `browser`
// when present is what makes the awaits below work identically everywhere.
const api = typeof browser !== 'undefined' ? browser : chrome;

const BRIDGE_URL = 'ws://127.0.0.1:47624';
const KEEPALIVE_ALARM = 'bridge-keepalive';
// A service worker with no pending events gets torn down by Chrome after ~30s
// idle, which kills the WebSocket living in it. An alarm below that interval
// wakes the worker back up in time to ping the socket (or reconnect it if it
// already died) before that happens.
const KEEPALIVE_INTERVAL_MINUTES = 0.4; // 24s — chrome.alarms ignores the 1-minute
// floor for unpacked/dev-loaded extensions, so this fires at the real interval.

// Matches MeetBridge.cs's AuthFailedCloseStatus — a distinct close code so we can
// tell "wrong/missing pairing code" apart from an ordinary disconnect and stop
// silently retrying forever with a code the plugin will only keep rejecting.
const AUTH_FAILED_CLOSE_CODE = 4001;

let socket = null;
let reconnectDelayMs = 1000;
const MAX_RECONNECT_DELAY_MS = 15000;
let pairingRequired = false;

/** The Meet tab commands should be routed to. Updated as tabs/windows change focus. */
let activeMeetTabId = null;

function isMeetUrl(url) {
  return typeof url === 'string' && url.startsWith('https://meet.google.com/');
}

async function connect() {
  if (pairingRequired) {
    return; // wait for the user to fix pairing via the options page instead of hammering the plugin
  }

  const { bridgeSecret } = await api.storage.local.get('bridgeSecret');
  if (!bridgeSecret) {
    console.warn('[MX Keys Bridge] no pairing code saved yet — open this extension\'s options page to pair with the plugin.');
    return;
  }

  socket = new WebSocket(BRIDGE_URL);

  socket.addEventListener('open', () => {
    reconnectDelayMs = 1000;
    socket.send(JSON.stringify({ type: 'auth', secret: bridgeSecret }));
    console.log('[MX Keys Bridge] connected to plugin at', BRIDGE_URL);
  });

  socket.addEventListener('message', (event) => {
    let message;
    try {
      message = JSON.parse(event.data);
    } catch {
      console.warn('[MX Keys Bridge] ignored malformed message from plugin');
      return;
    }
    if (message?.type === 'command') routeCommand(message.command, message.param);
  });

  socket.addEventListener('close', (event) => {
    if (event.code === AUTH_FAILED_CLOSE_CODE) {
      pairingRequired = true;
      console.warn('[MX Keys Bridge] plugin rejected the pairing code — open this extension\'s options page and re-pair.');
      return;
    }
    scheduleReconnect();
  });
  socket.addEventListener('error', () => socket?.close());
}

function scheduleReconnect() {
  setTimeout(connect, reconnectDelayMs);
  reconnectDelayMs = Math.min(reconnectDelayMs * 2, MAX_RECONNECT_DELAY_MS);
}

// If the user just fixed pairing, let the next keepalive tick (at most ~24s away)
// or a fresh command attempt pick it back up rather than requiring a full reload.
api.storage.onChanged.addListener((changes, area) => {
  if (area === 'local' && changes.bridgeSecret) {
    pairingRequired = false;
    reconnectDelayMs = 1000;
    if (socket?.readyState !== WebSocket.OPEN && socket?.readyState !== WebSocket.CONNECTING) {
      connect();
    }
  }
});

function sendState(state) {
  if (socket?.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify({ type: 'state', ...state }));
  }
}

async function routeCommand(command, param) {
  let tabId = activeMeetTabId;

  if (tabId == null) {
    const [fallbackTab] = await api.tabs.query({ url: 'https://meet.google.com/*' });
    tabId = fallbackTab?.id ?? null;
  }

  if (tabId == null) {
    console.warn(`[MX Keys Bridge] no Google Meet tab open — ignoring "${command}"`);
    return;
  }

  try {
    await api.tabs.sendMessage(tabId, { type: 'command', command, param });
  } catch (err) {
    console.warn('[MX Keys Bridge] could not reach the Meet tab (was it closed/reloaded?):', err.message);
    activeMeetTabId = null;
  }
}

// --- Track which Meet tab is "active" so commands go to the right place when
// --- the user has more than one meeting/tab open, and so a key press made while
// --- the user has moved on to a different tab doesn't act on a stale background
// --- meeting (confirmed live, 2026-09: activeMeetTabId previously only ever got
// --- SET, never cleared, so leaving a Meet tab for something else left commands
// --- routing to it indefinitely). ---

api.tabs.onActivated.addListener(async ({ tabId }) => {
  const tab = await api.tabs.get(tabId).catch(() => null);
  activeMeetTabId = tab && isMeetUrl(tab.url) ? tabId : null;
});

api.tabs.onUpdated.addListener((tabId, _changeInfo, tab) => {
  if (!tab.active) return;
  activeMeetTabId = isMeetUrl(tab.url) ? tabId : null;
});

api.tabs.onRemoved.addListener((tabId) => {
  if (tabId === activeMeetTabId) activeMeetTabId = null;
});

// content.js reports call state (mic/camera/hand) here; forward it to the plugin.
api.runtime.onMessage.addListener((message, sender) => {
  if (message?.type === 'state') {
    if (sender.tab?.active && sender.tab.id != null) activeMeetTabId = sender.tab.id;
    sendState({
      inCall: message.inCall,
      micMuted: message.micMuted,
      cameraOn: message.cameraOn,
      handRaised: message.handRaised,
      captionsOn: message.captionsOn,
    });
  }
});

// Keep the connection (and the service worker hosting it) alive between key presses.
api.alarms.create(KEEPALIVE_ALARM, { periodInMinutes: KEEPALIVE_INTERVAL_MINUTES });

api.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name !== KEEPALIVE_ALARM) return;
  if (socket?.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify({ type: 'ping' }));
  } else if (socket?.readyState !== WebSocket.CONNECTING) {
    connect();
  }
});

connect();
