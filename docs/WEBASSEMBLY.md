# WebAssembly (Avalonia.Browser) — Status & Design

> **Status: PREVIEW — builds a wasm AppBundle deployed to GitHub Pages; the app loads/renders the
> Avalonia shell in a browser. Full in-browser ROM-editing is maturing.** The 4th Avalonia head
> after desktop, Android ([docs/ANDROID.md](ANDROID.md)) and iOS ([docs/IOS.md](IOS.md)), added in
> [#1864](https://github.com/laqieer/FEBuilderGBA/issues/1864).

FEBuilderGBA runs in a browser via **WebAssembly**: `FEBuilderGBA.Browser/` builds the shared
`FEBuilderGBA.Avalonia` GUI (→ `FEBuilderGBA.Core` + `FEBuilderGBA.SkiaSharp`) into a `net9.0-browser`
AppBundle, deployed to **https://laqieer.github.io/FEBuilderGBA/**. It reuses the *same*
platform-agnostic seams the mobile heads introduced.

## 1. Which layers are browser-capable

| Layer | Browser-capable | Notes |
|---|---|---|
| `FEBuilderGBA.Core` | ✅ | Pure `net9.0`. |
| `FEBuilderGBA.SkiaSharp` | ✅ | Managed SkiaSharp 2.88.9; the head adds the matching `SkiaSharp.NativeAssets.WebAssembly` 2.88.9 native. |
| `FEBuilderGBA.Avalonia` | ✅ | Multi-targets `net9.0;net9.0-browser` when `EnableBrowserTarget=true` (opt-in, default OFF). Reuses the exact compile surface the mobile heads build. |
| `FEBuilderGBA` (WinForms) | ❌ | Never referenced — not browser-capable. |

## 2. Lifetime & windowing — reused from the mobile ports

Avalonia's browser target uses the **single-view** application lifetime
(`ISingleViewApplicationLifetime` → `Views/MainView`) — exactly like Android/iOS. The shared
`App.OnFrameworkInitializationCompleted` single-view branch sets the `MainView` shell, and `MainView`
installs the single-view nav service itself. **No browser-specific UI code was needed.**
`FEBuilderGBA.Browser/Program.cs` (`BuildAvaloniaApp().StartBrowserAppAsync("out")`) is the browser
entry point.

## 3. Rendering (SkiaSharp + HarfBuzz native relink)

Avalonia.Browser 11.2.3 depends on managed `SkiaSharp 2.88.9` **and** `HarfBuzzSharp 7.3.0.3`, and the
head adds their static native archives (`libSkiaSharp.a` + `libHarfBuzzSharp.a`) as
`@(NativeFileReference)` via the `*.NativeAssets.WebAssembly` packages. **Those natives are only
emcc-relinked into `dotnet.native.wasm` when `WasmBuildNative=true`** (set in the head csproj) —
otherwise the .NET wasm SDK merely *warns* ("native references won't be linked in") and ships a runtime
with **no Skia**, so the first Skia call (`SKImageInfo`'s static ctor) throws a `TypeInitialization`
exception and the app renders nothing but the splash (#1867). The head therefore sets
`WasmBuildNative=true` and pins **both** native packages — `SkiaSharp.NativeAssets.WebAssembly` 2.88.9 +
`HarfBuzzSharp.NativeAssets.WebAssembly` 7.3.0.3 (HarfBuzz shapes text) — plus `Avalonia.Fonts.Inter`
(wasm has **no system fonts**, so an embedded font is required). We do **not** enable AOT
(`RunAOTCompilation`); `WasmBuildNative` relinks the natives without AOT-compiling managed code.

## 4. `config/` in the browser (no filesystem)

There is no real filesystem or app bundle in the browser sandbox, so `config/` is delivered as a
fetched zip and extracted into the writable in-memory filesystem (MEMFS):

1. A `ZipConfigForBrowser` MSBuild target zips `config/**` (EXCLUDING `patch2`) into
   `wwwroot/config.zip`, **rooted so entries retain the `config/` prefix** (`config/data/…`) — which
   the extractor + `ZipAssetSource` require (the app resolves `{BaseDirectory}/config/config.xml`).
2. `Program.Main` fetches `config.zip` over HTTP (base = `document.baseURI`, passed from `main.js`,
   so it resolves under the Pages project path `/FEBuilderGBA/`) → wraps it in a `ZipArchive` →
   `AndroidConfigExtractorCore.EnsureExtracted(new ZipAssetSource(archive), "/appdata", …)` into MEMFS
   → sets `App.BaseDirectoryOverride` **before** `StartBrowserAppAsync`. The pure
   `FEBuilderGBA.Core/ZipAssetSource` is the browser analog of Android's `AssetManager` adapter and
   iOS's `DirectoryAssetSource`; it is desktop-unit-tested.
3. Config load is **non-fatal**: on failure it logs to the browser console and the shell still
   renders with defaults (`Config.LoadOrCreate` creates defaults; translations are `File.Exists`-guarded).

`config/patch2` and the FE-Repo submodules are **not bundled** (large git-delivered payloads) — same
accepted limitation as the mobile heads.

## 5. Single-threaded (GitHub Pages constraint)

GitHub Pages sends **no COOP/COEP headers**, so `SharedArrayBuffer` is unavailable and wasm threads
must be **off** (`WasmEnableThreads=false`; this also selects Avalonia's single-threaded `st/*.a`
native variants). The shell boot path has no `new Thread(` (verified), so a single-threaded runtime
renders the launcher fine. Threading-dependent Core caches (e.g. the background ASM-map cache) are a
follow-up. Publishing also sets `PublishTrimmed=false` (reflection-heavy Core would break under
aggressive trim) and `CompressionEnabled=false` (Pages does no Brotli/gzip content-negotiation).

## 6. Build & deploy

Not in `FEBuilderGBA.sln` (the required desktop `build` check builds the whole solution on
windows-latest without `wasm-tools`). Build standalone:

```bash
dotnet workload install wasm-tools
dotnet publish FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj -c Release \
  -p:EnableBrowserTarget=true -p:WasmEnableThreads=false -p:PublishTrimmed=false -p:CompressionEnabled=false
# AppBundle: FEBuilderGBA.Browser/bin/Release/net9.0-browser/publish/wwwroot
```

`-p:EnableBrowserTarget=true` is REQUIRED as a **global** property — NuGet restore's static graph
ignores the per-reference `AdditionalProperties`, else `NETSDK1005` (same mechanism as android/iOS).

- **`.github/workflows/pages.yml`** — installs `wasm-tools`, publishes the head, rewrites the
  published `index.html` `<base href>` to `/FEBuilderGBA/` (the Pages project path; `WasmRelativePathBase`
  doesn't exist in the .NET 9 SDK), adds `.nojekyll` (so `_framework/` isn't Jekyll-stripped), verifies
  the `config.zip`/`_framework` are present, then `upload-pages-artifact` + `deploy-pages`. The **build**
  job runs on PRs (validates the wasm build); the **deploy** job runs only on `master`/dispatch.
- GitHub Pages source is set to **GitHub Actions** (`build_type: workflow`).
- **Boot smoke test** (`FEBuilderGBA.Browser/tests/smoke/`) — the build job runs a headless-Chromium
  (Playwright) test that serves the AppBundle under `/FEBuilderGBA/` and asserts the app actually
  *renders* (Avalonia canvas mounts; no `avalonia.js` response `>= 400`). It runs on PRs (a boot
  regression fails the PR) and gates the deploy on `master`. Added after #1867, where every asset
  returned `200` but the app never booted — an HTTP-200 check could not catch that.

### `avalonia.js` module resolution (#1867)

Avalonia loads its browser JS modules via `JSHost.ImportAsync(name, resolver(file))`; the default
resolver is `file => "./" + file`, which the .NET wasm runtime resolves **relative to `_framework/`**
(where `dotnet.js` lives) → `_framework/avalonia.js`. But Avalonia's static web assets publish with
`RelativePath = $(WasmRuntimeAssetsLocation)/<file>`, and `WasmRuntimeAssetsLocation` is **empty** in
this AppBundle, so `avalonia.js` / `storage.js` land at the wwwroot **root**, not `_framework/`. The
mismatch 404s `avalonia.js` and the app hangs on the splash forever. Fix: `FEBuilderGBA.Browser/Program.cs`
overrides `BrowserPlatformOptions.FrameworkAssetPathResolver` to `file => "../" + file` — climb one
segment out of `_framework/` to app-root, where the modules actually are; stays relative, so correct at
both `/FEBuilderGBA/` and a local `/`. This also repairs `storage.js` (file dialogs), which 404s the
same way. It is valid **only** for the two `import()`-loaded modules; `sw.js` (opt-in, off by default)
registers against the document base, not `_framework/`. **Canonical follow-up:** set
`<WasmRuntimeAssetsLocation>_framework</WasmRuntimeAssetsLocation>` so the modules land next to
`dotnet.js` and Avalonia's default resolver works unmodified — deferred because it couldn't be
validated without a local wasm workload.

## 7. Known preview limitations / follow-ups

- **On-device parity maturing** — threading-dependent caches + some file-flows aren't browser-ported
  (single-threaded on Pages). Milestone = builds + deploys + loads/renders the shell (config-loaded).
- **`config/patch2` / FE-Repo not bundled** — same as the mobile heads.
- **Large first-load** — no trimming + the ~6.8 MB config tree (downloaded + unzipped before boot);
  a loading splash is shown. Optimized/trimmed AOT is a follow-up.
- **SDK/emscripten float** — no `global.json`; the native relink uses the CI runner's `9.0.x` SDK
  emscripten (stable within .NET 9). Pin via `global.json` if reproducibility becomes an issue.

## See also

- [docs/ANDROID.md](ANDROID.md) / [docs/IOS.md](IOS.md) — the sibling heads (shared seams).
- [`FEBuilderGBA.Browser/README.md`](../FEBuilderGBA.Browser/README.md) — the head skeleton.
- [docs/CROSS_PLATFORM.md](CROSS_PLATFORM.md) — platform support.
