# Copilot Instructions for FEBuilderGBA

## Build, test, and lint commands

This repository has two main build paths:

- Windows/x86 solution builds for the WinForms app and Windows-only test projects.
- Cross-platform `dotnet build` and `dotnet test` for `FEBuilderGBA.Core`, `FEBuilderGBA.CLI`, `FEBuilderGBA.SkiaSharp`, and `FEBuilderGBA.Avalonia`.

The repository also depends on the `config\patch2` git submodule. If runtime patch data is missing, run:

```powershell
git submodule update --init --recursive
```

### Full solution and Windows builds

```powershell
msbuild /m /p:Configuration=Release /p:Platform=x86 /t:build /restore FEBuilderGBA.sln
msbuild /m /p:Configuration=Debug /p:Platform=x86 /t:build /restore FEBuilderGBA.sln
```

### Cross-platform builds

```powershell
dotnet build FEBuilderGBA.Core\FEBuilderGBA.Core.csproj -c Release
dotnet build FEBuilderGBA.CLI\FEBuilderGBA.CLI.csproj -c Release
dotnet build FEBuilderGBA.SkiaSharp\FEBuilderGBA.SkiaSharp.csproj -c Release
dotnet build FEBuilderGBA.Avalonia\FEBuilderGBA.Avalonia.csproj -c Release
```

### Test commands

CI runs the full Windows test pass like this:

