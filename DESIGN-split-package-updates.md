# Split Package Update System - Design Document

## 1. Overview

### 1.1 Goals
- Reduce update download size by splitting packages
- Allow users to update program code separately from patches
- Maintain backward compatibility with existing update system
- Preserve complete download option for new users

### 1.2 Current Issues
- Full release package contains ~44,000 files (mostly in patch2/)
- **Slow extraction time** when updating (thousands of files to extract)
- Users must extract entire package even for small code changes
- Patch updates require extracting full program files
- Monolithic repository mixes code and data (patch2/)

### 1.3 Proposed Solution

**Primary Solution: Git Submodule**
- Move `config/patch2/` to separate git repository
- Add as submodule to main repository
- Independent version control and updates
- Cleaner repository separation (code vs data)

**Secondary Solution: Split Packages**
- Create three release packages (full, core, patch2)
- Allow selective updates
- Faster extraction for code-only or patch-only updates

**Combined Approach:**
1. Use git submodule for repository management
2. Build separate packages from submodule structure
3. Update mechanism supports updating main repo or submodule independently

---

## 2. Git Submodule Architecture

### 2.1 Repository Structure

**Current (Monolithic):**
```
laqieer/FEBuilderGBA (single repo)
└── All code + patch2/
```

**Proposed (Submodule):**
```
laqieer/FEBuilderGBA (main repo - code only)
└── config/patch2/ → submodule → laqieer/FEBuilderGBA-patch2

laqieer/FEBuilderGBA-patch2 (separate repo - patches only)
└── FE6/, FE7/, FE8/, FE8U/
```

### 2.2 Submodule Setup

```bash
# 1. Create new repository for patches
cd /path/to/FEBuilderGBA
git clone FEBuilderGBA FEBuilderGBA-patch2-temp
cd FEBuilderGBA-patch2-temp

# 2. Remove everything except config/patch2
git filter-branch --subdirectory-filter config/patch2 --prune-empty -- --all
git remote set-url origin https://github.com/laqieer/FEBuilderGBA-patch2.git
git push -u origin master --force

# 3. Add as submodule to main repository
cd /path/to/FEBuilderGBA
git rm -r config/patch2
git commit -m "Remove patch2 directory (will be replaced by submodule)"
git submodule add https://github.com/laqieer/FEBuilderGBA-patch2.git config/patch2
git commit -m "Add patch2 as submodule"
```

### 2.3 Submodule Updates

**User updating main repo:**
```bash
git pull
# Does NOT automatically update submodule
```

**User updating patch2 only:**
```bash
cd config/patch2
git pull
cd ../..
git add config/patch2
git commit -m "Update patch2 to latest"
```

**User updating both:**
```bash
git pull
git submodule update --remote
```

---

## 3. Current Architecture (Pre-Submodule)

### 2.1 Package Structure
```
FEBuilderGBA/
├── FEBuilderGBA.exe
├── config/
│   ├── data/
│   ├── patch2/          ← ~44K files, majority of package size
│   │   ├── FE6/
│   │   ├── FE7/
│   │   ├── FE8/
│   │   └── FE8U/
│   └── translate/
├── bin/
└── [other files]
```

### 2.2 Current Update Flow
1. User launches FEBuilderGBA
2. Program checks updateinfo.txt for new version
3. If update available, shows ToolUpdateDialogForm
4. User clicks "Auto Update" → downloads full package
5. Extracts to _update/, runs updater.bat to replace files
6. Restarts application

### 2.3 Current updateinfo.txt Format
```
VERSION=20240225
URL=https://github.com/...
CHECKSUM=...
```

---

## 3. Proposed Architecture

### 3.1 New Package Structure

**Version Format:** `YYYYMMDD.HH` (e.g., `20260204.22`)

#### Full Package (for new installations - keep current pattern!)
```
FEBuilderGBA_{VERSION}.zip
└── [everything as current]
```

**Important:** Keep the original filename for backward compatibility. Old clients will continue to work.

#### Core Package (program only)
```
FEBuilderGBA-core_{VERSION}.zip (NEW)
├── FEBuilderGBA.exe
├── config/
│   ├── data/
│   ├── translate/
│   └── [everything EXCEPT patch2/]
├── bin/
└── [other files]
```

#### Patch2 Package (patches only)
```
FEBuilderGBA-patch2_{VERSION}.zip (NEW)
└── config/
    └── patch2/
        ├── FE6/
        ├── FE7/
        ├── FE8/
        └── FE8U/
```

