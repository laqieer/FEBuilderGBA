# FEBuilderGBA.iOS (Avalonia.iOS head)

> **Builds an iOS `.app` / unsigned `.ipa`; PREVIEW — not device-validated.** The iOS
> counterpart of the [`FEBuilderGBA.Android/`](../FEBuilderGBA.Android/README.md) head
> (epic [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070)), added in
> [#1859](https://github.com/laqieer/FEBuilderGBA/issues/1859). Read the full assessment:
> **[docs/IOS.md](../docs/IOS.md)**.

This is a minimal Avalonia.iOS *head* that reuses the shared `FEBuilderGBA.Avalonia` UI
(which transitively pulls in `FEBuilderGBA.Core` + `FEBuilderGBA.SkiaSharp`). The WinForms
project is **never** referenced — it is not iOS-capable.

## Why it is NOT in `FEBuilderGBA.sln`

`.github/workflows/check.yml` builds the **entire solution** on a `windows-latest` runner
that does **not** have the `ios` .NET workload — and iOS builds require **macOS + Xcode**
regardless. Adding this `net10.0-ios` project to `FEBuilderGBA.sln` would break the required
`build` check for every unrelated PR. It is therefore intentionally excluded and built
standalone (on macOS) by the advisory `.github/workflows/ios.yml` workflow.

## Build (macOS only)

```bash
dotnet workload install ios          # one-time
dotnet build FEBuilderGBA.iOS/FEBuilderGBA.iOS.csproj -c Release -p:EnableIosTarget=true
```

The `-p:EnableIosTarget=true` flag is **required** as a **global** property (mirrors the
android head's `EnableAndroidTarget` / `NETSDK1005` fix). `AdditionalProperties` on the
`ProjectReference` activates the ios TFM for the *build* phase, but NuGet *restore* uses a
separate static graph that ignores per-reference `AdditionalProperties`, so without the
global property restore writes a net10.0-only assets file for `FEBuilderGBA.Avalonia` and
the build fails with `NETSDK1005`.

An **unsigned `.ipa`** (for downstream re-signing / sideloading via AltStore / Sideloadly —
**not** directly installable) is produced by:

```bash
dotnet publish FEBuilderGBA.iOS/FEBuilderGBA.iOS.csproj -c Release \
  -p:EnableIosTarget=true -p:RuntimeIdentifier=ios-arm64 \
  -p:EnableCodeSigning=false -p:CodesignKey= -p:CodesignProvision=
```

## What ships inside the `.app`

- The shared Avalonia editor UI under the single-view lifetime (`Views/MainView`).
- `config/**` (excluding `patch2`) as `<BundleResource>` (`LogicalName=config/…`), extracted
  once on first run into an app-private writable dir (`Library/febuildergba`) by
  `AppDelegate.FinishedLaunching` via the pure `FEBuilderGBA.Core/AndroidConfigExtractorCore`
  + `FEBuilderGBA.Core/DirectoryAssetSource` — then `App.BaseDirectoryOverride` points Core
  there. Desktop base-directory resolution is unchanged.
- `LICENSE` + `THIRD-PARTY-NOTICES.md` (GPLv3 §4-6).

## Current status (honest)

- ✅ **The head compiles** against the shared, conditionally multi-targeted
  `FEBuilderGBA.Avalonia` (opt-in `EnableIosTarget`, default OFF). It reuses the exact
  compile surface the android head already builds (same `Program.cs`/`GapSweep` excludes).
- ✅ **config asset bundling + first-run extraction** reuses the desktop-unit-tested
  `AndroidConfigExtractorCore` via `DirectoryAssetSource` (covered by `DirectoryAssetSourceTests`).
- ⚠️ **Not device/emulator-validated.** iOS Release is full AOT + trimmed and the Core/ROM
  pipeline is reflection-heavy, so the head enables the Mono interpreter + `MtouchLink=SdkOnly`
  to maximize runtime viability, but the interactive UX (touch, file pickers, editors) is
  unproven. See [docs/IOS.md](../docs/IOS.md).

## Known preview limitations

- The ~24 per-editor `OperatingSystem.IsAndroid()` file-flow guards in the shared UI are
  **not** yet extended to iOS, so those editors take the untested desktop file path on iOS
  (documented follow-up). Single-view navigation works (`MainView` installs the nav service
  unconditionally).
- `config/patch2` and the FE-Repo submodules are **not bundled** (large git-delivered
  payloads) — the same accepted limitation as Android (#1641). On-device delivery is a
  follow-up.
- Signed App Store / TestFlight distribution needs a paid Apple Developer account + the
  `APPLE_*` GitHub Actions secrets (see [docs/IOS.md](../docs/IOS.md) / `ios.yml`).
