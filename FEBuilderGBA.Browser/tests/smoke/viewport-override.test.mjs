// #1998 follow-up: focused regression coverage for parseViewportOverride() (viewport-override.mjs).
// Proves the exact bypass flagged in code review — a fractional explicit viewport value (e.g.
// "600.5") previously passed the finite/>0-only check and reached Playwright's
// `browser.newContext({ viewport })`, which requires integer dimensions and throws an unhandled
// setup error — is now rejected with a controlled, bounded "positive integers" message, alongside
// zero/negative/non-finite values and an incomplete override pair; and that a valid explicit pair
// and the default (both-unset) matrix are both accepted.
//
// Cases live in the SHARED viewport-override.cases.mjs table so smoke.mjs's
// runViewportParserSelfCheck() (a no-`node --test` re-verification run before any server/browser
// starts, since `.github/workflows/pages.yml` only ever invokes smoke.mjs directly) can never
// silently diverge from this file's coverage.
//
// Uses only Node's built-in `node:test` + `node:assert/strict` — no new test framework/dependency.
// Run with: node viewport-override.test.mjs   (or: node --test viewport-override.test.mjs)

import test from 'node:test';
import assert from 'node:assert/strict';
import { parseViewportOverride, DEFAULT_VIEWPORT_PLAN } from './viewport-override.mjs';
import { CASES } from './viewport-override.cases.mjs';

for (const c of CASES) {
  test(c.name, () => {
    const result = parseViewportOverride(c.wRaw, c.hRaw);
    if (c.expect === 'accept') {
      assert.equal(result.ok, true);
      assert.deepEqual(result.plan, c.expectedPlan);
    } else {
      assert.equal(result.ok, false);
      assert.equal(typeof result.error, 'string');
      assert.ok(result.error.length > 0, 'expected a non-empty error message');
      if (c.errorIncludes) {
        assert.ok(result.error.includes(c.errorIncludes),
          `expected the error message to include "${c.errorIncludes}", got: ${result.error}`);
      }
    }
  });
}

test('DEFAULT_VIEWPORT_PLAN is exactly the compact+acceptance matrix used when both vars are unset', () => {
  const result = parseViewportOverride(undefined, undefined);
  assert.equal(result.ok, true);
  assert.equal(result.plan, DEFAULT_VIEWPORT_PLAN);
});

test('empty-string env values are treated the same as unset (both-or-neither)', () => {
  const result = parseViewportOverride('', '');
  assert.equal(result.ok, true);
  assert.deepEqual(result.plan, DEFAULT_VIEWPORT_PLAN);
});
