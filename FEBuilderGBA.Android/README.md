# FEBuilderGBA.Android (Avalonia.Android head)

> **Builds an APK; not yet runnable / device-validated.** Part of epic
> [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070). The structural
> prerequisite landed in
> [#1121](https://github.com/laqieer/FEBuilderGBA/issues/1121) — this head now
> builds a real APK against the shared, conditionally multi-targeted
> `FEBuilderGBA.Avalonia`. Read the full feasibility assessment first:
> **[docs/ANDROID.md](../docs/ANDROID.md)**.

This is a minimal Avalonia.Android *head* that reuses the shared
`FEBuilderGBA.Avalonia` UI (which transitively pulls in `FEBuilderGBA.Core` +
`FEBuilderGBA.SkiaSharp`). The WinForms project is **never** referenced — it is
not Android-capable.

## Why it is NOT in `FEBuilderGBA.sln`

`.github/workflows/check.yml` builds the **entire solution** on a `windows-latest`
runner that does **not** have the `android` .NET workload. Adding this
`net9.0-android` project to `FEBuilderGBA.sln` would break the required `build`
check for every unrelated PR. It is therefore intentionally excluded and built
standalone.

## Build

```bash
dotnet workload install android          # one-time
dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj -c Release -p:EnableAndroidTarget=true
```

The `-p:EnableAndroidTarget=true` flag is **required**. `AdditionalProperties` on
the `ProjectReference` correctly activates the android TFM for the *build* phase,
but NuGet *restore* uses a separate static graph that ignores per-reference
`AdditionalProperties`, so without the global property restore writes a
net9.0-only assets file for `FEBuilderGBA.Avalonia` and the build fails with
`NETSDK1005`. Passing it as a global property flows it to the referenced
project's restore + build. A successful build emits
`com.laqieer.febuildergba-Signed.apk` under
`bin/Release/net9.0-android/`.

## Current build status (honest)

- ✅ A minimal `net9.0-android` project builds + packages a signed APK with the
  workload installed (toolchain verified).
- ✅ This head's own code (`MainActivity`) **compiles** against the shared
  `FEBuilderGBA.Avalonia` reference.
- ✅ **The full APK builds (#1121).** `FEBuilderGBA.Avalonia` conditionally
  multi-targets `net9.0;net9.0-android` (opt-in via `EnableAndroidTarget`,
  default OFF); the `ProjectReference` below activates the android TFM with
  `AdditionalProperties="EnableAndroidTarget=true"` (an MSBuild-verified
  requirement). `dotnet build … -c Release` produces an APK under
  `bin/Release/net9.0-android/`. On the android TFM the shared project excludes
  `Program.cs`, `Avalonia.Desktop`, `app.manifest`, the `GapSweep` Roslyn
  dev-tooling, and `Microsoft.CodeAnalysis.CSharp`.
- ⚠️ **Not yet device/emulator-validated** — it builds, but the runtime port
  (single-view `MainView`, config asset extraction, SAF ROM I/O) is still
  pending. See
  [docs/ANDROID.md §7](../docs/ANDROID.md#7-build-status-in-this-environment).

## Known skeleton limitations

- The shared `App.OnFrameworkInitializationCompleted` only builds UI under the
  **classic desktop** lifetime, so under Android's single-view lifetime the app
  boots the Avalonia runtime but shows no editor. Wiring
  `ISingleViewApplicationLifetime.MainView` + porting `WindowManager`'s
  multi-window model to page navigation is the largest follow-up.
- `config/` asset bundling + first-run extraction and SAF-stream ROM I/O are
  separate follow-ups (the `<AndroidAsset>` line in the csproj is intentionally
  commented out — config bundling is deferred to #1123). Today `config/**` ships
  as loose Content beside the APK, which is harmless but not the shipping layout.
