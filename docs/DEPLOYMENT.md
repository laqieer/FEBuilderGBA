# Deployment Guide: Core Artifact + In-App Patch2 Git Updater

> **For the full-suite release flow** (WinForms + CLI + Avalonia + Android + iOS), see **[RELEASE.md](RELEASE.md)** — that runbook is the entry point for cutting a release. This guide covers the **core application artifact** built by CI and the **two-track update model** that delivers patch data separately.
>
> **Code-signing / notarization** of the Windows exe and macOS bundles is conditional and secret-gated — see **[RELEASE.md → §6.1 Code-signing & notarization](RELEASE.md#61-code-signing--notarization-1634)** for the required GitHub Actions secrets and the unsigned-artifact SmartScreen/Gatekeeper workaround. (Windows Authenticode signing is wired into the build job below.)

This guide explains how the application is packaged and how its updates reach users.

## The two-track update model

FEBuilderGBA keeps the **application** and the **patch database (patch2)** on independent update tracks:

| Component | What it contains | How it updates |
|-----------|-----------------|----------------|
| **Core** | `FEBuilderGBA.exe`, DLLs, `config/data`, `config/translate`, bundled `tools/bin/` | Download `FEBuilderGBA_YYYYMMDD.HH.zip` from a GitHub Release or from nightly.link, then unpack over the install |
| **Patch2** | ~44,000 patch files in `config/patch2/` | `git clone` / `git fetch` + `git reset --hard` via the in-app Git updater |

The previous FULL / CORE / PATCH2 `.7z` split-package release system (and its `scripts/create-split-packages.ps1` generator + `config/patch2/version.txt` version file) **has been removed**. There is now a **single core artifact** and patch2 is delivered over Git — not as a release package.

## Prerequisites

- Push access to the repository
- GitHub CLI (`gh`) installed (optional, for automation)

## The core artifact

CI builds exactly **one** application artifact. There is no separate exe/patch package split.

### Built by the "MSBuild" workflow

[`.github/workflows/msbuild.yml`](../.github/workflows/msbuild.yml) (display name **MSBuild**) runs on every push to `master`:

1. Checks out the repo and inits the build-required submodules (`config/patch2`, `tools/Event-Assembler`, `tools/ColorzCore`).
2. Builds the solution with MSBuild (`Configuration=Release`, `Platform=x86`) and publishes a self-contained `win-x86` ColorzCore.
3. Optionally Authenticode-signs `FEBuilderGBA.exe` (only when both `WINDOWS_CERT_BASE64` and `WINDOWS_CERT_PASSWORD` secrets are present; absent in this fork's CI ⇒ unsigned build, unchanged).
4. Runs the test suite, generates a coverage report, and uploads test/coverage artifacts.
5. **Post Build** step stages the deliverable:
   - moves `FEBuilderGBA.exe`, the `*.dll`s and `*.json`s, and the `config/` directory (data + translate) to the workspace root;
   - **strips `config/patch2/`** to five empty version subdirs (`FE6`, `FE7J`, `FE7U`, `FE8J`, `FE8U`) so the app still starts but ships no patch payload — users fetch patch2 over Git;
   - copies the self-contained ColorzCore into `tools/bin/` (and `lyn.exe` if present).
6. **Upload Core Artifact** publishes a single artifact named `FEBuilderGBA_{build_time}` (where `build_time` = `YYYYMMDD.HH`) containing:

   ```
   *.exe  *.dll  *.json  config/  tools/bin/  README*.md  LICENSE  THIRD-PARTY-NOTICES.md
   ```

There is **no** `split-packages_{buildTime}` artifact and no three-package `.7z` set.

### Downloading the CI artifact

1. Go to [Actions](https://github.com/laqieer/FEBuilderGBA/actions).
2. Click on the latest successful **MSBuild** workflow run.
3. Scroll to the **Artifacts** section.
4. Download `FEBuilderGBA_{build_time}` and extract it.

For an always-current direct link the in-app updater uses
`https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master`, which exposes the latest run's
`FEBuilderGBA_YYYYMMDD.HH.zip`.

## Creating a release

### Option 0: Automated tag-triggered release (recommended)

The [`.github/workflows/release.yml`](../.github/workflows/release.yml) workflow (#1629) creates the GitHub Release
and attaches **every platform package** automatically when you push a `ver_*`
tag (the project's tag convention, `ver_YYYYMMDD.HH`):

```bash
# From an up-to-date master:
TAG="ver_$(date +%Y%m%d.%H)"
git tag "$TAG"
git push origin "$TAG"
```

The release body is **auto-generated** — no hand-typing. A workflow step runs
[`scripts/generate-changelog.sh`](../scripts/generate-changelog.sh) (#1632) to
build type-grouped notes (🚀 Features / 🐛 Bug Fixes / 📖 Documentation / 🤖 CI
/ 🧰 Maintenance / 🔧 Other) from the conventional-commit subjects between the
previous `ver_*` tag and the new one, and passes it as the release body;
GitHub's native auto-notes (grouped by PR label via
[`.github/release.yml`](../.github/release.yml)) are appended after it. The
full backlog log lives in the top-level [`CHANGELOG.md`](../CHANGELOG.md).

Pushing the tag runs four jobs and uploads each platform package as a zipped
release asset:

| Asset                                | Built by job     | Mirrors workflow      |
|--------------------------------------|------------------|-----------------------|
| `FEBuilderGBA_<tag>.zip`             | `winforms`       | `msbuild.yml`         |
| `FEBuilderGBA-cli-<rid>.zip`         | `crossplatform`  | `crossplatform.yml`   |
| `FEBuilderGBA-avalonia-<rid>.zip`    | `crossplatform`  | `crossplatform.yml`   |
| `FEBuilderGBA-android-apk.zip`       | `android`        | `android.yml`         |
| `FEBuilderGBA-ios-unsigned-ipa.zip`  | `ios` (soft)     | `ios.yml`             |

(`<rid>` = `linux-x64`, `osx-arm64`, `win-x64`.)

The WinForms/CLI/Avalonia/Android build jobs are **required** — if any of those platform
builds fails, the release is **not** published (no partial release missing a required
platform download), and a preflight step verifies every expected zip is present before
publishing. The **iOS** job (#1859) is deliberately **soft/advisory** during the preview:
it is `continue-on-error` and is **not** in the mandatory verify-assets list, so its
`FEBuilderGBA-ios-unsigned-ipa.zip` is attached when the macOS build succeeds and simply
absent otherwise — a fragile iOS build degrades to "release without iOS" rather than
blocking the other platforms. Promote it to required once the iOS build is proven stable.

**Dry run:** triggering the workflow via `workflow_dispatch` (the "Run workflow"
button) runs the build jobs only and does **not** create a release — the
release-creation step is gated on `refs/tags/ver_*`. To exercise the full path
and populate the release page, push a real test `ver_*` tag.

### Option 1: Manual release with GitHub CLI

The WinForms desktop asset is the `FEBuilderGBA_YYYYMMDD.HH.zip` core artifact described above (download it
from the MSBuild run, or zip a clean `Release` output yourself). Attach it — and, if you also built them, the
per-RID CLI/Avalonia bundles and the Android APK — to a `ver_*`-tagged release:

```bash
BUILD_TIME=$(date +%Y%m%d.%H)
TAG="ver_${BUILD_TIME}"   # the project's ver_YYYYMMDD.HH convention

# Generate type-grouped release notes from conventional commits (#1632)
scripts/generate-changelog.sh > notes.md
# ...or an explicit range:
# scripts/generate-changelog.sh ver_20260204.22 ver_20260601.00 > notes.md

gh release create "$TAG" \
  --title "Build $BUILD_TIME" \
  --notes-file notes.md \
  FEBuilderGBA_${BUILD_TIME}.zip
```

> Prefer Option 0 (push the `ver_*` tag) whenever you want the full platform set — it builds and attaches every
> platform package and runs the same `generate-changelog.sh`.

### Verify the release

- Check that the core `FEBuilderGBA_*.zip` (and any per-platform assets) are downloadable.
- Confirm the in-app **Check for Updates** flow detects the new core version (see below).

## How clients update

The in-app updater is split across two cooperating pieces — one for the core app, one for patch2.

### Core application

[`FEBuilderGBA/UpdateCheckSplitPackage.cs`](../FEBuilderGBA/UpdateCheckSplitPackage.cs) checks for a newer core
build, from either source:

- **nightly.link** — scrapes `https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master` for
  `FEBuilderGBA_(\d{8}\.\d{2})\.zip` (`CheckSplitPackageUpdateByNightlyLink`).
- **GitHub Releases** — reads `releases/latest` for a `FEBuilderGBA_(\d{8}\.\d{2})\.(7z|zip)` asset
  (`CheckSplitPackageUpdateByGitHub`).

The remote version (`YYYYMMDD.HH`) is compared against the local assembly build date in
[`FEBuilderGBA.Core/UpdateInfo.cs`](../FEBuilderGBA.Core/UpdateInfo.cs). `UpdateInfo.PackageType` has only three
values — `Unknown`, `CoreOnly`, and `None` — there is no FULL/PATCH2 package type. `DetermineUpdateType`
returns `CoreOnly` when the remote is newer, otherwise `None` ("already up to date"). The user then downloads and
unpacks the single core `.zip`.

### Patch2 (Git)

Patch data is updated independently via Git, driven by
[`FEBuilderGBA.Core/GitUtil.cs`](../FEBuilderGBA.Core/GitUtil.cs):

- **First install / missing `config/patch2/`:** `GitUtil.Clone` runs `git clone --progress --depth=1 <url> <path>`.
- **Subsequent updates:** `GitUtil.Update` runs `git fetch --progress --depth=1 origin` followed by
  `git reset --hard FETCH_HEAD` (it can also `git remote set-url origin` first to switch to a custom remote).

The patch2 remote is returned by `GitUtil.GetPatch2RemoteUrl()`: it defaults to
`github.com/laqieer/FEBuilderGBA-patch2`, unless a custom `submodule_patch2_url`
override is set in `config.xml`, in which case that value is used instead.

In the UI this is the **Welcome screen** update button ("Update FEBuilderGBA to the Latest Version"), which opens the update
dialog ([`FEBuilderGBA/ToolUpdateDialogForm.cs`](../FEBuilderGBA/ToolUpdateDialogForm.cs)): a dedicated
**Git Patch2** button performs the clone/update. Since #1816 that button is reachable even when the
core app is already up-to-date (as long as `config/patch2/` is empty) and is shown even when Git is
absent — clicking it offers to auto-install Git first. A fresh install keeps the five empty
`config/patch2/{FE6,FE7J,FE7U,FE8J,FE8U}` stub directories so startup still succeeds, and the Patch
Manager shows a "patch database not downloaded yet" notice rather than an error (#1811). Avalonia has
no equivalent in-app flow yet ([#1817](https://github.com/laqieer/FEBuilderGBA/issues/1817)).

See **[README → 🔄 Update System / Updating Patch2 via Git](../README.md#-update-system)** for the user-facing summary.

## Versioning

### Core version (application)

- **Format:** `YYYYMMDD.HH` (e.g., `20260226.14`)
- **Source:** build date/time baked into the assembly (`U.getVersion()`)
- **Compared by:** `UpdateInfo.CompareVersions` (numeric compare); `IsValidVersion` enforces `^\d{8}\.\d{2}$`

### Patch2 version (patch database)

Patch2 is versioned **by its Git history**, not by a `version.txt` file. To read the installed patch2 version:

```bash
git -C config/patch2 log -1 --format="%h %s"
```

## Testing a release

### Before publishing

1. **Download the core artifact** from the MSBuild run (or build a clean `Release` output).
2. **Verify contents** — the app, the config data/translate, the bundled tools, and the empty patch2 stubs:

   ```bash
   unzip -l FEBuilderGBA_*.zip | grep "FEBuilderGBA.exe"
   unzip -l FEBuilderGBA_*.zip | grep "config/data/"
   unzip -l FEBuilderGBA_*.zip | grep "tools/bin/"
   # patch2 should ship as empty version dirs only (no patch payload)
   unzip -l FEBuilderGBA_*.zip | grep "config/patch2/"
   ```

3. **Test extraction & launch:**

   ```bash
   mkdir test_extract && cd test_extract
   unzip ../FEBuilderGBA_*.zip
   ./FEBuilderGBA.exe --version
   ```

### After publishing

1. **Test the core update flow:**
   - Install an older build.
   - Run **Tools → Check for Updates**.
   - Confirm the app reports a `CoreOnly` update and the download URL points at the new core `.zip`.
2. **Test the patch2 Git flow:**
   - On a clean install (no `config/patch2/` content), click the **Git Patch2** button and confirm the clone
     populates the patch database.
   - On an existing install, re-run it and confirm `git fetch` + `reset --hard` updates in place.
3. **Verify URLs:**

   ```bash
   gh release view "$TAG" --json assets
   ```

## Troubleshooting

### Core update not detected

**Problem:** "Check for Updates" reports up-to-date when a newer build exists.

**Debug:**
1. Confirm the release/nightly.link asset is named `FEBuilderGBA_YYYYMMDD.HH.zip` (the regex in
   `UpdateCheckSplitPackage` requires that exact `\d{8}\.\d{2}` pattern).
2. Compare the remote `YYYYMMDD.HH` against the local assembly build date — `DetermineUpdateType` only flags an
   update when the remote is strictly newer.
3. Review logs from `UpdateCheckSplitPackage` / `ToolUpdateDialogForm`.

### Patch2 Git update fails

**Problem:** The Git Patch2 button errors or does nothing.

**Solutions:**
- Confirm Git is installed and discoverable — `GitUtil.FindGitExecutable()` checks the configured path, then
  `git` on `PATH`, then common Windows install locations. Since #1816 the button stays visible even when
  Git is absent and offers to **auto-install Git** on click (rather than hiding the entry).
- Check network access to the patch2 remote (`github.com/laqieer/FEBuilderGBA-patch2`, or a custom `submodule_patch2_url`).
- The patch2 repo is public; `GIT_TERMINAL_PROMPT=0` is set, so a credential prompt would surface as a failure
  rather than a hang — re-run with a clean `config/patch2/` to force a fresh `--depth=1` clone.

## Rollback procedure

If a release has critical issues:

1. **Mark the release as pre-release** (not latest):

   ```bash
   gh release edit "$TAG" --prerelease
   ```

2. **Publish a hotfix release** from the previous working build (push a new `ver_*` tag, or attach the prior core
   `.zip`).
3. **Fix issues** and cut a new release when ready.

## Best practices

1. ✅ **Always test locally** before releasing.
2. ✅ **Generate the changelog** from conventional commits — never hand-type release notes.
3. ✅ **Keep patch2 on its own Git track** — never re-bundle it into the core artifact.
4. ✅ **Monitor the first 24 hours** after release.
5. ✅ **Document breaking changes** prominently.
6. ✅ **Tag releases** with the `ver_YYYYMMDD.HH` convention.

## Commit & PR Title Convention

To keep generated changelogs reliable, commit subjects and pull-request titles
follow the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>(<optional scope>): <subject>
```

**Allowed types:** `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`,
`refactor`, `revert`, `style`, `test`.

**Scopes** are free-form and optional (e.g. `avalonia`, `core`, `cli`,
`gap-sweep`).

Examples:

```
feat(avalonia): add World Map path-move editor
fix(core): guard EOF in LDR pointer rescan
docs: document the commit/PR-title convention
ci: lint PR titles and commit messages
```

This is enforced **in CI only** — there are no local git hooks, so contributors
are never blocked offline. The `.github/workflows/pr-title-lint.yml` workflow
runs on every pull request and has two jobs:

- **`pr-title`** validates the PR title with
  [`amannn/action-semantic-pull-request`](https://github.com/amannn/action-semantic-pull-request)
  — this covers **squash merges**, where the PR title becomes the commit subject.
- **`commits`** validates every commit in the PR range with
  [`wagoid/commitlint-github-action`](https://github.com/wagoid/commitlint-github-action),
  driven by `commitlint.config.mjs` at the repo root — this covers **merge** and
  **rebase** merges, where individual commit subjects land on `master`.

The allowed type list in `commitlint.config.mjs` and the `types:` input of the
`pr-title` job are kept in sync. A clean, conventional-commit `master` history is
the prerequisite for the auto-changelog generation tracked by #1632.

## Automation Roadmap

Future improvements for CI/CD:

- [x] Auto-create GitHub release on pushed `ver_*` tag (`.github/workflows/release.yml`, #1629)
- [ ] Auto-create GitHub release on successful master build
- [x] Auto-generate changelog from commits — `scripts/generate-changelog.sh`
      builds type-grouped notes from conventional-commit subjects; wired into
      `.github/workflows/release.yml` (release body) and seeded into
      [`CHANGELOG.md`](../CHANGELOG.md). The conventional-commit prerequisite is
      enforced in CI (see [Commit & PR Title Convention](#commit--pr-title-convention)
      and `.github/workflows/pr-title-lint.yml`). (#1632)
- [ ] Auto-test the core artifact before publishing
- [ ] CDN upload for faster downloads
- [ ] Update notification system
- [ ] Beta/nightly channel support

## Android release signing

The Android head (`FEBuilderGBA.Android`) is built by a separate workflow,
`.github/workflows/android.yml` (advisory / non-required — see
[docs/ANDROID.md §7](ANDROID.md#7-build-status-in-this-environment)). It produces
a **release-signed APK + AAB** *only when* the maintainer adds a release keystore
to the repository's GitHub Actions secrets; otherwise it falls back to the
debug-keystore `*-Signed.apk` (the fork's current CI), so the existing check is
unchanged.

Required secrets (set ALL four together, or none):

| Secret | Meaning |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | base64 of the release keystore — Linux `base64 -w0 release.keystore`, macOS/BSD `base64 -i release.keystore` (`-w0` is GNU-only) |
| `ANDROID_KEY_ALIAS` | the key alias inside the keystore |
| `ANDROID_KEYSTORE_PASSWORD` | the keystore (store) password |
| `ANDROID_KEY_PASSWORD` | the key password |

Add them at **Settings → Secrets and variables → Actions → New repository secret**.
A partial set fails the workflow fast with a clear error. The `.aab` is the
artifact Google Play requires; the signed APK is for direct sideload. Attaching
the signed artifact to a GitHub release is handled by the tag-triggered release
workflow ([#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629)). No
production signing key is committed to the repo.

## iOS release signing

The iOS head (`FEBuilderGBA.iOS`) is built by a separate workflow,
`.github/workflows/ios.yml` (advisory / non-required — see [docs/IOS.md](IOS.md)). It
produces a **release-signed `.ipa`** *only when* the maintainer adds Apple signing
material to the repository's GitHub Actions secrets; otherwise it falls back to an
**unsigned `.ipa`** (the fork's current CI), which is **not directly installable** — it is
for downstream re-signing / sideloading (AltStore, Sideloadly, Apple Configurator).

Required secrets (set ALL four together, or none):

| Secret | Meaning |
| --- | --- |
| `APPLE_CERTIFICATE_BASE64` | base64 of the signing certificate `.p12` — `base64 -i cert.p12` |
| `APPLE_CERTIFICATE_PASSWORD` | the `.p12` password |
| `APPLE_PROVISIONING_PROFILE_BASE64` | base64 of the `.mobileprovision` profile |
| `APPLE_CODESIGN_IDENTITY` | the codesign identity (e.g. `Apple Distribution: …`) |

Add them at **Settings → Secrets and variables → Actions → New repository secret**. A
partial set fails the workflow fast with a clear error. App Store / TestFlight distribution
needs a paid Apple Developer account. Attaching the artifact to a GitHub release is the
soft `ios` job in the tag-triggered release workflow (#1859). No production signing key is
committed to the repo.

## References

- CI/CD Workflows: `.github/workflows/msbuild.yml` (core artifact), `.github/workflows/crossplatform.yml`, `.github/workflows/android.yml`, `.github/workflows/ios.yml`
- Tag-triggered Release Workflow: `.github/workflows/release.yml` (#1629)
- Changelog Generator: `scripts/generate-changelog.sh` + `CHANGELOG.md` + `.github/release.yml` (#1632)
- Core update check: `FEBuilderGBA/UpdateCheckSplitPackage.cs` + `FEBuilderGBA.Core/UpdateInfo.cs`
- Patch2 Git updater: `FEBuilderGBA.Core/GitUtil.cs` + `FEBuilderGBA/ToolUpdateDialogForm.cs`
- Android build + signing: `.github/workflows/android.yml`, [docs/ANDROID.md §7](ANDROID.md#7-build-status-in-this-environment)
- iOS build + signing: `.github/workflows/ios.yml`, [docs/IOS.md](IOS.md)

---

**Last Updated:** 2026-06-28
**Maintainer:** FEBuilderGBA Development Team
