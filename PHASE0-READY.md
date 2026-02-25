# Phase 0: Git Submodule Migration - Ready to Execute

## Status: ✅ READY

All Phase 0 preparation is complete. Scripts, documentation, and checklists are ready for execution.

## What's Been Prepared

### 1. Migration Scripts
- **`scripts/phase0-submodule-migration.sh`** - Automated migration script (bash)
- **`scripts/phase0-submodule-migration.bat`** - Windows wrapper for Git Bash

### 2. Build Configuration
- **`scripts/csproj-submodule-build.xml`** - MSBuild target to copy submodule to output

### 3. Documentation
- **`docs/phase0-migration-guide.md`** - Detailed step-by-step guide (21 pages)
- **`docs/submodule-workflow-quickref.md`** - Quick reference for daily submodule usage
- **`docs/PHASE0-CHECKLIST.md`** - Complete checklist with validation steps

### 4. Design Documentation
- **`DESIGN-split-package-updates.md`** - Complete architecture and design (17 pages)

## What This Migration Does

**Current State:**
```
FEBuilderGBA/
├── bin/
│   ├── Debug/config/patch2/     ← 43,950 patch files in main repo
│   └── Release/config/patch2/   ← 43,950 patch files in main repo
```

**After Migration:**
```
FEBuilderGBA/                     ← Main repository (code only)
└── config/patch2/                ← Git submodule → separate repository

FEBuilderGBA-patch2/              ← New separate repository
├── FE6/                          ← Fire Emblem 6 patches
├── FE7J/                         ← Fire Emblem 7 Japanese patches
├── FE7U/                         ← Fire Emblem 7 US patches
├── FE8J/                         ← Fire Emblem 8 Japanese patches
├── FE8U/                         ← Fire Emblem 8 US patches
├── README.md
└── version.txt
```

**Benefits:**
- ✅ Independent versioning for patches vs code
- ✅ Faster updates (download only patches or only code)
- ✅ Cleaner repository separation
- ✅ Enables split package releases (Phase 2)

## Prerequisites Checklist

Before running migration, ensure:

- [ ] **Clean working directory**
  ```bash
  git status  # Should show "nothing to commit"
  ```

- [ ] **GitHub repository created**
  1. Go to: https://github.com/new
  2. Name: `FEBuilderGBA-patch2`
  3. Visibility: Public
  4. **Do NOT** initialize with README
  5. Click "Create repository"

- [ ] **Backup created**
  ```bash
  git tag backup-before-phase0-$(date +%Y%m%d)
  ```

- [ ] **Time allocated**
  - Script runtime: ~10-15 minutes
  - Testing: ~15-30 minutes
  - Total: ~30-45 minutes

## How to Execute

### Recommended: Automatic Migration

```bash
cd C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA

# Review the checklist first
cat docs/PHASE0-CHECKLIST.md

# Run the migration
bash scripts/phase0-submodule-migration.sh
```

