#!/usr/bin/env bash
# Copies the shared extension files into firefox/, which needs real file copies
# (not symlinks — Firefox's temporary-add-on loader doesn't reliably follow them,
# confirmed live 2026-09 via a blank options.html page). Run this after editing
# background.js, content.js, options.html, options.js, or any icon.
set -euo pipefail
cd "$(dirname "$0")"

for f in background.js content.js options.html options.js icon16.png icon32.png icon48.png icon128.png; do
  cp "$f" "firefox/$f"
done

echo "firefox/ synced."
