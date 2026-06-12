# FEBuilderGBA.Android (Avalonia.Android head skeleton)

> **Exploration skeleton — not shipped, not runnable yet.** Part of epic
> [#1070](https://github.com/laqieer/FEBuilderGBA/issues/1070). Read the full
> feasibility assessment first: **[docs/ANDROID.md](../docs/ANDROID.md)**.

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
dotnet build FEBuilderGBA.Android/FEBuilderGBA.Android.csproj
```

## Current build status (honest)

- ✅ A minimal `net9.0-android` project builds + packages a signed APK with the
  workload installed (toolchain verified).
- ✅ This head's own code (`MainActivity`) **compiles** against the shared
  `FEBuilderGBA.Avalonia` reference; MSBuild builds `FEBuilderGBA.Avalonia` as
  its own `net9.0` TFM (it is **not** recompiled under the Android TFM) and the
  build reaches the Android RID-packaging stage.
- ❌ The **full APK** does NOT build yet: `FEBuilderGBA.Avalonia` targets plain
  `net9.0`, so per-RID (`android-arm64`/`android-x64`) packaging fails
  (`NETSDK1047` / `NETSDK1112`). The fix is to multi-target / split the shared
  UI — tracked as a follow-up under #1070. See
  [docs/ANDROID.md §7](../docs/ANDROID.md#7-build-status-in-this-environment).

## Known skeleton limitations

- The shared `App.OnFrameworkInitializationCompleted` only builds UI under the
  **classic desktop** lifetime, so under Android's single-view lifetime the app
  boots the Avalonia runtime but shows no editor. Wiring
  `ISingleViewApplicationLifetime.MainView` + porting `WindowManager`'s
  multi-window model to page navigation is the largest follow-up.
- `config/` asset bundling + first-run extraction and SAF-stream ROM I/O are
  separate follow-ups (the `<AndroidAsset>` line in the csproj is intentionally
  commented out).
