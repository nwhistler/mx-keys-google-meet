# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Logitech MX Creative Keypad plugin that controls Google Meet (mic, camera, hand, captions,
screen share, emoji reactions, leave call) with live per-key state icons.

Logi Options+ plugins cannot see into browser tabs, so this ships as **two halves that must be
changed together**:

- `MX-Keys-CSharp/` — C#/.NET Logi Actions SDK plugin. Hosts a loopback WebSocket server, holds
  live call state, renders key images.
- `Google Meet/` — the browser extension. Clicks Meet's own on-screen controls and reports state
  back.

```
keypad key -> Options+ -> plugin -> ws://127.0.0.1:47624 -> extension background.js
                                                                   |
                                                            content.js in the Meet tab
```

## Commands

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
export DOTNET_ROLL_FORWARD=LatestMajor     # LogiPluginTool targets .NET 8; local SDK is 10.x

cd MX-Keys-CSharp
dotnet build -c Release                     # also writes the dev .link and fires a reload deeplink

cd MxKeysGoogleMeetPlugin.Tests
dotnet test
dotnet test --filter "FullyQualifiedName~Oversized_message"   # single test
```

Packaging for distribution (install the tool first — it is not part of the repo):

```bash
dotnet tool install --global LogiPluginTool
logiplugintool pack ./bin/Release ./MxKeysGoogleMeet.lplug4
logiplugintool verify ./MxKeysGoogleMeet.lplug4    # must say OK
```

Plugin log (the first place to look when a key does nothing):
`~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/MxKeysGoogleMeet.log`
Look for `N dynamic actions loaded` and `[MeetBridge] listening on ws://127.0.0.1:47624`.
The extension's own diagnostics go to the Meet tab's DevTools console, prefixed `[MX Keys Bridge]`.

There is no build, lint, or test tooling on the extension side — it is plain unbundled JS loaded
unpacked.

## Build layout quirks

- `bin/$(Configuration)/` is the *plugin root*; the DLL goes one level deeper in `bin/`, because
  `LoupedeckPackage.yaml` declares `pluginFolderMac/Win: bin`. Don't "fix" the doubled path.
- The build references `PluginApi.dll` straight out of the installed LogiPluginService app bundle
  (`/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/` on macOS). It is
  host-provided at runtime and never shipped in the package — except in the test project, which
  copies it locally since a standalone test host has no service to supply it.
- The test project lives *inside* the main project's directory, so the main `.csproj` explicitly
  excludes `MxKeysGoogleMeetPlugin.Tests/**/*.cs` from its compile glob.
- The test project passes `SkipPluginLink=true` via `AdditionalProperties` (not `Properties`).
  Changing that makes `dotnet test` clobber a Release deployment with a stray Debug one.
- Tests run a real `MeetBridge` on an ephemeral port with a fixed test secret, so they never touch
  the real port or the on-disk pairing secret and can run alongside a live installed plugin.

## Changing the extension: sync Firefox

