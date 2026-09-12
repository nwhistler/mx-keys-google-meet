# Privacy Policy — MX Keys Bridge for Google Meet

Last updated: 2026-09-12

## Summary

This extension does not collect, transmit, sell, or store any of your data anywhere
outside your own computer. It exists to let a physical Logitech MX Keys/Keypad button
control Google Meet, and it only ever talks to two things: the `meet.google.com` tab
you're in, and a companion program (the "MX Keys Google Meet" Logi Options+ plugin)
running on the same machine, over a connection that never leaves your computer
(`ws://127.0.0.1:...` — the `127.0.0.1` loopback address, which is not reachable from
the network or the internet).

## What this extension does

- Reads the visible state of Google Meet's own on-screen controls (mute, camera,
  hand-raise, captions, screen share, reactions) so it can report "muted" / "not
  muted" etc. back to the physical keypad's key icon, and so it knows which button to
  click when you press a key.
- Clicks those same on-screen Meet controls on your behalf when you press a physical
  key, exactly as if you had clicked them yourself.
- Stores one thing in your browser's local extension storage (`chrome.storage.local`):
  a random pairing code that proves to the local plugin that commands are really
  coming from this extension. This code never leaves your computer and is not
  associated with your identity, your Google account, or anything else.

## What this extension does NOT do

- It does not read, store, or transmit the content of your meetings — no audio, no
  video, no chat messages, no participant names, no transcripts.
- It does not communicate with any server on the internet. Its only network
  connection is a loopback WebSocket to a program running on your own computer.
- It does not use analytics, telemetry, crash reporting, or advertising of any kind.
- It does not share data with any third party, because it does not send data
  anywhere in the first place.

## Permissions this extension requests, and why

| Permission | Why it's needed |
|---|---|
| `tabs` | To find which browser tab is a Google Meet call, and to send it the click commands the physical key triggers. |
| `host_permissions` for `https://meet.google.com/*` | The content script that reads Meet's UI and clicks its buttons only ever runs on Google Meet pages. |
| `host_permissions` for `http://127.0.0.1:47624/*` | Lets the extension's options page fetch the one-time pairing code from the local plugin, entirely on your own machine. |
| `alarms` | Keeps the background connection to the local plugin alive; browsers can otherwise shut down an idle extension background process. |
| `storage` | Stores the local pairing code mentioned above. |

This extension does not request the `debugger` permission or any permission beyond
what's listed here.

## Data retention and deletion

The only thing stored is the pairing code in `chrome.storage.local`. Uninstalling the
extension, or clearing its storage from your browser's extension settings, deletes it
immediately. There is nothing else to delete, because nothing else is ever stored.

## Changes to this policy

If this extension's data practices ever change, this file will be updated and the
extension's Chrome Web Store listing will note the change.

## Contact

This is an independently developed, unofficial companion tool for Logitech MX Keys
hardware and is not affiliated with, endorsed by, or associated with Google or the
Google Meet product.
