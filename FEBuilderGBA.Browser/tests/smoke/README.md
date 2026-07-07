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
   command for "Move Cost Editor", and verifies the current single-view editor title.

It always writes a screenshot for proof / debugging.

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
# 1) Publish the wasm AppBundle. Needs the wasm-tools workload. NOTE: on a .NET 9 SDK, `wasm-tools`
#    already IS the net9 native toolchain; on a NEWER (10.x) SDK band you must ALSO install
#    `wasm-tools-net9`, or the WasmBuildNative native relink fails with NETSDK1147 (CI installs both).
dotnet workload install wasm-tools   # + wasm-tools-net9 if your default SDK is a 10.x band
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release -p:EnableBrowserTarget=true

# 2) Run the smoke test against the publish output
cd FEBuilderGBA.Browser/tests/smoke
npm ci
npx playwright install --with-deps chromium
SMOKE_WWWROOT="$(git rev-parse --show-toplevel)/FEBuilderGBA.Browser/bin/Release/net9.0-browser/publish/wwwroot" \
SMOKE_BASE_PATH="/FEBuilderGBA/" \
node smoke.mjs
```

Editor-nav proof with the license-clean synthetic ROM:

```bash
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release \
  -p:EnableBrowserTarget=true -p:E2E_HOOKS=true

cd FEBuilderGBA.Browser/tests/smoke
SMOKE_WWWROOT="$(git rev-parse --show-toplevel)/FEBuilderGBA.Browser/bin/Release/net9.0-browser/publish/wwwroot" \
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
| `SMOKE_SCREENSHOT` | `web-smoke.png` | Screenshot output path. |
| `SMOKE_TIMEOUT_MS` | `120000` | Boot timeout (cold wasm + ~6.8 MB `config.zip` is slow). |
| `SMOKE_ROM` | (unset) | ROM path for editor-nav proof, or `synthetic` for the license-clean generated FE8U-shaped ROM. |
