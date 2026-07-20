// #1998 follow-up: pure, dependency-free parser for smoke.mjs's SMOKE_VIEWPORT_WIDTH /
// SMOKE_VIEWPORT_HEIGHT explicit-viewport override pair, extracted from smoke.mjs so the SAME
// function backs both real env-var resolution and a focused regression test / fast CI-executed
// self-check — without booting a browser or spawning a process.
//
// A code-review finding showed a fractional explicit value (e.g. "600.5") passed the previous
// finite/>0-only check, then made Playwright's `browser.newContext({ viewport })` throw an
// UNHANDLED setup error (Playwright requires integer viewport dimensions) — turning what should be
// a clean, controlled `exit 2` (before any server/browser starts) into a crash after startup had
// already begun. `Number.isInteger` rejects fractional AND non-finite (NaN/Infinity) values in one
// check, so no separate `Number.isFinite` guard is needed.

import { bounded } from './layout-metrics-validation.mjs';

export const DEFAULT_VIEWPORT_PLAN = [
  { width: 600, height: 500, tag: 'compact', owning: false },
  { width: 1920, height: 852, tag: 'acceptance', owning: true },
];

// Parses the raw SMOKE_VIEWPORT_WIDTH/SMOKE_VIEWPORT_HEIGHT string values (as read from
// `process.env`, or an equivalent test double) into a viewport plan.
//
// Returns `{ ok: true, plan }`:
//   - both absent -> `DEFAULT_VIEWPORT_PLAN` (the in-process compact+acceptance matrix).
//   - both present AND valid (positive integers) -> a single-entry explicit plan.
// Returns `{ ok: false, error }` (one bounded, human-readable string) for every other case: only
// one of the pair set, or either value non-integer (including fractional numeric strings like
// "600.5"), non-finite, zero, or negative. Never throws.
export function parseViewportOverride(wRaw, hRaw) {
  const wSet = wRaw !== undefined && wRaw !== '';
  const hSet = hRaw !== undefined && hRaw !== '';
  if (wSet !== hSet) {
    return {
      ok: false,
      error: 'SMOKE_VIEWPORT_WIDTH and SMOKE_VIEWPORT_HEIGHT must both be set together, or both left ' +
        `unset for the default compact+acceptance matrix (got SMOKE_VIEWPORT_WIDTH=${bounded(wRaw ?? '<unset>')}, ` +
        `SMOKE_VIEWPORT_HEIGHT=${bounded(hRaw ?? '<unset>')})`,
    };
  }
  if (!wSet) {
    return { ok: true, plan: DEFAULT_VIEWPORT_PLAN };
  }
  const width = Number(wRaw);
  const height = Number(hRaw);
  if (!Number.isInteger(width) || width <= 0 || !Number.isInteger(height) || height <= 0) {
    return {
      ok: false,
      error: 'SMOKE_VIEWPORT_WIDTH/SMOKE_VIEWPORT_HEIGHT must both be positive integers (Playwright ' +
        `requires integer viewport dimensions) — got "${bounded(wRaw)}"x"${bounded(hRaw)}"`,
    };
  }
  return { ok: true, plan: [{ width, height, tag: 'explicit', owning: true }] };
}
