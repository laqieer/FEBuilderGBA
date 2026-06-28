# Build Output Directory Structure

## Overview

The build system automatically maintains the correct directory structure for `config/patch2/` in both Debug and Release builds.

---

## Directory Structure

### Source (Repository)

The build copies from the **repo-root `config/`** directory. `config/data` and
`config/translate` are tracked in the main repository; `config/patch2` is a git
submodule. There is no separate `r/` resources directory.

```
FEBuilderGBA/
├── FEBuilderGBA/           (Main project)
│   └── FEBuilderGBA.csproj
└── config/                  (Repo-root config — TrackedConfigPath)
    ├── data/                (Game data definitions — tracked)
    │   ├── 6c_name_*.txt
    │   ├── 6c_script_*.txt
    │   └── ... (data files)
    ├── translate/           (Localization files — tracked)
    │   ├── en.txt
    │   ├── zh.txt
    │   └── *_tbl/ (character tables: ar_tbl, en_tbl, ko_tbl, kr_tbl, zh_tbl)
    └── patch2/              (Git submodule — patch database)
        ├── FE6/
        ├── FE7J/
        ├── FE7U/
        ├── FE8J/
        ├── FE8U/
        └── README.md
```

### Build Output (Debug)
```
FEBuilderGBA/bin/Debug/
├── FEBuilderGBA.exe
├── *.dll
├── *.json
└── config/                  ← Relative path preserved
    ├── data/                ← Copied from repo-root config/data
    │   ├── 6c_name_*.txt
    │   ├── 6c_script_*.txt
    │   └── ... (game data definitions)
    ├── translate/           ← Copied from repo-root config/translate
    │   ├── en.txt
    │   ├── zh.txt
    │   ├── ar_tbl/
    │   ├── en_tbl/
    │   ├── ko_tbl/
    │   ├── kr_tbl/
    │   └── zh_tbl/
    └── patch2/              ← Copied from config/patch2 submodule (.git excluded)
        ├── FE6/
        ├── FE7J/
        ├── FE7U/
        ├── FE8J/
        ├── FE8U/
        └── README.md
```

### Build Output (Release)
```
FEBuilderGBA/bin/Release/
├── FEBuilderGBA.exe
├── *.dll
├── *.json
└── config/                  ← Relative path preserved
    ├── data/                ← Copied from repo-root config/data
    │   ├── 6c_name_*.txt
    │   ├── 6c_script_*.txt
    │   └── ... (game data definitions)
    ├── translate/           ← Copied from repo-root config/translate
    │   ├── en.txt
    │   ├── zh.txt
    │   ├── ar_tbl/
    │   ├── en_tbl/
    │   ├── ko_tbl/
    │   ├── kr_tbl/
    │   └── zh_tbl/
    └── patch2/              ← Copied from config/patch2 submodule (.git excluded)
        ├── FE6/
        ├── FE7J/
        ├── FE7U/
        ├── FE8J/
        ├── FE8U/
        └── README.md
```

---

## Build Configuration

The relative path `config/` structure is maintained by the MSBuild target in `FEBuilderGBA.csproj`:

### MSBuild Target: CopyConfigDirectories

```xml
<Target Name="CopyConfigDirectories" AfterTargets="Build">
  <PropertyGroup>
    <!-- Source: repo-root config/ (config/data and config/translate are tracked) -->
    <TrackedConfigPath>$(MSBuildProjectDirectory)\..\config</TrackedConfigPath>

    <!-- Source: Repository config/patch2 (git submodule) -->
    <Patch2SubmodulePath>$(MSBuildProjectDirectory)\..\config\patch2</Patch2SubmodulePath>

    <!-- Target: bin/{Configuration}/config -->
    <TargetConfigDir>$(OutDir)config</TargetConfigDir>
  </PropertyGroup>

  <ItemGroup>
    <DataFiles Include="$(TrackedConfigPath)\data\**\*.*" />
    <TranslateFiles Include="$(TrackedConfigPath)\translate\**\*.*" />
    <!-- Exclude the submodule .git link/dir -->
    <Patch2Files Include="$(Patch2SubmodulePath)\**\*.*"
                 Exclude="$(Patch2SubmodulePath)\.git;$(Patch2SubmodulePath)\.git\**\*.*" />
  </ItemGroup>

  <!-- Copy data/ files preserving directory structure -->
  <Copy SourceFiles="@(DataFiles)"
        DestinationFolder="$(TargetConfigDir)\data\%(RecursiveDir)"
        SkipUnchangedFiles="true"
        OverwriteReadOnlyFiles="true" />

  <!-- Copy translate/ files preserving directory structure -->
  <Copy SourceFiles="@(TranslateFiles)"
        DestinationFolder="$(TargetConfigDir)\translate\%(RecursiveDir)"
        SkipUnchangedFiles="true"
        OverwriteReadOnlyFiles="true" />

  <!-- Copy patch2/ files preserving directory structure -->
  <Copy SourceFiles="@(Patch2Files)"
        DestinationFolder="$(TargetConfigDir)\patch2\%(RecursiveDir)"
        SkipUnchangedFiles="true"
        OverwriteReadOnlyFiles="true" />
</Target>
```

