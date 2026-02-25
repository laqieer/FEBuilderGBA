#!/bin/bash
# Phase 0: Git Submodule Migration Script
# Extracts patch2/ directory into a separate repository and adds as submodule
#
# IMPORTANT: This script will:
# 1. Create a new branch for safety
# 2. Extract patch2/ history to a new repository
# 3. Add the new repository as a submodule
# 4. Remove old patch2/ files from main repository
#
# Prerequisites:
# - Clean working directory (commit all changes first)
# - GitHub repository created: laqieer/FEBuilderGBA-patch2
# - GitHub credentials configured

set -e  # Exit on error

# Configuration
MAIN_REPO="$(git rev-parse --show-toplevel)"
PATCH2_REPO_NAME="FEBuilderGBA-patch2"
PATCH2_REPO_URL="https://github.com/laqieer/${PATCH2_REPO_NAME}.git"
PATCH2_PATH_DEBUG="FEBuilderGBA/bin/Debug/config/patch2"
PATCH2_PATH_RELEASE="FEBuilderGBA/bin/Release/config/patch2"
TEMP_DIR="${MAIN_REPO}/../${PATCH2_REPO_NAME}-temp"
NEW_REPO="${MAIN_REPO}/../${PATCH2_REPO_NAME}"

echo "============================================"
echo "Phase 0: Git Submodule Migration"
echo "============================================"
echo ""
echo "This script will:"
echo "1. Create backup branch: backup-before-submodule"
echo "2. Extract patch2/ history to new repository"
echo "3. Create FEBuilderGBA-patch2 repository"
echo "4. Add patch2 as submodule at config/patch2"
echo "5. Remove old patch2/ directories from main repo"
echo "6. Update build scripts to copy from submodule"
echo ""
read -p "Continue? (y/n) " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Aborted."
    exit 1
fi

cd "$MAIN_REPO"

# Step 1: Check for clean working directory
echo ""
echo "[1/8] Checking working directory..."
if ! git diff-index --quiet HEAD --; then
    echo "ERROR: Working directory is not clean. Commit or stash changes first."
    git status --short
    exit 1
fi
echo "✓ Working directory is clean"

# Step 2: Create backup branch
echo ""
echo "[2/8] Creating backup branch..."
git branch backup-before-submodule 2>/dev/null || echo "Backup branch already exists"
echo "✓ Backup branch created: backup-before-submodule"

# Step 3: Extract patch2 history using git filter-branch
echo ""
echo "[3/8] Extracting patch2 history (this may take several minutes)..."
echo "Creating temporary repository..."

# Clone main repo to temp location
git clone "$MAIN_REPO" "$TEMP_DIR"
cd "$TEMP_DIR"

# Extract only patch2 paths using filter-branch
echo "Filtering patch2 files..."
git filter-branch --prune-empty --subdirectory-filter "${PATCH2_PATH_DEBUG}" HEAD || \
git filter-branch --prune-empty --subdirectory-filter "${PATCH2_PATH_RELEASE}" HEAD

# Clean up and reorganize to root-level structure
echo "Reorganizing directory structure..."
# The filter-branch should have moved patch2 contents to root
# We need to restore the FE6/, FE7J/, FE7U/, FE8J/, FE8U/ structure

# Check current structure
if [ -d "FE6" ] || [ -d "FE7J" ] || [ -d "FE7U" ] || [ -d "FE8J" ] || [ -d "FE8U" ]; then
    echo "✓ Patch directories at root level"
else
    echo "ERROR: Expected patch directories not found after filter-branch"
    ls -la
    exit 1
fi

echo "✓ patch2 history extracted"

# Step 4: Create new repository
echo ""
echo "[4/8] Creating new FEBuilderGBA-patch2 repository..."
rm -rf "$NEW_REPO"
mkdir -p "$NEW_REPO"
cd "$NEW_REPO"
git init

