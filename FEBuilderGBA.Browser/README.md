# FEBuilderGBA.Browser (Avalonia.Browser / WebAssembly head)

> **Builds a WebAssembly app deployed to GitHub Pages; PREVIEW — full in-browser editing is
> maturing.** The 4th Avalonia head after desktop, [Android](../FEBuilderGBA.Android/README.md),
> and [iOS](../FEBuilderGBA.iOS/README.md), added in
> [#1864](https://github.com/laqieer/FEBuilderGBA/issues/1864). Full assessment:
> **[docs/WEBASSEMBLY.md](../docs/WEBASSEMBLY.md)**.

This is an Avalonia.Browser *head* that reuses the shared `FEBuilderGBA.Avalonia` UI (→
`FEBuilderGBA.Core` + `FEBuilderGBA.SkiaSharp`) and runs it in a browser via WebAssembly, with no
install. The live site is deployed to GitHub Pages by
[`.github/workflows/pages.yml`](../.github/workflows/pages.yml).

## Why it is NOT in `FEBuilderGBA.sln`

The required desktop `build` check builds the whole solution on a `windows-latest` runner with **no
`wasm-tools` workload**. Adding this `net10.0-browser` head to `FEBuilderGBA.sln` would break the
required check for every unrelated PR. It is therefore excluded and built standalone.

## Build & run locally

```bash
dotnet workload install wasm-tools          # one-time
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release \
  -p:EnableBrowserTarget=true -p:WasmEnableThreads=false -p:PublishTrimmed=false -p:CompressionEnabled=false
# serve the AppBundle:
dotnet serve -d FEBuilderGBA.Browser/bin/Release/net10.0-browser/publish/wwwroot   # or any static server
```

`-p:EnableBrowserTarget=true` is **required** as a **global** property (NuGet restore's static graph
ignores the `ProjectReference` `AdditionalProperties`, else `NETSDK1005` — same as the android/iOS
heads).

## How it works

- **UI**: the shared Avalonia single-view shell (`Views/MainView`), same as Android/iOS.
- **Rendering**: `Avalonia.Browser` + `SkiaSharp.NativeAssets.WebAssembly` 2.88.9 +
  `HarfBuzzSharp.NativeAssets.WebAssembly` 7.3.0.3 (both emcc-relinked into `dotnet.wasm`) +
  `Avalonia.Fonts.Inter` (wasm has no system fonts).
- **config**: `config/**` (excl. `patch2`) is zipped into `wwwroot/config.zip` at build; `Program.Main`
  fetches it over HTTP and extracts it into a writable MEMFS dir via the pure
  `FEBuilderGBA.Core/ZipAssetSource` + `AndroidConfigExtractorCore`, then sets
  `App.BaseDirectoryOverride`. Config load is non-fatal (the shell renders with defaults if it fails).
- **Threads OFF** (`WasmEnableThreads=false`): GitHub Pages sends no COOP/COEP → no `SharedArrayBuffer`.
- **No trimming** (`PublishTrimmed=false`): the reflection-heavy Core would break under aggressive trim.
- **No compression** (`CompressionEnabled=false`): Pages does no Brotli/gzip content-negotiation.
- **GPLv3**: `LICENSE` + `THIRD-PARTY-NOTICES.md` ship as static web assets.

## Preview limitations

- On-device parity is maturing: threading-dependent caches and some file-flows are not yet
  browser-ported (single-threaded on Pages). The milestone is **builds + deploys + loads/renders**.
- `config/patch2` and the FE-Repo resources are **not bundled** (large git-delivered payloads), same
  as the mobile heads.
- Large first-load (no trimming + the ~6.8 MB config tree, downloaded + unzipped before boot).