The script will:
1. Create backup branch: `backup-before-submodule`
2. Extract patch2/ history to temporary repository
3. Create FEBuilderGBA-patch2 repository
4. Push to GitHub (you'll need to create the repo first)
5. Add submodule to main repository
6. Remove old patch2/ directories
7. Update .gitignore
8. Commit the migration

### Alternative: Manual Migration

If you prefer manual control:

1. Read: `docs/phase0-migration-guide.md`
2. Follow: "Option 2: Manual Step-by-Step" section
3. Complete all 10 steps
4. Validate with checklist

## Post-Migration Steps

After script completes:

### 1. Update FEBuilderGBA.csproj

Add post-build event to copy submodule:

```bash
# Open project file
notepad FEBuilderGBA/FEBuilderGBA.csproj
```

Insert content from `scripts/csproj-submodule-build.xml` before closing `</Project>` tag.

### 2. Test Build

```bash
msbuild /m /p:Configuration=Release /p:Platform=x86 FEBuilderGBA.sln
```

Look for message: "Copying config/patch2 submodule to build output..."

### 3. Verify Output

```bash
ls FEBuilderGBA/bin/Release/config/patch2/FE8U/ | head -20
# Should show patch files
```

### 4. Test Runtime

```bash
./FEBuilderGBA/bin/Release/FEBuilderGBA.exe
# Open ROM → Tools → Patches → Should list patches
```

### 5. Test Fresh Clone

```bash
cd /tmp
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git test-clone
cd test-clone
ls config/patch2/FE8U/
# Should show patches
```

### 6. Update CI/CD

Update GitHub Actions workflows:

**`.github/workflows/msbuild.yml`:**
```yaml
- name: Checkout code
  uses: actions/checkout@v4
  with:
    submodules: recursive  # ← ADD THIS
```

**`.github/workflows/check.yml`:**
```yaml
- name: Checkout code
  uses: actions/checkout@v4
  with:
    submodules: recursive  # ← ADD THIS
```

## Validation Checklist

Phase 0 complete when:

- ✅ Submodule created: `https://github.com/laqieer/FEBuilderGBA-patch2`
- ✅ Main repository updated with submodule at `config/patch2`
- ✅ Old bin/Debug/config/patch2 removed
- ✅ Old bin/Release/config/patch2 removed
- ✅ Build copies submodule to output directory
- ✅ Application loads patches correctly
- ✅ Fresh clone works: `git clone --recursive`
- ✅ CI/CD builds successfully
- ✅ All 43,950 patch files present and functional

Use `docs/PHASE0-CHECKLIST.md` for detailed validation.

## Rollback Procedure

If migration fails:

```bash
# Reset to backup
git reset --hard backup-before-submodule
git clean -fdx

# Remove submodule
git submodule deinit -f config/patch2
git rm -rf config/patch2
rm -rf .git/modules/config/patch2

# Rebuild
msbuild /m /p:Configuration=Release FEBuilderGBA.sln
```

## Timeline Estimate

- **Preparation:** 5 minutes (create GitHub repo, backup)
- **Execution:** 10-15 minutes (script runtime)
- **Testing:** 15-30 minutes (build, runtime, validation)
- **CI/CD Updates:** 10 minutes (workflow files)
- **Documentation:** 10 minutes (README updates)

**Total: 50-70 minutes**

## Support & References

### Documentation
- **Design:** `DESIGN-split-package-updates.md`
- **Full Guide:** `docs/phase0-migration-guide.md`
- **Quick Ref:** `docs/submodule-workflow-quickref.md`
- **Checklist:** `docs/PHASE0-CHECKLIST.md`

### Scripts
- **Migration:** `scripts/phase0-submodule-migration.sh`
- **Windows:** `scripts/phase0-submodule-migration.bat`
- **Build:** `scripts/csproj-submodule-build.xml`

### Git Resources
- [Git Submodules Book](https://git-scm.com/book/en/v2/Git-Tools-Submodules)
- [GitHub Submodules](https://github.blog/2016-02-01-working-with-submodules/)

## What's Next

After Phase 0 is complete and validated:

**Phase 1: Backend Infrastructure**
- Implement `UpdateInfo` class with VERSION_CORE and VERSION_PATCH2
- Add version.txt tracking
- Update version comparison logic
- Prepare for split downloads

See: `DESIGN-split-package-updates.md` Section 11 - Phase 1

---

## Ready to Execute?

1. ✅ Review this document
2. ✅ Check prerequisites (clean repo, GitHub repo created)
3. ✅ Create backup: `git tag backup-before-phase0`
4. ✅ Run script: `bash scripts/phase0-submodule-migration.sh`
5. ✅ Follow post-migration steps above
6. ✅ Validate with checklist: `docs/PHASE0-CHECKLIST.md`

**Current commit:** 84312191 Add Phase 0 submodule migration scripts and documentation

**Last updated:** 2026-02-25

---

## Questions?

If you encounter issues:

1. Check: `docs/phase0-migration-guide.md` - Troubleshooting section
2. Review: Script output for error messages
3. Verify: Prerequisites are met
4. Rollback: Use procedure above if needed
5. Retry: After fixing issues

The migration is safe - backup branch created automatically. You can always rollback.
