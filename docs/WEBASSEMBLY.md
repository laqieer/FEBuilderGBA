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

### ROM open/save + editor navigation (#1870)

The single-view `MainView` shell provides its own **Open ROM** / **Save ROM** top-bar buttons and a
status strip, since the desktop `MainWindow` File menu does not exist here. Both go through
`Services/RomFileService` (`OpenRomAsync(Visual)` / `SaveRomAsync(Visual)`), which resolves the
`StorageProvider` from `TopLevel.GetTopLevel(owner)` — the shell owner is a `Visual`, **not** a
`Window` — via the new `Visual`-owner overloads of `Dialogs/FileDialogHelper` (the `Window` overloads
still delegate to them, so desktop is unchanged). Open reads through the **stream** API
(`IStorageFile.OpenReadAsync` → `ROM.LoadFromStreamAsync`) because a browser pick is a read-only
`Blob` with no local path; Save uses a **Save-As** picker + a fresh writable stream (File System
Access `showSaveFilePicker` on Chromium, a download elsewhere) — never a retained read-handle, which
is not writable off-Chromium. The shared post-load init (`RomFileService.InitializeLoadedRom`) is the
SAME method the desktop `MainWindow.FinishLoadedRom` calls, so desktop and web wire CoreState
identically and never drift.

**Editors do not open in the browser yet (tracked by #1873).** Every editor view is a `Window`
subclass (`UnitEditorView : TranslatedWindow : Window`) and the single-view nav service opens one by
`new T()`. On the browser there is no windowing platform, so `Window..ctor()` throws
`System.NotSupportedException: "Browser doesn't support windowing platform"` **before** any content can
be extracted — the launcher buttons therefore can't host an editor. This is **not** patchable with a
custom windowing platform: Avalonia's `IWindowImpl`/`ITopLevelImpl` are `[NotClientImplementable]`, so a
user-supplied window impl fails at compile time (`CS0535`). The **Android and iOS** single-view heads
share the exact same block (same `new T()` path, no windowing platform) — it is only "green" in unit
tests because `Avalonia.Headless` supplies an internal `HeadlessWindowImpl`. Until the editor-hosting
rewrite (#1873) makes editor content instantiable without a `Window`, the launcher disables its editor
buttons until a ROM is loaded and surfaces a friendly "Editors aren't available in this browser build
yet" status instead of failing silently.

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

### Why the web app first hung on the splash (#1867)

The initial deploy hung on the loading splash and 404'd on `avalonia.js`. It looked like a module-path
bug, but the true root cause was an **incomplete wasm build**: the head published *without* a native
build — no `WasmBuildNative`, and CI installed only the generic `wasm-tools` workload, not
`wasm-tools-net9`. That single gap caused **two** failures at once:

1. **SkiaSharp/HarfBuzz natives were never linked.** `@(NativeFileReference)` (`libSkiaSharp.a` +
   `libHarfBuzzSharp.a`) is only emcc-relinked into `dotnet.native.wasm` when `WasmBuildNative=true`
   (or AOT). Without it the SDK merely *warned*, and the first Skia call — `SKImageInfo`'s static
   ctor — threw a `TypeInitialization` exception, so the app rendered nothing but the splash.
2. **Avalonia's JS modules were misplaced.** Their `RelativePath = $(WasmRuntimeAssetsLocation)/<file>`
   expanded with an empty `WasmRuntimeAssetsLocation`, so `avalonia.js` / `storage.js` landed at the
   wwwroot **root** instead of `_framework/`. Avalonia's default resolver (`./avalonia.js`, resolved by
   `JSHost.ImportAsync` **relative to `_framework/`** where `dotnet.js` lives) then 404'd them.

**Fix:** do a proper native build — `WasmBuildNative=true` in the head csproj + `wasm-tools-net9` in
`pages.yml`. That links the natives (Skia works) **and** produces the canonical layout with
`avalonia.js` / `storage.js` in `_framework/`, so Avalonia's **default** resolver works with **no
override**. The headless boot smoke test then verifies the canvas actually renders — the class of
failure a "returns HTTP 200" check can't catch, which is exactly how #1867 shipped.

## 7. Known preview limitations / follow-ups

- **Editors don't open yet (#1873)** — editor views are `Window` subclasses, and `new Window()` throws
  `NotSupportedException` on the browser (no windowing platform); Android/iOS share the same block. ROM
  **open/save** works (#1870); the single-view launcher disables editor buttons until a ROM loads and
  shows a friendly status when an editor can't open. Hosting editor content without a `Window` is the
  remaining architectural work.
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