**Key Points:**
- ✅ Runs automatically after every build
- ✅ Works for both Debug and Release configurations
- ✅ Copies three directories:
  - `config/data/` from repo-root `config/data/` (game data definitions)
  - `config/translate/` from repo-root `config/translate/` (localization files)
  - `config/patch2/` from the `config/patch2/` submodule (patch database; `.git` excluded)
- ✅ Preserves relative path structure
- ✅ Maintains all subdirectory structures
- ✅ Only copies changed files (efficient)
- ✅ Overwrites read-only files if needed

---

## Path Resolution

### At Runtime

The application loads patch files from the `config/patch2/` directory relative to
the application directory (`bin/Debug` or `bin/Release`). The relative `config/`
path is preserved so the same lookup works in development and in shipped builds.

### Patch2 Versioning

Patch2 data is **versioned and updated via git, not via build artifacts**.
`UpdateInfo` (`FEBuilderGBA.Core/UpdateInfo.cs`) tracks only the **core**
application version (`VERSION_CORE`, format `yyyyMMdd.HH`) and exposes a
`PackageType` enum with just `{ Unknown, CoreOnly, None }`. There is no
`ReadPatch2Version()` / `VERSION_PATCH2` / `config/patch2/version.txt`
mechanism — patch2 freshness is managed by the in-app git updater (clone / fetch
/ reset of the `config/patch2` submodule). See `FEBuilderGBA.Core/GitUtil.cs`
and the "Updating patch2" section of the README.

### Absolute Paths

The repo-root `config/` is the build source; the per-configuration output is the
target. For example:

```
Source: <repo>\config\data\,  <repo>\config\translate\,  <repo>\config\patch2\
Target: <repo>\FEBuilderGBA\bin\Debug\config\...   (or bin\Release\config\...)
```

---

## Verification

### Check Directory Structure

```bash
# Debug build
ls -la FEBuilderGBA/bin/Debug/config/patch2/

# Release build
ls -la FEBuilderGBA/bin/Release/config/patch2/

# Should show:
# - FE6/, FE7J/, FE7U/, FE8J/, FE8U/ directories
# - README.md file
```

### Run Application

```bash
# Debug
./FEBuilderGBA/bin/Debug/FEBuilderGBA.exe

# Release
./FEBuilderGBA/bin/Release/FEBuilderGBA.exe

# Application should:
# ✅ Start without errors
# ✅ Access patch files from config/patch2/
```

---

## CI/CD Packaging

### Single Core Artifact

The CI workflow `.github/workflows/msbuild.yml` (display name **MSBuild**) builds
the Release x86 solution and uploads **one** artifact per push to `master`:

```
{repo}_{build_time}            (e.g. FEBuilderGBA_20260226.00)
├── *.exe
├── *.dll
├── *.json
├── config/
│   ├── data/                 ← bundled
│   ├── translate/            ← bundled
│   └── patch2/               ← STRIPPED: only empty version subdirs
│       ├── FE6/   (empty)
│       ├── FE7J/  (empty)
│       ├── FE7U/  (empty)
│       ├── FE8J/  (empty)
│       └── FE8U/  (empty)
├── tools/bin/                ← self-contained ColorzCore (+ lyn.exe if present)
├── README*.md
├── LICENSE
└── THIRD-PARTY-NOTICES.md
```

