# Full-Suite Release Runbook

End-to-end runbook for cutting a FEBuilderGBA release across **all four ship targets**:

| Target | What ships | Built by | Runtime |
|--------|-----------|----------|---------|
| **WinForms** | `FEBuilderGBA.exe` + DLLs + `config/` + `tools/bin/` (bundled ColorzCore) | [`msbuild.yml`](../.github/workflows/msbuild.yml) | Windows (x86) |
| **CLI** | `cli-{rid}` self-contained bundle (+ bundled ColorzCore) | [`crossplatform.yml`](../.github/workflows/crossplatform.yml) | Linux / macOS / Windows |
| **Avalonia desktop** | `avalonia-{rid}` self-contained bundle (+ bundled ColorzCore) | [`crossplatform.yml`](../.github/workflows/crossplatform.yml) | Linux / macOS / Windows |
| **Android APK** | `*-Signed.apk` (debug keystore — see caveat) | [`android.yml`](../.github/workflows/android.yml) | Android |

> **What's live today:** the release process is **manual** (Section 4). No workflow has a `tags:` trigger yet, and nothing in-repo runs `gh release create` — so pushing a `ver_*` tag does **not** currently create a GitHub release. The automated tag-triggered flow is tracked by **[#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629)** (Section 5) and is **not yet merged**. Treat Section 4 as the source of truth until #1629 lands.
>
> This runbook is part of the release-readiness umbrella **[#1628](https://github.com/laqieer/FEBuilderGBA/issues/1628)**. For the WinForms split-package update system see [DEPLOYMENT.md](DEPLOYMENT.md) (and its **Status** caveat below).

---

## 1. Version & tag scheme

Releases are tagged **`ver_YYYYMMDD.NN`**:

- `YYYYMMDD` — the build date (UTC).
- `NN` — a two-digit sequence within that day, starting at `00` (or the hour of the build — both forms appear in history). Increment for each additional release on the same day.

Recent tags: `ver_20260204.22`, `ver_20250926.15`, `ver_20250423.22`. List the latest with:

```bash
git tag --sort=-creatordate | head -5
```

The WinForms **core version** is derived from the assembly build date (`YYYYMMDD.HH`); the in-app updater compares it against the release tag. The patch database (`config/patch2/`) is versioned independently via `config/patch2/version.txt` — see [DEPLOYMENT.md → Versioning Strategy](DEPLOYMENT.md#versioning-strategy).

---

## 2. CI artifact inventory (what each workflow produces)

Every push to `master` runs the build workflows and uploads artifacts via `actions/upload-artifact@v4` (≈90-day expiry, login-gated — these are **not** release assets until you attach them). Download from the **[Actions](https://github.com/laqieer/FEBuilderGBA/actions)** tab → the relevant workflow run → **Artifacts**.

| Workflow | Artifact name | Contents |
|----------|---------------|----------|
| `msbuild.yml` | `FEBuilderGBA_{build_time}` | `FEBuilderGBA.exe`, `*.dll`, `*.json`, `config/` (with empty `patch2/{FE6,FE7J,FE7U,FE8J,FE8U}` placeholders — patch2 ships via git), `tools/bin/` (self-contained ColorzCore + `lyn.exe`), `README*.md` |
| `crossplatform.yml` | `cli-linux-x64`, `cli-osx-arm64`, `cli-win-x64` | Self-contained CLI per RID + `tools/bin/` ColorzCore |
| `crossplatform.yml` | `avalonia-linux-x64`, `avalonia-osx-arm64`, `avalonia-win-x64` | Self-contained Avalonia desktop per RID + `tools/bin/` ColorzCore |
| `android.yml` | `febuildergba-android-apk` | `*-Signed.apk` (debug keystore) — **advisory / non-required** check (`android-build`), so a flaky Android build can never block a merge |

**RIDs published:** `linux-x64`, `osx-arm64`, `win-x64`.

> **Android signing caveat:** `android.yml` produces a **debug-keystore**-signed APK only. A production-signed (release) APK/AAB is **not** yet produced — tracked by [#1631](https://github.com/laqieer/FEBuilderGBA/issues/1631). Do not publish the debug APK as a trusted production download without flagging it.

> **patch2 in cross-platform bundles:** the `cli-{rid}` / `avalonia-{rid}` bundles do **not** embed the `config/patch2/` patch database, so the Patch Manager is empty in those builds until the user fetches patch2 via git. Tracked by [#1630](https://github.com/laqieer/FEBuilderGBA/issues/1630).

---

## 3. Building the cross-platform bundles locally

If you want to produce the CLI/Avalonia bundles without waiting for CI (e.g. for a manual release), use the helper:

```bash
# Requires the tool submodules:
git submodule update --init config/patch2 tools/Event-Assembler tools/ColorzCore

# Build all three RIDs (or pass a subset):
./scripts/publish-all.sh                      # linux-x64 osx-arm64 win-x64
./scripts/publish-all.sh linux-x64

# Output: publish/cli-{rid}/, publish/avalonia-{rid}/, and per-RID .tar.gz archives
```

For the WinForms package, build the solution (`Release`/`x86`) then stage with **`release.ps1`** (Section 4.1).

---

## 4. Manual release (the live path)

This is the process to follow **today**. It produces a GitHub release with all-platform assets, which then auto-mirrors to Gitee (Section 6).

### 4.1 Stage the WinForms package — `release.ps1`

After building the solution in `Release`/`x86`:

```powershell
# From repo root. Stages exe + DLLs + config + docs into .\release\
pwsh ./release.ps1                      # default -OutputDir release
pwsh ./release.ps1 -OutputDir dist -Clean
```

`release.ps1`:
- Stages `FEBuilderGBA.exe`, `*.dll`, `*.json`, the **Release** `config/` (falling back to the repo-root `config/`), and the docs `*.md`.
- Creates empty `config/etc` and `config/log` staging folders.
- If `publish/cli-*` / `publish/avalonia-*` bundles exist (from Section 3), stages them alongside too — so a single run can mirror the full-suite asset set.
- Is **idempotent** and **parameterized** (`-OutputDir`, `-WinFormsBinDir`, `-ConfigDir`, `-Clean`).

A no-build smoke test covers the staging behavior:

```powershell
pwsh -NoProfile -File scripts/release.test.ps1
```

### 4.2 Collect every platform asset

Gather one zip per platform (download CI artifacts from Section 2, or build locally):

- `FEBuilderGBA_{ver}.zip` — the WinForms package (from `release.ps1` staging, zipped).
- `cli-linux-x64.zip`, `cli-osx-arm64.zip`, `cli-win-x64.zip`.
- `avalonia-linux-x64.zip`, `avalonia-osx-arm64.zip`, `avalonia-win-x64.zip`.
- `FEBuilderGBA-android-Signed.apk` (debug-signed — see caveat in Section 2).

### 4.3 Create the GitHub release

```bash
VER="ver_$(date -u +%Y%m%d).00"      # e.g. ver_20260227.00 — bump NN as needed

git tag "$VER"
git push origin "$VER"

gh release create "$VER" -R laqieer/FEBuilderGBA \
  --title "$VER" \
  --notes "Release $VER — full-suite (WinForms + CLI + Avalonia + Android)." \
  FEBuilderGBA_${VER}.zip \
  cli-linux-x64.zip cli-osx-arm64.zip cli-win-x64.zip \
  avalonia-linux-x64.zip avalonia-osx-arm64.zip avalonia-win-x64.zip \
  FEBuilderGBA-android-Signed.apk
```

Publishing the release (`release: published`) auto-fires the Gitee sync (Section 6).

### 4.4 Verify

```bash
gh release view "$VER" -R laqieer/FEBuilderGBA --json assets --jq '.assets[].name'
```

Confirm every platform download is listed and downloadable, then check the [Gitee release](https://gitee.com/laqieer/FEBuilderGBA/releases/latest) mirrored the same asset set.

---

## 5. Automated release (planned — #1629)

> **Status: not merged.** Until [#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629) lands, use the manual path in Section 4.

The intended automation: a workflow triggered on `push: tags: ['ver_*']` that

1. builds/collects the WinForms package, the per-RID `cli-{rid}` and `avalonia-{rid}` bundles, and the Android APK,
2. runs `gh release create` / `softprops/action-gh-release` and attaches all of them as release assets (one zip per platform),
3. thereby auto-fires the existing Gitee sync (Section 6).

When #1629 merges, the release flow collapses to: **bump the tag → push it → CI does the rest.** This document should be updated then to make Section 5 the source of truth and demote Section 4 to "manual fallback".

---

## 6. Gitee sync (already wired — document only)

[`sync-release-to-gitee.yml`](../.github/workflows/sync-release-to-gitee.yml) triggers automatically on **`release: published`** (and is manually re-runnable via `workflow_dispatch`). It uses `H-TWINKLE/sync-action` with the `gitee_token` secret to mirror the GitHub release — title, notes, and **all attached assets** — to [`gitee.com/laqieer/FEBuilderGBA`](https://gitee.com/laqieer/FEBuilderGBA/releases/latest), the mirror for users in mainland China.

No action is required to invoke it: creating the GitHub release in Section 4.3 (or Section 5, once live) fires it. If a sync fails (e.g. transient upload error), re-run it from the Actions tab via **Run workflow** (`workflow_dispatch`).

---

## 7. Pre-release checklist

- [ ] `master` is green on `msbuild.yml`, `crossplatform.yml`, and `check.yml`.
- [ ] Tag name follows `ver_YYYYMMDD.NN` and does not already exist.
- [ ] `config/patch2/version.txt` is current if patches changed (see [DEPLOYMENT.md](DEPLOYMENT.md#patch2-version-management)).
- [ ] All four platform assets collected (WinForms zip, 3× CLI, 3× Avalonia, Android APK).
- [ ] `LICENSE` + `THIRD-PARTY-NOTICES.md` present inside every artifact (the WinForms zip via `release.ps1`, the CLI/Avalonia bundles via `crossplatform.yml` — GPLv3 compliance, [#1633](https://github.com/laqieer/FEBuilderGBA/issues/1633)).
- [ ] Release notes / changelog drafted (changelog automation tracked by [#1632](https://github.com/laqieer/FEBuilderGBA/issues/1632)).
- [ ] `gh release create` run with every asset attached.
- [ ] GitHub release verified, then Gitee mirror verified.

### Known release-readiness gaps (umbrella #1628)

These do **not** block a manual WinForms release, but flag them in release notes when relevant:

| Gap | Issue |
|-----|-------|
| Tag-triggered release workflow (automate Sections 4–5) | [#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629) |
| patch2 not bundled into CLI/Avalonia artifacts | [#1630](https://github.com/laqieer/FEBuilderGBA/issues/1630) |
| Release-signed (non-debug) Android APK/AAB | [#1631](https://github.com/laqieer/FEBuilderGBA/issues/1631) |
| Changelog / release-notes generation | [#1632](https://github.com/laqieer/FEBuilderGBA/issues/1632) |
| Code-sign / notarize Windows + macOS artifacts | [#1634](https://github.com/laqieer/FEBuilderGBA/issues/1634) |

---

## 8. Rollback

If a release has critical issues, mark it as not-latest and ship a hotfix:

```bash
gh release edit "$VER" -R laqieer/FEBuilderGBA --prerelease        # demote from "latest"
# then cut a fixed release with a new tag (ver_YYYYMMDD.NN+1)
```

See [DEPLOYMENT.md → Rollback Procedure](DEPLOYMENT.md#rollback-procedure) for the split-package specifics.

---

## References

- [DEPLOYMENT.md](DEPLOYMENT.md) — WinForms split-package (FULL/CORE/PATCH2) update system.

  > **Status:** the split-package `.7z` generator (`scripts/create-split-packages.ps1`) and a `split-packages_{buildTime}` CI artifact described in DEPLOYMENT.md are **not present in the current tree** — `msbuild.yml` uploads only the single `FEBuilderGBA_{build_time}` artifact (Section 2). Treat the split-package flow in DEPLOYMENT.md as the design of the in-app updater, not a step you can run today. The live artifact set is the one in Section 2.
- Workflows: [`msbuild.yml`](../.github/workflows/msbuild.yml) · [`crossplatform.yml`](../.github/workflows/crossplatform.yml) · [`android.yml`](../.github/workflows/android.yml) · [`sync-release-to-gitee.yml`](../.github/workflows/sync-release-to-gitee.yml)
- Scripts: [`release.ps1`](../release.ps1) (WinForms staging) · [`scripts/publish-all.sh`](../scripts/publish-all.sh) (CLI/Avalonia bundles) · [`scripts/release.test.ps1`](../scripts/release.test.ps1) (release.ps1 smoke test)
- [docs/ANDROID.md](ANDROID.md) — Android build & signing details.
- Umbrella: [#1628](https://github.com/laqieer/FEBuilderGBA/issues/1628) (release readiness) · automation: [#1629](https://github.com/laqieer/FEBuilderGBA/issues/1629)
