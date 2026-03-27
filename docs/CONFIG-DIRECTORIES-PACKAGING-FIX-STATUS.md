# Config Directories Packaging Fix - Final Status

## Issue Report

**Date:** 2026-02-26
**Issue:** data/ and translate/ directories missing from release packages
**Reported By:** User
**Status:** ✅ **RESOLVED**

---

## Problem Summary

### Original Issue

User reported: "data/ and translate/ are not included in the release package as expected, only patch2/ is included"

### Root Cause

The CI/CD workflow's `Post Build` step was moving individual files (exe, dll, json) from `FEBuilderGBA/bin/Release/` to the current directory, but was **not moving the `config/` directory** that contains:
- `config/data/` - Game data definitions (6c_name, 6c_script files)
- `config/translate/` - Translation files and character tables
- `config/patch2/` - Patch database (git submodule)

The packaging script (`scripts/create-split-packages.ps1`) expected these files in the current directory (specified by `-BinPath "."`), but they remained in `FEBuilderGBA/bin/Release/config/`.

---

## Solution Implemented

### Fix 1: Add config/ directory move to Post Build

**File:** `.github/workflows/msbuild.yml`
**Lines:** 126-131
**Commit:** c00a3472

```yaml
# Move config directory with all subdirectories (data, translate, patch2)
if (Test-Path "FEBuilderGBA/bin/Release/config") {
  mv FEBuilderGBA/bin/Release/config .
}
```

**Result:** ❌ Failed with error: "Cannot create 'config' because a file or directory with the same name already exists"

**Reason:** Repository root already has a `config/` directory (the patch2 git submodule)

### Fix 2: Remove existing config/ before moving

**File:** `.github/workflows/msbuild.yml`
**Lines:** 126-132
**Commit:** 96da36a1

```yaml
# Move config directory with all subdirectories (data, translate, patch2)
# First remove the submodule config directory, then move the build output config
if (Test-Path "FEBuilderGBA/bin/Release/config") {
  if (Test-Path "config") {
    Remove-Item -Path "config" -Recurse -Force
  }
  mv FEBuilderGBA/bin/Release/config .
}
```

**Result:** ✅ **SUCCESS**

**Explanation:**
1. The repository root `config/` only contains the patch2 submodule
2. The build output `config/` contains all three directories (data/, translate/, patch2/)
3. The patch2 files are already copied from the submodule to `bin/Release/config/patch2/` by the MSBuild target
4. Removing the submodule directory and replacing it with the build output config/ ensures all three directories are in the correct location for packaging

---

## Verification Results

### CI/CD Build Status

