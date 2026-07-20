// #1998 follow-up (review PRRT_kwDOH0Mc1M6STCRA): focused regression coverage for
// getViewportArtifactPaths()/deriveSidecar() (viewport-artifacts.mjs).
//
// Proves the exact gap flagged in code review — the per-viewport startup cleanup previously
// removed only `mainPath`, so an early failure before `beforePath`/the Move Cost sidecars were
// (re)written by THIS run could leave a stale PRIOR invocation's sidecar sitting next to a fresh
// mainPath/fallback screenshot, making it look like a matching before/after (or Move Cost) pair
// from the same run when it is not. `getViewportArtifactPaths()` is the single place the FULL set
// of artifacts a viewport run may produce is defined, so smoke.mjs's startup cleanup can never
// silently forget one.
//
// Uses only Node's built-in `node:test` + `node:assert/strict` — no new test framework/dependency.
// Run with: node viewport-artifacts.test.mjs   (or: node --test viewport-artifacts.test.mjs)

import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { deriveSidecar, getViewportArtifactPaths } from './viewport-artifacts.mjs';

test('deriveSidecar inserts the suffix before the extension, preserving the directory', () => {
  assert.equal(deriveSidecar(path.join('out', 'web-boot-smoke.png'), 'compact'),
    path.join('out', 'web-boot-smoke.compact.png'));
  assert.equal(deriveSidecar('web-boot-smoke.png', 'before'), 'web-boot-smoke.before.png');
});

test('deriveSidecar defaults to a .png extension when the base path has none', () => {
  assert.equal(deriveSidecar('artifact', 'before'), 'artifact.before.png');
});

test('getViewportArtifactPaths returns exactly the 4 artifacts a single viewport run may produce', () => {
  const mainPath = path.join('out', 'web-boot-smoke.png');
  const paths = getViewportArtifactPaths(mainPath);
  assert.deepEqual(paths, [
    mainPath,
    path.join('out', 'web-boot-smoke.before.png'),
    path.join('out', 'web-boot-smoke.movecost.before.png'),
    path.join('out', 'web-boot-smoke.movecost.png'),
  ]);
});

test('getViewportArtifactPaths always includes mainPath itself as the first entry, all 4 distinct', () => {
  const mainPath = path.join('scratch', 'derived.compact.png');
  const paths = getViewportArtifactPaths(mainPath);
  assert.ok(paths.includes(mainPath), 'expected mainPath itself to be one of the cleaned-up paths');
  assert.equal(paths.length, 4, 'expected exactly 4 artifact paths (main + before + 2 movecost sidecars)');
  assert.equal(new Set(paths).size, 4, 'expected all 4 artifact paths to be distinct');
});
