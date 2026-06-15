# Native Android (Avalonia.Android) — Feasibility Assessment

> **Status: builds an APK; not yet device-validated.** This document is the
> feasibility/scoping deliverable for epic
> [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070). The structural
> prerequisite — packaging the shared Avalonia UI into an APK — **landed in
> [#1121](https://github.com/laqieer/FEBuilderGBA/issues/1121)**: the Android head
> (`FEBuilderGBA.Android/`) now builds a real APK against the conditionally
> multi-targeted `FEBuilderGBA.Avalonia`. A *runnable, usable* Android app is
> still **a substantial, separate port** (single-view navigation, config asset
> extraction, SAF ROM I/O, touch UX) and is **not** a free byproduct of Avalonia.
> The head remains intentionally **not** part of `FEBuilderGBA.sln` (see
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

> **Status: implemented (#1122) — build-only validated, not yet device-validated.**

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
- **Carved to #1070 (honest):** per-editor attached-`Window` flows (file pickers
  via `StorageProvider`, `MessageBoxWindow.Show(this)`, in-page `Close()`),
  page-transition/touch-UX polish, and the full desktop `MainWindow` shell
  controller (ROM open/save actions, recent files, undo UI). A detached
  never-shown `Window` is not a reliable top-level owner, so those dialog flows
  need routing through `TopLevel.GetTopLevel(content)` — a device-validatable
  follow-up. The `MainView` ships an editor-launcher root + the nav host so
  editors are reachable.

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

Desktop ROM I/O is path-based and will not work under Android scoped storage:

- `FEBuilderGBA.Core/Rom.cs:606` / `Rom.cs:619` `File.ReadAllBytes(name)`;
  `Rom.cs:654` `Save(string name, bool silent)` writes by path.
- `FEBuilderGBA.Avalonia/Dialogs/FileDialogHelper.cs:52,65` collapse every
  picker result to `IStorageFile.TryGetLocalPath()`. On Android, SAF returns
  **`content://` URIs** that frequently have **no stable local path**, so
  `TryGetLocalPath()` returns `null` and a perfectly valid pick reads as
  "cancelled".

**What Avalonia's `IStorageProvider` gives you on Android:** the same
`OpenFilePickerAsync` / `SaveFilePickerAsync` API the desktop helper already uses
(`FileDialogHelper.cs:45,58`), but the returned `IStorageFile` must be consumed
via **streams** (`OpenReadAsync` / `OpenWriteAsync`), keeping the storage handle
/ URI rather than reducing it to a path.

**Required port:** add **stream-based** ROM load/save to `Rom` (alongside the
path-based overloads) and return/retain the `IStorageFile` instead of a path.
Also redirect the desktop side-writes — `Log` (`FEBuilderGBA.Core/Log.cs:53`),
`Config.Save`, and `AutoSaveService` (`FEBuilderGBA.Avalonia/Services/AutoSaveService.cs:33-114`),
which write beside the exe / ROM — to **app-private storage** (`Context.FilesDir`).

---

## 5. `config/` packaging for an APK

> **Status: implemented (#1123) — build-only validated, not yet device-validated.**

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

> **patch2 delivery decision: `config/patch2` is NOT bundled for Android
> (deferred).** It is a runtime-installed git submodule (hundreds of MB) that the
> desktop app installs on demand via `GitUtil`; bundling it would bloat the APK
> enormously and Android has no in-process git. Deciding on-device patch delivery
> (e.g. an on-demand download into `FilesDir`) is tracked under the epic #1070,
> not this issue. The issue explicitly accepts "deferred / not bundled."

> **Validation note (#1123).** No device/emulator was available, so this was
> **desktop/build-only validated**: the extraction logic is unit-tested on desktop
> (`FEBuilderGBA.Core.Tests/AndroidConfigExtractorCoreTests.cs` — fresh extract,
> version-stamp skip, version-bump re-extract, partial/corrupt re-extract,
> crash-before-stamp recovery, manifest-completeness, nested paths,
> path-traversal rejection, unrelated-dir isolation) and the APK is verified to
> contain `assets/config/...` while excluding `assets/config/patch2/` (unzip +
> grep). On-device first-run extraction runtime is tracked under #1070.

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

The Android APK builds against the shared Avalonia UI (#1121). What remains is
**runtime**, not structural — the APK is **not yet device/emulator-validated**:

- Under the single-view Android lifetime, `App.OnFrameworkInitializationCompleted`
  now sets `singleView.MainView = new Views.MainView()` (#1122), and
  `WindowManager` routes the ~356 editor-launch call sites through
  `AndroidNavigationService` (a single-view page/view-stack nav host) — so the
  booted app presents the editor-launcher shell. **Build-only validated** (no
  device): the nav-stack core is unit-tested and the desktop nav is
  regression-verified behavior-identical; the on-device runtime UX (touch,
  per-editor attached-`Window` dialogs) is carved to #1070 (see §2).
- `config/**` ships as an extracted `AndroidAsset` with version-stamped first-run
  extraction to `Context.FilesDir` (#1123, build-only validated — see §5). ROM
  open/save (the remaining storage item) is still path-based.
- ROM open/save still goes through path-based I/O; SAF stream I/O is #1124 (see §4).

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

---

## 8. Follow-up sub-issues

The real port work surfaced by this exploration is split into concrete issues,
linked under #1070 as its checklist:

1. ~~**Android: multi-target `FEBuilderGBA.Avalonia` (or split a shared UI library)
   so the Android head can be packaged into an APK.**~~ **DONE (#1121)** — the
   shared project conditionally multi-targets `net9.0;net9.0-android`; the head
   builds a real APK (build-only — not yet device-validated). *(prerequisite —
   unblocked everything below; see §7.)*
2. ~~**Android: single-activity navigation model for the multi-window editors**
   (`WindowManager` page/view-stack rework + `ISingleViewApplicationLifetime`
   `MainView`).~~ **DONE (#1122)** — `INavigationService` abstraction
   (`DesktopNavigationService` behavior-identical + `AndroidNavigationService`
   single-view nav host over the pure `NavigationStack`), `WindowManager` kept as
   a stable facade (~356 call sites untouched), `App` single-view branch +
   `Views/MainView` shell. Build-only validated (no device); per-editor
   attached-`Window` dialog flows + touch-UX polish carved to #1070. *(was the
   largest item; see §2.)*
3. ~~**Android: bundle `config/` as `AndroidAsset` + extract to `FilesDir` at
   first run** (version-stamped); decide `patch2` delivery.~~ **DONE (#1123)** —
   build-only validated (no device); `config/patch2` deferred / not bundled.
   *(see §5.)*
4. **Android: ROM open/save via `IStorageProvider`/SAF streams** (+ redirect
   `Log` / `Config.Save` / `AutoSaveService` to app-private storage). *(see §4.)*
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
   `android-build` context, non-required). Signed-release-key packaging and the
   emulator byte-parity run (#1125) are deferred. *(see §7.)*

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
   (#2), config extraction (#3 — **DONE (#1123)**, build-only), and SAF ROM I/O
   (#4) — enough to open a ROM and show one editor on a device.
3. **Phase C — make it usable.** Touch UX adaptation (larger hit targets,
   phone/tablet layouts, touch-friendly numeric entry replacing the ~2,300
   `NumericUpDown` spinner usages and the desktop menu bar), then the Skia parity smoke
   test (#5) and a CI/APK job (#6).

Phase A landed in #1121: the `FEBuilderGBA.Android/` head now builds a real APK
against the shared, conditionally-multi-targeted Avalonia UI. It is **not yet a
runnable app** — booting it presents no editor under the single-view lifetime
until the navigation rework (Phase B / #1122) lands.

---

## See also

- [docs/CROSS_PLATFORM.md → Running on Android](CROSS_PLATFORM.md#running-on-android)
- Epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070) (this exploration)
- Emulation/Winlator route: [#1069](https://github.com/laqieer/FEBuilderGBA/issues/1069)
- Source discussion: [#1062](https://github.com/laqieer/FEBuilderGBA/discussions/1062)