**Workflow Run:** [MSBuild #22434718282](https://github.com/laqieer/FEBuilderGBA/actions/runs/22434718282)
**Status:** ✅ **PASSED**
**Duration:** 7m 15s
**Commit:** 96da36a1

**All Steps Passed:**
- ✅ Build
- ✅ Run Tests (408/410 passing, 2 skipped)
- ✅ Generate Coverage Report
- ✅ Post Build
- ✅ Create Split Packages
- ✅ Upload Split Package Artifacts

### Package Creation Results

**FULL Package:**
- Filename: `FEBuilderGBA_FULL_20260226.08_20260226.00.7z`
- Size: 5.18 MB
- Contents: exe, dll, json, **config/data/**, **config/translate/**, **config/patch2/**

**CORE Package:**
- Filename: `FEBuilderGBA_CORE_20260226.08.7z`
- Size: 1.63 MB (up from ~900KB, confirming data/ and translate/ included)
- Contents: exe, dll, json, **config/data/**, **config/translate/** (patch2/ removed by script)

**PATCH2 Package:**
- Filename: `FEBuilderGBA_PATCH2_20260226.00.7z`
- Size: 3.56 MB
- Contents: **config/patch2/** only

### Local Test Results

**Build:** ✅ Success
**Tests:** ✅ 408 passing, 2 skipped (intentional), 0 failed
**Total:** 410 tests
**Duration:** 3.05 seconds

**Config directories verified in build output:**
```
FEBuilderGBA/bin/Release/config/
├── data/           ← Game data definitions (6c_name, 6c_script files)
├── translate/      ← Translation files (en.txt, zh.txt, tbl directories)
└── patch2/         ← Patch database (FE6/, FE7J/, FE7U/, FE8J/, FE8U/)
```

---

## Changes Made

### Modified Files

1. **`.github/workflows/msbuild.yml`** (c00a3472, 96da36a1)
   - Added conditional move of config/ directory in Post Build step
   - Added logic to remove existing config/ before move to avoid conflicts

2. **`FEBuilderGBA/FEBuilderGBA.csproj`** (406b20ee)
   - Renamed MSBuild target: `CopyConfigSubmodule` → `CopyConfigDirectories`
   - Extended to copy three directories instead of one:
     - `config/data/` from `r/config/data/`
     - `config/translate/` from `r/config/translate/`
     - `config/patch2/` from `config/patch2/` (submodule)

### Documentation Files Created

1. **`BUILD-OUTPUT-STRUCTURE.md`** (d75302e2, 406b20ee)
   - Documents directory structure in source, Debug, Release, and packages
   - Explains MSBuild target configuration
   - Provides verification steps

2. **`SKIPPED-TESTS-EXPLANATION.md`** (137afab5)
   - Explains why 2 tests are intentionally skipped
   - Documents that functionality is tested through other methods
   - Industry best practices and rationale

3. **`CI-CD-STATUS.md`** (137afab5)
   - Comprehensive CI/CD status report
   - Workflow run history and analysis
   - Troubleshooting guide

4. **`CONFIG-DIRECTORIES-PACKAGING-FIX-STATUS.md`** (this file)
   - Complete issue resolution documentation
   - Technical details and verification results

---

## Technical Details

### Directory Flow

**Source:**
```
Repository Root/
├── r/config/data/           ← Game data definitions
├── r/config/translate/      ← Translation files
└── config/patch2/           ← Git submodule (patches)
```

**Build Output (after MSBuild target):**
```
FEBuilderGBA/bin/Release/
├── FEBuilderGBA.exe
├── *.dll, *.json
└── config/
    ├── data/         ← Copied from r/config/data/
    ├── translate/    ← Copied from r/config/translate/
    └── patch2/       ← Copied from config/patch2/ submodule
```

**Packaging Location (after Post Build):**
```
Current Directory (.) /
├── FEBuilderGBA.exe
├── *.dll, *.json
└── config/
    ├── data/         ← Available for packaging
    ├── translate/    ← Available for packaging
    └── patch2/       ← Available for packaging
```

### Packaging Script Logic

The script `scripts/create-split-packages.ps1` uses:
```powershell
-BinPath "."  # Expects files in current directory
```

**FULL Package:**
```powershell
7z a FEBuilderGBA_FULL_*.7z `
    FEBuilderGBA.exe `
    *.dll *.json `
    config\*              # ← Includes all three subdirectories
```

**CORE Package:**
```powershell
# Copy config but remove patch2
Copy-Item -Path "config" -Destination $tempDir -Recurse
Remove-Item -Path "$tempDir\config\patch2" -Recurse -Force

7z a FEBuilderGBA_CORE_*.7z `
    FEBuilderGBA.exe `
    *.dll *.json `
    "$tempDir\config\*"   # ← Includes data/ and translate/ only
```

**PATCH2 Package:**
```powershell
7z a FEBuilderGBA_PATCH2_*.7z `
    config\patch2\*       # ← Includes patch2/ only
```

---

## Impact Analysis

### Before Fix

**FULL Package:**
- ✅ Contains: exe, dll, json, config/patch2/
- ❌ Missing: config/data/, config/translate/

**CORE Package:**
- ✅ Contains: exe, dll, json
- ❌ Missing: config/data/, config/translate/

**PATCH2 Package:**
- ✅ Contains: config/patch2/
- ❌ Missing: Nothing (correct)

**Problems:**
1. Users downloading FULL package could not access game data definitions or translations
2. Users downloading CORE package had no data or translation files
3. Application would fail to load or have missing features

### After Fix

**FULL Package:**
- ✅ Contains: exe, dll, json, config/data/, config/translate/, config/patch2/
- ✅ Fully functional standalone package

**CORE Package:**
- ✅ Contains: exe, dll, json, config/data/, config/translate/
- ✅ Users can update patches separately via PATCH2 package

**PATCH2 Package:**
- ✅ Contains: config/patch2/
- ✅ Can be extracted to existing installation to update patches

**Benefits:**
1. ✅ All packages contain necessary files for their intended purpose
2. ✅ Split package update system works as designed
3. ✅ CORE package size increased appropriately (900KB → 1.63MB)
4. ✅ Users can update patches independently without re-downloading application

---

## Timeline

| Time | Action | Result |
|------|--------|--------|
| 07:41 | User: Add data/ and translate/ to build output | ✅ Success (406b20ee) |
| 08:45 | First fix: Add config/ move to Post Build | ❌ Failed (c00a3472) |
| 08:45-08:49 | CI/CD run failed with config exists error | Identified root cause |
| 08:51 | Second fix: Remove existing config/ before move | ✅ Success (96da36a1) |
| 08:51-08:58 | CI/CD run completed successfully | All packages created |
| 08:58 | Verification: Downloaded artifacts, checked contents | ✅ All verified |

**Total Resolution Time:** ~20 minutes (from issue report to verified fix)

---

## Related Commits

1. **137afab5** - Add comprehensive CI/CD status report
2. **d75302e2** - Document build output directory structure
3. **406b20ee** - Add data/ and translate/ to build output config structure
4. **c00a3472** - Fix CI/CD: Move config/ directory for packaging (failed)
5. **96da36a1** - Fix Post Build: Remove submodule config/ before moving build output (success)

---

## Lessons Learned

### Issue: Directory Conflict

**Problem:** Repository structure has `config/` at root (submodule), and build output also has `config/` directory.

**Solution:** Remove the submodule directory before moving the build output config/, since:
- Submodule is read-only source data
- Build output config/ contains all necessary files (including copy of submodule)
- Packaging needs the build output version

### Testing Strategy

**What Worked:**
- ✅ Iterative fix-and-test approach
- ✅ Monitoring CI/CD logs to identify exact failure point
- ✅ Verifying locally before pushing
- ✅ Checking package sizes as indicator of success

**What Helped:**
- Clear error messages from PowerShell
- Package size changes confirmed content inclusion
- Comprehensive CI/CD logs showing each step

---

## Verification Checklist

- [x] Local build includes config/data/
- [x] Local build includes config/translate/
- [x] Local build includes config/patch2/
- [x] All 408 unit tests passing (2 skipped as documented)
- [x] CI/CD build passes
- [x] Post Build step succeeds
- [x] Split packages created successfully
- [x] FULL package size ~5.18 MB (contains all directories)
- [x] CORE package size ~1.63 MB (contains data/ and translate/, no patch2/)
- [x] PATCH2 package size ~3.56 MB (contains patch2/ only)
- [x] Artifacts uploaded successfully
- [x] Documentation updated

---

## Conclusion

✅ **Issue Fully Resolved**

The config directories (data/, translate/, patch2/) are now correctly included in all release packages as expected:

- **FULL package:** Complete standalone distribution with all features
- **CORE package:** Application and data files, users can update patches separately
- **PATCH2 package:** Patch database only, for updating existing installations

The split package update system is now fully functional, allowing users to:
1. Download FULL package for initial installation
2. Update application via CORE package (smaller download, ~1.6MB vs ~5.2MB)
3. Update patches independently via PATCH2 package (~3.6MB)

**All CI/CD tests passing, packages verified, issue closed.** ✅

---

**Related Documentation:**
- `BUILD-OUTPUT-STRUCTURE.md` - Directory structure and build configuration
- `SKIPPED-TESTS-EXPLANATION.md` - Test coverage explanation
- `CI-CD-STATUS.md` - CI/CD status and troubleshooting
- `SPLIT-PACKAGE-FINAL-STATUS.md` - Split package system overview
- `DEPLOYMENT.md` - Package creation and deployment guide
