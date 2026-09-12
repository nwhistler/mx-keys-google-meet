// Pairing UI: lets the user fetch (or manually paste) the per-install secret the
// C# plugin generates, and stores it in chrome.storage.local so background.js can
// send it as the first message on every new bridge connection. See MeetBridge.cs's
// class-level SECURITY MODEL comment for what this secret does and doesn't protect
// against.

const api = typeof browser !== 'undefined' ? browser : chrome;

const secretInput = document.getElementById('secret');
const statusEl = document.getElementById('status');

function setStatus(text, kind) {
  statusEl.textContent = text;
  statusEl.className = kind || '';
}

async function loadExisting() {
  const { bridgeSecret } = await api.storage.local.get('bridgeSecret');
  if (bridgeSecret) {
    secretInput.value = bridgeSecret;
    setStatus('Currently paired.', 'ok');
  }
}

document.getElementById('fetch').addEventListener('click', async () => {
  setStatus('Fetching from plugin…');
  try {
    const res = await fetch('http://127.0.0.1:47624/pairing-code');
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}`);
    }
    const { secret } = await res.json();
    if (!secret) {
      throw new Error('plugin returned no code');
    }
    secretInput.value = secret;
    setStatus('Fetched — click Save to finish pairing.', 'ok');
  } catch (err) {
    setStatus(`Could not reach the plugin (${err.message}). Is Logi Options+ running?`, 'err');
  }
});

document.getElementById('save').addEventListener('click', async () => {
  const value = secretInput.value.trim();
  if (!value) {
    setStatus('Enter or fetch a pairing code first.', 'err');
    return;
  }
  await api.storage.local.set({ bridgeSecret: value });
  setStatus('Saved. Reload any open Meet tabs to reconnect with the new code.', 'ok');
});

loadExisting();
