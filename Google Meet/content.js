// Runs inside meet.google.com. Finds Meet's own mic/camera/hand/leave controls by
// their accessibility label (Meet doesn't expose stable class names, but the
// aria-label text is what screen readers rely on, so it's the most durable hook
// available without a private API). If Google reshuffles these labels, this file
// is the only place that needs updating.

const api = typeof browser !== 'undefined' ? browser : chrome;

const BUTTON_MATCHERS = {
  mic: /microphone/i,
  camera: /camera/i,
  hand: /(raise|lower)\s*hand/i,
  leave: /leave call/i,
  // Deliberately "turn (on|off) captions", not just /captions/i: once captions are
  // on, Meet adds a transcript panel wrapped in <div aria-label="Captions"> plus a
  // "Jump to most recent captions" button, both ahead of the real toggle button in
  // DOM order — a bare /captions/i match grabs the inert wrapper div instead of the
  // toggle, so turning captions off silently does nothing (confirmed live, 2026-09).
  captions: /turn (on|off) captions/i,
  shareScreen: /share screen/i,
  stopSharing: /stop (presenting|sharing)/i,
  openReactions: /send a reaction/i,
};

/** An element that's present in the DOM but not actually a usable, visible control —
 * hidden, disabled, zero-size, or explicitly marked inert/aria-disabled. The captions
 * bug (a transcript-panel wrapper div matching /captions/i ahead of the real toggle in
 * DOM order) is the concrete example of why "matches the label" isn't enough on its
 * own: that wrapper WAS the first DOM match but wasn't the control anyone could
 * actually click through the UI. */
function isVisible(el) {
  if (!(el instanceof Element)) return false;
  if (el.getAttribute('aria-hidden') === 'true') return false;
  if (el.getAttribute('aria-disabled') === 'true') return false;
  if (el.disabled) return false;
  const style = getComputedStyle(el);
  if (style.visibility === 'hidden' || style.display === 'none' || Number(style.opacity) === 0) return false;
  const rect = el.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}

/** True for an actual interactive element — a <button> or anything with role="button" —
 * as opposed to a structural container (role="dialog", role="toolbar", or no role at
 * all) that merely relays the same aria-label as an interactive element nested inside
 * it. Confirmed live, 2026-09: Meet's reaction-picker toggle has a role="dialog"
 * wrapper AND a role="toolbar" wrapper both carrying the exact same
 * aria-label="Send a reaction" as the real <button role="button"> nested inside them —
 * three elements matching the same label, only one of them actually clickable. */
function isClickable(el) {
  return el.tagName === 'BUTTON' || el.getAttribute('role') === 'button';
}

/** Shared core for every find* helper below: filters candidates to visible/enabled
 * elements, then requires exactly one match before returning it. Meet's DOM can (and
 * has) contained an invisible/inert element that also happens to match a given label —
 * failing closed on ambiguity turns a future duplicate match into a logged no-op
 * instead of a click on the wrong element, which matters most for the single-instance
 * controls (mic/camera/hand/leave/captions/screen-share) this is mainly used for.
 *
 * Before failing closed, narrows to just the clickable candidates (see isClickable) —
 * a labelled structural wrapper around the real control is a benign, common pattern
 * (the reaction picker's role="dialog"/role="toolbar" wrappers are exactly this), not
 * genuine ambiguity about which element to click. Only truly ambiguous matches (more
 * than one actually-clickable element, or none at all) still fail closed. */
function findUnique(selector, predicate, description) {
  const matches = [...document.querySelectorAll(selector)].filter((el) => predicate(el) && isVisible(el));
  if (matches.length === 0) return null;
  if (matches.length === 1) return matches[0];

  const clickable = matches.filter(isClickable);
  if (clickable.length === 1) return clickable[0];

  console.warn(`[MX Keys Bridge] ${matches.length} visible elements matched "${description}" (${clickable.length} of them clickable) — refusing to guess, treating as not found`);
  return null;
}

function findButton(matcher) {
  return findUnique('[aria-label]', (el) => matcher.test(el.getAttribute('aria-label') || ''), matcher.source);
}

/** Finds a button whose accessible name or visible text contains the given literal
 * string — used for the reaction picker, whose buttons are labelled with the raw
 * emoji character itself (e.g. aria-label="👍") rather than an English description. */
function findButtonByText(text) {
  return findUnique(
    '[role="button"], button',
    (el) => ((el.getAttribute('aria-label') || '') + (el.textContent || '')).includes(text),
    text,
  );
}

/** Regex version of findButtonByText — needed for the screen-share "Stop presenting"
 * control, whose accessible name comes from aria-labelledby rather than a literal
 * aria-label attribute (see toggleScreenShare). */
