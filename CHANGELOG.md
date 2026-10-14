# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Both halves — the Logi Options+ plugin (`MX-Keys-CSharp/`) and the browser
extension (`Google Meet/`) — share a single version number, because they speak a
common protocol and are not independently useful.

## [Unreleased]

## [0.2.0] - 2026-10-02

### Added

- Live on/off state for the screen-share key: green while sharing, blue when idle
  and able to start, gray with no active call. Sharing is derived from the presence
  of Meet's "Stop presenting" control — the same signal the toggle already used to
  decide direction — and carried to the plugin as a new `sharing` field on the state
  message.
- Test coverage for the sharing field: both transitions plus the absent-field
  default, so an older extension build that omits it reads as "not sharing" rather
  than latching a stale value.
- `CLAUDE.md` documenting build/test commands and the cross-file architecture
  (the two-halves contract, the Firefox sync step, full-bleed key rendering, and
  the build layout traps).

### Fixed

- Camera key lagging up to 4 seconds behind reality. A single 200 ms state re-read
  after a click was enough for the mic but not the camera, which starts or stops a
  physical capture device and is only relabelled once the track actually changes.
  Replaced with staggered re-reads, widened the MutationObserver beyond `aria-label`
  to the attributes the camera actually toggles, and debounced observer-driven reads
  so the wider filter doesn't fire excessively during Meet's re-renders.
- Overlapping bridge reconnect attempts. The close handler and the keepalive alarm
  both called `connect()` without checking whether a reconnect was already pending,
  and an `await` before the socket assignment let two callers each open one. Added a
  re-entrancy guard, a single tracked retry timer, and a close of any superseded
  socket. Not yet exercised against a prolonged plugin outage.

### Changed

- Extension version bumped from 0.1.0 to match the plugin and package manifests,
  which were already at 0.2.0 — a mismatched pair made bug reports ambiguous about
  which bridge protocol was in play.
- Extension description now lists every shipped action (captions, screen share and
  reactions were missing).
- Pairing options page: removed a stale reference to Gemini Notes, a feature that was
  deliberately removed before Chrome Web Store submission; corrected the
  `bridge-secret.txt` location, which lives in its own app-data directory rather than
  the plugin's install folder; and added dark-mode support, which previously left
  near-black text on the browser's dark chrome.

## [0.1.0] - 2026-09-12

### Added

- Initial release: Logi Options+ plugin (C# / Logi Actions SDK) plus companion
  browser extension, bridged over a loopback WebSocket on port 47624.
- Keypad actions for microphone, camera, raise/lower hand, captions, screen share
  and leave call, each with a live full-bleed state icon, plus an emoji Reactions
  dynamic folder.
- Pairing handshake with a per-install secret, a command allowlist and an in-call
  precondition on the bridge.
- Firefox support via a dedicated `firefox/` folder (Manifest V3 service workers are
  unsupported there, and its temporary-add-on loader is unreliable with non-standard
  manifest filenames).

### Fixed

- Firefox temporary-add-on install failing with a `background.service_worker`
  error.
- Blank Firefox options page caused by symlinked extension files; replaced with real
  copies kept in sync by `sync-firefox.sh`.
- Firefox's default extension CSP silently upgrading the bridge's `ws://127.0.0.1`
  connection to `wss://`, which broke the handshake outright.