```powershell
dotnet test --configuration Release --no-build --verbosity normal --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

Project-level test commands:

```powershell
dotnet test FEBuilderGBA.Core.Tests\FEBuilderGBA.Core.Tests.csproj -c Release
dotnet test FEBuilderGBA.Tests\FEBuilderGBA.Tests.csproj -c Release
dotnet test FEBuilderGBA.E2ETests\FEBuilderGBA.E2ETests.csproj -c Release --no-build
```

If you want the Avalonia-dependent E2E scenarios, build Avalonia first:

```powershell
dotnet build FEBuilderGBA.Avalonia\FEBuilderGBA.Avalonia.csproj -c Release
```

### Run a single test

The test projects use xUnit, so single-test runs go through `dotnet test --filter`:

```powershell
dotnet test FEBuilderGBA.Core.Tests\FEBuilderGBA.Core.Tests.csproj --filter "FullyQualifiedName~EditorFormRefTests"
dotnet test FEBuilderGBA.Tests\FEBuilderGBA.Tests.csproj --filter "FullyQualifiedName~RegexCacheTests"
dotnet test FEBuilderGBA.E2ETests\FEBuilderGBA.E2ETests.csproj -c Release --no-build --filter "FullyQualifiedName~CliHelpTests.Version_ContainsLicense"
```

Local E2E runs discover built executables and ROMs from the solution tree, but you can override them with environment variables used by the test helpers:

- `FEBUILDERGBA_EXE`
- `FEBUILDERGBA_CLI_EXE`
- `AVALONIA_EXE`
- `ROMS_DIR`
- `FEBUILDERGBA_SCREENSHOT_DIR`
- `FEBUILDERGBA_CLI_LOG_DIR`

### Lint

There is no separate C# style-lint command wired into CI. In this repository, `lint` usually means ROM validation:

```powershell
dotnet run --project FEBuilderGBA.CLI -- --lint --rom=roms\FE8U.gba
FEBuilderGBA\bin\Release\FEBuilderGBA.exe --rom roms\FE8U.gba --lint
```

## High-level architecture

- `FEBuilderGBA.Core` is the canonical shared domain layer. It contains ROM loading and version detection (`Rom.cs`, `ROMFE*.cs`), undo (`Undo.cs`), text encoding, event scripts, export/import, patch detection, lint scanning, rebuild logic, and platform abstraction points exposed through `CoreState`.
- `FEBuilderGBA` is the WinForms host. `FEBuilderGBA\Program.cs` does early command-line detection so `--version` and other non-GUI flows can run before WinForms or GDI initialization.
- `FEBuilderGBA.CLI` is a separate cross-platform host. Its `Program.cs` sets `CoreState.Services` to headless services, sets `CoreState.ImageService` to the SkiaSharp implementation, and routes the CLI commands.
- `FEBuilderGBA.Avalonia` is the newer cross-platform GUI. It reuses `FEBuilderGBA.Core` and adds its own lightweight editor auto-binding via `FEBuilderGBA.Core\EditorFormRef.cs`.
- `FEBuilderGBA.SkiaSharp` supplies the image service implementation used by CLI and Avalonia code paths.
- `FEBuilderGBA.Tests`, `FEBuilderGBA.Core.Tests`, and `FEBuilderGBA.E2ETests` split coverage across Windows-host behavior, pure Core behavior, and black-box executable behavior.

The central runtime object is `ROM`. `FEBuilderGBA.Core\Rom.cs` reads the ROM header at `0x080000AC`, selects a version-specific `ROMFEINFO` implementation (`ROMFE6JP`, `ROMFE7JP`, `ROMFE7U`, `ROMFE8JP`, `ROMFE8U`), and then exposes all reads and writes through `u8`, `u16`, `u32`, `p32`, `write_u8`, `write_u16`, `write_u32`, and `write_p32`.

Version-specific addresses live on `ROMFEINFO` subclasses, not in scattered constants. If a feature depends on game-specific offsets, look for an existing `RomInfo` property before introducing new version checks.

Runtime data is not embedded into the binaries. The WinForms project copies `config\data`, `config\translate`, and the `config\patch2` submodule into build output after the build. `7-zip32.dll` is optional and copied separately when present.

## Key conventions

- Treat `FEBuilderGBA.Core\*.cs` as the canonical copy for shared ROM logic. The WinForms project still contains moved files such as `Rom.cs`, `ROMFE*.cs`, and `Undo.cs`, but `FEBuilderGBA\FEBuilderGBA.csproj` explicitly removes them and references `FEBuilderGBA.Core`.
- Keep cross-platform logic in `FEBuilderGBA.Core` and reach platform behavior through `CoreState` interfaces such as `IAppServices`, `ISystemTextEncoder`, `IAsmMapCache`, and `IImageService`. Avoid adding WinForms dependencies to Core code.
- Use the ROM primitives consistently. `p32` and `write_p32` are the pointer-aware APIs; they handle GBA pointer conversion instead of raw `0x08000000` arithmetic at call sites.
- ROM mutations are expected to participate in undo. Existing code uses either explicit `Undo.UndoData` parameters or `using (ROM.BeginUndoScope(undoData)) { ... }`. Undo also clears cached counts and ASM/event caches, so bypassing it can leave stale state behind.
- WinForms editor behavior is heavily name-driven. `InputFormRef.MakeLinkEvent(...)` wires controls through naming patterns like `L_{id}_{linktype}_{args}` plus numeric field prefixes. Renaming designer controls can break editor behavior without causing compile errors.
- Avalonia editor binding is also name-driven, but uses `EditorFormRef` field names: `B{offset}`, `S{offset}`, `W{offset}`, `D{offset}`, and `P{offset}`.
- Headless paths matter. `FEBuilderGBA\Program.cs` checks `--version` and other CLI flags before WinForms initialization so CI can run the executable in headless mode. Preserve that startup ordering when changing command routing.
- WinForms and E2E validation assume `x86` builds. If a change touches WinForms startup, command-line behavior in `FEBuilderGBA.exe`, or E2E helpers, prefer validating with the `msbuild` x86 solution build rather than only with project-level `dotnet build`.
- When creating a git commit message or any GitHub post for this repository (issue, discussion, comment, or reply), append a footer with the current Copilot CLI version and model configuration used for that action. Use the live session values rather than hardcoded text, e.g. `Copilot CLI: 1.0.5-0` and `Model: GPT-5.4 (gpt-5.4)`.
