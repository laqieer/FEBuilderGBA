# Web boot smoke test (#1867)

A headless-browser test that proves the FEBuilderGBA **WebAssembly** app actually *renders*, not
merely that its assets return HTTP 200.

## Why

Issue [#1867](https://github.com/laqieer/FEBuilderGBA/issues/1867) shipped a web build where every
static asset returned `200` yet the app hung on the loading splash forever: Avalonia's runtime
requested `_framework/avalonia.js` (a `404`, because the module actually publishes to the AppBundle
root). HTTP-200 checks structurally cannot catch that. Only a real browser executing the wasm runtime
can — this test does.

## What it asserts

1. No `avalonia.js` request returns `>= 400` (the exact #1867 symptom).
2. The Avalonia Skia `<canvas>` mounts (the splash in `#out` is replaced) — i.e. the app rendered.
3. When `SMOKE_ROM` is set, the real wasm runtime loads a ROM, invokes the production launcher
   command for "Move Cost Editor", verifies the current single-view editor title, then opens the
   Visual Map Editor (#1998) and asserts its compact/desktop split-scroller layout at the run's
   viewport.

It always writes a screenshot for proof / debugging — see "Viewport coverage and screenshots" below
for exactly which run owns which filename.

## Viewport coverage and screenshots (#1998 follow-up)

By default — when **both** `SMOKE_VIEWPORT_WIDTH` and `SMOKE_VIEWPORT_HEIGHT` are left unset — the
script runs an **in-process, sequential dual-viewport matrix** in a single browser process, using a
fresh isolated context/page per viewport:

1. **600×500 ("compact")** — exercises the Map Editor's compact upper-controls-scroll path.
2. **1920×852 ("acceptance")** — exercises the normal desktop layout.

Failures from either viewport are aggregated; the process exits nonzero if either run fails. This is
the mode `.github/workflows/pages.yml` uses today (it never sets the viewport env vars), so both of
its existing invocations get dual-viewport coverage with no workflow changes.

If you set **both** `SMOKE_VIEWPORT_WIDTH` and `SMOKE_VIEWPORT_HEIGHT`, the script instead runs
**exactly that single viewport** (no matrix). Both values must be **positive integers** (Playwright
requires integer viewport dimensions — a fractional value like `600.5` is rejected before any
server/browser starts, not left to crash Playwright's `newContext` call with an unhandled setup
error). Setting only one of the two, or an invalid value (fractional, zero, negative, or
non-numeric), is rejected with a clear, bounded error and exit code `2`. This parsing lives in the
pure `parseViewportOverride()` function (`viewport-override.mjs`), shared by the real env-var
resolution below, `viewport-override.test.mjs`'s `node --test` coverage, and a fast,
dependency-free self-check the script runs on itself before launching any server/browser (see
"Validator contract self-check" below).

Screenshot ownership: the acceptance run (or the single explicit-viewport run) owns the exact
`SMOKE_SCREENSHOT` path and its `.before.png` — the same two files
`.github/workflows/pages.yml` uploads today. Every other screenshot (the compact matrix pass, and
the Move Cost Editor's own before/after proof at every viewport) is written to a collision-free
sidecar filename derived from `SMOKE_SCREENSHOT`, e.g. `web-editor-nav-smoke.compact.png` or
`web-editor-nav-smoke.movecost.png`. **These sidecars are not uploaded as CI artifacts** — they
exist purely for local/manual inspection; only `SMOKE_SCREENSHOT` and its `.before.png` are uploaded
by the workflow.

The **main** pair (`SMOKE_SCREENSHOT` / its sidecar equivalent, and its `.before.png`) are always
full-viewport captures with **no content clip**, at device-pixel-ratio 1 — so they are exactly
`width`×`height` pixels. The `.before.png` is taken right after boot, before any editor opens; the
main screenshot is taken **last**, after opening the Visual Map Editor, so it proves the Map
Editor's actual on-screen layout at that viewport. The Move Cost Editor's own proof pair
(`*.movecost.before.png` / `*.movecost.png`) uses a recomputed 80px-header content clip specific to
each run's viewport, and is diffed byte-for-byte to prove the editor body actually re-rendered.

## Real ROM vs. synthetic ROM (#1998 follow-up)

Synthetic map pixels are injected into the Map Editor's canvas **only** when `SMOKE_ROM` is exactly
the literal string `synthetic` — never for a real ROM path. With synthetic pixels active, the
in-process E2E hook (`TestHooks.MapEditorLayoutMetrics`) is asked to inject synthetic pixels *and*
requesting that against a real ROM load is treated as an inconsistent configuration and hard-fails
with a JSON `error` field (never silently overwrites real ROM-derived pixels). With synthetic
pixels active, the smoke test hard-asserts both inner-canvas axes overflow their scroller viewport
(`extent > viewport` for width AND height) — the synthetic fixture is deliberately oversized
(~2200×1200) to guarantee this at any viewport up to 1920×852.

With a **real** ROM, the same extent/viewport metrics (and the desktop "upper controls fit without
scrolling" expectation) are **logged**, not hard-asserted, because a real chapter's on-screen map
size — and how much space its populated palette/terrain lists need — is ROM-data-dependent; a
smaller/larger chapter can legitimately need more or less room than the synthetic fixture assumes.
Real ROM runs always render the ROM's own authentic map/palette pixels.

## Fail-closed metrics contract (post-review hardening)

`TestHooks.MapEditorLayoutMetrics` never returns `{}` or a success-shaped payload with missing/
sentinel fields. Every situation where trustworthy metrics cannot be produced — no navigation host/
content is active, the wrong editor is open (a named upper-controls/map-canvas/canvas-scroller
control is missing), synthetic injection was requested but `MapImageControl` is absent, an unset
compute result, or a caught exception — returns a bounded JSON `error` string instead. `smoke.mjs`
treats the OWN-property **presence** of an `error` key as a hard failure regardless of its
type/value (`''`, `null`, a number, or an object all reject — not only a non-empty string), plus
non-object/array JSON, an unexpected title, or any of the 8 required numeric metrics being
missing/non-finite/negative (shared `layout-metrics-validation.mjs` gate, using `Object.hasOwn` so
metrics can never carry an `error` key of any shape), for both synthetic and real-ROM runs — a
probe/runtime failure can never be silently logged as "nothing to assert" and exit `0`. The smoke
script also calls the hook once before any editor is open and asserts it fails closed, proving the
C# contract against the live app rather than only checking source text.

### Synthetic authorization is revoked before every load attempt

`TestHooks.LoadRomBase64` resets its internal synthetic-authorization flag to `false` as the very
first statement of every call — before Base64 decode, ROM load, `InitializeLoadedRom`, or
`Refresh` — and re-grants it only after the entire load+initialization+refresh sequence completes
successfully. A prior successful synthetic load's authorization can never leak into a later failed
or exceptional load attempt (e.g. a subsequent non-synthetic reload that fails). The synthetic
smoke run proves this live: after its own MapEditor assertions/screenshot are captured, it attempts
a deliberately-invalid non-synthetic reload in the same page, asserts `LoadRomBase64` returns
`false`, then requests synthetic injection again and asserts `MapEditorLayoutMetrics` now returns
an `error` — confirming the earlier `true` authorization did not survive the failed reload. This
probe runs only for the synthetic-ROM run and only after that run's own evidence is already
captured, so it can never mutate accepted screenshots/metrics.

### Validator contract self-check (runs before any browser launches)

The fail-closed gate above is itself covered by a small case table
(`layout-metrics-validation.cases.mjs`) that is imported by **both**:

- `layout-metrics-validation.test.mjs` — the full `node:test` regression suite (run directly with
  `node layout-metrics-validation.test.mjs`, or with `node --test`).
- `smoke.mjs` — which re-runs the SAME cases as a fast, dependency-free self-check the moment it
  starts (before the HTTP server or the browser are created). If the gate itself has silently
  regressed (e.g. a future edit stops rejecting `{}`), the self-check logs bounded diagnostics and
  exits `2` — before any browser work is attempted.

`.github/workflows/pages.yml` only ever invokes `smoke.mjs` directly; it does not run
`node --test`. This self-check is what gives that CI-gating entrypoint the same regression coverage
as the pure test file, without any change to the workflow file itself. A normal, successful smoke
run logs a single `validator contract self-check passed (...)` line confirming the check ran.

The same pattern covers the viewport-override parser: a small case table
(`viewport-override.cases.mjs`) is imported by both `viewport-override.test.mjs` (`node --test`) and
`smoke.mjs`'s own fast self-check, which calls the exact same `parseViewportOverride()` function
used to resolve the real `SMOKE_VIEWPORT_WIDTH`/`SMOKE_VIEWPORT_HEIGHT` env values, before the HTTP
server or the browser are created. A normal, successful smoke run logs a
`viewport-override parser self-check passed (...)` line.

## Diagnostic screenshot retention on failure

Every viewport run tracks whether it has already written its `mainPath` screenshot via one of the
normal success/known-failure code paths. If a run instead fails **before** ever reaching an existing
screenshot call site (e.g. the initial `page.goto()` itself times out), the script makes one
best-effort, full-viewport fallback capture at `mainPath` right before closing the page/context — so
a run that fails early still leaves visual evidence instead of none at all. A fallback capture
failure is logged but never added to the run's failure list: it can never hide or replace the
original recorded failure reason. Any stale file already at `mainPath` from a prior invocation is
removed at the start of the run so it can never be mistaken for the current run's outcome.

## Why the editor proof uses a JSExport hook

Avalonia.Browser renders the UI into one Skia `<canvas>`, so Playwright cannot reliably click
individual Avalonia controls through the DOM. The editor-nav smoke therefore enables an E2E-only
interop seam by publishing with `-p:E2E_HOOKS=true` and loading the page with `?e2e=1`.
`main.js` then exposes `globalThis.__febTest = ex.TestHooks` before `runMain`.

The hook is intentionally build-time gated: `FEBuilderGBA.Browser/TestHooks.cs` is wrapped in
`#if E2E_HOOKS`, so production Pages publishes contain no TestHooks IL. ROM bytes are passed as a
base64 string (`LoadRomBase64`) because string marshaling is robust in wasm; direct `byte[]` JS
marshaling is not used.

For CI, use `SMOKE_ROM=synthetic`. The smoke script generates a non-copyrighted 16 MiB byte buffer
with only the FE8U header signature (`BE8E01` at `0xAC`), which is enough for the ROM loader to pick
the FE8U metadata and for the embeddable Move Cost Editor to render.

## Run it locally

```bash
# 1) Publish the wasm AppBundle. Needs the .NET 10 wasm-tools workload.
dotnet workload install wasm-tools
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release -p:EnableBrowserTarget=true

# 2) Run the smoke test against the publish output
cd FEBuilderGBA.Browser/tests/smoke
npm ci
npx playwright install --with-deps chromium
SMOKE_WWWROOT="$(git rev-parse --show-toplevel)/FEBuilderGBA.Browser/bin/Release/net10.0-browser/publish/wwwroot" \
SMOKE_BASE_PATH="/FEBuilderGBA/" \
node smoke.mjs
```

Editor-nav proof with the license-clean synthetic ROM:

```bash
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release \
  -p:EnableBrowserTarget=true -p:E2E_HOOKS=true

cd FEBuilderGBA.Browser/tests/smoke
SMOKE_WWWROOT="$(git rev-parse --show-toplevel)/FEBuilderGBA.Browser/bin/Release/net10.0-browser/publish/wwwroot" \
SMOKE_BASE_PATH="/FEBuilderGBA/" \
SMOKE_ROM=synthetic \
SMOKE_SCREENSHOT="web-editor-nav-smoke.png" \
node smoke.mjs
```

Exit `0` = the app booted; `1` = it did not (see the logged failures + `web-smoke.png`).

## CI

`.github/workflows/pages.yml` runs this in the **build** job (on PRs and on push), so a boot
regression fails the PR and — on `master` — blocks the Pages deploy.

On PRs, the workflow also publishes a separate `E2E_HOOKS` bundle and runs `SMOKE_ROM=synthetic`.
That hooked bundle is only used for the editor-nav smoke artifact; the deployed Pages artifact is
still produced by the clean production publish.

## Env vars

| Var | Default | Meaning |
|---|---|---|
| `SMOKE_WWWROOT` | (required) | Absolute path to the published `.../publish/wwwroot`. |
| `SMOKE_BASE_PATH` | `/FEBuilderGBA/` | Sub-path to serve under (mirror the deployed `<base href>`). |
| `SMOKE_SCREENSHOT` | `web-smoke.png` | Screenshot output path — see "Viewport coverage and screenshots" above for exactly which run owns this vs. a derived sidecar. |
| `SMOKE_TIMEOUT_MS` | `120000` | Boot timeout (cold wasm + ~6.8 MB `config.zip` is slow). |
| `SMOKE_ROM` | (unset) | ROM path for editor-nav proof, or `synthetic` for the license-clean generated FE8U-shaped ROM (the only value that triggers synthetic map-pixel injection). |
| `SMOKE_VIEWPORT_WIDTH` / `SMOKE_VIEWPORT_HEIGHT` | (unset; both-or-neither) | Explicit single-viewport override — both must be **positive integers**. Leave **both** unset for the default 600×500 + 1920×852 matrix described above. |
