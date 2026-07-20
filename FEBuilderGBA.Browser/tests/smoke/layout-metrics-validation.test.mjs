// #1998 follow-up: focused regression coverage for the fail-closed MapEditorLayoutMetrics()
// validation gate (layout-metrics-validation.mjs). Proves the false-pass class flagged in code
// review — `{}`, missing fields, non-finite/negative metrics, and hook `error` payloads must all be
// REJECTED (metrics=null, errors non-empty) — and that a complete valid metrics object is ACCEPTED.
//
// Cases live in the SHARED layout-metrics-validation.cases.mjs table so smoke.mjs's
// runContractSelfCheck() (a no-`node --test` re-verification run before any browser launches,
// since `.github/workflows/pages.yml` only ever invokes smoke.mjs directly) can never silently
// diverge from this file's coverage.
//
// Uses only Node's built-in `node:test` + `node:assert/strict` — no new test framework/dependency.
// Run with: node layout-metrics-validation.test.mjs   (or: node --test layout-metrics-validation.test.mjs)

import test from 'node:test';
import assert from 'node:assert/strict';
import { validateMapEditorLayoutMetrics, bounded, REQUIRED_METRIC_KEYS } from './layout-metrics-validation.mjs';
import { CASES, missingKeyCases } from './layout-metrics-validation.cases.mjs';

for (const c of [...CASES, ...missingKeyCases(REQUIRED_METRIC_KEYS)]) {
  test(c.name, () => {
    const { metrics, errors } = validateMapEditorLayoutMetrics(c.input, c.options);
    if (c.expect === 'accept') {
      assert.deepEqual(errors, []);
      assert.notEqual(metrics, null);
    } else {
      assert.equal(metrics, null);
      assert.ok(errors.length > 0, 'expected at least one rejection reason');
      if (c.errorIncludes) {
        assert.ok(errors.some((e) => e.includes(c.errorIncludes)),
          `expected an error message to include "${c.errorIncludes}", got: ${JSON.stringify(errors)}`);
      }
    }
  });
}

test('accepted metrics expose the expected field values (VALID_METRICS shape)', () => {
  const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify({
    title: 'Visual Map Editor',
    upperExtentHeight: 613,
    upperViewportHeight: 189,
    mapCanvasWidth: 326,
    mapCanvasHeight: 240,
    canvasExtentWidth: 324,
    canvasViewportWidth: 324,
    canvasExtentHeight: 238,
    canvasViewportHeight: 238,
  }));
  assert.deepEqual(errors, []);
  assert.notEqual(metrics, null);
  assert.equal(metrics.title, 'Visual Map Editor');
  assert.equal(metrics.mapCanvasHeight, 240);
});

test('bounded() truncates long diagnostic strings and leaves short ones untouched', () => {
  assert.equal(bounded('short'), 'short');
  const long = 'x'.repeat(1000);
  const result = bounded(long, 50);
  assert.ok(result.length < long.length);
  assert.ok(result.includes('truncated'));
});