function findButtonByPattern(pattern) {
  return findUnique(
    '[role="button"], button',
    (el) => pattern.test((el.getAttribute('aria-label') || '') + (el.textContent || '')),
    pattern.source,
  );
}

/** Meet's toggle buttons are labelled with the action pressing them would take,
 * e.g. "Turn off microphone" while the mic is ON. Returns true/false/null. */
function isCurrentlyOn(button) {
  if (!button) return null;
  const label = button.getAttribute('aria-label') || '';
  if (/^turn off/i.test(label)) return true;
  if (/^turn on/i.test(label)) return false;
  return null;
}

/** `chrome.runtime.id` reads as undefined the instant the extension context dies —
 * checking this before ever calling a chrome.* API avoids the "Extension context
 * invalidated" throw entirely, rather than trying (and sometimes failing) to catch it. */
function isContextValid() {
  return !!api?.runtime?.id;
}

function reportState() {
  if (!isContextValid()) {
    stopReporting();
    return;
  }

  const micButton = findButton(BUTTON_MATCHERS.mic);
  const cameraButton = findButton(BUTTON_MATCHERS.camera);
  const handButton = findButton(BUTTON_MATCHERS.hand);
  const captionsButton = findButton(BUTTON_MATCHERS.captions);
  const micOn = isCurrentlyOn(micButton);

  try {
    api.runtime.sendMessage({
      type: 'state',
      // Presence of the mic/camera controls is also true on the pre-join lobby screen,
      // not just once a call has actually started — good enough to know Meet is "live".
      inCall: !!(micButton || cameraButton),
      micMuted: micOn === null ? null : !micOn,
      cameraOn: isCurrentlyOn(cameraButton),
      handRaised: handButton ? /lower hand/i.test(handButton.getAttribute('aria-label') || '') : null,
      captionsOn: isCurrentlyOn(captionsButton),
    });
  } catch {
    // "Extension context invalidated" — this happens when the extension gets
    // reloaded while this tab was already open. Nothing short of refreshing the
    // tab fixes it, so stop polling instead of throwing on every tick/mutation.
    stopReporting();
  }
}

function stopReporting() {
  clearInterval(pollIntervalId);
  observer.disconnect();
}

/** Starting a share only gets as far as Meet's own "Share screen" button — the
 * browser then shows its own native tab/window/screen picker, which is a real OS
 * permission dialog no content script can drive. The user finishes that part
 * themselves. Stopping an active share IS a plain in-page button, so that half is
 * fully automatable — we just look for it first and prefer it over starting a new one.
 *
 * Uses findButtonByPattern, not findButton, because the "Stop presenting" control
 * while actively sharing has no aria-label attribute at all — its accessible name
 * comes from aria-labelledby pointing at a descendant text node (confirmed live,
 * 2026-09). findButton() only searches
 * `[aria-label]`, so it never even saw this element as a candidate — the "can turn
 * on but not off" bug wasn't the regex, it was the attribute-only lookup missing
 * the element entirely. */
function toggleScreenShare() {
  const stopButton = findButtonByPattern(BUTTON_MATCHERS.stopSharing);
  if (stopButton) {
    stopButton.click();
    return;
  }
  const startButton = findButtonByPattern(BUTTON_MATCHERS.shareScreen);
  if (startButton) {
    startButton.click();
  } else {
    console.warn('[MX Keys Bridge] could not find the share-screen control');
  }
}

/** Whether Meet's reaction picker is currently open, detected by checking whether its
 * heart-reaction button (always first/present in Meet's default reaction set) is
 * currently visible — more robust than relying on a specific aria-expanded attribute
 * we haven't confirmed Meet actually sets on the toggle, and avoids duplicating the
 * full emoji list that already lives in EmojiReactionsDynamicFolder.cs. */
function isReactionPickerOpen() {
  return !!findButtonByText('💖');
}

/** Closes the reaction picker if it's open — a no-op otherwise. Used when the user
 * backs out of the keypad's Reactions folder (see EmojiReactionsDynamicFolder's
 * Deactivate override), since the picker is deliberately left open between individual
 * reaction presses (see sendReaction) and needs an explicit close at that point. */
function closeReactionsPicker() {
  if (!isReactionPickerOpen()) {
    return;
  }
  const toggle = findButton(BUTTON_MATCHERS.openReactions);
  if (toggle) {
    toggle.click();
  }
}

