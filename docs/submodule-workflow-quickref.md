# Git Submodule Workflow - Quick Reference

This quick reference covers common git submodule operations for the FEBuilderGBA patch2 separation.

## Initial Clone

### Clone with submodules (recommended)
```bash
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git
cd FEBuilderGBA
```

### Clone without submodules, then init
```bash
git clone https://github.com/laqieer/FEBuilderGBA.git
cd FEBuilderGBA
git submodule update --init --recursive
```

## Checking Submodule Status

```bash
# Show submodule status
git submodule status

# Should output something like:
# a1b2c3d4 config/patch2 (heads/master)

# Show which commit the submodule is at
cd config/patch2
git log -1 --oneline
cd ../..
```

## Updating Submodules

### Update to latest commit
```bash
# Update all submodules to latest on their tracked branches
git submodule update --remote

# Or specifically for patch2:
cd config/patch2
git pull origin master
cd ../..

# Commit the submodule update
git add config/patch2
git commit -m "Update patch2 submodule to latest"
git push
```

### Update to specific commit
```bash
cd config/patch2
git checkout a1b2c3d4  # specific commit hash
cd ../..

git add config/patch2
git commit -m "Update patch2 submodule to commit a1b2c3d4"
git push
```

## Making Changes to Patch2

### Workflow for modifying patches:

1. **Navigate to submodule**
   ```bash
   cd config/patch2
   ```

2. **Make changes**
   ```bash
   # Add a new patch
   mkdir FE8U/MyPatch
   echo "My patch content" > FE8U/MyPatch/PATCH_MyPatch.txt

   # Or modify existing patch
   nano FE8U/ExistingPatch/PATCH_ExistingPatch.txt
   ```

3. **Commit to patch2 repository**
   ```bash
   git add .
   git commit -m "Add MyPatch for FE8U"
   git push origin master
   ```

4. **Return to main repository and update submodule reference**
   ```bash
   cd ../..
   git add config/patch2
   git commit -m "Update patch2: Add MyPatch for FE8U"
   git push origin master
   ```

## Building with Submodule

```bash
# Regular build - submodule copied automatically by post-build event
msbuild /m /p:Configuration=Release /p:Platform=x86 FEBuilderGBA.sln

# Verify patches copied
ls FEBuilderGBA/bin/Release/config/patch2/FE8U/
```

## Troubleshooting

### Submodule is empty after clone
```bash
git submodule update --init --recursive
```

### Submodule shows modified when it isn't
```bash
# Check what Git thinks changed
git diff config/patch2

# If it's just line endings or mode bits:
git config core.fileMode false  # ignore file mode changes
git config core.autocrlf false  # ignore line ending changes
```

### Submodule is in detached HEAD state
```bash
cd config/patch2
git checkout master
git pull
cd ../..
```

### Accidentally committed changes in submodule
```bash
# Undo in submodule
cd config/patch2
git reset --hard HEAD~1  # undo last commit
git push --force-with-lease origin master  # force push (be careful!)
cd ../..

# Update main repo
git submodule update
```

### Want to work on submodule in separate window
```bash
# Clone the patch2 repo separately
cd ~/projects
git clone https://github.com/laqieer/FEBuilderGBA-patch2.git

# Make changes
cd FEBuilderGBA-patch2
# ... do work ...
git commit -am "My changes"
git push

# Update main repo's submodule
cd ~/projects/FEBuilderGBA
cd config/patch2
git pull
cd ../..
git add config/patch2
git commit -m "Update patch2"
git push
```

## Common Commands Summary

| Task | Command |
|------|---------|
| Clone with submodules | `git clone --recursive <url>` |
| Init submodules after clone | `git submodule update --init --recursive` |
| Update submodule to latest | `git submodule update --remote` |
| Check submodule status | `git submodule status` |
| Enter submodule directory | `cd config/patch2` |
| Commit submodule changes | In submodule: `git commit && git push` |
| Update main repo to track submodule change | In main: `git add config/patch2 && git commit` |
| View submodule diff | `git diff --submodule` |
| Reset submodule to tracked commit | `git submodule update --force` |

## CI/CD Integration

GitHub Actions workflows need to init submodules:

```yaml
- name: Checkout code
  uses: actions/checkout@v4
  with:
    submodules: recursive  # Important!

# Or manually:
- name: Checkout code
  uses: actions/checkout@v4

- name: Init submodules
  run: git submodule update --init --recursive
```

## For Contributors

### Contributing patches (patch2 repository):

1. Fork `laqieer/FEBuilderGBA-patch2`
2. Clone your fork
3. Create branch: `git checkout -b my-patch`
4. Add your patch to appropriate game directory (FE6/, FE7U/, etc.)
5. Commit: `git commit -am "Add MyPatch for FE8U"`
6. Push: `git push origin my-patch`
7. Create pull request on GitHub

### Contributing code (main repository):

1. Fork `laqieer/FEBuilderGBA`
2. Clone with submodules: `git clone --recursive <your-fork>`
3. Create branch: `git checkout -b my-feature`
4. Make changes to code
5. Build and test
6. Commit: `git commit -am "Add MyFeature"`
7. Push: `git push origin my-feature`
8. Create pull request on GitHub

**Note:** Don't include submodule updates in code PRs unless you specifically need a patch change.

## Maintenance

### Checking for submodule updates
```bash
# Show if submodule has updates available
cd config/patch2
git fetch
git log HEAD..origin/master --oneline

# If commits are shown, updates are available
cd ../..
```

### Mass update all submodules
```bash
# Update all submodules to latest on their tracked branches
git submodule update --remote --merge

# Commit all submodule updates
git add .
git commit -m "Update all submodules"
git push
```

## References

- [Git Submodules Book](https://git-scm.com/book/en/v2/Git-Tools-Submodules)
- [GitHub Submodules Guide](https://github.blog/2016-02-01-working-with-submodules/)
- Phase 0 Migration Guide: `docs/phase0-migration-guide.md`
- Design Document: `DESIGN-split-package-updates.md`
