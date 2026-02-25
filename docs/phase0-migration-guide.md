# Phase 0: Git Submodule Migration Guide

## Overview

This guide covers the migration of `patch2/` directories to a separate git submodule repository. This is Phase 0 of the split package update system implementation.

## Why Submodule?

**Problems solved:**
- **Independent versioning**: Patches can be updated without rebuilding the main application
- **Faster updates**: Users can update only patches (43,950 files) without re-downloading the entire application
- **Cleaner repository**: Main repository focuses on code, patch repository focuses on game data
- **Easier contributions**: Patch contributors don't need to fork the entire codebase

**Current state:**
- 43,950 patch files in `FEBuilderGBA/bin/Debug/config/patch2/`
- Patches mixed with code in main repository
- Every patch update requires full application rebuild and redistribution

**Target state:**
- Separate repository: `laqieer/FEBuilderGBA-patch2`
- Main repository includes patches as submodule at `config/patch2`
- Build process copies from submodule to output directories
- Independent version tracking for patches

## Prerequisites

Before running the migration:

1. **Clean working directory**
   ```bash
   git status
   # Should show "nothing to commit, working tree clean"
   ```
   If not, commit or stash your changes first.

2. **Create GitHub repository**
   - Go to https://github.com/new
   - Repository name: `FEBuilderGBA-patch2`
   - Description: "Patches for FEBuilderGBA - separated for independent updates"
   - Visibility: **Public**
   - **Do NOT** initialize with README, .gitignore, or license
   - Click "Create repository"

3. **Backup your work**
   The script creates a backup branch, but it's good practice to have an additional backup:
   ```bash
   git tag backup-$(date +%Y%m%d-%H%M%S)
   ```

## Migration Process

### Option 1: Automatic (Recommended)

Run the migration script:

**Windows (Git Bash):**
```bash
cd /c/Users/zhiwenzhu/source/repos/laqieer/FEBuilderGBA
bash scripts/phase0-submodule-migration.sh
```

**Windows (Command Prompt):**
```cmd
cd C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA
scripts\phase0-submodule-migration.bat
```

**Linux/Mac:**
```bash
cd ~/source/repos/laqieer/FEBuilderGBA
bash scripts/phase0-submodule-migration.sh
```

The script will:
1. Create backup branch: `backup-before-submodule`
2. Extract patch2 history to temporary repository
3. Create new `FEBuilderGBA-patch2` repository
4. Prompt you to create GitHub repository (if not done)
5. Push to GitHub
6. Add submodule to main repository
7. Remove old patch2 directories
8. Update build scripts
9. Commit the migration

### Option 2: Manual Step-by-Step

If the automatic script fails, follow these manual steps:

#### Step 1: Create Backup
```bash
git branch backup-before-submodule
git tag backup-manual-$(date +%Y%m%d-%H%M%S)
```

#### Step 2: Extract Patch2 History
```bash
# Clone to temporary directory
cd ..
git clone FEBuilderGBA FEBuilderGBA-patch2-temp
cd FEBuilderGBA-patch2-temp

# Extract only patch2 directory with history
git filter-branch --prune-empty \
  --subdirectory-filter FEBuilderGBA/bin/Debug/config/patch2 HEAD

# Verify structure (should see FE6/, FE7J/, etc. at root)
ls -la
```

#### Step 3: Create New Repository
```bash
cd ..
mkdir FEBuilderGBA-patch2
cd FEBuilderGBA-patch2
git init

# Copy extracted files
cp -r ../FEBuilderGBA-patch2-temp/* .

# Add README
cat > README.md << 'EOF'
# FEBuilderGBA-patch2

Patches for FEBuilderGBA, separated from main repository for independent updates.

## Structure
- FE6/ - Fire Emblem 6 patches
- FE7J/ - Fire Emblem 7 Japanese patches
- FE7U/ - Fire Emblem 7 US patches
- FE8J/ - Fire Emblem 8 Japanese patches
- FE8U/ - Fire Emblem 8 US patches
EOF

# Create version file
date +"%Y%m%d.%H" > version.txt

# Commit
git add .
git commit -m "Initial commit: Extract patch2 from main repository"
```

#### Step 4: Push to GitHub
```bash
# Add remote (replace with your actual repository URL)
git remote add origin https://github.com/laqieer/FEBuilderGBA-patch2.git
git branch -M master
git push -u origin master
```

#### Step 5: Add Submodule to Main Repository
```bash
cd ../FEBuilderGBA
mkdir -p config
git submodule add https://github.com/laqieer/FEBuilderGBA-patch2.git config/patch2
```

#### Step 6: Remove Old Patch Directories
```bash
git rm -rf FEBuilderGBA/bin/Debug/config/patch2
git rm -rf FEBuilderGBA/bin/Release/config/patch2
```

#### Step 7: Update .gitignore
```bash
cat >> .gitignore << 'EOF'

# Build output config directories (copied from submodule)
FEBuilderGBA/bin/Debug/config/
FEBuilderGBA/bin/Release/config/
EOF

git add .gitignore
```

