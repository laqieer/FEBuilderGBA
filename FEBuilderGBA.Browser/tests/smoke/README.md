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

It always writes a screenshot for proof / debugging.

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

Exit `0` = the app booted; `1` = it did not (see the logged failures + `web-smoke.png`).

## CI

`.github/workflows/pages.yml` runs this in the **build** job (on PRs and on push), so a boot
regression fails the PR and — on `master` — blocks the Pages deploy.

## Env vars

| Var | Default | Meaning |
|---|---|---|
| `SMOKE_WWWROOT` | (required) | Absolute path to the published `.../publish/wwwroot`. |
| `SMOKE_BASE_PATH` | `/FEBuilderGBA/` | Sub-path to serve under (mirror the deployed `<base href>`). |
| `SMOKE_SCREENSHOT` | `web-smoke.png` | Screenshot output path. |
| `SMOKE_TIMEOUT_MS` | `120000` | Boot timeout (cold wasm + ~6.8 MB `config.zip` is slow). |