### 3.2 New updateinfo.txt Format
```
VERSION=20240225
VERSION_CORE=20240225
VERSION_PATCH2=20240220

URL_FULL=https://github.com/.../FEBuilderGBA_{VERSION}.zip
URL_CORE=https://github.com/.../FEBuilderGBA-core_{VERSION}.zip
URL_PATCH2=https://github.com/.../FEBuilderGBA-patch2_{VERSION}.zip

CHECKSUM_FULL=...
CHECKSUM_CORE=...
CHECKSUM_PATCH2=...
```

### 3.3 Version Tracking
Store installed versions in config/version.txt:
```
CORE=20240225
PATCH2=20240220
```

---

## 4. UI Changes

### 4.1 Update Dialog Modifications

**Current Layout:**
```
┌─────────────────────────────────┐
│ New version available           │
│                                 │
│ [Auto Update]                   │
│ [Open Browser]                  │
│ [Ignore]                        │
└─────────────────────────────────┘
```

**Proposed Layout:**
```
┌─────────────────────────────────────────┐
│ Update Available                        │
│ Program: v20240225 (you have v20240220) │
│ Patches: v20240220 (up to date)         │
│                                         │
│ [Update All]           ← full or both   │
│ [Update Program Only]  ← core only      │
│ [Update Patches Only]  ← patch2 only    │
│ [Open Browser]                          │
│ [Ignore]                                │
└─────────────────────────────────────────┘
```

### 4.2 Button States
- **Update All**: Always enabled if any update available
- **Update Program Only**: Enabled if VERSION_CORE > installed CORE
- **Update Patches Only**: Enabled if VERSION_PATCH2 > installed PATCH2
- Disable buttons during download/extraction

### 4.3 Progress Display
Show which package is being downloaded:
- "Downloading program update..."
- "Downloading patch updates..."
- "Downloading complete update..."

---

## 5. Backend Changes

### 5.1 Version Check Logic

**File:** `ToolUpdateDialogForm.cs` or version check code

```csharp
public enum UpdateType
{
    None,
    CoreOnly,
    Patch2Only,
    Both,
    Full
}

public class UpdateInfo
{
    public string VersionCore { get; set; }
    public string VersionPatch2 { get; set; }
    public string UrlFull { get; set; }
    public string UrlCore { get; set; }
    public string UrlPatch2 { get; set; }
    public string ChecksumFull { get; set; }
    public string ChecksumCore { get; set; }
    public string ChecksumPatch2 { get; set; }

    public UpdateType GetRequiredUpdate(string installedCore, string installedPatch2)
    {
        bool needCore = CompareVersion(VersionCore, installedCore) > 0;
        bool needPatch2 = CompareVersion(VersionPatch2, installedPatch2) > 0;

        if (needCore && needPatch2) return UpdateType.Both;
        if (needCore) return UpdateType.CoreOnly;
        if (needPatch2) return UpdateType.Patch2Only;
        return UpdateType.None;
    }
}
```

### 5.2 Download Logic

**Update All:**
```csharp
// Option A: Download full package
DownloadAndExtract(updateInfo.UrlFull, "_update");

// Option B: Download both packages
DownloadAndExtract(updateInfo.UrlCore, "_update");
DownloadAndExtract(updateInfo.UrlPatch2, "_update");
```

**Update Program Only:**
```csharp
DownloadAndExtract(updateInfo.UrlCore, "_update");
UpdateVersionFile("CORE", updateInfo.VersionCore);
```

**Update Patches Only:**
```csharp
DownloadAndExtract(updateInfo.UrlPatch2, "_update");
UpdateVersionFile("PATCH2", updateInfo.VersionPatch2);
```

### 5.3 Version File Management

**File:** `config/version.txt`

```csharp
public static void WriteVersionFile()
{
    string versionFile = Path.Combine(Program.BaseDirectory, "config", "version.txt");
    File.WriteAllText(versionFile,
        $"CORE={GetCurrentCoreVersion()}\n" +
        $"PATCH2={GetCurrentPatch2Version()}\n");
}

public static (string core, string patch2) ReadVersionFile()
{
    string versionFile = Path.Combine(Program.BaseDirectory, "config", "version.txt");
    if (!File.Exists(versionFile))
        return (GetProgramVersion(), GetProgramVersion()); // Fallback to exe version

    var lines = File.ReadAllLines(versionFile);
    string core = U.at(lines, "CORE");
    string patch2 = U.at(lines, "PATCH2");
    return (core, patch2);
}
```

---

## 6. Extraction Strategy

### 6.1 Full Package
```
Extract to: _update/
Contents: Everything
Result: Complete replacement
```