#### Step 8: Create Build Script
Create `scripts/copy-config-submodule.ps1` - see script content in migration.sh

#### Step 9: Update FEBuilderGBA.csproj
Add post-build event (see next section)

#### Step 10: Commit Migration
```bash
git add -A
git commit -m "Migrate patch2/ to git submodule"
git push origin master
```

## Post-Build Configuration

The build system needs to copy the submodule contents to the output directory.

### Update FEBuilderGBA.csproj

Add this target to `FEBuilderGBA/FEBuilderGBA.csproj` before the closing `</Project>` tag:

```xml
<Target Name="CopyConfigSubmodule" AfterTargets="Build">
  <Message Text="Copying config/patch2 submodule to output directory..." Importance="high" />

  <!-- Define source and target paths -->
  <PropertyGroup>
    <SubmodulePath>$(MSBuildProjectDirectory)\..\config\patch2</SubmodulePath>
    <TargetConfigPath>$(OutDir)config\patch2</TargetConfigPath>
  </PropertyGroup>

  <!-- Create target directory -->
  <MakeDir Directories="$(TargetConfigPath)" />

  <!-- Copy all files from submodule -->
  <ItemGroup>
    <SubmoduleFiles Include="$(SubmodulePath)\**\*.*" />
  </ItemGroup>

  <Copy SourceFiles="@(SubmoduleFiles)"
        DestinationFolder="$(TargetConfigPath)\%(RecursiveDir)"
        SkipUnchangedFiles="true" />

  <Message Text="✓ Config submodule copied to: $(TargetConfigPath)" Importance="high" />
</Target>
```

## Testing the Migration

After migration completes:

### 1. Clone Test
Test that a fresh clone works:
```bash
cd /tmp
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git test-clone
cd test-clone
ls -la config/patch2/FE8U/  # Should show patch files
```

### 2. Build Test
```bash
cd /c/Users/zhiwenzhu/source/repos/laqieer/FEBuilderGBA
msbuild /m /p:Configuration=Release /p:Platform=x86 FEBuilderGBA.sln
```

Verify patches copied:
```bash
ls FEBuilderGBA/bin/Release/config/patch2/FE8U/  # Should show patch files
```

### 3. Runtime Test
```bash
./FEBuilderGBA/bin/Release/FEBuilderGBA.exe
# Open a ROM
# Navigate to Tools > Patches menu
# Verify patches are listed
```

### 4. Update Test
Test updating the submodule:
```bash
cd config/patch2
git pull origin master
cd ../..
git add config/patch2
git commit -m "Update patch2 submodule"
```

## Rollback Procedure

If something goes wrong, rollback to pre-migration state:

```bash
# Reset to backup branch
git reset --hard backup-before-submodule

# Remove submodule
git submodule deinit -f config/patch2
git rm -rf config/patch2
rm -rf .git/modules/config/patch2

# Clean working directory
git clean -fdx

# Rebuild
msbuild /m /p:Configuration=Release FEBuilderGBA.sln
```

## Common Issues

### Issue: "submodule path 'config/patch2' already exists"
**Solution:** Remove existing submodule first:
```bash
git submodule deinit -f config/patch2
git rm -rf config/patch2
rm -rf .git/modules/config/patch2
```

### Issue: "fatal: not a git repository"
**Solution:** Run commands from repository root:
```bash
cd /c/Users/zhiwenzhu/source/repos/laqieer/FEBuilderGBA
```

### Issue: Build doesn't copy patch files
**Solution:** Check post-build event is configured:
```bash
grep -A 20 "CopyConfigSubmodule" FEBuilderGBA/FEBuilderGBA.csproj
```

### Issue: Clone doesn't include submodule
**Solution:** Initialize submodules:
```bash
git submodule update --init --recursive
```

## Next Steps

After successful migration:

1. **Update documentation**: Update README.md with submodule clone instructions
2. **Update CI/CD**: Modify workflows to init submodules
3. **Begin Phase 1**: Implement backend infrastructure for split updates
4. **Test workflow**: Practice updating patches independently

## References

- Design Document: `DESIGN-split-package-updates.md`
- Git Submodules: https://git-scm.com/book/en/v2/Git-Tools-Submodules
- Git Filter-Branch: https://git-scm.com/docs/git-filter-branch

## Timeline

- Preparation: 30 minutes (create GitHub repo, backup)
- Migration: 30-60 minutes (depends on history size)
- Testing: 30 minutes (build, runtime, update tests)
- **Total: ~2 hours**

## Success Criteria

✅ Migration complete when:
- [ ] New repository created at github.com/laqieer/FEBuilderGBA-patch2
- [ ] Submodule added to main repository at `config/patch2`
- [ ] Old patch directories removed from main repository
- [ ] Build copies submodule to output directory
- [ ] Fresh clone works with `git clone --recursive`
- [ ] Build succeeds with patches in output
- [ ] Application loads and lists patches correctly
- [ ] Submodule can be updated independently
