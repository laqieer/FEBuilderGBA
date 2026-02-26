# Build Output Directory Structure

## Overview

The build system automatically maintains the correct directory structure for `config/patch2/` in both Debug and Release builds.

---

## Directory Structure

### Source (Repository)
```
FEBuilderGBA/
├── FEBuilderGBA/           (Main project)
│   └── FEBuilderGBA.csproj
└── config/                  (Submodule root)
    └── patch2/              (Git submodule)
        ├── FE6/
        ├── FE7J/
        ├── FE7U/
        ├── FE8J/
        ├── FE8U/
        ├── version.txt
        └── README.md
```

### Build Output (Debug)
```
FEBuilderGBA/bin/Debug/
├── FEBuilderGBA.exe
├── *.dll
├── *.json
└── config/                  ← Relative path preserved
    └── patch2/              ← Copied from submodule
        ├── FE6/
        ├── FE7J/
        ├── FE7U/
        ├── FE8J/
        ├── FE8U/
        ├── version.txt
        └── README.md
```

### Build Output (Release)
```
FEBuilderGBA/bin/Release/
├── FEBuilderGBA.exe
├── *.dll
├── *.json
└── config/                  ← Relative path preserved
    └── patch2/              ← Copied from submodule
        ├── FE6/
        ├── FE7J/
        ├── FE7U/
        ├── FE8J/
        ├── FE8U/
        ├── version.txt
        └── README.md
```

---

## Build Configuration

The relative path `config/patch2/` is maintained by the MSBuild target in `FEBuilderGBA.csproj`:

### MSBuild Target: CopyConfigSubmodule

```xml
<Target Name="CopyConfigSubmodule" AfterTargets="Build">
  <PropertyGroup>
    <!-- Source: Repository root/config/patch2 -->
    <SubmodulePath>$(MSBuildProjectDirectory)\..\config\patch2</SubmodulePath>

    <!-- Target: bin/{Configuration}/config/patch2 -->
    <TargetConfigPath>$(OutDir)config\patch2</TargetConfigPath>
  </PropertyGroup>

  <!-- Copy files preserving directory structure -->
  <Copy SourceFiles="@(SubmoduleFiles)"
        DestinationFolder="$(TargetConfigPath)\%(RecursiveDir)"
        SkipUnchangedFiles="true"
        OverwriteReadOnlyFiles="true" />
</Target>
```

**Key Points:**
- ✅ Runs automatically after every build
- ✅ Works for both Debug and Release configurations
- ✅ Preserves relative path: `config/patch2/`
- ✅ Maintains subdirectory structure (FE6, FE7J, etc.)
- ✅ Only copies changed files (efficient)
- ✅ Overwrites read-only files if needed

---

## Path Resolution

### At Runtime

The application reads the patch2 version using:

```csharp
// In UpdateInfo.cs
public static string ReadPatch2Version()
{
    string versionFile = Path.Combine(
        Program.BaseDirectory,    // = Application directory (bin/Debug or bin/Release)
        "config",                 // Relative path preserved
        "patch2",                 // Relative path preserved
        "version.txt"
    );

    // Result: bin/Debug/config/patch2/version.txt
    // or:     bin/Release/config/patch2/version.txt
}
```

### Absolute Paths

For **Debug** build:
```
Source: C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA\config\patch2\
Target: C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA\FEBuilderGBA\bin\Debug\config\patch2\
```

For **Release** build:
```
Source: C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA\config\patch2\
Target: C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA\FEBuilderGBA\bin\Release\config\patch2\
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
# - version.txt file
# - README.md file
```

### Check Version File

```bash
# Debug
cat FEBuilderGBA/bin/Debug/config/patch2/version.txt

# Release
cat FEBuilderGBA/bin/Release/config/patch2/version.txt

# Should show: 20260226.00 (or current version)
```

### Run Application

```bash
# Debug
./FEBuilderGBA/bin/Debug/FEBuilderGBA.exe

# Release
./FEBuilderGBA/bin/Release/FEBuilderGBA.exe

# Application should:
# ✅ Start without errors
# ✅ Read patch2 version correctly
# ✅ Access patch files from config/patch2/
```

---

## CI/CD Packaging

### Split Package Structure

The CI/CD pipeline creates three packages, all maintaining the `config/patch2/` relative path:

**FULL Package:**
```
FEBuilderGBA_FULL_{coreVer}_{patch2Ver}.7z
├── FEBuilderGBA.exe
├── *.dll
├── *.json
└── config/
    └── patch2/              ← Relative path maintained
        └── ...
```

**CORE Package:**
```
FEBuilderGBA_CORE_{coreVer}.7z
├── FEBuilderGBA.exe
├── *.dll
├── *.json
└── config/
    (patch2/ excluded)
```

**PATCH2 Package:**
```
FEBuilderGBA_PATCH2_{patch2Ver}.7z
└── config/
    └── patch2/              ← Relative path maintained
        └── ...
```

### Packaging Script

The PowerShell script `scripts/create-split-packages.ps1` creates packages using:

```powershell
# FULL package - includes everything with relative path
7z a FEBuilderGBA_FULL_*.7z `
    FEBuilderGBA.exe `
    *.dll `
    *.json `
    config\*               # Includes config/patch2/

# PATCH2 package - only patch2 with relative path
7z a FEBuilderGBA_PATCH2_*.7z `
    config\patch2\*        # Relative path: config/patch2/
```

---

## Why This Structure Matters

### 1. Consistent Runtime Behavior
- Application always finds patches at `config/patch2/`
- No special cases for Debug vs Release
- Works the same in development and production

### 2. Split Package Updates
- PATCH2 updates can extract directly to application directory
- No path translation needed
- Structure: `config/patch2/` in package → `config/patch2/` on disk

### 3. Version Reading
- `UpdateInfo.ReadPatch2Version()` reads from `config/patch2/version.txt`
- Path is consistent across all environments
- No configuration needed

### 4. Submodule Benefits
- Independent versioning of patch database
- Can update patches without rebuilding application
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
# Rebuild to trigger CopyConfigSubmodule target
dotnet build FEBuilderGBA/FEBuilderGBA.csproj -c Debug
```

### Problem: version.txt missing

**Symptom:**
```
UpdateInfo.VERSION_PATCH2 = "00000000.00" (default)
```

**Solution:**
```bash
# Check submodule is initialized
git submodule update --init --recursive

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

- `config/patch2/` relative path is maintained in all builds
- MSBuild target automatically copies files after every build
- Structure is consistent across Debug, Release, and packaged builds
- No manual intervention required
- Split package system relies on this structure

**No changes needed** - the configuration is already correct and operational.

---

**Build Configuration:** `FEBuilderGBA/FEBuilderGBA.csproj` (lines 203-237)
**Related Documentation:**
- `SPLIT-PACKAGE-FINAL-STATUS.md` - Split package system overview
- `DEPLOYMENT.md` - Package creation and deployment
- `UPDATE-GUIDE.md` - User-facing update documentation
