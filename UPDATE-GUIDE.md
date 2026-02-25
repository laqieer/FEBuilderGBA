# FEBuilderGBA Update Guide

## 🔄 Automatic Smart Updates

FEBuilderGBA features an intelligent update system that automatically downloads only what's changed, saving you time and bandwidth.

## How to Update

### Simple Method (Recommended)

1. **Open FEBuilderGBA**
2. **Click "Tools" → "Check for Updates"** (or press the update button)
3. **Review what's being updated:**
   - The dialog shows which components have updates
   - Current version vs. latest version displayed
4. **Click the update button** to download and install
5. **Wait for completion:**
   - For patch updates: No restart needed, just refresh
   - For application updates: FEBuilderGBA will restart automatically

That's it! The system handles everything else automatically.

### Manual Download Method

If automatic updates don't work:

1. Go to [Latest Release](https://github.com/laqieer/FEBuilderGBA/releases/latest)
2. Download the appropriate package (see "Which Package to Download" below)
3. Extract the archive
4. **For FULL/CORE packages:** Replace your existing FEBuilderGBA folder
5. **For PATCH2 packages:** Extract into your existing FEBuilderGBA folder (merge/replace files)

## Which Package to Download?

### 🔹 FULL Package (Recommended for new users)
- **Filename:** `FEBuilderGBA_FULL_{version}_{patchVersion}.7z`
- **Size:** ~60-80MB
- **Contains:** Complete application + all patches
- **Use when:**
  - First time installation
  - You missed several updates
  - You want everything fresh

### 🔹 CORE Package (Application updates only)
- **Filename:** `FEBuilderGBA_CORE_{version}.7z`
- **Size:** ~10-20MB
- **Contains:** Application executable and libraries only
- **Use when:**
  - Only the application was updated
  - You have the latest patches already
  - **Automatic updater selects this for you**

### 🔹 PATCH2 Package (Patch database updates only)
- **Filename:** `FEBuilderGBA_PATCH2_{version}.7z`
- **Size:** ~10-20MB
- **Contains:** Patch database only (~44,000 patch files)
- **Use when:**
  - Only patches were added or updated
  - Your application version is up to date
  - **Automatic updater selects this for you**

**💡 Pro Tip:** Let the automatic updater choose for you! It always picks the smallest package needed.

## Understanding Versions

FEBuilderGBA tracks **two separate versions:**

### Core Version (Application)
- **What it is:** The main program (FEBuilderGBA.exe + DLLs)
- **Check it:** "Help" → "About" → Version number
- **Format:** `YYYYMMDD.HH` (e.g., `20260226.14`)
- **Updates when:** Bug fixes, new features, code improvements

### Patch2 Version (Patch Database)
- **What it is:** The patch files database (~44,000 files)
- **Check it:** Look in `config/patch2/version.txt`
- **Format:** `YYYYMMDD.HH` (e.g., `20260226.14`)
- **Updates when:** New patches added, existing patches improved

**Why separate versions?**
Patches update frequently (new hacks, bug fixes) but don't require recompiling the application. This means:
- Faster patch updates (10-20MB instead of 60-80MB)
- No need to restart FEBuilderGBA for patch updates
- Less bandwidth usage overall

## Update Scenarios

### Scenario 1: Only Patches Updated
```
Your version:    Core 20260226.00, Patch2 20260225.00
Latest version:  Core 20260226.00, Patch2 20260226.14

Update downloads: PATCH2 package (~10-20MB)
Restart required: No ✅
Time to complete: ~30 seconds
```

### Scenario 2: Only Application Updated
```
Your version:    Core 20260225.00, Patch2 20260226.00
Latest version:  Core 20260226.14, Patch2 20260226.00

Update downloads: CORE package (~10-20MB)
Restart required: Yes (automatic)
Time to complete: ~1-2 minutes
```

### Scenario 3: Both Updated
```
Your version:    Core 20260225.00, Patch2 20260225.00
Latest version:  Core 20260226.14, Patch2 20260226.14

Update downloads: FULL package (~60-80MB)
Restart required: Yes (automatic)
Time to complete: ~2-5 minutes
```

### Scenario 4: No Update Available
```
Your version:    Core 20260226.14, Patch2 20260226.14
Latest version:  Core 20260226.14, Patch2 20260226.14

Update downloads: Nothing
Message: "You are already running the latest version"
```

## Bandwidth Savings Example

**Old system (single package):**
- Every update: Download 60-80MB
- 10 updates per month: **600-800MB**

**New system (split packages):**
- Patch updates (90% of updates): Download 10-20MB each
- Application updates (10% of updates): Download 10-20MB each
- 10 updates per month: **100-200MB** ⚡ **75% less bandwidth!**

## Troubleshooting

### "Update failed" or "Cannot download"

**Solutions:**
1. **Check internet connection** - Ensure stable internet
2. **Disable antivirus temporarily** - Some AVs block downloads
3. **Use manual download method** - See "Manual Download Method" above
4. **Check GitHub status** - Visit https://www.githubstatus.com/
5. **Try again later** - Temporary network issues may resolve

### "Update installed but version unchanged"

**Patch2 updates:**
- Expected behavior: Version shows in `config/patch2/version.txt`
- Application version stays the same (only patches changed)
- Restart FEBuilderGBA to see changes

**Core updates:**
- Check you closed all FEBuilderGBA windows
- Updater might be waiting - check Task Manager
- Reboot computer if issue persists

### "Wrong package downloaded"

**Cause:** Automatic selection might fallback to FULL package if:
- Split packages unavailable in the release
- Network error during detection
- Version format not recognized

**Solution:** This is safe! FULL package works for all scenarios, just takes longer to download.

### "Extract error" or "Corrupted file"

**Solutions:**
1. **Re-download the package** - File may have downloaded incorrectly
2. **Check available disk space** - Need ~200MB free for extraction
3. **Use 7-Zip** - Download from https://www.7-zip.org/
4. **Check file size** - Compare with release page, should match

## Frequently Asked Questions

### Q: Do I need to download FULL package every time?
**A:** No! The automatic updater selects the smallest package needed. You only need FULL package for:
- First installation
- When you haven't updated in a while
- When both Core and Patch2 need updates

### Q: Can I skip updates?
**A:** Yes, updates are optional. However, we recommend staying updated for:
- Latest patches and features
- Bug fixes
- Security improvements
- Compatibility with new ROM hacks

### Q: Will my ROM edits be affected?
**A:** No, updates only affect the FEBuilderGBA application and patch database. Your ROM files, projects, and edits remain unchanged.

### Q: Can I go back to an older version?
**A:** Yes! Download the older release from [Releases page](https://github.com/laqieer/FEBuilderGBA/releases) and extract over your current installation. Your ROMs and projects are safe.

### Q: Why does patch update not require restart?
**A:** Patch files are loaded when you open the patch menu. Updating patches doesn't change the running application, so no restart is needed. Just re-open the patch menu to see new patches.

### Q: What if automatic update fails?
**A:** The system automatically falls back to showing the download URL. You can:
1. Click "Open in Browser" to download manually
2. Extract the downloaded package
3. Replace your FEBuilderGBA folder

### Q: How much bandwidth do updates use?
**A:** With the split package system:
- Patch-only updates: 10-20MB (most common)
- App-only updates: 10-20MB (rare)
- Full updates: 60-80MB (first install or both changed)

This is **70-90% less bandwidth** than the old single-package system!

## Advanced: Manual Version Check

Want to check versions without opening FEBuilderGBA?

### Check Core Version:
```bash
# Windows CMD
FEBuilderGBA.exe --version

# PowerShell
(Get-Item FEBuilderGBA.exe).VersionInfo.FileVersion
```

### Check Patch2 Version:
```bash
# Windows CMD
type config\patch2\version.txt

# PowerShell
Get-Content config\patch2\version.txt

# Linux/Mac (if running under Wine)
cat config/patch2/version.txt
```

## Need Help?

- **Bug Reports:** [GitHub Issues](https://github.com/laqieer/FEBuilderGBA/issues)
- **Community Support:** [Discord Server](https://discordapp.com/invite/Yzztqqa)
- **Documentation:** [Wiki](https://dw.ngmansion.xyz/doku.php?id=en:guide:febuildergba:index)

---

**Remember:** The update system is designed to be automatic and seamless. Most users never need to think about packages or versions - just click "Update" and let the system handle the rest!

**Last Updated:** 2026-02-26
