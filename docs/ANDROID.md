# Native Android (Avalonia.Android) — Feasibility Assessment

> **Status: exploration only.** This document is the feasibility/scoping
> deliverable for epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070).
> A native Android build is **technically feasible but a substantial, separate
> port** — it is **not** a free byproduct of moving to Avalonia. **Nothing here
> is shipped, and there is no commitment to ship.** The repository carries an
> authored Avalonia.Android **head skeleton** (`FEBuilderGBA.Android/`) that is
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
| `FEBuilderGBA.Avalonia` | `net9.0` (desktop) | **Source-portable, but desktop-targeted today** | `FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj:3-8`: `OutputType=WinExe`, plain `net9.0`, `Avalonia.Desktop`, Windows `app.manifest`. The C# compiles for `net9.0-android` (proven in §7), but the project is not multi-targeted, so it cannot be *packaged* into an APK as-is. |
| `FEBuilderGBA` (WinForms) | `net9.0-windows` | **No — must be excluded** | WinForms + P/Invoke + `System.Drawing`. Never reference it from an Android head. |

**The Android head must reference only `FEBuilderGBA.Avalonia`** (which transitively
pulls in `Core` + `SkiaSharp`). The WinForms project is explicitly out of scope.

---

## 2. Avalonia-on-Android specifics (lifetime & windowing)

Avalonia 11 ships a real Android target: the `Avalonia.Android` package, an
`[Activity]`-attributed `MainActivity : AvaloniaMainActivity<App>` entry point,
and a **single-view** application lifetime (`ISingleViewApplicationLifetime`) —
one Activity hosting one root view.

The current desktop app is built around the **classic desktop** lifetime and a
**multi-window** model, which is the single biggest port item:

- **Entry point / lifetime.** `FEBuilderGBA.Avalonia/Program.cs:33`
  `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` with `[STAThread]`.
  `App.OnFrameworkInitializationCompleted` (`App.axaml.cs:188-203`) only builds
  its UI inside `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)`
  and sets `desktop.MainWindow = new Views.MainWindow()`. **Under the single-view
  Android lifetime, that branch is never entered** — so the skeleton boots the
  Avalonia runtime but presents no editor. Android needs an
  `else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)`
  branch that sets `singleView.MainView`.
