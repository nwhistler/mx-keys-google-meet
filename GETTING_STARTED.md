# Getting Started

Control Google Meet from your MX Keys/MX Creative Keypad — mute, camera, hand-raise,
captions, screen share, reactions, and leave-call, each with a live status icon on the
key itself.

## What you need

- A Logitech MX Creative Keypad (or MX Creative Console) with Logi Options+ installed.
- Chrome, Edge, Brave, Arc, Dia, or Firefox.

## 1. Install the plugin

- **Via Logi Marketplace** (once listed there): search for "MX Keys — Google Meet
  Controls" in Options+'s plugin marketplace and install it.
- **Manual install for now**: get `MxKeysGoogleMeet.lplug4` from the project, then in
  Options+ go to **Settings → Plugins → Install from file** and select it.

Check it loaded: Options+ → MX Keypad → Customize → All Actions → you should see a
**Google Meet** group with several actions listed.

## 2. Install the browser extension

This piece is what actually lets the plugin see and control your Meet tab — the
plugin alone can't reach into your browser.

**Chrome / Edge / Brave / Arc / Dia:**
1. Go to `chrome://extensions` (or that browser's equivalent page).
2. Turn on **Developer mode** (top right).
3. Click **Load unpacked** and select the `Google Meet/` folder.

**Firefox:**
1. Go to `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on…** and select `Google Meet/firefox/manifest.json`
   (a Firefox-specific folder, separate from the top-level `manifest.json` used by
   Chrome/Edge/Brave/Arc/Dia). Firefox doesn't support Manifest V3 service workers —
   it needs `background.scripts` instead — and picking the wrong manifest fails with
   `background.service_worker is currently disabled. Add background.scripts.` Firefox's
   temporary-add-on loader has also been unreliable about respecting a manifest that
   isn't literally named `manifest.json`, even when you explicitly select the right
   file, so the `firefox/` folder exists to remove that ambiguity entirely.
3. This lasts until Firefox restarts — you'll need to reload it each session until a
   signed version is available.

## 3. Pair the extension with the plugin

The extension and plugin talk over a connection that's local to your machine only,
and they need to be introduced to each other once:

1. Make sure Logi Options+ is running.
2. Open the extension's options page — right-click its icon in your browser's toolbar
   and choose **Options** (or find it on the extensions page).
3. Click **Fetch from plugin**, then click **Save**.
4. If you already had a Meet tab open, refresh it.

You only need to do this once per browser — **until something resets the pairing**.
**Re-do these steps whenever:**
- You reinstall or remove-and-reload the **extension** (this wipes its saved pairing
  code — a normal update/reload of an already-loaded extension does *not* wipe it,
  only a full reinstall does).

Reinstalling/updating the **plugin** should *not* require re-pairing — its pairing
code lives in its own stable location separate from wherever Options+ installs the
plugin's files, specifically so updates don't reset it.

If either half gets reinstalled and buttons stop doing anything, this is almost
always why — re-pair before troubleshooting anything else. You'll also see a
"rejected pairing code" warning in the Meet tab's console if this happens.

## 4. Assign keys

1. Open Logi Options+ → your MX Keypad → **Customize**.
2. In the actions list, expand **Google Meet**.
3. Drag each action you want (Toggle Microphone, Toggle Camera, Raise/Lower Hand,
   Toggle Captions, Toggle Screen Share, Reactions, Leave Call) onto a key.

## 5. Try it

Join a Google Meet call, then press your keys. Each key's icon lights up green/red/
amber to reflect the live state — muted, hand raised, captions on, etc. Pressing a key
while you're *not* in a Meet call does nothing (safe by design).

**Reactions** is a folder key — press it once to expand into a row of emoji, then
press one to send it.

## Known quirks

- **Firefox + HTTPS-Only Mode**: if buttons do nothing and the extension's background
  console (`about:debugging` → **Inspect**) shows
  `NS_ERROR_GENERATE_FAILURE(NS_ERROR_MODULE_SECURITY, SSL_ERROR_RX_RECORD_TOO_LONG)`,
  Firefox's **HTTPS-Only Mode** is silently upgrading the plugin's plain
  `ws://127.0.0.1:47624` connection to `wss://` — the plugin only speaks plain
  WebSocket, so the TLS handshake fails immediately. Fix: Firefox **Settings → Privacy
  & Security → HTTPS-Only Mode → Manage Exceptions…** → add `http://127.0.0.1:47624`
  → **Allow** → **Save Changes**, then reload the extension and the Meet tab.
- **Screen share**: pressing the key opens Meet's own share picker (choosing a
  screen/window/tab is a native OS dialog no key can drive for you) — you finish that
  last step yourself. Stopping an active share is fully automatic.
- **Gemini Notes** isn't available yet — Google blocks that specific control from
  being triggered by anything other than a real click, and we haven't found a way
  around it that doesn't come with its own tradeoffs. See the main README for details.
- If a key stops responding, open the Meet tab's DevTools console (F12) — the
  extension logs a specific warning there when it can't find a control, which usually
  means Google changed some wording and the extension needs a small update.

## Something not working?

**First, check pairing.** If you recently reinstalled the plugin or the extension,
re-pair (step 3) before anything else — it's the most common cause of "nothing
happens when I press a key."

Otherwise, check `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/MxKeysGoogleMeet.log`
(macOS) for what the plugin sees, and the Meet tab's DevTools console for what the
extension sees. See [`MX-Keys-CSharp/README.md`](MX-Keys-CSharp/README.md) for the
full technical details, including how the pairing/security model works.
