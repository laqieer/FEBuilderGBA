// #1998 follow-up: pure, dependency-free validation for TestHooks.MapEditorLayoutMetrics()'s JSON
// payload, extracted from smoke.mjs so it can be exercised directly by a focused regression test
// WITHOUT booting a browser.
//
// This is the fail-closed gate that rejects `{}`, a hook `error` payload, an unexpected title, and
// missing/non-finite/negative metrics BEFORE any layout assertion ever sees the data. A code-review
// finding showed the previous implementation let a probe/runtime exception (which returned `{}`)
// silently false-pass a real-ROM desktop run: `undefined` metric comparisons never evaluated to a
// pushed failure, so a completely broken hook still exited 0. Every caller (smoke.mjs) MUST treat a
// non-empty `errors` array from `validateMapEditorLayoutMetrics` as a hard smoke failure — never as
// "there is nothing to assert this run".
//
// No new test framework/dependency is introduced: the accompanying test file uses only Node's
// built-in `node:test` + `node:assert/strict`.

export const REQUIRED_METRIC_KEYS = [
  'upperExtentWidth',
  'upperViewportWidth',
  'upperExtentHeight',
  'upperViewportHeight',
  'mapCanvasWidth',
  'mapCanvasHeight',
  'canvasExtentWidth',
  'canvasViewportWidth',
  'canvasExtentHeight',
  'canvasViewportHeight',
];

export const EXPECTED_MAP_EDITOR_TITLE = 'Visual Map Editor';

// Bounds any raw/diagnostic string embedded in a failure message so a malformed or unexpectedly
// large hook payload never gets dumped into CI logs unbounded.
export function bounded(value, maxLen = 300) {
  const s = String(value);
  return s.length > maxLen ? `${s.slice(0, maxLen)}\u2026(truncated, ${s.length} chars total)` : s;
}

// Parses + validates a MapEditorLayoutMetrics() JSON string.
//
// Returns `{ metrics, errors }`:
//   - `metrics` is the parsed object ONLY when it is fully trustworthy: valid JSON, a plain
//     (non-array) object, no `error` field, the expected Visual Map Editor title, and every
//     REQUIRED_METRIC_KEYS entry is a finite, non-negative number. Otherwise `metrics` is `null`.
//   - `errors` lists every bounded, human-readable reason metrics were rejected (empty when
//     `metrics` is non-null).
//
// `requireTitle` (default true) lets a caller validate a payload where no Map Editor is expected to
// be open yet (see the pre-navigation fail-closed probe in smoke.mjs) by only requiring an `error`
// field OR a title mismatch/missing-metrics outcome — i.e. "this must NOT look like valid Map Editor
// metrics" rather than needing the very same title check to be the rejection reason.
export function validateMapEditorLayoutMetrics(metricsRaw, { requireTitle = true } = {}) {
  const errors = [];

  let parsed;
  try {
    parsed = JSON.parse(metricsRaw);
  } catch {
    errors.push(`MapEditorLayoutMetrics() returned non-JSON: ${bounded(metricsRaw)}`);
    return { metrics: null, errors };
  }

  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    errors.push(`MapEditorLayoutMetrics() returned a non-object payload: ${bounded(metricsRaw)}`);
    return { metrics: null, errors };
  }

  // #1998 follow-up (review): reject the OWN-property PRESENCE of `error` regardless of its type or
  // value — an empty string, `null`, `0`, `false`, or an object/array all still mean "the hook is
  // reporting a problem" and must never be treated as absent just because the earlier
  // `typeof === 'string' && length > 0` check happened not to match that particular value/type.
  // `Object.hasOwn` (rather than `'error' in parsed` or a truthiness check) also correctly ignores
  // any `error` inherited from a prototype rather than actually present in the parsed JSON object.
  if (Object.hasOwn(parsed, 'error')) {
    errors.push(`MapEditorLayoutMetrics() reported a hard config error: ${bounded(JSON.stringify(parsed.error))}`);
    return { metrics: null, errors };
  }

  if (requireTitle && parsed.title !== EXPECTED_MAP_EDITOR_TITLE) {
    errors.push(`MapEditorLayoutMetrics() reported title ${bounded(JSON.stringify(parsed.title))}; ` +
      `expected ${JSON.stringify(EXPECTED_MAP_EDITOR_TITLE)}`);
    return { metrics: null, errors };
  }

  const badKeys = REQUIRED_METRIC_KEYS.filter((k) => !Number.isFinite(parsed[k]) || parsed[k] < 0);
  if (badKeys.length > 0) {
    errors.push('MapEditorLayoutMetrics() returned missing/non-finite/negative metric(s): ' +
      badKeys.map((k) => `${k}=${bounded(JSON.stringify(parsed[k]))}`).join(', '));
    return { metrics: null, errors };
  }

  return { metrics: parsed, errors };
}
