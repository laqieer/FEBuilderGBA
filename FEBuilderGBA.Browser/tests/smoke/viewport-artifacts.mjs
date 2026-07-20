// #1998 follow-up (review PRRT_kwDOH0Mc1M6STCRA): pure, dependency-free helpers for deriving every
// artifact path a single smoke.mjs viewport run may produce, extracted so the full set is defined
// in exactly ONE place and can be exercised directly by a focused regression test without booting
// a browser.
//
// A code-review finding showed the per-run startup cleanup previously removed only `mainPath`. On
// a repeated local invocation, an early failure (e.g. before `beforePath` or the Move Cost sidecars
// were ever (re)written by this run) left the PRIOR invocation's stale sidecar sitting next to a
// fresh mainPath/fallback screenshot — making a stale pre-navigation `.before.png` (or stale Move
// Cost pair) look like it belongs to the current failing run. Removing every path this viewport may
// produce at startup prevents that.

import path from 'node:path';

// deriveSidecar('web-boot-smoke.png', 'compact') -> 'web-boot-smoke.compact.png'.
export function deriveSidecar(basePath, suffix) {
  const parsed = path.parse(basePath);
  return path.join(parsed.dir || '.', `${parsed.name}.${suffix}${parsed.ext || '.png'}`);
}

// Every artifact path a single runViewport() invocation may write, given its already-resolved
// `mainPath` (itself already vp.owning ? SCREENSHOT : deriveSidecar(SCREENSHOT, vp.tag) in the
// caller). Order is not significant; callers should remove/ignore-missing every entry at startup.
export function getViewportArtifactPaths(mainPath) {
  return [
    mainPath,
    deriveSidecar(mainPath, 'before'),
    deriveSidecar(mainPath, 'movecost.before'),
    deriveSidecar(mainPath, 'movecost'),
  ];
}
