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

## Publishing to an extension store

Chrome stopped letting regular users sideload unsigned extensions outside Developer
Mode years ago — a normal install experience requires actually publishing to the
Chrome Web Store (and separately, Firefox Add-ons / Edge Add-ons for those browsers).
That's an external process this repo can prepare for but can't complete on its own:

- **[`Google Meet/PRIVACY_POLICY.md`](Google%20Meet/PRIVACY_POLICY.md)** — required by
  Chrome Web Store's Developer Program Policies for the permissions this extension
  requests. **Must be hosted at a public URL** (GitHub Pages, a personal site, etc.) —
  a file bundled in the extension package isn't sufficient for the listing form.
- **Icons** (`Google Meet/icon{16,32,48,128}.png`) are generated from the Google Meet
  logo. **Before actually submitting**, decide deliberately whether that's acceptable:
  Google scrutinizes third-party use of their own trademarks in Web Store listings,
  and this is a different context than the plugin's own local-only Options+ icon —
  worth a considered choice (an original mark instead), not a default.
- A Google Developer account (one-time $5 registration) is required to submit at all.
- The extension currently requests `tabs`, `alarms`, `storage`, and host permissions
  for `meet.google.com` and the local loopback bridge — no `debugger` permission (see
  the C# README's **Known limitations** for why Gemini Notes automation isn't
  currently shipped, and what it would take to bring back).

## Known limitations

See the C# project's [README](MX-Keys-CSharp/README.md#known-limitations) for the full,
current list (Gemini Notes, screen-share start, DOM-selector fragility).
