# Third-Party Notices

FEBuilderGBA is licensed under the **GNU General Public License v3.0** (see the
`LICENSE` file bundled in every release artifact).

This file lists third-party components that are **bundled into** the published
release artifacts (the WinForms zip, the `cli-{rid}` / `avalonia-{rid}` bundles,
and the Android package). Each component remains under its own license. Where a
component is licensed under the GPLv3 or LGPL, the full GPLv3 text in the
accompanying `LICENSE` file also serves as that component's license text.

## Bundled binaries

| Component | License | Where it ships | Upstream |
|-----------|---------|----------------|----------|
| ColorzCore (Event Assembler core) | GNU GPL v3.0 | `tools/bin/` in the CLI and Avalonia bundles | https://github.com/FireEmblemUniverse/ColorzCore |
| Event Assembler / `lyn.exe` | GNU GPL v3.0 | `tools/bin/Tools/lyn.exe` in the CLI and Avalonia bundles (when present) | https://github.com/laqieer/Event-Assembler |
| 7-zip32.dll (optional native archive DLL) | GNU LGPL v2.1 | `7-zip32.dll` next to `FEBuilderGBA.exe` in the WinForms zip (optional) | https://github.com/HBhcraft/7-Zip32 / http://www.7-zip.org/ |

## Bundled .NET / NuGet runtime dependencies

These managed dependencies are published into the self-contained CLI and
Avalonia bundles (and the WinForms output) as part of the .NET runtime payload.

| Component | License | Upstream |
|-----------|---------|----------|
| Avalonia (and Avalonia.Desktop / Avalonia.Android / Avalonia.Themes.Fluent) | MIT | https://github.com/AvaloniaUI/Avalonia |
| SkiaSharp (and SkiaSharp.NativeAssets.*) | MIT | https://github.com/mono/SkiaSharp |
| SharpCompress | MIT | https://github.com/adamhathcock/sharpcompress |
| System.Drawing.Common | MIT | https://github.com/dotnet/runtime |
| System.Text.Encoding.CodePages | MIT | https://github.com/dotnet/runtime |
| Microsoft.CodeAnalysis.CSharp | MIT | https://github.com/dotnet/roslyn |

## License obligations

Because FEBuilderGBA and several bundled components are distributed under the
GPLv3 (§4–6), the full license text **must accompany every binary
distribution**. The `LICENSE` file is therefore copied into:

- the WinForms release zip (`release.ps1`),
- the `cli-{rid}` and `avalonia-{rid}` artifacts (`.github/workflows/crossplatform.yml`),

alongside this `THIRD-PARTY-NOTICES.md`.

### Android

The Android package (APK/AAB) embeds application assets differently from a flat
zip, but the same GPLv3 license obligations apply. The Android build is owned by
the Android release workflow; the `LICENSE` and this notices file are part of
the repository root and are bundled into the source/release the same way. See
`docs/ANDROID.md` for Android packaging specifics.

## Corresponding source

The complete corresponding source code for FEBuilderGBA and for the bundled
GPL/LGPL components is available from this repository and the upstream URLs
listed above.
