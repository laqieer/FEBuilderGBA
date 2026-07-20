// #1998 follow-up (review): the SHARED case table for the fail-closed
// validateMapEditorLayoutMetrics() gate (layout-metrics-validation.mjs). Two independent callers
// exercise these exact cases so they can never silently diverge:
//   1. layout-metrics-validation.test.mjs — full `node:test` assertion detail per case (run via
//      `node layout-metrics-validation.test.mjs` or `node --test`).
//   2. smoke.mjs's runContractSelfCheck() — a fast, dependency-free re-verification of the SAME
//      contract, run once before any browser is launched. `.github/workflows/pages.yml` only ever
//      invokes smoke.mjs directly (never `node --test`), so without this self-check a future
//      accidental change that silently weakens the gate would have zero coverage on the path that
//      actually gates CI/merges — no workflow file is touched to get that coverage.
//
// No new test framework/dependency is introduced.

export const VALID_METRICS = {
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

// Each case: { name, input: raw JSON string, options?: passed through to
// validateMapEditorLayoutMetrics, expect: 'accept' | 'reject', errorIncludes?: substring at least
// one returned error message must contain when expect === 'reject'. }
export const CASES = [
  {
    name: 'accepts a complete, valid metrics object',
    input: JSON.stringify(VALID_METRICS),
    expect: 'accept',
  },
  {
    name: 'rejects an empty object `{}` (the reviewed false-pass class)',
    input: '{}',
    expect: 'reject',
  },
  {
    name: 'rejects non-JSON payloads',
    input: 'not json at all',
    expect: 'reject',
    errorIncludes: 'non-JSON',
  },
  {
    name: 'rejects array payloads',
    input: '[1,2,3]',
    expect: 'reject',
    errorIncludes: 'non-object payload',
  },
  {
    name: 'rejects a hook `error` payload',
    input: JSON.stringify({
      error: 'MapEditorLayoutMetrics: no active navigation host/content — no editor is open.',
    }),
    expect: 'reject',
    errorIncludes: 'hard config error',
  },
  {
    // #1998 follow-up (review): an `error` key must reject regardless of type/value — an empty
    // string is still an OWN property named `error` and must not slip past a truthiness/length
    // check the way the pre-review implementation would have allowed.
    name: 'rejects a hook payload with an empty-string `error` property',
    input: JSON.stringify({ ...VALID_METRICS, error: '' }),
    expect: 'reject',
    errorIncludes: 'hard config error',
  },
  {
    name: 'rejects a hook payload with a `null`-valued `error` property',
    input: JSON.stringify({ ...VALID_METRICS, error: null }),
    expect: 'reject',
    errorIncludes: 'hard config error',
  },
  {
    name: 'rejects a hook payload with a numeric (non-string) `error` property',
    input: JSON.stringify({ ...VALID_METRICS, error: 0 }),
    expect: 'reject',
    errorIncludes: 'hard config error',
  },
  {
    name: 'rejects a hook payload with an object-valued `error` property',
    input: JSON.stringify({ ...VALID_METRICS, error: { code: 42 } }),
    expect: 'reject',
    errorIncludes: 'hard config error',
  },
  {
    name: 'rejects an unexpected title',
    input: JSON.stringify({ ...VALID_METRICS, title: 'Move Cost Editor' }),
    expect: 'reject',
    errorIncludes: 'reported title',
  },
  {
    name: 'a wrong-editor payload (`{}`) is still rejected without requireTitle',
    input: '{}',
    options: { requireTitle: false },
    expect: 'reject',
  },
  {
    name: 'rejects a non-finite metric value (null after JSON round-trip)',
    input: JSON.stringify({ ...VALID_METRICS, canvasExtentWidth: null }),
    expect: 'reject',
    errorIncludes: 'canvasExtentWidth',
  },
  {
    name: 'rejects a negative sentinel metric value (the previous `-1` fallback shape)',
    input: JSON.stringify({ ...VALID_METRICS, upperExtentHeight: -1 }),
    expect: 'reject',
    errorIncludes: 'upperExtentHeight',
  },
];

// Generates one reject-case per REQUIRED_METRIC_KEYS entry (each key individually deleted from an
// otherwise-valid payload) — a function of the LIVE key list (rather than a baked-in array) so
// both callers exercise identical generation logic against layout-metrics-validation.mjs's actual
// REQUIRED_METRIC_KEYS export.
export function missingKeyCases(requiredMetricKeys) {
  return requiredMetricKeys.map((key) => {
    const payload = { ...VALID_METRICS };
    delete payload[key];
    return {
      name: `rejects when required metric key "${key}" is missing`,
      input: JSON.stringify(payload),
      expect: 'reject',
      errorIncludes: key,
    };
  });
}