/** Opens Meet's reaction picker and clicks the specific emoji. The picker's buttons
 * are labelled with the raw emoji character (confirmed live against Meet's actual
 * DOM), which is far more stable than however Google words the English description
 * this month — matching on the character itself is the resilient choice here.
 *
 * Meet closes the picker automatically right after a reaction is picked — sensible
 * for a human clicking it once, but the keypad's Reactions folder is meant to stay on
 * the emoji row across repeated presses until the user explicitly backs out (confirmed
 * live, 2026-09: watching it snap closed after every single press read as broken, even
 * though every reaction was actually being sent correctly). So this reopens the picker
 * shortly after each send. The open-button toggles, so it only clicks it when the
 * picker isn't already open — clicking an already-open toggle would close it instead,
 * which is exactly the flicker this is meant to avoid. */
function sendReaction(emoji) {
  if (!emoji) {
    return;
  }
  const alreadyOpen = isReactionPickerOpen();
  if (!alreadyOpen) {
    const openButton = findButton(BUTTON_MATCHERS.openReactions);
    if (!openButton) {
      console.warn('[MX Keys Bridge] could not find the reaction button');
      return;
    }
    openButton.click();
  }
  setTimeout(() => {
    const emojiButton = findButtonByText(emoji);
    if (!emojiButton) {
      console.warn(`[MX Keys Bridge] could not find the ${emoji} reaction option — the picker may not have opened in time`);
      return;
    }
    emojiButton.click();
    setTimeout(() => {
      const reopenButton = findButton(BUTTON_MATCHERS.openReactions);
      if (reopenButton && !isReactionPickerOpen()) {
        reopenButton.click();
      }
    }, 350);
  }, alreadyOpen ? 0 : 250);
}

/* REMOVED FEATURE — Gemini Notes toggle (2026-09). See code-review.md's remediation notes
 * for the full writeup. Short version: Meet's "Take notes with Gemini" control ignores every
 * event a content script can dispatch (plain .click(), a full pointerdown/mousedown/
 * pointerup/mouseup/click sequence, correct clientX/clientY, fully-realistic PointerEvent
 * properties — none of them moved its aria-expanded off "false"). The only mechanism that
 * worked was routing the click through chrome.debugger + CDP's Input.dispatchMouseEvent,
 * which the renderer treats as genuinely trusted input the same way Puppeteer-style tools
 * are. That worked reliably, but the "debugger" permission is a major red flag for Chrome
 * Web Store review (heavy scrutiny, frequent rejection for consumer extensions), so it was
 * pulled back out ahead of public listing. Reintroducing this needs either: (a) accepting
 * the debugger permission and its review risk, (b) shipping a separate "developer mode"
 * build that isn't Store-listed, or (c) finding some other way to produce a trusted click
 * that isn't invented yet. The removed implementation (toggle finder, state detection,
 * chrome.debugger relay in background.js) is recoverable from git history if resumed. */

function runCommand(command, param) {
  switch (command) {
    case 'toggle-mic':
      return clickMatched(BUTTON_MATCHERS.mic, command);
    case 'toggle-camera':
      return clickMatched(BUTTON_MATCHERS.camera, command);
    case 'toggle-hand':
      return clickMatched(BUTTON_MATCHERS.hand, command);
    case 'leave-call':
      return clickMatched(BUTTON_MATCHERS.leave, command);
    case 'toggle-captions':
      return clickMatched(BUTTON_MATCHERS.captions, command);
    case 'toggle-screen-share':
      toggleScreenShare();
      return setTimeout(reportState, 200);
    case 'send-reaction':
      return sendReaction(param);
    case 'close-reactions':
      return closeReactionsPicker();
    default:
      console.warn(`[MX Keys Bridge] unknown command "${command}"`);
  }
}

function clickMatched(matcher, command) {
  const button = findButton(matcher);
  if (!button) {
    console.warn(`[MX Keys Bridge] could not find the control for "${command}" — Meet's UI may have changed`);
    return;
  }
  button.click();
  setTimeout(reportState, 200);
}

api.runtime.onMessage.addListener((message) => {
  if (message?.type === 'command') runCommand(message.command, message.param);
});

// Meet re-renders its toolbar constantly; watch aria-label changes rather than polling the DOM shape.
const observer = new MutationObserver(() => reportState());
observer.observe(document.body, { attributes: true, attributeFilter: ['aria-label'], subtree: true });

// Declared before the first reportState() call so stopReporting() has something
// valid to clear even if that very first call is the one that fails.
let pollIntervalId = null;
reportState();
pollIntervalId = setInterval(reportState, 4000);

window.addEventListener('beforeunload', () => {
  clearInterval(pollIntervalId);
  try {
    api.runtime.sendMessage({ type: 'state', inCall: false, micMuted: null, cameraOn: null, handRaised: null, captionsOn: null });
  } catch {
    // context may already be gone — nothing to do, the tab is closing anyway.
  }
});
