# WebAssembly (Avalonia.Browser) — Status & Design

> **Status: PREVIEW — builds a wasm AppBundle deployed to GitHub Pages; the app loads/renders the
> Avalonia shell in a browser. Full in-browser ROM-editing is maturing.** The 4th Avalonia head
> after desktop, Android ([docs/ANDROID.md](ANDROID.md)) and iOS ([docs/IOS.md](IOS.md)), added in
> [#1864](https://github.com/laqieer/FEBuilderGBA/issues/1864).

FEBuilderGBA runs in a browser via **WebAssembly**: `FEBuilderGBA.Browser/` builds the shared
`FEBuilderGBA.Avalonia` GUI (→ `FEBuilderGBA.Core` + `FEBuilderGBA.SkiaSharp`) into a `net10.0-browser`
AppBundle, deployed to **https://laqieer.github.io/FEBuilderGBA/**. It reuses the *same*
platform-agnostic seams the mobile heads introduced.

## 1. Which layers are browser-capable

| Layer | Browser-capable | Notes |
|---|---|---|
| `FEBuilderGBA.Core` | ✅ | Pure `net10.0`. |
| `FEBuilderGBA.SkiaSharp` | ✅ | Managed SkiaSharp 2.88.9; the head adds the matching `SkiaSharp.NativeAssets.WebAssembly` 2.88.9 native. |
| `FEBuilderGBA.Avalonia` | ✅ | Multi-targets `net10.0;net10.0-browser` when `EnableBrowserTarget=true` (opt-in, default OFF). Reuses the exact compile surface the mobile heads build. |
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

**Embeddable editors were the #1873 path during the rollout.** Single-view heads cannot construct `Window`-derived
editors (`new Window()` still throws before content exists), so converted editors inherit
`TranslatedUserControl` and implement `IEmbeddableEditor` with an `EditorDescriptor` (title, preferred
size, min size, modal capability) plus `CloseRequested`. Desktop keeps true multi-window behavior by
wrapping those controls in the generic `EditorHostWindow`; browser/Android/iOS push the same
`UserControl` directly as a page with no `Window` construction. Legacy `Window` editors used the
old desktop path while the rollout continued. `MoveCostEditorView` was the first converted proof editor;
the first rollout batch also converted the simple AI editors (`AIASMCALLTALKView`,
`AIASMCoordinateView`, `AIASMRangeView`, `AIMapSettingView`, `AIPerformItemView`,
`AIPerformStaffView`, `AIStealItemView`, `AITargetView`, `AITilesView`, and `AIUnitsView`). Slice 3
added the first reusable script-driven rollout batch across simple Item/Map/Event/Menu/Sound/WorldMap
editors (for example `ItemStatBonusesViewerView`, `MapPointerView`, `EventFunctionPointerView`,
`MenuDefinitionView`, `SoundFootStepsViewerView`, and `WorldMapPointView`). Slice 4 extended the same
script-driven path across additional simple Text/Tool/Status/Unit-support/WorldMap editors (for
example `TextMainView`, `HexEditorView`, `ToolFELintView`, `StatusParamView`,
`SupportUnitEditorView`, and `WorldMapPathView`). Slice 5 converted the script-safe launcher/resource/menu/support/class-demo/ending surfaces
(including `MainSimpleMenuView`, `ResourceView`, `ArenaClassViewerView`, `ItemShopViewerView`,
`MonsterItemViewerView`, `OPClassDemoViewerView`, `EDView`, and `SoundBossBGMViewerView`). Slice 6
exhausted the remaining script-safe pool with `SupportTalkView`, `SupportTalkFE6View`,
`SupportTalkFE7View`, `UnitFE6View`, `UnitFE7View`, and `UnitMainView`. The converter rerouted
optional desktop `Window` owner arguments from `WindowManager.PickFromEditor(..., this)` to
`TopLevel.GetTopLevel(this) as Window` when the caller becomes a `UserControl`; picker targets were
deferred during that phase. Slice 7 started the complex phase by converting the first self-close-only dialog/tool batch,
where converted `Close()` calls become `RequestClose()` and close through the hosting
`EditorHostWindow`. Subsequent slices completed #1873; #1891 now exposes the full shared catalog in
the single-view launcher once a ROM is loaded.

**Full editor catalog in the single-view launcher (#1891).** With #1873 complete (every editor is now
an embeddable `IEmbeddableEditor` `UserControl` — 0 `Window`-derived editors except the 6 legacy
`EventTemplate` editors), the `MainView` launcher renders the **full** desktop editor set instead of a
9-editor stub. The list is the shared **`Services/EditorCatalog`** — a single source of truth that
mirrors the desktop `MainWindow` body (28 categories, ~223 editors) including its version/patch open
dispatch, and gates entries to the loaded ROM version exactly as the desktop does
(`EditorCatalog.AppliesTo`). The launcher renders it as collapsible, filterable category expanders.
`EditorCatalog` is kept in lock-step with the desktop body by `EditorCatalogParityTests`, and every
catalog editor is asserted to be `IEmbeddableEditor` (so a `Window`-derived editor can never slip in
and throw on the single-view host). The 6 `EventTemplate` editors stay desktop-only (they derive from
`TranslatedWindow`).

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

### CJK glyph fallback (#1890)

Inter is Latin-only, and because wasm has **no system fonts** there is nothing to fall back to, so
Japanese game text and the `ja`/`zh` UI translations rendered as tofu boxes on the web app only
(desktop/Android/iOS fall back to OS CJK fonts). The head therefore **embeds a compact CJK fallback**:
`Assets/Fonts/NotoSansCJKsc-Subset.otf` — a character SUBSET of Noto Sans CJK SC (OFL, ~2.8 MB, family
name `Noto Sans CJK SC`) covering every Shift-JIS + GB2312 codepoint, kana, CJK punctuation, the
fullwidth/halfwidth forms, and the CJK characters used by `config/translate` + `config/data`
(regenerate with `scripts/build-cjk-font-subset.py`). It is added as an `<AvaloniaResource>` (browser
head only) and registered as a per-codepoint `FontFallback` in `Program.CreateBrowserFontManagerOptions`
— the same mechanism the desktop uses (`FEBuilderGBA.Avalonia/Program.CreateFontManagerOptions`), but
with an embedded font because the sandbox has none. `.WithInterFont()` stays the default (Latin); the
fallback fills only the glyphs Inter lacks. The subset's coverage, its family name (which must match
the `#Noto Sans CJK SC` avares suffix), and the `Program.cs`/csproj wiring are guarded by
`BrowserCjkFontTests`. The OFL license ships at `wwwroot/fonts/OFL.txt` and is credited in
`THIRD-PARTY-NOTICES.md`.

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
# AppBundle: FEBuilderGBA.Browser/bin/Release/net10.0-browser/publish/wwwroot
```

`-p:EnableBrowserTarget=true` is REQUIRED as a **global** property — NuGet restore's static graph
ignores the per-reference `AdditionalProperties`, else `NETSDK1005` (same mechanism as android/iOS).

- **`.github/workflows/pages.yml`** — installs `wasm-tools`, publishes the head, rewrites the
  published `index.html` `<base href>` to `/FEBuilderGBA/` (the Pages project path), adds
  `.nojekyll` (so `_framework/` isn't Jekyll-stripped), verifies
  the `config.zip`/`_framework` are present, then `upload-pages-artifact` + `deploy-pages`. The **build**
  job runs on PRs (validates the wasm build); the **deploy** job runs only on `master`/dispatch.
- GitHub Pages source is set to **GitHub Actions** (`build_type: workflow`).
- **Boot smoke test** (`FEBuilderGBA.Browser/tests/smoke/`) — the build job runs a headless-Chromium
  (Playwright) test that serves the AppBundle under `/FEBuilderGBA/` and asserts the app actually
  *renders* (Avalonia canvas mounts; no `avalonia.js` response `>= 400`). It runs on PRs (a boot
  regression fails the PR) and gates the deploy on `master`. Added after #1867, where every asset
  returned `200` but the app never booted — an HTTP-200 check could not catch that.
  - **Map Editor layout probe (#1998, follow-up).** By default — when both
    `SMOKE_VIEWPORT_WIDTH`/`SMOKE_VIEWPORT_HEIGHT` are unset — `smoke.mjs` runs an in-process,
    sequential dual-viewport matrix in one browser process (fresh isolated context/page per
    viewport): `600x500` ("compact", below the internal `700`px classification threshold) then
    `1920x852` ("acceptance"). Failures from either viewport are aggregated; the process exits
    nonzero if either fails. This is the mode both `pages.yml` invocations use today (neither sets
    the viewport vars), so CI gets both layouts covered with no workflow changes. Setting **both**
    vars runs exactly that single viewport instead (setting only one is rejected, exit code `2`).
    With `SMOKE_ROM` set, each viewport run opens the Map Editor through the real launcher
    (`OpenEditor('MapEditor')`), logs `devicePixelRatio` / `visualViewport.scale`, and calls the
    E2E-only `MapEditorLayoutMetrics(injectSyntheticMapPixels)` hook to assert the split-scroller
    layout: the map canvas stays ≥240px tall at any viewport, and its upper info/toolbar/palette
    scroller overflows at compact heights (asserted universally — any realistic content overflows a
    ~189px viewport). The desktop "upper controls fit without scrolling" expectation and the
    both-axis (width AND height) inner-canvas-overflow expectation are hard-asserted **only** when
    `SMOKE_ROM=synthetic`, whose fixture map is deliberately oversized (~2200×1200) to guarantee
    both; for a **real** ROM the same metrics are logged instead of hard-asserted, since a real
    chapter's on-screen map size and populated-palette content height are ROM-data-dependent (a
    real FE8U chapter has been observed needing more upper-region height than the 1920×852
    viewport provides, correctly falling back to scrolling rather than indicating a layout bug).
    `injectSyntheticMapPixels` is opt-in and only honored when the currently-loaded ROM was itself
    loaded via `LoadRomBase64(..., isSynthetic: true)`; requesting synthetic injection against a
    real ROM load is rejected with a JSON `error` field rather than silently overwriting the real
    ROM's authentic rendered map/palette pixels. The Map Editor's own screenshot is taken **last**,
    full-viewport with no content clip (device-pixel-ratio 1), after all other checks pass; the
    Move Cost Editor keeps its own separate before/after screenshot pair (recomputed per-viewport
    content clip) under sidecar filenames derived from `SMOKE_SCREENSHOT`, and only the acceptance
    (or single explicit) run's `SMOKE_SCREENSHOT`/`.before.png` pair matches what
    `.github/workflows/pages.yml` uploads.

### Why the web app first hung on the splash (#1867)

The initial deploy hung on the loading splash and 404'd on `avalonia.js`. It looked like a module-path
bug, but the true root cause was an **incomplete wasm build**: the head published *without* a native
build — no `WasmBuildNative`, and CI lacked the target-framework-specific WebAssembly toolchain
required by the SDK at the time. That single gap caused **two** failures at once:

1. **SkiaSharp/HarfBuzz natives were never linked.** `@(NativeFileReference)` (`libSkiaSharp.a` +
   `libHarfBuzzSharp.a`) is only emcc-relinked into `dotnet.native.wasm` when `WasmBuildNative=true`
   (or AOT). Without it the SDK merely *warned*, and the first Skia call — `SKImageInfo`'s static
   ctor — threw a `TypeInitialization` exception, so the app rendered nothing but the splash.
2. **Avalonia's JS modules were misplaced.** Their `RelativePath = $(WasmRuntimeAssetsLocation)/<file>`
   expanded with an empty `WasmRuntimeAssetsLocation`, so `avalonia.js` / `storage.js` landed at the
   wwwroot **root** instead of `_framework/`. Avalonia's default resolver (`./avalonia.js`, resolved by
   `JSHost.ImportAsync` **relative to `_framework/`** where `dotnet.js` lives) then 404'd them.

**Fix:** do a proper native build — `WasmBuildNative=true` in the head csproj plus the matching
WebAssembly workload. The original .NET 9 fix used the matching `wasm-tools-net9`;
the current .NET 10 workflow uses `wasm-tools` in `pages.yml`. That links the
natives (Skia works) **and** produces the canonical layout with
`avalonia.js` / `storage.js` in `_framework/`, so Avalonia's **default** resolver works with **no
override**. The headless boot smoke test then verifies the canvas actually renders — the class of
failure a "returns HTTP 200" check can't catch, which is exactly how #1867 shipped.

## 7. Known preview limitations / follow-ups

- **Full single-view editor catalog (#1873/#1891 — complete).** The shared
  `EditorCatalog` is available in the launcher, and every editor is embeddable
  except the six `EventTemplate` editors, which remain desktop-only by design.
  ROM **open/save** works (#1870); remaining preview gaps concern runtime/browser
  parity rather than editor conversion.
- **On-device parity maturing** — threading-dependent caches + some file-flows aren't browser-ported
  (single-threaded on Pages). Milestone = builds + deploys + loads/renders the shell (config-loaded).
- **`config/patch2` / FE-Repo not bundled** — same as the mobile heads.
- **Large first-load** — no trimming + the ~6.8 MB config tree (downloaded + unzipped before boot);
  a loading splash is shown. Optimized/trimmed AOT is a follow-up.
- **SDK/emscripten float** — no `global.json`; the native relink uses the CI runner's `10.0.x` SDK
  Emscripten toolchain. Pin via `global.json` if reproducibility becomes an issue.

## See also

- [docs/ANDROID.md](ANDROID.md) / [docs/IOS.md](IOS.md) — the sibling heads (shared seams).
- [`FEBuilderGBA.Browser/README.md`](../FEBuilderGBA.Browser/README.md) — the head skeleton.
- [docs/CROSS_PLATFORM.md](CROSS_PLATFORM.md) — platform support.