### 6.2 Core Package
```
Extract to: _update/
Contents: Everything except patch2/
Merge: Preserve existing _update/config/patch2/ if present
Result: Update program, keep patches
```

### 6.3 Patch2 Package
```
Extract to: _update/
Contents: Only config/patch2/
Merge: Preserve existing _update/ files
Result: Update patches, keep program
```

### 6.4 Merge Logic
```csharp
void ExtractWithMerge(string archiveFile, string targetDir, bool preservePatch2)
{
    // Extract to temp location
    string tempDir = Path.Combine(Path.GetTempPath(), "febuilder_update_" + Guid.NewGuid());
    Extract(archiveFile, tempDir);

    if (preservePatch2 && Directory.Exists(Path.Combine(targetDir, "config/patch2")))
    {
        // Skip patch2 folder during copy
        CopyDirectoryExcept(tempDir, targetDir, "config/patch2");
    }
    else
    {
        // Copy everything
        CopyDirectory(tempDir, targetDir);
    }

    Directory.Delete(tempDir, true);
}
```

---

## 7. Build/Packaging Process

### 7.1 Build Script (PowerShell/Bash)

```powershell
# package-releases.ps1

param(
    [string]$Version = "20240225",
    [string]$BuildDir = "FEBuilderGBA/bin/Release"
)

$OutputDir = "releases/$Version"
New-Item -ItemType Directory -Force -Path $OutputDir

# 1. Create Full Package (keep original name for backward compatibility!)
Write-Host "Creating full package..."
Compress-Archive -Path "$BuildDir/*" `
    -DestinationPath "$OutputDir/FEBuilderGBA_{VERSION}.zip" `
    -Force

# 2. Create Core Package (exclude patch2)
Write-Host "Creating core package..."
$TempCore = "temp_core"
Copy-Item -Path "$BuildDir" -Destination $TempCore -Recurse
Remove-Item -Path "$TempCore/config/patch2" -Recurse -Force
Compress-Archive -Path "$TempCore/*" `
    -DestinationPath "$OutputDir/FEBuilderGBA-core_{VERSION}.zip" `
    -Force
Remove-Item -Path $TempCore -Recurse -Force

# 3. Create Patch2 Package (only patch2)
Write-Host "Creating patch2 package..."
$TempPatch2 = "temp_patch2"
New-Item -ItemType Directory -Force -Path "$TempPatch2/config"
Copy-Item -Path "$BuildDir/config/patch2" `
    -Destination "$TempPatch2/config/patch2" `
    -Recurse
Compress-Archive -Path "$TempPatch2/*" `
    -DestinationPath "$OutputDir/FEBuilderGBA-patch2_{VERSION}.zip" `
    -Force
Remove-Item -Path $TempPatch2 -Recurse -Force

Write-Host "Packages created in $OutputDir"
```

### 7.2 GitHub Release Process
1. Build Release configuration
2. Run packaging script
3. Upload three .zip files to GitHub release
4. Update updateinfo.txt with all three URLs
5. Commit updateinfo.txt to repository

---

## 8. Migration Strategy

### 8.1 Backward Compatibility

**For users with old versions (no version.txt):**
```csharp
if (!File.Exists("config/version.txt"))
{
    // First run after update system change
    // Assume both core and patch2 are at program version
    WriteVersionFile(GetProgramVersion(), GetProgramVersion());
}
```

**For updateinfo.txt without new fields:**
```csharp
if (string.IsNullOrEmpty(updateInfo.UrlCore))
{
    // Old format, fallback to full package only
    ShowSimpleUpdateDialog(updateInfo.UrlFull);
    return;
}
```

### 8.2 Transition Period
- Keep old update dialog code for 1-2 releases
- Log warnings when old format detected
- Eventually remove fallback code

---

## 9. Error Handling

### 9.1 Download Failures
```csharp
try
{
    DownloadPackage(url);
}
catch (WebException ex)
{
    ShowError("Download failed. Please check internet connection.");
    // Offer retry or fallback to browser download
}
```

### 9.2 Extraction Failures
```csharp
try
{
    Extract(archive, target);
}
catch (Exception ex)
{
    ShowError("Extraction failed. File may be corrupted.");
    // Clean up partial extraction
    Directory.Delete(target, true);
}
```

### 9.3 Checksum Validation
```csharp
string downloadedChecksum = CalculateSHA256(downloadedFile);
if (downloadedChecksum != expectedChecksum)
{
    File.Delete(downloadedFile);
    throw new Exception("Checksum mismatch. File may be corrupted.");
}
```

---

## 10. Testing Checklist

