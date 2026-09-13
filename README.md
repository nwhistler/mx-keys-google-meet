# MX Keys — Google Meet Controls

A Logitech MX Keys/MX Creative Keypad key that mutes/unmutes, toggles your camera,
raises your hand, toggles captions, shares your screen, sends a reaction, or leaves a
call — live icon state on the key itself, and it only ever acts on a real Google Meet
tab.

**New here?** See [`GETTING_STARTED.md`](GETTING_STARTED.md) for install + setup steps.

Two pieces, because a Logi Options+ plugin has no visibility into browser tabs:

- **[`MX-Keys-CSharp/`](MX-Keys-CSharp/README.md)** — the Logi Actions SDK plugin
  (C#/Loupedeck). Runs a loopback WebSocket server, tracks live Meet state, renders
  each key's live full-bleed icon.
- **`Google Meet/`** — the browser extension. Tested on Chrome, Dia, and Firefox; Edge,
  Brave, and Arc use the same Chromium extension APIs and should work identically but
  haven't been personally verified. Reads and clicks Meet's own on-screen controls,
  reports state back to the plugin.

Start with the C# project's [README](MX-Keys-CSharp/README.md) for build/run/test
instructions and its **Security** section for how the two pieces authenticate each
other.

## Browser extension setup

1. **Chrome/Dia** (tested) **/Edge/Brave/Arc** (untested, should work identically):
   `chrome://extensions` (or that browser's equivalent) → enable **Developer mode** →
   **Load unpacked** → select `Google Meet/`.
2. **Firefox**: `about:debugging#/runtime/this-firefox` → **Load Temporary Add-on…** →
   select `Google Meet/firefox/manifest.json` (a dedicated folder — Firefox's loader
   is unreliable about non-standard manifest filenames, and Firefox needs
   `background.scripts` instead of the MV3 `service_worker` the other manifest uses).
   Temporary-only until Mozilla signs a packaged build.
3. Open the extension's **options page** (right-click its toolbar icon → Options, or
   find it on the extensions page) and click **Fetch from plugin** then **Save** to
   pair it with the plugin — this has to happen once per browser profile before any
   key press will do anything (see the C# README's Security section for why).
4. In Options+: MX Keypad → Customize → All Actions → **Google Meet** group → assign
   actions to keys.

## Known limitations

See the C# project's [README](MX-Keys-CSharp/README.md#known-limitations) for the full,
current list (Gemini Notes, screen-share start, DOM-selector fragility).
