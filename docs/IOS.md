# Native iOS (Avalonia.iOS) — Status & Design

> **Status: PREVIEW — builds an iOS `.app` / unsigned `.ipa` on macOS CI; the on-device
> runtime is NOT yet validated.** The iOS counterpart of the native-Android epic
> ([#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070) / [docs/ANDROID.md](ANDROID.md)),
> added in [#1859](https://github.com/laqieer/FEBuilderGBA/issues/1859).

FEBuilderGBA now ships a native **Avalonia.iOS** head (`FEBuilderGBA.iOS/`) that reuses the
shared `FEBuilderGBA.Avalonia` GUI (→ `FEBuilderGBA.Core` + `FEBuilderGBA.SkiaSharp`). It is a
close mirror of the Android head, because the runtime seams that made Android possible are
**platform-agnostic** and reused verbatim on iOS.

## 1. Which layers are iOS-capable

| Layer | iOS-capable | Notes |
|---|---|---|
| `FEBuilderGBA.Core` | ✅ | Pure `net10.0`, no platform deps. |
| `FEBuilderGBA.SkiaSharp` | ✅ | Managed SkiaSharp 2.88.9; the iOS head adds the matching `SkiaSharp.NativeAssets.iOS` 2.88.9 native. |
| `FEBuilderGBA.Avalonia` | ✅ | Multi-targets `net10.0;net10.0-ios` when `EnableIosTarget=true` (opt-in, default OFF). Reuses the exact compile surface the android TFM already builds. |
| `FEBuilderGBA` (WinForms) | ❌ | Never referenced — not iOS-capable. |

## 2. Lifetime & windowing — reused from the Android port

Avalonia's iOS target uses the **single-view** application lifetime
(`ISingleViewApplicationLifetime`) — one root view — exactly like Android. The shared
`App.OnFrameworkInitializationCompleted` single-view branch (added for Android in #1122) sets
`singleView.MainView = new Views.MainView()`, and `MainView` installs the single-view
`INavigationService` (page/view-stack host with a back stack) itself. **No `App` change was
needed for iOS** — it presents the same editor-launcher shell as Android.

`FEBuilderGBA.iOS/AppDelegate.cs` (`: AvaloniaAppDelegate<App>`) is the iOS equivalent of the
Android `MainActivity` / the desktop `Program.Main`.

## 3. SkiaSharp native-version constraint (CRITICAL)

The managed SkiaSharp MUST match the native `libSkiaSharp` Avalonia bundles (2.88.x). A 3.x
managed package rejects the 2.88 native and crashes inside the Avalonia process. The iOS head
therefore pins `SkiaSharp.NativeAssets.iOS` **2.88.9**, byte-compatible with
`FEBuilderGBA.SkiaSharp.csproj` (same constraint as android — see [docs/ANDROID.md §3](ANDROID.md#3-skiasharp-native-version-constraint-critical)).

## 4. `config/` packaging for an `.app`

Inside an `.app` bundle there is no "beside the exe" loose-file layout, and the bundle is
**read-only**, so `config/` must be extracted into a writable location on first run — the same
pattern as Android's `Context.FilesDir` extraction:

1. `config/**` (EXCLUDING `patch2`) ships as **`<BundleResource>`** with
   `LogicalName=config/%(RecursiveDir)%(Filename)%(Extension)` so the `config/<subpath>` tree
   is preserved inside the bundle (iOS flattens BundleResource by default). It lands at
   `<App>.app/config/…` — a real, readable directory at runtime.
2. On first run, `AppDelegate.FinishedLaunching` extracts the bundled `config/` into an
   app-private writable dir (`Library/febuildergba`), **version-stamped** by `CFBundleVersion`
   so a version bump re-extracts. The extraction logic is the **pure, desktop-unit-tested**
   `FEBuilderGBA.Core/AndroidConfigExtractorCore` (reused verbatim), fed by the new pure
   `FEBuilderGBA.Core/DirectoryAssetSource` (a `System.IO`-only `IAssetSource` over the bundle
   directory — the iOS analog of Android's `AssetManager`-backed `AndroidAssetSource`).
3. It then points `App.BaseDirectoryOverride` at the extracted root **before** the Avalonia
   app boots, so `CoreState.BaseDirectory` resolves `config/<sub>` there. On desktop the
   override is always null, so **desktop base-directory resolution is unchanged**.

CI verifies the built `.app` actually contains `config/config.xml` (a `LogicalName` flatten
would silently make extraction find nothing).

`config/patch2` and the FE-Repo submodules are **not bundled** (large git-delivered payloads) —
the same accepted limitation as Android ([#1641](https://github.com/laqieer/FEBuilderGBA/issues/1641)).

## 5. Runtime viability (AOT + trimming)

Unlike Android (Mono, JIT-capable), iOS Release is full **AOT + trimmed**. The Core/ROM
pipeline is reflection-heavy, so the head sets `<UseInterpreter>true</UseInterpreter>` (Mono
interpreter fallback for dynamic/reflection code) and `<MtouchLink>SdkOnly</MtouchLink>` (link
only the SDK, leaving Core/UI assemblies untrimmed) to maximize the chance the shipped `.ipa`
actually runs. This is the single most important preview caveat: the app may **build** cleanly
yet still need on-device validation.

## 6. Build & CI

Not in `FEBuilderGBA.sln` (same reason as the Android head: `check.yml` builds the whole
solution on windows-latest without the mobile workload; iOS also needs macOS + Xcode). Build
standalone on macOS:

```bash
dotnet workload install ios
dotnet build FEBuilderGBA.iOS/FEBuilderGBA.iOS.csproj -c Release -p:EnableIosTarget=true
```

`-p:EnableIosTarget=true` is REQUIRED as a **global** property — NuGet restore's static graph
ignores the per-reference `AdditionalProperties`, so without it restore writes a net10.0-only
assets file for `FEBuilderGBA.Avalonia` and the build fails with `NETSDK1005` (the same
mechanism as android's `EnableAndroidTarget`).

- **`.github/workflows/ios.yml`** — advisory (context `ios-build`, non-required), `macos-latest`,
  builds an **unsigned `.ipa`** and verifies the bundled `config/` tree. Cannot block PR merges.
- **`.github/workflows/release.yml`** — a **soft** `ios` job (`continue-on-error`, NOT in the
  mandatory verify-assets list) attaches `FEBuilderGBA-ios-unsigned-ipa.zip` to a `ver_*` release
  when the build succeeds, and degrades to "release without iOS" when it doesn't.

### Unsigned `.ipa` (this fork's CI)

With no Apple Developer secret, CI produces an **unsigned** `.ipa` (`-p:EnableCodeSigning=false`,
packaged as `Payload/<App>.app`). An unsigned `.ipa` is **NOT directly installable** on a stock
device — it is intended for **downstream re-signing / sideloading** (AltStore, Sideloadly, Apple
Configurator) or a later maintainer signing step. This mirrors Android's debug-keystore fallback.

### Signed `.ipa` (when the maintainer adds secrets)

Set ALL of these repository secrets together (or none) to switch `ios.yml` to a release-signed build:

| Secret | Meaning |
| --- | --- |
| `APPLE_CERTIFICATE_BASE64` | base64 of the signing certificate `.p12` |
| `APPLE_CERTIFICATE_PASSWORD` | the `.p12` password |
| `APPLE_PROVISIONING_PROFILE_BASE64` | base64 of the `.mobileprovision` profile |
| `APPLE_CODESIGN_IDENTITY` | the codesign identity (e.g. `Apple Distribution: …`) |

A partial set fails the workflow fast. App Store / TestFlight distribution needs a paid Apple
Developer account.

## 7. Known preview limitations / follow-ups

- **On-device runtime unvalidated** — touch UX, file pickers, editors (§5). The ~24 per-editor
  `OperatingSystem.IsAndroid()` file-flow guards in the shared UI are build-safe on iOS (they
  compile and return false) but are **not yet extended to iOS**, so those editors take the
  untested desktop file path. A follow-up should generalize them to a mobile predicate.
- **`config/patch2` / FE-Repo not bundled** — desktop-only, same as Android (#1641).
- **Signed distribution** — needs a paid Apple Developer account + the `APPLE_*` secrets (§6).

## See also

- [docs/ANDROID.md](ANDROID.md) — the Android head (this doc's sibling; the seams are shared).
- [`FEBuilderGBA.iOS/README.md`](../FEBuilderGBA.iOS/README.md) — the head skeleton.
- [docs/DEPLOYMENT.md](DEPLOYMENT.md) / [docs/RELEASE.md](RELEASE.md) — the release flow.
