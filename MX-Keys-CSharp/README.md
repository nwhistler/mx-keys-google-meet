# MX Keys — Google Meet Controls (C# / Logi Actions SDK)

A Logi Options+ plugin for the MX Creative Keypad: mic/camera/hand/captions/screen-share
toggles, an emoji-reactions folder, and leave-call, each with a live full-bleed color icon
on the key itself.

## Architecture

Options+ plugins can't see into a browser tab, so this ships two pieces:

```
MX Keypad key -> Options+ -> this plugin -> loopback WebSocket (ws://127.0.0.1:47624)
                                                      |
                                                      v
                              companion browser extension (../Google Meet)
                                                      |
                                                      v
                                          your active Google Meet tab
```

The plugin (`src/Bridge/MeetBridge.cs`) runs the WebSocket server and tracks live call
state; the browser extension reads/clicks Meet's own on-screen controls and reports
state back. See [`../Google Meet/README`-equivalent] the extension's own files for its
side; see **Security**, below, for how the two authenticate each other.

## Layout

- `MxKeysGoogleMeetPlugin.cs` — `Plugin` subclass; `Load()` starts the bridge.
- `MxKeysGoogleMeetApplication.cs` — a required (see Gotchas) empty `ClientApplication`.
- `src/Bridge/MeetBridge.cs` — the WebSocket server: pairing/auth, command allowlist,
  message framing, connection lifecycle.
- `src/Actions/*.cs` — one `PluginDynamicCommand` per toggle, plus
  `EmojiReactionsDynamicFolder` (a `PluginDynamicFolder`) for reactions.
- `src/KeyImage.cs` — draws each key full-bleed: a solid state color plus a centered
  icon glyph, no text.
- `MxKeysGoogleMeetPlugin.Tests/` — xunit integration tests against a real `MeetBridge`
  instance on an ephemeral port (never the real plugin's port).

## Build & run

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
export DOTNET_ROLL_FORWARD=LatestMajor

cd MX-Keys-CSharp
dotnet build -c Release   # writes a dev .link + fires a reload deeplink automatically
```

Check `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/MxKeysGoogleMeet.log`
for `N dynamic actions loaded` and `[MeetBridge] listening on ws://127.0.0.1:47624`.

Then in Options+: MX Keypad → Customize → All Actions → **Google Meet** group → assign
actions to keys.

### Tests

```bash
cd MxKeysGoogleMeetPlugin.Tests
dotnet test
```

Runs a real `MeetBridge` on an ephemeral port with a fixed test secret — covers the
auth handshake, command allowlist, in-call precondition, and fragmented/oversized
message framing. Building/running this project does **not** touch the real plugin's
`.link` or trigger a reload (see the test csproj's comments if that ever regresses).

### Packaging for distribution

```bash
dotnet tool install --global LogiPluginTool   # targets .NET 8 — run every invocation with:
export DOTNET_ROLL_FORWARD=LatestMajor

logiplugintool pack ./bin/Release ./MxKeysGoogleMeet.lplug4
logiplugintool verify ./MxKeysGoogleMeet.lplug4   # must say OK
```

## Security

The bridge listens on loopback only, but loopback is not an authentication boundary —
any local process can attempt to connect. Two layered mitigations:

1. **Pairing handshake.** The plugin generates a random per-install secret on first
   run (stored at `<Options+ plugin data dir>/MxKeysGoogleMeet/bridge-secret.txt`,
   readable only by your user account on macOS/Linux). The extension's options page
   fetches it once (`GET /pairing-code`, also loopback-only) and stores it in
   `chrome.storage.local`; every new connection must send it as its first message or
   gets disconnected. **This is not cryptographically bulletproof** — the pairing
   endpoint is deliberately unauthenticated so the options page can reach it with a
   plain `fetch()`, which means any other local process that knows to ask for it can
   also read the secret. It stops a generic scanner or unrelated app from silently
   controlling your meeting; it does not stop a targeted local attacker who reads this
   exact protocol. If that's ever needed, see `code-review.md`'s remediation notes for
   what a stronger design (e.g. OS-level named pipe with ACLs) would look like.
2. **Command allowlist + in-call precondition.** Even an authenticated client can only
   invoke the fixed set of Meet commands this plugin ships, and only while the
   extension has reported an active call — an authenticated-but-compromised client
   can't ask for anything the plugin wasn't already going to do.

Message framing is bounded (16 KiB max, text-only, reassembled across fragments) so a
malformed or oversized message can't wedge or crash the receive loop.

## Known limitations

- **Gemini Notes is not currently implemented.** Google gates the "Take notes with
  Gemini" control behind genuinely trusted input — no event a content script can
  dispatch opens its panel, confirmed across several event-shape variants. The only
  working approach found uses `chrome.debugger` + CDP's `Input.dispatchMouseEvent`
  (which the renderer does treat as trusted), but the `debugger` permission is a major
  red flag for Chrome Web Store review, so it was pulled back out ahead of public
  listing. See `code-review.md`'s remediation notes for the full investigation and
  what would need to change to bring it back.
- **Screen-share start is manual by design.** Clicking "Share screen" opens the
  browser's native tab/window/screen picker, which no content script can drive — the
  key gets you to the picker, you finish the selection. Stopping an active share is
  fully automatic.
- **DOM selectors are inherently fragile.** Meet's UI has no public API; the extension
  matches controls by `aria-label`/text, which Google can change without notice. The
  extension logs a specific console warning when a control can't be found or a match
  is ambiguous (see its own source for the exact pattern) — check DevTools on the Meet
  tab first if something stops working.
- **`Icon256x256.png`** is the plugin's own list icon in Options+, not a per-key face.

## Gotchas found the hard way

- **`{PluginName}Application : ClientApplication` is mandatory**, even for a universal
  plugin with `HasNoApplication`, even if it overrides nothing. Omitting it fails
  plugin load silently — no exception anywhere, and `logiplugintool verify` doesn't
  catch it either, since it's a live-loader requirement, not a package-validation one.
- **`SetWidget(true)` + a zero-width space (`"​"`) from `GetCommandDisplayName`**
  is what gets a full-bleed custom icon with no Options+-drawn caption. An empty
  string from `GetCommandDisplayName` falls back to the declared `displayName` instead
  of suppressing the label — it has to be non-empty-but-invisible.
- **`PluginDynamicFolder` has no `SetWidget` equivalent** — the Reactions folder's
  sub-keys are stuck in Options+'s default small-boxed-icon-with-caption layout;
  there's no way to get them full-bleed the way top-level commands can.
- **The "disabled plugins list" is real and sticky.** Once a plugin name fails to
  load, the service won't retry it, sometimes even across a restart, until the
  underlying defect is actually fixed.