# Copy extracted files
echo "Copying patch2 files..."
cp -r "$TEMP_DIR"/* .

# Add README
cat > README.md << 'EOF'
# FEBuilderGBA-patch2

This repository contains patches for FEBuilderGBA, separated from the main repository
for independent versioning and faster updates.

## Structure

- `FE6/` - Fire Emblem 6 (Binding Blade) patches
- `FE7J/` - Fire Emblem 7 (Blazing Blade) Japanese patches
- `FE7U/` - Fire Emblem 7 US/International patches
- `FE8J/` - Fire Emblem 8 (Sacred Stones) Japanese patches
- `FE8U/` - Fire Emblem 8 US/International patches

## Version Tracking

The version is tracked in `version.txt` in the format `YYYYMMDD.HH`.

## Usage

This repository is included in the main FEBuilderGBA repository as a git submodule:

```bash
git submodule update --init --recursive
```

## Contributing

To contribute patches:
1. Fork this repository
2. Add your patch to the appropriate game directory
3. Follow the existing patch structure (PATCH_*.txt descriptor file + assets)
4. Submit a pull request

## License

Patches are provided as-is under various licenses. Check individual patch directories
for specific license information.
EOF

# Create version.txt
date +"%Y%m%d.%H" > version.txt

# Commit
git add .
git commit -m "Initial commit: Extract patch2 from main FEBuilderGBA repository"

echo "✓ New repository created at: $NEW_REPO"

# Step 5: Push to GitHub (manual step - requires repository to exist)
echo ""
echo "[5/8] Ready to push to GitHub..."
echo ""
echo "MANUAL STEP REQUIRED:"
echo "1. Create GitHub repository at: https://github.com/laqieer/${PATCH2_REPO_NAME}"
echo "2. Set it to public"
echo "3. Do NOT initialize with README (we already have one)"
echo ""
read -p "Press Enter once repository is created on GitHub..."

# Add remote and push
git remote add origin "$PATCH2_REPO_URL"
git branch -M master
git push -u origin master

echo "✓ Pushed to GitHub"

# Step 6: Add submodule to main repository
echo ""
echo "[6/8] Adding submodule to main repository..."
cd "$MAIN_REPO"

# Create submodule directory structure
mkdir -p config
git submodule add "$PATCH2_REPO_URL" config/patch2

echo "✓ Submodule added"

# Step 7: Remove old patch2 directories
echo ""
echo "[7/8] Removing old patch2 directories from main repository..."
git rm -rf "${PATCH2_PATH_DEBUG}"
git rm -rf "${PATCH2_PATH_RELEASE}"

# Update .gitignore to ignore build output config directories
echo "" >> .gitignore
echo "# Build output config directories (copied from submodule)" >> .gitignore
echo "FEBuilderGBA/bin/Debug/config/" >> .gitignore
echo "FEBuilderGBA/bin/Release/config/" >> .gitignore

git add .gitignore

echo "✓ Old directories removed"

# Step 8: Update build configuration
echo ""
echo "[8/8] Updating build configuration..."

# Create post-build script to copy from submodule
cat > scripts/copy-config-submodule.ps1 << 'EOF'
# Post-build script to copy config/patch2 submodule to build output directories
param(
    [string]$TargetDir,
    [string]$ProjectDir
)

$submodulePath = Join-Path (Split-Path $ProjectDir -Parent) "config\patch2"
$debugConfigPath = Join-Path $TargetDir "config\patch2"

Write-Host "Copying patch2 submodule from: $submodulePath"
Write-Host "To build output: $debugConfigPath"

# Create target directory
New-Item -ItemType Directory -Force -Path $debugConfigPath | Out-Null

# Copy submodule contents
Copy-Item -Path "$submodulePath\*" -Destination $debugConfigPath -Recurse -Force

Write-Host "✓ Patch2 submodule copied to build output"
EOF

echo "✓ Build scripts updated"

# Commit the migration
git add -A
git commit -m "Migrate patch2/ to git submodule

- Extract patch2 to separate repository: laqieer/FEBuilderGBA-patch2
- Add as submodule at config/patch2
- Remove old bin/Debug/config/patch2 and bin/Release/config/patch2
- Update .gitignore for build output directories
- Add post-build script to copy submodule to build output

This enables independent versioning and faster updates for patches
while keeping the main FEBuilderGBA repository focused on code.

Ref: DESIGN-split-package-updates.md Phase 0"

echo ""
echo "============================================"
echo "✓ Phase 0 Migration Complete!"
echo "============================================"
echo ""
echo "Summary:"
echo "- Backup branch: backup-before-submodule"
echo "- New repository: $PATCH2_REPO_URL"
echo "- Submodule path: config/patch2"
echo "- Commit: $(git rev-parse --short HEAD)"
echo ""
echo "Next steps:"
echo "1. Test build: msbuild FEBuilderGBA.sln /p:Configuration=Release"
echo "2. Verify patch2 files copied to bin/Release/config/patch2/"
echo "3. Test runtime patch loading"
echo "4. If successful, push to main repository:"
echo "   git push origin master"
echo ""
echo "To rollback if needed:"
echo "   git reset --hard backup-before-submodule"
echo "   git submodule deinit -f config/patch2"
echo "   rm -rf config/patch2"
echo ""

# Cleanup
echo "Cleaning up temporary files..."
rm -rf "$TEMP_DIR"

echo "Done!"
