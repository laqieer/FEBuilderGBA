# Native Android (Avalonia.Android) — Feasibility Assessment

> **Status: EXPERIMENTAL / PREVIEW — builds an APK; boots on an emulator
> (#1640); not a full ROM-editing app yet.** This document is the
> feasibility/scoping deliverable for epic
> [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070). The structural
> prerequisite — packaging the shared Avalonia UI into an APK — **landed in
> [#1121](https://github.com/laqieer/FEBuilderGBA/issues/1121)**: the Android head
> (`FEBuilderGBA.Android/`) now builds a real APK against the conditionally
> multi-targeted `FEBuilderGBA.Avalonia`. An **emulator BOOT SMOKE TEST landed in
> [#1640](https://github.com/laqieer/FEBuilderGBA/issues/1640)**: CI now installs
> the real signed APK on an API-34 `x86_64` emulator, launches it, and asserts the
> activity reaches the RESUMED state with no fatal exception — so config first-run
> extraction (#1123) and the single-view Avalonia boot (#1122) into the
> editor-launcher shell are now **emulator-validated**, not merely build-only.
> What remains **unvalidated on-device** is the *interactive* ROM-editing path:
> SAF ROM open/save (#1124) is **implemented** (stream-based `Rom` load/save +
> the Avalonia head reads/writes the picked `IStorageFile` via its stream API —
> see §4), but exercising it, and reaching/using an editor, require the system
> file picker and touch UX, which a non-interactive CI smoke test cannot drive.
> The Android head therefore ships as **experimental/preview** — a *runnable, usable*
> Android app is still **a substantial, separate port** (touch UX, on-device
> validation) and is **not** a free byproduct of Avalonia. The head remains
> intentionally **not** part of `FEBuilderGBA.sln` (see
> [§7 Build status in this environment](#7-build-status-in-this-environment)).

This assessment is evidence-backed: every claim cites a `file:line` in the
actual codebase as it stood when the doc was written.

---

## 1. Which layers are Android-capable

FEBuilderGBA is split into layers with very different platform coupling:

| Project | TFM | Android-capable? | Evidence |
|---------|-----|------------------|----------|
| `FEBuilderGBA.Core` | `net9.0` | **Yes** | `FEBuilderGBA.Core/FEBuilderGBA.Core.csproj:3` targets plain `net9.0`; no WinForms / `System.Drawing`. ROM engine, undo, LZ77, Huffman/text codec, etc. are portable. Image/font coupling is behind the `IImageService` / `IFontRasterizer` abstraction seams. |
| `FEBuilderGBA.SkiaSharp` | `net9.0` | **Yes (with a native-version pin — see §3)** | `FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj:13` |
| `FEBuilderGBA.Avalonia` | `net9.0`, opt-in `net9.0-android` (#1121) | **Yes — conditionally multi-targeted** | `FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj`: opts into `net9.0;net9.0-android` when `EnableAndroidTarget=true` (default OFF, so the desktop `.sln`/CI see `net9.0` only). On the android TFM `OutputType=Library`, `app.manifest` / `Avalonia.Desktop` / `Microsoft.CodeAnalysis.CSharp` / `Program.cs` / the `GapSweep` dev-tooling are all excluded. The Android head's `ProjectReference` activates the android TFM via `AdditionalProperties="EnableAndroidTarget=true"`. This resolves the prerequisite #1121. |
| `FEBuilderGBA` (WinForms) | `net9.0-windows` | **No — must be excluded** | WinForms + P/Invoke + `System.Drawing`. Never reference it from an Android head. |

**The Android head must reference only `FEBuilderGBA.Avalonia`** (which transitively
pulls in `Core` + `SkiaSharp`). The WinForms project is explicitly out of scope.

---

## 2. Avalonia-on-Android specifics (lifetime & windowing)

> **Status: implemented (#1122) — single-view BOOT is emulator-smoke-validated (#1640); interactive touch UX / per-editor attached-Window dialogs remain on-device-unvalidated (preview).**

Avalonia 11 ships a real Android target: the `Avalonia.Android` package, an
`[Activity]`-attributed `MainActivity : AvaloniaMainActivity<App>` entry point,
and a **single-view** application lifetime (`ISingleViewApplicationLifetime`) —
one Activity hosting one root view.

The desktop app is built around the **classic desktop** lifetime and a
**multi-window** model — this was the single biggest port item, now resolved by
the `INavigationService` abstraction (#1122):

- **Entry point / lifetime.** `FEBuilderGBA.Avalonia/Program.cs`
  `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` with `[STAThread]`.
  `App.OnFrameworkInitializationCompleted` builds its desktop UI inside
  `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)`
  (`desktop.MainWindow = new Views.MainWindow()`) AND now has an
  `else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)`
  branch that sets `singleView.MainView = new Views.MainView()` (#1122). The
  desktop branch is **unchanged**; on Android the desktop cast is false, so the
  single-view branch presents the editor UI.
- **Multi-window → single-view (`INavigationService`, #1122).**
  `FEBuilderGBA.Avalonia/Services/WindowManager.cs` is now a thin **facade** over
  `INavigationService` (its public API — `Open`/`Navigate`/`OpenModal`/
  `PickFromEditor`/`FindOpen`/`CloseAll`/`MainWindow` — is unchanged, so the ~356
  call sites are untouched). Two implementations:
  - **`DesktopNavigationService`** — the original multi-window body moved
    verbatim (`Open<T>` → `window.Show()`, `OpenModal`/`PickFromEditor` →
    `window.ShowDialog(parent)`, `Dictionary<Type,Window>` cache).
    **Behavior-identical to the pre-#1122 WindowManager** (regression-safe).
  - **`AndroidNavigationService`** — a single-view page/view-stack host with a
    back stack, built on the pure, desktop-unit-tested `NavigationStack<TPage>`.
    `Open<T>` instantiates the view `Window` as a content factory, detaches its
    `Content`, and pushes that control as a page (the `Window` is retained but
    never shown, so callers' `NavigateTo`/view-method calls still work). Modal =
    overlay page; `PickFromEditor` = push the pick view + await via
    `NavigationStack.PushForResult` (`SelectionConfirmed` resolves, back cancels
    to null). The service implements `INavigationHost` (`Back`/`CanGoBack`/
    `CurrentContent`/`StackChanged`), which `Views/MainView` binds to.
  - The service is selected once via `OperatingSystem.IsAndroid()`.
- **Carved to #1873 (honest):** per-editor attached-`Window` flows (file pickers
  via `StorageProvider`, `MessageBoxWindow.Show(this)`, in-page `Close()`),
  page-transition/touch-UX polish, and the rest of the desktop `MainWindow` shell
  controller (recent files, undo UI, menu commands — ROM open/save already landed
  via #1870). A detached
  never-shown `Window` is not a reliable top-level owner, so those dialog flows
  need routing through `TopLevel.GetTopLevel(content)` — a device-validatable
  follow-up. The `MainView` ships an editor-launcher root + the nav host so
  the editor launcher is reachable.

> ✅ **Embeddable-editor path (#1873, slice 1).** Real single-view backends still cannot construct
> legacy editor `Window`s, so converted editors are `TranslatedUserControl` + `IEmbeddableEditor`.
> `AndroidNavigationService` pushes those controls directly as pages (no `Window`, no reflective
> `Opened`/`Closed` lifecycle); desktop wraps the same content in `EditorHostWindow` and preserves
> multi-window behavior. `MoveCostEditorView` was the first converted proof editor; Slice 2 converts
> the simple AI editors (`AIASMCALLTALKView`, `AIASMCoordinateView`, `AIASMRangeView`,
> `AIMapSettingView`, `AIPerformItemView`, `AIPerformStaffView`, `AIStealItemView`, `AITargetView`,
> `AITilesView`, and `AIUnitsView`). Slice 3 adds the first reusable script-driven batch across simple
> Item/Map/Event/Menu/Sound/WorldMap editors (including `ItemStatBonusesViewerView`,
> `MapPointerView`, `EventFunctionPointerView`, `MenuDefinitionView`, `SoundFootStepsViewerView`,
> and `WorldMapPointView`). Slice 4 extends the same script-driven path across additional simple
> Text/Tool/Status/Unit-support/WorldMap editors (including `TextMainView`, `HexEditorView`,
> `ToolFELintView`, `StatusParamView`, `SupportUnitEditorView`, and `WorldMapPathView`). Slice 5
> converts the script-safe launcher/resource/menu/support/class-demo/ending surfaces
> (including `MainSimpleMenuView`, `ResourceView`, `ArenaClassViewerView`,
> `ItemShopViewerView`, `MonsterItemViewerView`, `OPClassDemoViewerView`, `EDView`,
> and `SoundBossBGMViewerView`). Slice 6 exhausts the remaining script-safe pool with
> `SupportTalkView`, `SupportTalkFE6View`, `SupportTalkFE7View`, `UnitFE6View`, `UnitFE7View`,
> and `UnitMainView`. The converter reroutes optional desktop `Window` owner arguments from
> `WindowManager.PickFromEditor(..., this)` to `TopLevel.GetTopLevel(this) as Window` when the caller
> becomes a `UserControl`; picker *targets* remain deferred. Slice 7 starts the complex phase by
> converting the first self-close-only dialog/tool batch, where converted `Close()` calls become
> `RequestClose()` and close through the hosting `EditorHostWindow`.
> Editors with owner-bound file/dialog/picker/closed-event flows remain deferred until the
> single-view dialog-flow slice.

The repository's `FEBuilderGBA.Android/MainActivity.cs` is the Android-equivalent
of `Program.Main` — it subclasses `AvaloniaMainActivity<App>` and reuses the
shared `App`, which now presents `MainView` under the single-view lifetime.

---

## 3. SkiaSharp native-version constraint (CRITICAL)

This is a known landmine in this repo. **A process loads exactly one native
`libSkiaSharp`, so the managed SkiaSharp version must match the native that
Avalonia bundles.** Avalonia 11.2.3 bundles native Skia `2.88.x`; managed
SkiaSharp `3.x` rejects the `2.88` native ("88.1") and crashes inside the
Avalonia process with a `TypeInitializationException` on non-Windows
(Windows can mask it). See `FEBuilderGBA.SkiaSharp/FEBuilderGBA.SkiaSharp.csproj:7-13`
(pinned to `SkiaSharp 2.88.9`) and issues #796 / #798.

**For Android:** add `SkiaSharp.NativeAssets.Android` **pinned to the same
`2.88.9` family** — never float it to `3.x` independently. The skeleton's
`FEBuilderGBA.Android.csproj` already pins it:

```xml
<PackageReference Include="SkiaSharp.NativeAssets.Android" Version="2.88.9" />
```

**Risk (mitigated — #1125):** the parity smoke test now EXISTS and is authored
**cross-platform** so the SAME assertions run everywhere SkiaSharp does. It lives
in the net9.0 cross-platform suite:

- `FEBuilderGBA.Core.Tests/SkiaSharpVersionGuardTests.cs` — three-layer pin guard:
  **(b1) declared** (every `SkiaSharp*` `<PackageReference>` across the repo's
  csprojs pins `2.88.x`, never `3.x`), **(b2) runtime** (the managed SkiaSharp
  assembly actually loaded is `2.88`), **(b3) restored** (`project.assets.json`
  resolved only `2.88.x` SkiaSharp libraries, with no duplicate major family).
- `FEBuilderGBA.Core.Tests/SkiaRenderByteParityTests.cs` — render byte-parity:
  the GBA 4bpp-tile → palette-index decode and the index → RGBA palette
  expansion are asserted **EXACT (zero tolerance)** against hand-derived golden
  bytes; the Tuffy `A` glyph (text + item) is asserted within the **documented
  pixel tolerance** (12 text / 18 item) shared with the desktop regression lock
  (`FEBuilderGBA.Avalonia.Tests/SkiaFontRasterizerTests.cs`) via the linked
  `SkiaFontGoldens` single source of truth.

These are **desktop-validated** today (image pixels exact; font within the
documented tolerance). **#1125 now runs these SAME assertions on-device** — the
advisory emulator CI workflow (`.github/workflows/android-emulator-parity.yml`)
is **GREEN** (run 27528853303: `executed=5 passed=5 failed=0 skipped=2` on a
booted API-34 x86_64 Android emulator):

- **Test head:** `FEBuilderGBA.Android.Tests.csproj` (net9.0-android, NOT in the
  .sln) links (never copies) `SkiaRenderByteParityTests.cs`,
  `SkiaSharpVersionGuardTests.cs`, and `SkiaFontGoldens.cs` via `<Compile Link>`
  — single source of truth with the desktop suite. `TestInstrumentation` is an
  `[Instrumentation]`-attributed **direct reflection-based Android.App.Instrumentation
  runner (NOT XHarness — xUnit discovery needs an on-disk `.dll`; .NET 9 Android
  embeds assemblies as `lib/<abi>/lib_*.dll.so` native libs with empty
  `Assembly.Location`; see csproj + Instrumentation.cs for rationale)**:
  it discovers and invokes `[Fact]`/`[SkippableFact]` methods via reflection,
  catches `Xunit.SkipException` vs failures, and writes its own xUnit-shaped
  `TestResults.xml` + an ADB result bundle. The test head restores from
  **nuget.org only** (`xunit`, `xunit.SkippableFact`,
  `SkiaSharp.NativeAssets.Android 2.88.9`) — no XHarness packages, no dnceng
  feed. `AndroidLinkMode=None`/`PublishTrimmed=false` ensure reflection-discovered
  classes survive the linker.
- **CI workflow:** `.github/workflows/android-emulator-parity.yml` runs on
  `ubuntu-latest` for a single-arch matrix (`x86_64`) on push/PR to master
  (API 34 has no 32-bit `x86` system image; `x86` was dropped at API 31+).
  It boots an API-34 `google_apis` AVD (KVM-accelerated, AVD snapshot cached),
  installs the test APK, runs `adb shell am instrument -w`, pulls
  `TestResults.xml`, and fails the step if any test fails.
- **ABI coverage (honest):** `x86_64` is the only on-device-proven ABI.
  API 34 has no 32-bit `x86` system image (Google dropped `x86` at API 31+).
  `arm64-v8a` and `armeabi-v7a` ship in the **same pinned
  `SkiaSharp.NativeAssets.Android 2.88.9` package** (same package version /
  same upstream Skia build, ABI-specific native binaries — not identical `.so`
  bytes) but are NOT bootable on GitHub-hosted `x86_64` runners — they require
  a self-hosted ARM runner or a paid ARM-emulator service. The `x86` (32-bit)
  ABI has no API-34 system image at all. The workflow provides direct on-device
  proof for `x86_64`; the other three ABIs (`arm64-v8a`, `armeabi-v7a`, `x86`)
  are covered by the same-package argument (same `SkiaSharp.NativeAssets.Android
  2.88.9` pin, same upstream Skia source build).
- **What runs on-device:** the image parity tests (EXACT byte equality — the tile
  decode + PNG round-trip golden); the font parity tests (within shared pixel
  tolerance); and `RuntimeLoadedSkiaSharpAssembly_Is_288` (the runtime-loaded
  managed SkiaSharp version guard). The declared/restored-graph guards skip on
  the device (no source tree / `.sln` present on an Android host), as documented
  in `SkiaSharpVersionGuardTests.cs`.

The advisory workflow is non-blocking by construction: separate workflow file,
job context `android-emulator-parity` (not `build`), and no `continue-on-error`.
A flaky emulator boot or test failure surfaces as a (non-blocking) red check.
Once the runs are consistently green, a maintainer can flip the check to required
via a branch-ruleset change — no code change needed. See §7 for workflow details.

---

## 4. ROM file access on Android (scoped storage / SAF)

Desktop ROM I/O is path-based; a parallel **stream-based** I/O seam (#1124) makes
it work under Android scoped storage:

- Path I/O: `FEBuilderGBA.Core/Rom.cs:615` `File.ReadAllBytes(name)` (in `Load`);
  `Rom.cs:688` `Save(string name, bool silent)` writes by path.
- **Stream I/O (#1124 — DONE):** `Rom.cs` adds `LoadFromStream` / `LoadFromStreamAsync`
  (`Rom.cs:624,635`) and `SaveToStream` / `SaveToStreamAsync` (`Rom.cs:701,719`),
  all converging on the same `LoadBytes` detection seam / `Modified` semantics as
  the path overloads. The Avalonia head consumes a SAF `IStorageFile` directly:
  `MainWindow.axaml.cs` reads via `IStorageFile.OpenReadAsync()` →
  `Rom.LoadFromStreamAsync` (`LoadRomFromStorageFile`) and writes via
  `OpenWriteAsync()` → `Rom.SaveToStreamAsync`, **retaining the picked
  `IStorageFile`** (`_currentRomStorageFile`) so a later Save writes back through
  the same handle — used only when the pick has no local path (Android `content://`);
  desktop picks with a real local path keep the path branch.
- **Editor import/export pickers (#1639):** the path-returning `FileDialogHelper`
  helpers (`OpenRomFile` / `OpenImageFile` / `OpenPaletteFile` / `OpenFile` /
  `OpenPatchFile`) used to collapse every pick to `IStorageFile.TryGetLocalPath()`.
  On Android, SAF returns **`content://` URIs** that frequently have **no stable
  local path**, so `TryGetLocalPath()` returned `null` and a perfectly valid pick
  read as "cancelled". These now transparently bridge a SAF source to a temp
  file (`ResolveReadPathAsync` / `CopyStreamToTempAsync`) so path-based Core APIs
  keep working with **no call-site change**. Save flows route a path-based writer
  through `FileDialogHelper.WriteViaAsync` (temp + write-back via `OpenWriteAsync`,
  truncating first). Flows that genuinely need desktop access — Event Assembler /
  ASM insert / custom build / executable-path config / Tiled (.tmx / .tmj) sibling-file
  export / `.instrument` + `.s` song imports that resolve sibling files — are
  **disabled on Android with an explicit message**, never silently. The stream
  core (`CopyStreamToTempAsync` / `WriteViaStreamsAsync`) is covered by
  `FileDialogHelperSafBridgeTests`.

**What Avalonia's `IStorageProvider` gives you on Android:** the same
`OpenFilePickerAsync` / `SaveFilePickerAsync` API the desktop helper already uses
(`FileDialogHelper.cs`), where the returned `IStorageFile` is consumed via
**streams** (`OpenReadAsync` / `OpenWriteAsync`), keeping the storage handle /
URI rather than reducing it to a path.

**Port status (#1124 — DONE):** the **stream-based** ROM load/save now exists on
`Rom` alongside the path-based overloads (`LoadFromStream*` / `SaveToStream*`,
above), and `MainWindow` retains the `IStorageFile` instead of a path. The
desktop side-writes that used to write beside the exe / ROM are likewise
redirected to **app-private storage** on Android: the log file resolves under
`CoreState.BaseDirectory` — the exe dir on desktop, `Context.FilesDir` on Android
(#1123) — so it is already app-private on Android
(`FEBuilderGBA.Core/Log.cs:122`), and `AutoSaveService.ComputeSidecarPath`
redirects the auto-save sidecar into `{CoreState.BaseDirectory}/autosave/…` when
`OperatingSystem.IsAndroid()`
(`FEBuilderGBA.Avalonia/Services/AutoSaveService.cs:35-52`), instead of beside the
`content://` ROM.

---

## 5. `config/` packaging for an APK

> **Status: implemented (#1123) — config first-run extraction is emulator-smoke-validated (#1640): it runs during the boot-smoke and a broken extract surfaces as a boot crash the test catches.**

The `config/` directory (game data, scripts, names, translations) is **required
at runtime**: `FEBuilderGBA.Core/PathUtil.cs:39` resolves `config/<subpath>`
relative to `CoreState.BaseDirectory`, which `App.axaml.cs` sets to
`AppDomain.CurrentDomain.BaseDirectory` on desktop. On desktop,
`FEBuilderGBA.Avalonia.csproj` copies `config/**` (excluding `patch2`) as loose
files beside the exe.

**Inside an APK there is no "beside the exe" loose-file layout.** The Android
head therefore ships + extracts config:

1. `config/**` (excluding `patch2`) ships as **`AndroidAsset`**
   (`FEBuilderGBA.Android.csproj`:
   `<AndroidAsset Include="..\config\**\*" Exclude="..\config\patch2\**" Link="config\..." />`).
   The glob is `**\*` (not `*.*`) so a future extensionless config file is not
   silently dropped, and the `Link` lands every asset under a clean
   `assets/config/...` tree inside the APK.
2. On first run, the assets are **extracted once** into app-private storage
   (`Context.FilesDir`), **version-stamped** so an app-version bump re-extracts.
   The extraction logic lives in the **pure, desktop-unit-testable**
   `FEBuilderGBA.Core/AndroidConfigExtractorCore.cs` (an `IAssetSource` seam +
   `EnsureExtracted`), with the Android `AssetManager` only as a source adapter
   (`FEBuilderGBA.Android/AndroidAssetSource.cs`). The version stamp doubles as a
   completeness manifest, so the guarantee is: skip when up-to-date; re-extract
   on version bump; **crash-before-stamp recovery** (the stamp is written LAST);
   and **manifest-completeness re-extract** (a matching stamp with any missing
   extracted file forces a clean re-extract). Path-traversal is rejected
   defensively: rooted / `..` entries from the asset source are dropped, a
   tampered/corrupt stamp listing rooted / `..` entries is treated as invalid
   (re-extract, never a false "up to date"), and the `stampFileName` public-API
   argument is validated to be a plain file name.
3. `FEBuilderGBA.Android/MainActivity.OnCreate` runs the extraction **before**
   the Avalonia app boots and points the android-only
   `App.BaseDirectoryOverride` at the extracted root, so the shared
   `App.OnFrameworkInitializationCompleted` sets `CoreState.BaseDirectory` there.
   On desktop the override is always null, so **desktop base-directory resolution
   is unchanged**. Extraction failure logs + rethrows (fail fast — a booted app
   with missing config is worse than a visible crash).

### 5.1 patch2 / FE-Repo on-device delivery decision (#1641)

**Decision: the binary-patch library (`config/patch2`) and the FE-Repo
graphics/music resource submodules are DESKTOP-ONLY on Android for now —
documented limitation, not on-device delivery.** This is a deliberate, honest
scoping decision (acceptance of #1641's "document the limitation" path), not an
oversight.

**Why they are not delivered on Android:**

- **No in-process git.** On desktop both `config/patch2/` and `resources/FE-Repo*`
  are runtime-installed git submodules that the app fetches on demand via the
  in-process `GitUtil`. Android has no in-process git, so the desktop delivery
  path simply does not exist on a device.
- **APK packaging / submodule size.** `config/patch2` and FE-Repo are large
  (the patch library is hundreds of MB; FE-Repo is a large graphics/music
  corpus). Bundling either as an `AndroidAsset` inside the APK would bloat the
  download enormously and is not how the desktop build delivers them either
  (the desktop ships them out-of-band via git, see §2 of `docs/RELEASE.md` and
  the `config/patch2/` empty placeholders).
- **Storage model differs.** The desktop relies on a "loose files beside the
  exe" layout; Android uses app-private `Context.FilesDir` + SAF (`content://`)
  storage with no equivalent loose-file tree (see §4 and §5 above). Even a
  bundled copy would need the same first-run extraction the rest of `config/`
  uses, which does not solve the size problem.

**What the user sees in-app today (the empty-state):**

- **Patch Manager** — the patch list resolves empty on Android (no
  `config/patch2/{version}/` on device). Instead of a silent blank list, the
  manager now shows the canonical Android notice
  (`AndroidResourceNoticeCore.PatchLibraryUnavailableMessage`): patch2 is not
  available on Android yet, ships on desktop builds via git, and on-device
  delivery is planned under #1070.
- **FE-Repo Resource Browser** — when the submodule is absent the browser
  already surfaces an actionable empty-state. On desktop that is the
  `git submodule update --init …` hint (#1380); on Android — where that command
  cannot work — it instead shows
  `AndroidResourceNoticeCore.FERepoUnavailableMessage` (the same desktop-only /
  planned-on-device-delivery message).

The decision is centralised in the GUI-free, desktop-unit-testable
`FEBuilderGBA.Core/AndroidResourceNoticeCore.cs` (an `IsResourceDeliverySupported`
predicate with a test-injectable platform seam + the two canonical message
strings), so the limitation is **verifiable on desktop CI** without an Android
build.

**Intended future delivery mechanism (tracked under epic #1070, NOT promised here):**
an **on-demand HTTP download** of a minimal patch index (and then the
user-selected patches / FE-Repo categories) into app-private `Context.FilesDir`,
**version-pinned** (reusing the version-stamp / completeness-manifest pattern
already used by `AndroidConfigExtractorCore`, §5), with integrity verification
and resumable/partial-download recovery. The `INTERNET` permission is already
declared in `AndroidManifest`, but **no downloader is implemented yet** — this
section deliberately does not over-claim one. See also the release-readiness
known-gaps table in `docs/RELEASE.md` §7.

> **Validation note (#1123).** This note was originally authored without a
> device/emulator (desktop/build-only): the extraction logic is unit-tested on desktop
> (`FEBuilderGBA.Core.Tests/AndroidConfigExtractorCoreTests.cs` — fresh extract,
> version-stamp skip, version-bump re-extract, partial/corrupt re-extract,
> crash-before-stamp recovery, manifest-completeness, nested paths,
> path-traversal rejection, unrelated-dir isolation) and the APK is verified to
> contain `assets/config/...` while excluding `assets/config/patch2/` (unzip +
> grep). The first-run extraction is **now emulator-validated via #1640's
> boot-smoke**: `MainActivity.OnCreate` runs the extraction and rethrows on
> failure (fail-fast), so a bad extract surfaces as a boot crash the boot-smoke
> test catches.

---

## 6. `Process.Start` / external tools

Roughly 40 `Process.Start` call sites across ~17 files assume a desktop OS:

- **Open-file / open-folder / open-URL** (`UseShellExecute`, `explorer.exe /select`)
  → on Android these become an `Intent ACTION_VIEW`, or are simply disabled.
- **Real subprocess launches** — the devkitARM assembler (`arm-none-eabi-as`
  + `objcopy`) in the ASM editor, and the GBA emulator test-play launcher — are
  **desktop-only** and must be disabled on Android (Android apps can't fork
  arbitrary native subprocesses).
- **Feature-parity note:** event-script compilation (EA/ColorzCore) is **not**
  wired into the Avalonia GUI yet (only the CLI `--compile-event` and the
  WinForms `EventAssemblerForm`). If it is ever brought to Avalonia, note that
  it spawns external EA/ColorzCore **processes**, which Android cannot do — it
  would need **in-process** ColorzCore (it is a .NET library) or stay
  desktop-only.

---

## 7. Build status in this environment

This section is deliberately precise about **what built vs what is authored-only**.

### Prerequisites (general)

- The **`net9.0-android` TFM** + the **`android` .NET workload**
  (`dotnet workload install android`).
- The **Android SDK** (platform + build-tools) with **accepted licenses**, and a
  suitable **JDK**. Microsoft's current .NET-for-Android guidance recommends
  **Microsoft OpenJDK (17+/21)**; a modern JDK is required. (Do not assume an
  old JDK works — verify against the actual build.)

### What this environment had

- `dotnet workload list` → **`android 35.0.78/9.0.100` is INSTALLED.**
- Android SDK present at `C:\Program Files (x86)\Android\android-sdk`
  (platforms `android-34` + `android-35`, build-tools `35.0.0`).
- JDK present: Microsoft OpenJDK 11.

### What actually built here

- ✅ **A minimal standalone `net9.0-android` project builds end-to-end** —
  `dotnet build` produced a managed `.dll` **and** packaged + signed `.apk`
  files (`*-Signed.apk`). This proves the workload + SDK + JDK toolchain is
  fully functional in this environment.
- ✅ **The Android head's own code compiles against `FEBuilderGBA.Avalonia`** —
  with the required `-p:EnableAndroidTarget=true` global flag (see the next
  bullet), `dotnet build` restores + compiles `MainActivity` (which references
  the shared `App` + views) and packages the APK. (A *bare*
  `dotnet build FEBuilderGBA.Android.csproj` — without the global property —
  fails restore with `NETSDK1005`, because NuGet restore's static graph ignores
  the per-reference `AdditionalProperties`; the next bullet explains this.)
- ✅ **The full FEBuilderGBA Android APK NOW builds (#1121).** The structural
  blocker below is resolved: `FEBuilderGBA.Avalonia` conditionally multi-targets
  `net9.0;net9.0-android` (opt-in via `EnableAndroidTarget`), and the Android
  head's `ProjectReference` carries `AdditionalProperties="EnableAndroidTarget=true"`.
  Build it with the property as a **global** flag:
  ```bash
  dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj -c Release -p:EnableAndroidTarget=true
  ```
  This produced both `com.laqieer.febuildergba.apk` and the signed
  `com.laqieer.febuildergba-Signed.apk` (~33 MB each) under
  `bin/Release/net9.0-android/`, with `FEBuilderGBA.Avalonia.dll` compiled under
  the android TFM. **Why the global `-p:` is required:** the `AdditionalProperties`
  on the ProjectReference correctly drives the *build* phase, but NuGet *restore*
  uses a separate static graph that does **not** apply per-reference
  `AdditionalProperties` when resolving a referenced project's target frameworks
  — verified via an MSBuild probe (`-getProperty:TargetFrameworks` returns
  `net9.0;net9.0-android` only when `EnableAndroidTarget=true` is set). Without
  the global property, restore writes a net9.0-only `project.assets.json` for the
  shared project and the build fails with `NETSDK1005`. On the android TFM the
  shared project excludes `Program.cs`, `Avalonia.Desktop`, `app.manifest`, the
  `GapSweep` Roslyn dev-tooling (`GapSweep/**` + `App.GapSweep.cs`) and the
  `Microsoft.CodeAnalysis.CSharp` package; the `GapSweep` dispatch in
  `App.OnFrameworkInitializationCompleted` is `#if !ANDROID`-guarded. The only
  android-specific warnings are benign `XA0141` 16-KB-page-size advisories on the
  prebuilt Skia/HarfBuzz `.so` natives.

### Historical blocker (resolved by #1121)

Before #1121, once the build moved to per-RID packaging
(`android-x64` / `android-arm64`) it failed with `NETSDK1047` (and, with an
explicit RID, `NETSDK1112`) because the referenced **`FEBuilderGBA.Avalonia`
project targeted plain `net9.0`** and therefore had no `net9.0-android` /
android-bionic runtime-pack target for the RID resolver to consume. The
conditional multi-target gives the resolver a real `net9.0-android` target.

### Honest conclusion

The Android APK builds against the shared Avalonia UI (#1121) and now **boots on
an emulator** (#1640). The app ships as **experimental/preview**: the boot path
is emulator-validated, but the *interactive* ROM-editing path is not.

- Under the single-view Android lifetime, `App.OnFrameworkInitializationCompleted`
  now sets `singleView.MainView = new Views.MainView()` (#1122), and
  `WindowManager` routes the ~356 editor-launch call sites through
  `AndroidNavigationService` (a single-view page/view-stack nav host) — so the
  booted app presents the editor-launcher shell. **Emulator-validated boot**
  (#1640): the boot-smoke CI job launches the real APK and asserts it reaches the
  RESUMED state with no fatal exception, so the single-view shell genuinely comes
  up on-device (not merely build-only). The nav-stack core is also unit-tested and
  the desktop nav is regression-verified behavior-identical; the on-device
  *interactive* runtime UX (touch, per-editor attached-`Window` dialogs) is carved
  to #1873 (see §2).
- `config/**` ships as an extracted `AndroidAsset` with version-stamped first-run
  extraction to `Context.FilesDir` (#1123). This extraction now runs inside the
  emulator boot-smoke (#1640) — `MainActivity.OnCreate` rethrows on extraction
  failure (fail-fast), so a broken extract would surface as a boot crash the smoke
  test catches.
- ROM open/save has a **SAF stream-based I/O seam (#1124 — DONE):** `Rom` has
  stream load/save overloads and `MainWindow` reads/writes the picked
  `IStorageFile` via `OpenReadAsync` / `OpenWriteAsync` (see §4). Still,
  *exercising* SAF open/save + reaching an editor are **not** driven by the
  boot-smoke CI (a non-interactive job cannot drive the system file picker) and
  remain **on-device-unvalidated** — the reason the head stays experimental/preview.

> The Android head needs `FEBuilderGBA.Avalonia` to be android-aware, which it
> now is *opt-in*; the `EnableAndroidTarget` default is OFF so the desktop build
> is unchanged. The `FEBuilderGBA.Android/` project remains **deliberately
> excluded from `FEBuilderGBA.sln`**: `.github/workflows/check.yml` builds the
> whole solution on a `windows-latest` runner with no android workload, so adding
> the project to the .sln would break the required `build` check for every
> unrelated PR. `crossplatform.yml` builds individual `csproj`s and does not
> touch the Android head. Build it standalone:
> ```bash
> dotnet workload install android
> dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj -c Release -p:EnableAndroidTarget=true
> ```

**Advisory CI workflow (#1126 — DONE):** `.github/workflows/android.yml` now
builds the Android APK on `ubuntu-latest` (`dotnet workload install android` +
`dotnet build … -p:EnableAndroidTarget=true`) and uploads the `*-Signed.apk` as
a `febuildergba-android-apk` workflow artifact on every push/PR to `master`. The
workflow is kept **off** the required `build` check by design: it is a separate
workflow file and the job context is `android-build` (not `build`), so a slow or
flaky Android build can never block a PR merge — the required checks live in
`check.yml` / `crossplatform.yml` and are unaffected. The job deliberately does
**not** set `continue-on-error`, so a genuine failure surfaces as a (still
non-blocking) red check instead of a misleading green one; the
`if-no-files-found: error` flag on the upload step likewise ensures a run that
produced no APK (e.g. a path regression) fails visibly rather than passing green
with an empty artifact.

**Conditional release signing (#1631 — DONE):** the same workflow now produces a
**release-signed** APK **and** AAB *when, and only when,* the maintainer adds a
release keystore to the repository's GitHub Actions secrets. The four secrets are:

| Secret | Meaning |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | base64 of the release keystore (`.keystore` / `.jks`) — Linux: `base64 -w0 release.keystore`; macOS/BSD: `base64 -i release.keystore` (the `-w0` flag is GNU-only) |
| `ANDROID_KEY_ALIAS` | the key alias inside the keystore |
| `ANDROID_KEYSTORE_PASSWORD` | the keystore (store) password |
| `ANDROID_KEY_PASSWORD` | the key password |

Behaviour by secret state:

- **All four secrets set →** the job decodes the keystore to a runner-temp file
  (`chmod 600`, never logged) and runs `dotnet publish …
  -p:AndroidPackageFormats='apk;aab' -p:AndroidKeyStore=true
  -p:AndroidSigningKeyStore=… -p:AndroidSigningKeyAlias=…
  -p:AndroidSigningStorePass=… -p:AndroidSigningKeyPass=…`, passing the passwords
  via `env:` so they never appear in the logged command line. The upload step
  attaches the release-signed `*-Signed.apk` **and** the `*.aab` to the
  `febuildergba-android-apk` artifact. The `.aab` is the format Google Play
  requires; the signed APK is for direct sideload / non-Play distribution.
- **No secrets set (the state on this fork's CI) →** the job falls back to the
  original `dotnet build … -p:EnableAndroidTarget=true` and uploads the
  **debug-keystore** `*-Signed.apk`, exactly as before — the existing
  (non-required) CI is unchanged.
- **A partial set →** the detect step fails fast with a clear
  `::error::Partial Android signing secrets configured …` so a half-configured
  maintainer never gets a confusing publish failure or a silent debug fallback.

No production signing key is committed to the repo — only the maintainer adds it,
as a secret. This mirrors how #1126 left the work: the implementation lands now;
activation requires the maintainer to add the keystore secret. `secrets.*` cannot
appear in a step-level `if:` (GitHub forbids it), so secret presence is classified
once into a step output (`haskeystore.outputs.present`) that the signing / fallback
steps gate on. **Attaching the signed artifact to a GitHub release is the separate
tag-triggered release workflow ([#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629));**
this workflow only produces the signed build.

**On-device byte-parity CI (#1125 — DONE, GREEN):**
`.github/workflows/android-emulator-parity.yml` runs `SkiaRenderByteParityTests`
+ `SkiaSharpVersionGuardTests` on an API-34 Android emulator for `x86_64`
(the only CI-bootable ABI at API 34 — `x86` was dropped at API 31+) on every push/PR to master. The
instrumented test head `FEBuilderGBA.Android.Tests/` uses a **direct reflection runner**
(NOT XHarness — .NET 9 Android embeds assemblies as `.so` files; xUnit requires an on-disk DLL)
and links the same test sources as `FEBuilderGBA.Core.Tests` — no duplication. Build
it standalone:

```bash
dotnet workload install android
dotnet build FEBuilderGBA.Android.Tests/FEBuilderGBA.Android.Tests.csproj -c Release
```

The workflow is advisory / non-blocking (job context `android-emulator-parity`,
not `build`); once consistently green it can be flipped to required via a
branch-ruleset change. `arm64-v8a`, `armeabi-v7a`, and `x86` are covered by
the same-package argument (same `SkiaSharp.NativeAssets.Android 2.88.9` package
version / same upstream Skia build, ABI-specific native binaries — not identical `.so`
bytes) but are not directly emulated on GitHub-hosted runners — `x86` has no
API-34 system image; `arm64-v8a`/`armeabi-v7a` need a self-hosted ARM runner
(see §3). This closes #1125 and completes the #1070 epic checklist item 5.

**On-device BOOT SMOKE TEST (#1640 — DONE):** the parity CI above instruments the
SkiaSharp byte-parity *test head* (`com.laqieer.febuildergba.tests`); it never
boots the **real app**. #1640 closed that gap. A second job in
`android-emulator-parity.yml` — `android-boot-smoke` — builds the **real signed
APK** (`dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj -c Release
-p:EnableAndroidTarget=true`), installs it on an API-34 `x86_64` emulator,
launches it via its **LAUNCHER intent** (`monkey -p com.laqieer.febuildergba -c
android.intent.category.LAUNCHER 1` — robust against the CRC-mangled
.NET-for-Android activity class name), and asserts the activity reaches the
**RESUMED** state (`dumpsys activity activities`) with **no fatal exception** in
logcat. Crash detection is **PID/package-scoped** (it uses the app's resolved PID
via `logcat --pid`, falling back to package-name matching) so an unrelated
emulator/system crash cannot false-fail the smoke test. Because
`MainActivity.OnCreate` does the config first-run extraction (#1123) and rethrows
on failure (fail-fast), a broken extract surfaces as a boot crash the test
catches — so this run **emulator-validates** config extraction + the single-view
Avalonia boot (#1122) into the editor-launcher shell. All logic lives in
`scripts/android-boot-smoke.sh` (single-line invocation — same
`android-emulator-runner` constraint as `android-parity-run.sh`). The job is its
own (own AVD cache key) so a slow/flaky boot cannot affect the parity job, and is
**advisory / non-blocking** by the same construction (job context
`android-boot-smoke`, never the required `build`).

**Scope honesty (#1640):** the boot-smoke job validates *boot*, not the full
ROM-editing flow. The SAF ROM open/save seam (#1124) is **implemented** (stream
I/O — see §4), but *exercising* it, and reaching/using an editor, need the
interactive system file picker + touch UX, which a non-interactive CI job cannot
drive — so they stay **on-device-unvalidated** and the Android head ships as
**experimental/preview** (see the §1 banner and §9). References epic #1070.

---

## 8. Follow-up sub-issues

The real port work surfaced by this exploration is split into concrete issues,
linked under #1070 as its checklist:

1. ~~**Android: multi-target `FEBuilderGBA.Avalonia` (or split a shared UI library)
   so the Android head can be packaged into an APK.**~~ **DONE (#1121)** — the
   shared project conditionally multi-targets `net9.0;net9.0-android`; the head
   builds a real APK (now emulator-boot-validated via #1640; the interactive ROM-editing flow remains preview). *(prerequisite —
   unblocked everything below; see §7.)*
2. ~~**Android: single-activity navigation model for the multi-window editors**
   (`WindowManager` page/view-stack rework + `ISingleViewApplicationLifetime`
   `MainView`).~~ **DONE (#1122)** — `INavigationService` abstraction
   (`DesktopNavigationService` behavior-identical + `AndroidNavigationService`
   single-view nav host over the pure `NavigationStack`), `WindowManager` kept as
   a stable facade (~356 call sites untouched), `App` single-view branch +
   `Views/MainView` shell. Single-view boot emulator-validated via #1640 (no device for interactive UX); per-editor
   attached-`Window` dialog flows + touch-UX polish carved to #1873. *(was the
   largest item; see §2.)*
3. ~~**Android: bundle `config/` as `AndroidAsset` + extract to `FilesDir` at
   first run** (version-stamped); decide `patch2` delivery.~~ **DONE (#1123)** —
   config extraction emulator-validated via #1640's boot-smoke. The `patch2` /
   FE-Repo on-device delivery **decision** is now made (#1641): **documented
   desktop-only limitation** with an Android-aware in-app empty-state; an
   on-demand download into `FilesDir` is the intended future mechanism under
   #1070, not yet implemented. *(see §5 and §5.1.)*
4. ~~**Android: ROM open/save via `IStorageProvider`/SAF streams** (+ redirect
   `Log` / `AutoSaveService` to app-private storage).~~ **DONE (#1124)** — `Rom`
   adds `LoadFromStream*` / `SaveToStream*`, `MainWindow` reads/writes the picked
   `IStorageFile` via its stream API and retains the handle, and the `Log` file +
   `AutoSaveService` sidecar redirect to app-private storage on Android (the log
   already resolves under `CoreState.BaseDirectory` = `Context.FilesDir`). The
   *interactive* SAF path is still on-device-unvalidated (no system-file-picker
   automation in CI). *(see §4.)*
5. ~~**Android: `SkiaSharp.NativeAssets.Android` version pinning + render
   byte-parity smoke test** on the Android native.~~ **DONE (#1125)** — the
   on-device parity run is wired in `android-emulator-parity.yml` and runs
   GREEN on `x86_64` (the runner-bootable ABI at API 34; `x86` dropped at API
   31+). The reflection-runner instrumented head (`FEBuilderGBA.Android.Tests/`)
   links the same `SkiaRenderByteParityTests` + `SkiaSharpVersionGuardTests`
   as the desktop suite (direct reflection runner, NOT XHarness — .NET 9
   Android embeds assemblies as `.so` files; xUnit's `Guard.FileExists` needs
   an on-disk DLL). `arm64-v8a`, `armeabi-v7a`, and `x86` ship in the same
   `SkiaSharp.NativeAssets.Android 2.88.9` package (same package version /
   same upstream Skia build, ABI-specific native binaries) but are not
   bootable on GitHub-hosted runners (see §3). *(see §3, §7.)*
6. ~~**Android: CI job + signed APK packaging** (separate android-workload job,
   not the desktop `.sln` build).~~ **DONE (#1126)** — `.github/workflows/android.yml`
   builds the APK on `ubuntu-latest` and uploads the debug-keystore `*-Signed.apk`
   as a workflow artifact; kept off the required `build` check (separate workflow +
   `android-build` context, non-required). ~~Signed-release-key packaging~~ is now
   **DONE (#1631)** — the same workflow conditionally produces a release-signed APK
   **+ AAB** when the maintainer adds the `ANDROID_KEYSTORE_BASE64` /
   `ANDROID_KEY_ALIAS` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_PASSWORD`
   secrets, and falls back to the debug-keystore `*-Signed.apk` when they are
   absent (see §7); attaching the signed artifact to a GitHub release is the
   tag-triggered release workflow ([#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629)).
   The emulator byte-parity run (#1125) is **DONE** (see item 5). *(see §7.)*

---

## 9. Recommendation & phased path

Treat native Android as a **separate port after the desktop Avalonia version
stabilizes** (it is still reaching WinForms parity). The Skia / Core foundation
is ready; the **windowing model, storage, and touch UX are the real work.** Do
not promise Android from "Avalonia supports Android" alone.

**Phased path:**

1. ~~**Phase A — make it packageable.**~~ **DONE (#1121)** — the shared UI is
   conditionally multi-targeted and the Android head builds an APK.
2. **Phase B — make it run.** Single-view lifetime + a minimal navigation host
   (#2 — **DONE (#1122)**, boot emulator-validated via #1640), config extraction
   (#3 — **DONE (#1123)**, emulator-boot-validated via #1640), and SAF ROM I/O
   (#4 — **DONE (#1124)**, stream-based load/save + side-write redirect; the
   *interactive* picker path is on-device-unvalidated) — enough to open a ROM and
   show one editor on a device.
3. **Phase C — make it usable.** Touch UX adaptation (larger hit targets,
   phone/tablet layouts, touch-friendly numeric entry replacing the ~2,300
   `NumericUpDown` spinner usages and the desktop menu bar), then the Skia parity smoke
   test (#5) and a CI/APK job (#6).

Phase A landed in #1121: the `FEBuilderGBA.Android/` head builds a real APK
against the shared, conditionally-multi-targeted Avalonia UI. Phase B is partly
in: the navigation rework (#1122), config extraction (#1123), and SAF stream I/O
seam (#1124) landed, and #1640 added an **emulator boot smoke test** that proves
the app actually launches into the single-view editor-launcher shell. It is now a
**runnable (experimental/preview)** app — it boots and presents the launcher —
but the *interactive* ROM-editing flow (SAF open + reaching an editor on a touch
device) is still on-device-unvalidated, so it is not yet a full ROM-editing app.

---

## See also

- iOS sibling head: [docs/IOS.md](IOS.md)
- [docs/CROSS_PLATFORM.md → Running on Android](CROSS_PLATFORM.md#running-on-android)
- Epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070) (this exploration)
- Emulation/Winlator route: [#1069](https://github.com/laqieer/FEBuilderGBA/issues/1069)
- Source discussion: [#1062](https://github.com/laqieer/FEBuilderGBA/discussions/1062)