- **Multi-window → single-view.** `FEBuilderGBA.Avalonia/Services/WindowManager.cs`
  is built on top-level `Window`s: `Open<T>` calls `window.Show()` (line 32),
  `OpenModal<T>` / `PickFromEditor<T>` call `window.ShowDialog(parent)` (lines
  50 / 94), and it caches a `Dictionary<Type, Window>` (line 17). Roughly
  356 of 358 views are top-level `Window` / `TranslatedWindow`. Android has **no
  desktop multi-window model**, so the navigation layer must be reworked to a
  page/view-stack (e.g. a single host view with a back stack, or Avalonia
  navigation controls). This is the **largest** follow-up
  ([single-activity navigation model](#8-follow-up-sub-issues)).

The repository's `FEBuilderGBA.Android/MainActivity.cs` is the Android-equivalent
of `Program.Main` — it subclasses `AvaloniaMainActivity<App>` and reuses the
shared `App`. Its XML doc records the single-view limitation above honestly.

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

**Risk:** font/image byte-parity is currently golden-tested only on the desktop
native. The Android native (`2.88.9` for the four Android ABIs) must be re-verified
for byte-identical rendering output — a parity smoke test is a tracked follow-up.

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

The `config/` directory (game data, scripts, names, translations) is **required
at runtime**: `FEBuilderGBA.Core/PathUtil.cs:39` resolves `config/<subpath>`
relative to `CoreState.BaseDirectory`, which `App.axaml.cs:135-136` sets to
`AppDomain.CurrentDomain.BaseDirectory`. On desktop, `FEBuilderGBA.Avalonia.csproj:25`
copies `config/**` (excluding `patch2`) as loose files beside the exe.

**Inside an APK there is no "beside the exe" loose-file layout.** The port must:

1. Ship `config/**` (excluding `patch2`) as **`AndroidAsset`** (the skeleton
   csproj shows the intended `<AndroidAsset Include="..\config\**\*.*" .../>`
   line, commented out — see below).
2. On first run, **extract** assets once into app-private storage
   (`Context.FilesDir`), **version-stamped** so an app update re-extracts.
3. Point `CoreState.BaseDirectory` at that extracted root.

> The skeleton leaves the `AndroidAsset` line **commented out on purpose**:
> without the first-run extraction code, bundling assets alone would give the
> app no readable config. Bundle + extract land together in the follow-up.

**`config/patch2` is deferred / not bundled** for the Android exploration. It is
a separate git submodule installed at runtime on desktop; deciding how (or
whether) to deliver patch data to Android is part of the config-bundling
follow-up, not this skeleton.

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
- ✅ **The shared Avalonia UI compiles for `net9.0-android`** — the first build
  pass of `FEBuilderGBA.Android.csproj` compiled the **entire**
  `FEBuilderGBA.Avalonia` view tree against the Android TFM (the only compile
  error was a single optional `.WithInterFont()` call in the skeleton's
  `MainActivity`, since removed).
- ❌ **The full FEBuilderGBA Android APK does NOT build** — once the build moves
  to per-RID packaging (`android-x64` / `android-arm64`), it fails with
  `NETSDK1047` / `NETSDK1112` because the referenced **`FEBuilderGBA.Avalonia`
  project targets plain `net9.0`** and therefore has no `net9.0-android` /
  android-bionic runtime-pack target for the RID resolver to consume.

### Honest conclusion

The Android toolchain works here, and the shared UI **source** is
Android-compilable. The blocker is purely structural: **a desktop (`net9.0`)
project cannot be packaged into an APK by reference.** Producing a runnable APK
requires making `FEBuilderGBA.Avalonia` **multi-target** (`net9.0;net9.0-android`)
or **splitting the shared UI into a class library** the Android head and the
desktop head both reference. That is the recommended first implementation step
(see follow-ups), and it is intentionally **out of scope** for this skeleton
(which must not perturb the existing desktop build / CI).

> Because the Android head needs `FEBuilderGBA.Avalonia` to be android-aware, the
> skeleton `FEBuilderGBA.Android/` project is **deliberately excluded from
> `FEBuilderGBA.sln`**. `.github/workflows/check.yml` builds the whole solution
> on a `windows-latest` runner with no android workload; adding the project to
> the .sln would break the required `build` check for every unrelated PR.
> `crossplatform.yml` builds individual `csproj`s and does not touch the Android
> head. Build the skeleton standalone:
> ```bash
> dotnet workload install android
> dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj
> ```

---

## 8. Follow-up sub-issues

The real port work surfaced by this exploration is split into concrete issues,
linked under #1070 as its checklist:

1. **Android: multi-target `FEBuilderGBA.Avalonia` (or split a shared UI library)
   so the Android head can be packaged into an APK.** *(prerequisite — unblocks
   everything below; see §7.)*
2. **Android: single-activity navigation model for the multi-window editors**
   (`WindowManager` page/view-stack rework + `ISingleViewApplicationLifetime`
   `MainView`). *(largest item; see §2.)*
3. **Android: bundle `config/` as `AndroidAsset` + extract to `FilesDir` at
   first run** (version-stamped); decide `patch2` delivery. *(see §5.)*
4. **Android: ROM open/save via `IStorageProvider`/SAF streams** (+ redirect
   `Log` / `Config.Save` / `AutoSaveService` to app-private storage). *(see §4.)*
5. **Android: `SkiaSharp.NativeAssets.Android` version pinning + render
   byte-parity smoke test** on the Android native. *(see §3.)*
6. **Android: CI job + signed APK packaging** (separate android-workload job,
   not the desktop `.sln` build). *(see §7.)*

---

## 9. Recommendation & phased path

Treat native Android as a **separate port after the desktop Avalonia version
stabilizes** (it is still reaching WinForms parity). The Skia / Core foundation
is ready; the **windowing model, storage, and touch UX are the real work.** Do
not promise Android from "Avalonia supports Android" alone.

**Phased path:**

1. **Phase A — make it packageable.** Multi-target / split the shared UI (#1
   above) so an APK can actually be produced.
2. **Phase B — make it run.** Single-view lifetime + a minimal navigation host
   (#2), config extraction (#3), and SAF ROM I/O (#4) — enough to open a ROM and
   show one editor on a device.
3. **Phase C — make it usable.** Touch UX adaptation (larger hit targets,
   phone/tablet layouts, touch-friendly numeric entry replacing the ~1,939
   `NumericUpDown` spinners and the desktop menu bar), then the Skia parity smoke
   test (#5) and a CI/APK job (#6).

Until Phase A lands, the `FEBuilderGBA.Android/` skeleton stands as an authored,
compile-verified starting point — not a runnable app.

---

## See also

- [docs/CROSS_PLATFORM.md → Running on Android](CROSS_PLATFORM.md#running-on-android)
- Epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070) (this exploration)
- Emulation/Winlator route: [#1069](https://github.com/laqieer/FEBuilderGBA/issues/1069)
- Source discussion: [#1062](https://github.com/laqieer/FEBuilderGBA/discussions/1062)