### 10.1 Unit Tests
- [ ] Version comparison logic
- [ ] UpdateInfo parsing
- [ ] Version file read/write
- [ ] Update type detection

### 10.2 Integration Tests
- [ ] Download core package
- [ ] Download patch2 package
- [ ] Download full package
- [ ] Extract with merge (preserve patch2)
- [ ] Extract with merge (preserve core)
- [ ] Update version.txt after each operation

### 10.3 UI Tests
- [ ] Button states (enabled/disabled)
- [ ] Progress display for each package type
- [ ] Error dialogs
- [ ] Backward compatibility mode

### 10.4 End-to-End Tests
- [ ] Fresh install → full package
- [ ] Update program only
- [ ] Update patches only
- [ ] Update all
- [ ] Updater.bat execution
- [ ] Application restart

---

## 11. Implementation Phases

### Phase 0: Git Submodule Migration (Week 1)
- [ ] Create FEBuilderGBA-patch2 repository
- [ ] Extract patch2/ history using filter-branch
- [ ] Push to new repository
- [ ] Remove patch2/ from main repository
- [ ] Add patch2 as submodule
- [ ] Update .gitignore and .gitmodules
- [ ] Test clone/build with submodule
- [ ] Document submodule workflow

### Phase 1: Backend Infrastructure (Week 2)
- [ ] Create UpdateInfo class with new fields
- [ ] Implement version.txt read/write
- [ ] Update version comparison logic
- [ ] Add package type detection

### Phase 2: Download Logic (Week 1)
- [ ] Modify download to support multiple URLs
- [ ] Implement merge extraction logic
- [ ] Add checksum validation
- [ ] Test with mock packages

### Phase 3: UI Changes (Week 2)
- [ ] Modify ToolUpdateDialogForm layout
- [ ] Add new buttons (Update Program, Update Patches)
- [ ] Implement button state logic
- [ ] Update progress messages

### Phase 4: Build/Packaging (Week 2)
- [ ] Create packaging script
- [ ] Test package generation
- [ ] Validate package structure
- [ ] Document release process

### Phase 5: Testing & Polish (Week 3)
- [ ] Integration testing
- [ ] Backward compatibility testing
- [ ] Error handling refinement
- [ ] Documentation updates

### Phase 6: Deployment (Week 3)
- [ ] Create test release with all three packages
- [ ] Beta testing
- [ ] Update documentation
- [ ] Public release

---

## 12. File Changes Summary

### Modified Files
- `ToolUpdateDialogForm.cs` - Update logic and button handlers
- `ToolUpdateDialogForm.Designer.cs` - UI layout
- `ToolUpdateDialogForm.resx` - UI resources
- `MainFormUtil.cs` (or wherever version check is) - Version detection
- `.github/workflows/msbuild.yml` - Add packaging step
- `README.md` - Document new update options

### New Files
- `config/version.txt` - Track installed versions
- `package-releases.ps1` (or .sh) - Build packaging script
- `UpdateInfo.cs` - New class for update metadata

---

## 13. Benefits

### For Users
- **Faster updates**: Download only what changed
- **Flexible updates**: Choose what to update
- **Bandwidth savings**: Significant for patch-only updates
- **Better control**: Update patches without risking program changes

### For Developers
- **Cleaner releases**: Separate concerns (code vs data)
- **Easier testing**: Test patches independently
- **Version tracking**: Know exactly what's installed
- **Update analytics**: Track which packages are downloaded

---

## 14. Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| User confusion | Medium | Clear UI labels, help text |
| Partial update failures | High | Atomic operations, rollback |
| Version file corruption | Medium | Fallback to exe version |
| Download interruptions | Low | Resume support or retry |
| Package size miscalculation | Low | Verify in testing |
| Backward compatibility issues | High | Maintain old code path for 2 releases |

---

## 15. Success Metrics

- [ ] Core package < 20% of full package size
- [ ] Patch2 package < 85% of full package size
- [ ] Update download time reduced by 50%+ for code updates
- [ ] Zero reported data loss from split updates
- [ ] 90%+ user adoption of split update system

---

## 16. Future Enhancements

- **Delta updates**: Only download changed files
- **Patch categories**: Update specific game versions (FE6, FE8, etc.)
- **Automatic update**: Background download with prompt
- **Rollback**: Revert to previous version
- **Update schedule**: Check weekly, daily, etc.

---

## Approval

Once this design is approved, implementation can begin following the phased approach in Section 11.

**Estimated Total Development Time:** 3 weeks
**Estimated Testing Time:** 1 week
**Target Release:** TBD
