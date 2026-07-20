// #1998 follow-up (review PRRT_kwDOH0Mc1M6STCRA, PRRT_kwDOH0Mc1M6SVA6P): focused regression
// coverage for getViewportArtifactPaths()/deriveSidecar() (viewport-artifacts.mjs).
//
// Proves the exact gap flagged in code review — the per-viewport startup cleanup previously
// removed only `mainPath`, so an early failure before `beforePath`/the Move Cost sidecars were
// (re)written by THIS run could leave a stale PRIOR invocation's sidecar sitting next to a fresh
// mainPath/fallback screenshot, making it look like a matching before/after (or Move Cost) pair
// from the same run when it is not. `getViewportArtifactPaths()` is the single place the FULL set
// of artifacts a viewport run may produce is defined, so smoke.mjs's startup cleanup can never
// silently forget one.
//
// A SECOND code-review finding (PRRT_kwDOH0Mc1M6SVA6P) showed this suite alone provides ZERO CI
// protection: `.github/workflows/pages.yml` only ever invokes `node smoke.mjs` directly, never
// `node --test` — so a regression here could merge while every required check stays green. Cases
// now live in the SHARED viewport-artifacts.cases.mjs table so smoke.mjs's
// runArtifactContractSelfCheck() (run before any server/browser starts) exercises the exact same
// case data as this file, matching the established layout-metrics-validation.cases.mjs /
// viewport-override.cases.mjs pattern — the two coverage paths can never silently diverge.
//
// Uses only Node's built-in `node:test` + `node:assert/strict` — no new test framework/dependency.
// Run with: node viewport-artifacts.test.mjs   (or: node --test viewport-artifacts.test.mjs)

import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { deriveSidecar, getViewportArtifactPaths } from './viewport-artifacts.mjs';
import { CASES } from './viewport-artifacts.cases.mjs';

const ARTIFACT_FNS = { deriveSidecar, getViewportArtifactPaths };

for (const c of CASES) {
  test(c.name, () => {
    const fn = ARTIFACT_FNS[c.fn];
    assert.equal(typeof fn, 'function', `expected a known artifact-path function named "${c.fn}"`);
    assert.deepEqual(fn(...c.args), c.expected);
  });
}

// Invariants that hold for EVERY getViewportArtifactPaths() result, not just the two case-table
// fixtures above — kept as direct node:test-only coverage since they assert a structural property
// of the return value rather than a specific input->output mapping.
test('getViewportArtifactPaths always includes mainPath itself as the first entry, all 4 distinct', () => {
  const mainPath = path.join('scratch', 'derived.compact.png');
  const paths = getViewportArtifactPaths(mainPath);
  assert.equal(paths[0], mainPath, 'expected mainPath itself to be the first cleaned-up path');
  assert.equal(paths.length, 4, 'expected exactly 4 artifact paths (main + before + 2 movecost sidecars)');
  assert.equal(new Set(paths).size, 4, 'expected all 4 artifact paths to be distinct');
});
