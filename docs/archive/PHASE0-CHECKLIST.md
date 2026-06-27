# Phase 0 Implementation Checklist

## Pre-Migration

- [ ] **Review design document**
  - Read: `DESIGN-split-package-updates.md`
  - Understand: Git submodule architecture
  - Understand: Why we're doing this (43,950 files, slow extraction)

- [ ] **Create GitHub repository**
  - Go to: https://github.com/new
  - Name: `FEBuilderGBA-patch2`
  - Visibility: **Public**
  - **Do NOT** initialize with README
  - Save the URL: `https://github.com/laqieer/FEBuilderGBA-patch2.git`

- [ ] **Clean working directory**
  ```bash
  cd C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA
  git status
  ```
  - If dirty: Commit or stash changes
  - **Must be clean before proceeding**

- [ ] **Create backup**
  ```bash
  git tag backup-before-phase0-$(date +%Y%m%d-%H%M%S)
  git push --tags
  ```

## Migration (Choose One Method)

### Method A: Automatic (Recommended)

- [ ] **Run migration script**
  ```bash
  cd C:\Users\zhiwenzhu\source\repos\laqieer\FEBuilderGBA
  bash scripts/phase0-submodule-migration.sh
  ```

- [ ] **Follow script prompts**
  - Confirm migration start
  - Wait for extraction (~5-10 minutes for 43,950 files)
  - Confirm GitHub repository created
  - Wait for push to GitHub

- [ ] **Review script output**
  - Check for errors
  - Note the commit hash
  - Verify "Migration Complete" message

### Method B: Manual

- [ ] **Follow manual guide**
  - Read: `docs/phase0-migration-guide.md`
  - Follow: "Option 2: Manual Step-by-Step" section
  - Complete all 10 steps

## Post-Migration

- [ ] **Update FEBuilderGBA.csproj**
  ```bash
  # Open in editor
  notepad FEBuilderGBA/FEBuilderGBA.csproj
  ```
  - Locate the closing `</Project>` tag
  - Insert the content from `scripts/csproj-submodule-build.xml` BEFORE `</Project>`
  - Save and close

- [ ] **Test build**
  ```bash
  msbuild /m /p:Configuration=Release /p:Platform=x86 FEBuilderGBA.sln
  ```
  - Should complete without errors
  - Look for "Copying config/patch2 submodule" message

- [ ] **Verify patch files copied**
  ```bash
  ls FEBuilderGBA/bin/Release/config/patch2/FE8U/ | head -20
  ```
  - Should show patch files
  - Should have ~43,950 files total

- [ ] **Test runtime**
  ```bash
  ./FEBuilderGBA/bin/Release/FEBuilderGBA.exe
  ```
  - Open a FE8U ROM
  - Navigate to: Tools → Patches
  - Verify patches are listed
  - Try installing a patch
  - Verify patch installs successfully

## Verify Submodule

- [ ] **Check submodule status**
  ```bash
  git submodule status
  # Should show: [commit] config/patch2 (heads/master)
  ```

- [ ] **Verify submodule content**
  ```bash
  ls config/patch2/
  # Should show: FE6/ FE7J/ FE7U/ FE8J/ FE8U/ README.md version.txt
  ```

- [ ] **Test fresh clone**
  ```bash
  cd /tmp
  git clone --recursive https://github.com/laqieer/FEBuilderGBA.git test-clone
  cd test-clone
  ls config/patch2/FE8U/
  # Should show patch files
  ```

- [ ] **Test submodule update**
  ```bash
  cd config/patch2
  git pull origin master
  cd ../..
  git add config/patch2
  git commit -m "Test: Update patch2 submodule"
  git reset --soft HEAD~1  # Undo test commit
  ```

## Update CI/CD

- [ ] **Update msbuild.yml workflow**
  - Open: `.github/workflows/msbuild.yml`
  - Find: `actions/checkout@v4` step
  - Add: `submodules: recursive`
  ```yaml
  - name: Checkout code
    uses: actions/checkout@v4
    with:
      submodules: recursive
  ```
  - Commit and push

- [ ] **Update check.yml workflow**
  - Open: `.github/workflows/check.yml`
  - Find: `actions/checkout@v4` step
  - Add: `submodules: recursive`
  - Commit and push

- [ ] **Test CI/CD**
  - Push to GitHub
  - Wait for workflows to run
  - Check that build succeeds
  - Check that patches are available in build artifacts

## Documentation

- [ ] **Update main README.md**
  - Add section: "Cloning"
  ```markdown
  ## Cloning

  This repository uses git submodules for patches. Clone with:

  ```bash
  git clone --recursive https://github.com/laqieer/FEBuilderGBA.git
  ```

  Or if already cloned:

  ```bash
  git submodule update --init --recursive
  ```
  ```

- [ ] **Update CLAUDE.md**
  - Add section about submodule workflow
  - Reference quick reference guide

- [ ] **Create release notes**
  - Document the migration
  - Explain submodule usage
  - Link to guides

## Validation

- [ ] **All tests pass**
  - Build: ✓
  - Runtime: ✓
  - Patch loading: ✓
  - Fresh clone: ✓
  - Submodule update: ✓
  - CI/CD: ✓

- [ ] **No data loss**
  - All 43,950 patch files present
  - All patch history preserved
  - All game versions (FE6, FE7J, FE7U, FE8J, FE8U) work

- [ ] **Performance check**
  - Build time: Reasonable (~same as before)
  - Patch loading time: Same as before
  - Update time: Will test in Phase 2

## Rollback Plan (If Needed)

If something goes wrong:

- [ ] **Reset to backup**
  ```bash
  git reset --hard backup-before-submodule
  git clean -fdx
  git submodule deinit -f config/patch2
  git rm -rf config/patch2
  rm -rf .git/modules/config/patch2
  ```

- [ ] **Rebuild**
  ```bash
  msbuild /m /p:Configuration=Release FEBuilderGBA.sln
  ```

- [ ] **Document issues**
  - What went wrong?
  - Error messages?
  - Steps to reproduce?

## Sign-Off

Phase 0 complete when ALL items checked:

- [ ] Submodule created and pushed to GitHub
- [ ] Main repository updated with submodule
- [ ] Build copies submodule to output
- [ ] Runtime loads patches correctly
- [ ] Fresh clone works
- [ ] CI/CD builds successfully
- [ ] Documentation updated

**Signed off by:** _______________
**Date:** _______________
**Commit:** _______________

---

## Next Phase

Once Phase 0 is complete and signed off:

➡️ **Phase 1: Backend Infrastructure**
- Implement UpdateInfo class
- Add version.txt tracking
- Prepare for split downloads

See: `DESIGN-split-package-updates.md` Section 11 - Phase 1
