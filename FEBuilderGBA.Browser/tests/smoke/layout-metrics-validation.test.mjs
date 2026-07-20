// #1998 follow-up: focused regression coverage for the fail-closed MapEditorLayoutMetrics()
// validation gate (layout-metrics-validation.mjs). Proves the false-pass class flagged in code
// review — `{}`, missing fields, non-finite/negative metrics, and hook `error` payloads must all be
// REJECTED (metrics=null, errors non-empty) — and that a complete valid metrics object is ACCEPTED.
//
// Uses only Node's built-in `node:test` + `node:assert/strict` — no new test framework/dependency.
// Run with: node --test tests/smoke/layout-metrics-validation.test.mjs

import test from 'node:test';
import assert from 'node:assert/strict';
import { validateMapEditorLayoutMetrics, bounded, REQUIRED_METRIC_KEYS } from './layout-metrics-validation.mjs';

const VALID_METRICS = {
  title: 'Visual Map Editor',
  upperExtentHeight: 613,
  upperViewportHeight: 189,
  mapCanvasWidth: 326,
  mapCanvasHeight: 240,
  canvasExtentWidth: 324,
  canvasViewportWidth: 324,
  canvasExtentHeight: 238,
  canvasViewportHeight: 238,
};

test('accepts a complete, valid metrics object', () => {
  const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify(VALID_METRICS));
  assert.deepEqual(errors, []);
  assert.notEqual(metrics, null);
  assert.equal(metrics.title, 'Visual Map Editor');
  assert.equal(metrics.mapCanvasHeight, 240);
});

test('rejects an empty object `{}` (the reviewed false-pass class)', () => {
  const { metrics, errors } = validateMapEditorLayoutMetrics('{}');
  assert.equal(metrics, null);
  assert.ok(errors.length > 0, 'expected at least one rejection reason');
});

test('rejects non-JSON payloads', () => {
  const { metrics, errors } = validateMapEditorLayoutMetrics('not json at all');
  assert.equal(metrics, null);
  assert.ok(errors[0].includes('non-JSON'));
});

test('rejects array payloads', () => {
  const { metrics, errors } = validateMapEditorLayoutMetrics('[1,2,3]');
  assert.equal(metrics, null);
  assert.ok(errors[0].includes('non-object payload'));
});

test('rejects a hook `error` payload', () => {
  const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify({
    error: 'MapEditorLayoutMetrics: no active navigation host/content — no editor is open.',
  }));
  assert.equal(metrics, null);
  assert.ok(errors[0].includes('hard config error'));
});

test('rejects an unexpected title', () => {
  const payload = { ...VALID_METRICS, title: 'Move Cost Editor' };
  const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify(payload));
  assert.equal(metrics, null);
  assert.ok(errors[0].includes('reported title'));
});

test('a wrong-editor payload with mismatched title but no `error` field is still rejected without `requireTitle`', () => {
  // Even when a caller does not require the Map Editor title specifically (e.g. the pre-navigation
  // probe, where NO editor should be open yet), a payload missing the required metrics must still
  // be rejected — proving the completeness gate does not depend solely on the title check.
  const { metrics, errors } = validateMapEditorLayoutMetrics('{}', { requireTitle: false });
  assert.equal(metrics, null);
  assert.ok(errors.length > 0);
});

test('rejects when every required metric key is individually missing', () => {
  for (const key of REQUIRED_METRIC_KEYS) {
    const payload = { ...VALID_METRICS };
    delete payload[key];
    const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify(payload));
    assert.equal(metrics, null, `expected rejection when "${key}" is missing`);
    assert.ok(errors[0].includes(key), `expected the failure message to name "${key}"`);
  }
});

test('rejects non-finite metric values (null/undefined-shaped after JSON round-trip)', () => {
  const payload = { ...VALID_METRICS, canvasExtentWidth: null };
  const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify(payload));
  assert.equal(metrics, null);
  assert.ok(errors[0].includes('canvasExtentWidth'));
});

test('rejects negative sentinel metric values (the previous `-1` fallback shape)', () => {
  const payload = { ...VALID_METRICS, upperExtentHeight: -1 };
  const { metrics, errors } = validateMapEditorLayoutMetrics(JSON.stringify(payload));
  assert.equal(metrics, null);
  assert.ok(errors[0].includes('upperExtentHeight'));
});

test('bounded() truncates long diagnostic strings and leaves short ones untouched', () => {
  assert.equal(bounded('short'), 'short');
  const long = 'x'.repeat(1000);
  const result = bounded(long, 50);
  assert.ok(result.length < long.length);
  assert.ok(result.includes('truncated'));
});