There is **no** three-package FULL/CORE/PATCH2 split and **no**
`scripts/create-split-packages.ps1`. The "Post Build" step in the workflow
deletes the bundled `config/patch2` content and recreates the five empty version
subdirectories (`FE6, FE7J, FE7U, FE8J, FE8U`) so the app starts without
crashing; users then populate patch2 via the in-app git updater. Relevant lines
from the workflow:

```powershell
# Remove patch2 content from artifact — users update it via git
# Keep the 5 empty version subdirs so the app doesn't crash on startup
if (Test-Path "config/patch2") {
  Remove-Item "config/patch2" -Recurse -Force
}
$null = New-Item -ItemType Directory -Force -Path "config/patch2/FE6"
$null = New-Item -ItemType Directory -Force -Path "config/patch2/FE7J"
$null = New-Item -ItemType Directory -Force -Path "config/patch2/FE7U"
$null = New-Item -ItemType Directory -Force -Path "config/patch2/FE8J"
$null = New-Item -ItemType Directory -Force -Path "config/patch2/FE8U"
```

---

## Why This Structure Matters

### 1. Consistent Runtime Behavior
- Application always finds patches at `config/patch2/`
- No special cases for Debug vs Release
- Works the same in development and production

### 2. Git-Based Patch2 Updates
- The in-app git updater clones/fetches/resets `config/patch2` in place
- No path translation needed — patches live at `config/patch2/` on disk
- Patch database updates without rebuilding or repackaging the application

### 3. Version Tracking
- `UpdateInfo` tracks only the core application version (`VERSION_CORE`)
- Patch2 freshness is determined by git, not a bundled `version.txt`
- Path is consistent across all environments; no configuration needed

### 4. Submodule Benefits
- Independent versioning of the patch database
- Can update patches without rebuilding the application
- Clean separation of code and data

---

## Troubleshooting

### Problem: patch2 directory not found

**Symptom:**
```
ERROR: Could not find directory 'bin/Debug/config/patch2/'
```

**Solution:**
```bash
# Rebuild to trigger the CopyConfigDirectories target
dotnet build FEBuilderGBA/FEBuilderGBA.csproj -c Debug
```

### Problem: patch2 directory is empty

**Symptom:**
`config/patch2/FE6` (etc.) exist but contain no patch files.

**Solution:**
```bash
# Initialize / update the patch2 submodule, then rebuild
git submodule update --init config/patch2

# Rebuild
dotnet build FEBuilderGBA/FEBuilderGBA.csproj -c Debug
```

### Problem: Old patch files

**Symptom:**
Patch files not updating after submodule update

**Solution:**
```bash
# Clean build output
rm -rf FEBuilderGBA/bin/Debug/config/patch2/
rm -rf FEBuilderGBA/bin/Release/config/patch2/

# Rebuild (will copy fresh files)
dotnet build FEBuilderGBA/FEBuilderGBA.csproj -c Debug
dotnet build FEBuilderGBA/FEBuilderGBA.csproj -c Release
```

---

## Summary

✅ **Current Status: WORKING CORRECTLY**

- `config/` relative path structure is maintained in all builds
- Three directories automatically copied:
  - `config/data/` - Game data definitions
  - `config/translate/` - Localization files
  - `config/patch2/` - Patch database (git submodule)
- MSBuild target automatically copies all files after every build
- Structure is consistent across Debug, Release, and the CI artifact
- No manual intervention required
- CI ships a single core artifact; patch2 is delivered via the in-app git updater

**Configuration is correct and operational** ✅

---

**Build Configuration:** `FEBuilderGBA/FEBuilderGBA.csproj` (`CopyConfigDirectories` target)
**CI Workflow:** `.github/workflows/msbuild.yml` (single `{repo}_{build_time}` artifact)
**Related Documentation:**
- `archive/SPLIT-PACKAGE-FINAL-STATUS.md` - Former split-package system (archived / historical)
- `DEPLOYMENT.md` - Package creation and deployment
- `RELEASE.md` - Release workflow and signing
- `UPDATE-GUIDE.md` - User-facing update documentation