`Google Meet/firefox/` holds **real copies**, not symlinks, of `background.js`, `content.js`,
`options.html`, `options.js`, and the icons. Only `manifest.json` genuinely differs (Firefox needs
`background.scripts` instead of MV3's `service_worker`, plus `browser_specific_settings`).

After editing any shared extension file, run:

```bash
"Google Meet/sync-firefox.sh"
```

Forgetting this leaves Firefox silently running stale code. Symlinks do not work — Firefox's
temporary-add-on loader does not reliably follow them.

## Architecture notes that span files

**Adding a command** touches four places, in this order:
1. `MeetBridge.Commands` — add the constant, and add it to the `ValidCommands` allowlist (a command
   missing from the allowlist is silently refused at runtime).
2. `src/Actions/<Name>Command.cs` — a `PluginDynamicCommand` subclass. These are **auto-discovered**
   by the SDK; nothing registers them explicitly, so `Load()` only starts the bridge.
3. `content.js` — a `runCommand` switch case plus a matcher in `BUTTON_MATCHERS`.
4. `sync-firefox.sh`.

**Key rendering** requires two things together, or you get Options+'s small-inset-icon-with-caption
layout instead of a full-bleed face: the action's constructor must call `SetWidget(true)`, *and*
`GetCommandDisplayName` must return a zero-width space `"​"` — `String.Empty` falls back to
the declared `displayName` rather than suppressing it. `KeyImage.Render` builds against
`ButtonCanvas` (full button dimensions) rather than `new BitmapBuilder(imageSize)` for the same
reason. `PluginDynamicFolder` (the Reactions folder) has no `SetWidget` equivalent, so its sub-keys
cannot be made full-bleed — hence `KeyImage.LightGray`, which exists only to stay legible inside
Options+'s black folder chrome.

**State flow** is one-directional: `content.js` polls/observes Meet's DOM → `background.js` →
`MeetBridge.HandleMessage` → `SetState` → `StateChanged` → each action calls `ActionImageChanged()`.
`MeetState` is a record, so `SetState` only fires on real change. Actions must keep their handler
delegate in a field and unsubscribe in `OnUnload` — an anonymous lambda cannot be removed, and each
reload would otherwise leak another subscriber.

**Meet DOM matching** is the fragile seam and the most likely cause of a regression. Everything
funnels through `findUnique`, which filters to visible/enabled elements and then **fails closed on
ambiguity** (logging a warning rather than clicking the wrong thing). Several hard-won specifics are
encoded there and in `BUTTON_MATCHERS` — the captions matcher is `turn (on|off) captions`, not
`/captions/i`, because a transcript wrapper div otherwise wins DOM order; "Stop presenting" needs
`findButtonByPattern` because its name comes from `aria-labelledby`, not an `aria-label` attribute.
Read the comments before loosening any matcher.

## Security model

The bridge is loopback-only, but loopback is **not** an authentication boundary. Two layered
mitigations, both deliberate and both documented honestly in `MX-Keys-CSharp/README.md`:

1. **Pairing secret.** Per-install random secret; the extension must send it as the first message
   or the socket is closed with code `4001` and never added to `_sockets`. `GET /pairing-code`
   serves it *unauthenticated* so the options page can fetch it with a plain `fetch()` — a
   deliberate trade-off that stops generic scanners but not a targeted local attacker. Do not
   describe this as bulletproof.
2. **Command allowlist + in-call precondition** in `Send()`, so an authenticated client still can
   only invoke the fixed command set, and only while a call is active.

The secret lives at `~/Library/Application Support/MxKeysGoogleMeet/bridge-secret.txt` —
deliberately **outside** the Options+ Plugins tree, which "Install from file" wipes on every
reinstall. Do not relocate it there. `AppContext.BaseDirectory` and `Assembly.Location` are both
unreliable inside the plugin host, which is why that path is hand-built.

Message framing is bounded (16 KiB, text-only, reassembled across fragments); oversized or binary
messages get a real close frame rather than an abrupt reset.

## Non-obvious runtime behaviours

- **`MxKeysGoogleMeetApplication : ClientApplication` must exist**, even though it is empty and the
  plugin declares `HasNoApplication`. Deleting it fails plugin load silently — no exception, and
  `logiplugintool verify` does not catch it. Once a plugin fails to load, the service adds it to a
  sticky "disabled plugins" list and stops retrying until the real defect is fixed.
- **`PluginConfiguration.xml` keeps an inert `LegacyParserSentinel` command** purely because the
  legacy SDK warns on an empty `<actions>` collection. It is `deviceType="0"`, so it never appears.
- **Zombie sockets are real.** Chrome's MV3 service worker restarts constantly and leaves sockets
  that go silent rather than erroring. `_lastSeen` plus the 90s reap timer clear them; without it,
  one keypress fires once per zombie. The extension pings every 24s (`chrome.alarms` ignores its
  1-minute floor for unpacked extensions) to stay well inside that threshold.
- **Screen-share start is manual by design** — the native picker is an OS dialog no content script
  can drive. Stopping a share is fully automatic.
- **Gemini Notes is intentionally absent.** It only ever worked via `chrome.debugger` + CDP, and
  that permission was pulled ahead of Chrome Web Store review. Don't reintroduce it without
  revisiting that decision; see the `REMOVED FEATURE` comment in `content.js`.
- The reaction picker is deliberately *reopened* after each send, because the keypad folder is meant
  to stay on the emoji row across presses; `close-reactions` closes it when the user backs out.

## Versioning

Keep these three in sync when releasing: `MxKeysGoogleMeetPlugin.csproj` `<Version>`,
`package/metadata/LoupedeckPackage.yaml` `version`, and `Google Meet/manifest.json` `version`
(plus the Firefox copy via `sync-firefox.sh`). The packaged `.lplug4` is gitignored — attach builds
to a GitHub Release rather than committing them.

## Further reading

- `MX-Keys-CSharp/README.md` — build/run/test, security, known limitations, SDK gotchas.
- `GETTING_STARTED.md` — end-user install and pairing, including when re-pairing is required.
- `MX-Keys-CSharp/code-review.md` — independent security review plus remediation notes. Issue
  numbers referenced in source comments (e.g. "Issue 6 in the original review") point here.
