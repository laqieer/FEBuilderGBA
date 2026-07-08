---
name: daily-maintenance
description: Run the full autonomous daily-maintenance routine for the laqieer/FEBuilderGBA fork end-to-end and unattended — check CI → open PRs → discussions → issues → docs/wiki → release, then loop until zero open issues AND zero open PRs. Activate when the user says "daily maintenance", "run the routine", "maintain the project", "auto-maintain", or when the scheduled maintenance prompt fires. Never ask questions — make the best safe decision and proceed, no matter how long it takes.
---

# Daily Autonomous Maintenance Routine — laqieer/FEBuilderGBA

Run the full routine end-to-end, **unattended**. NEVER ask questions or wait for confirmation — make the best safe decision and proceed, no matter how long it takes. Finish with a concise summary.

> This skill pairs with **`dev-flow`** (the enforced development workflow) and `DEVELOPMENT-WORKFLOW.md`. Any code change made during the routine MUST go through `dev-flow`.

## Guardrails (mandatory, every run)

**(a) Target the fork only.** EVERY `gh`/GitHub op targets `-R laqieer/FEBuilderGBA` — NEVER the upstream `FEBuilderGBA/FEBuilderGBA`. When in doubt, inspect `git remote -v` and confirm before any read/write.

**(b) Footer on every GitHub post.** End EVERY GitHub post (issue/PR/discussion body, review, comment, commit message) with a 2-line footer:
```
Copilot CLI: <version>
Model: <display-name> (<model-id>)
```
Use the **running session's** version (the value injected in the system prompt at launch), NOT `copilot --version` — the latter reflects the on-disk install, which can update mid-session while the session keeps running its launch version.

**(c) Identity.** Commit as `laqieer <laqieer@126.com>`; always `git push` immediately after commit. Include the `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` trailer.

**(d) Worktree isolation.** Use an isolated worktree for ANY code change — never `git checkout`/`switch`/`stash`/`reset` in the main worktree (other sessions share it). In the main worktree only `git fetch origin` is allowed. Prune all worktrees after every merge.

**(e) Review Gate (developer-aware).** Follow `DEVELOPMENT-WORKFLOW.md`; activate the `dev-flow` skill BEFORE any code change. For Copilot-CLI-authored code the Review Gate is the **in-session cross-model board** (`claude-opus-4.8`, `gpt-5.5`, `gemini-3.5-flash`): launch one `task` sub-agent per model — the `code-review` agent type for PR reviews, the `rubber-duck` agent type for plan/design reviews (both are valid `task` agent types) — then synthesize and post the consolidated `## Cross-Model Review Board` review via `gh`. Never `agency cc` / Claude Code. Instruct `gemini` to verify any claimed test/build failure on a clean PR-head build (it has produced stale-build false positives).

**(f) Pre-Commit Checklist.** Tests + docs for every code change. For `feat`/`fix` GUI PRs (Avalonia/WinForms), capture a **real** GUI screenshot — the automation-free path is `dotnet run --project FEBuilderGBA.Avalonia -- --rom roms/FE8U.gba --screenshot-all` (headless `RenderTargetBitmap` yields 0-byte files here); or `PrintWindow`/`tools/WinCapture`. NEVER fabricate images; NEVER use feature-branch URLs (master raw URLs only). Screenshots are OPTIONAL for `docs`/`chore` PRs.

**(g) Clear ALL feedback before merge.** Check ALL THREE channels — unresolved inline review threads (incl. the `copilot-pull-request-reviewer` bot), PR-level conversation comments, and top-level review bodies. Fix every point (never just "acknowledged"), reply, and resolve threads. The repo ruleset blocks merge unless: CI green, branch up-to-date with base (`gh pr update-branch -R laqieer/FEBuilderGBA` after other merges land), AND all conversations resolved.

**(h) Post-merge CI check (keep master green).** After EVERY merge to `master` (a PR merge OR a release tag), re-check master CI once it settles — never assume green. Use the Step 1 recipe: a **required**-check failure is a regression to fix immediately; an **advisory / `continue-on-error`** failure (e.g. the known-flaky `Android Boot Smoke`) that is a confirmed infra flake just gets re-run (`gh run rerun <run-id> --failed`).

## Steps

1. **CI HEALTH CHECK (run FIRST — keep master green is a top priority).** Before anything else, inspect the latest CI on `master` and act on any real failure:
   ```bash
   # per-check conclusions on the current master tip (advisory continue-on-error jobs
   # surface here as a failed check-run even though the workflow RUN is "success")
   gh api repos/laqieer/FEBuilderGBA/commits/master/check-runs \
     --jq '.check_runs[] | select(.conclusion=="failure") | .name'
   ```
   - **Required checks** (`build`, `build (ubuntu-latest|macos-latest|windows-latest)`, `Build wasm AppBundle`, `Cross-Platform Build`, `MSBuild`, `Check`, `Deploy to GitHub Pages`, `e2e / E2E FE6|FE7J|FE7U|FE8J|FE8U`, `Android Emulator Parity`) failing = a **real regression**. Open a tracking issue (`gh issue create -R laqieer/FEBuilderGBA`) capturing the failing check + a log snippet (`gh run view <run-id> --job <job-id> --log-failed`), then FIX it via the full dev workflow (Step 4) — it is the **highest-priority** item this run. Never start a release while master is red.
   - **Advisory / `continue-on-error` checks** (names containing `advisory`, plus the known-flaky `Android Boot Smoke`) can fail on infra flakes — e.g. a corrupt Android SDK emulator-image download (`Error on ZipFile unknown archive` → `could not connect to TCP port 5554`) — WITHOUT failing the workflow run. Read the failing step (`--log-failed`): if it is infra (image download / emulator boot timeout / adb), NOT an app crash / `FATAL EXCEPTION`, just **re-run** it (`gh run rerun <run-id> --failed`) and confirm it goes green. Do not file an issue for a confirmed flake.

2. **OPEN PRs.** For each open PR: run the cross-model Review Gate; post the consolidated comment. Then:
   - Ready + no concerns → **merge** (`gh pr merge <N> -R laqieer/FEBuilderGBA --merge --delete-branch`), then **check post-merge CI** per guardrail (h).
   - Not acceptable → **close** with a reason posted as a comment.
   - Needs author work → **leave open** with actionable feedback.
   - `maintainerCanModify` + small fix → **edit the branch** then merge.

3. **DISCUSSIONS.** Review all open discussions + NEW replies. Reply where useful; check Google Docs / image links in them. Create issues to track surfaced bugfixes / feature requests. **DO NOT write code here.** Close old discussions with no follow-up needed.

4. **ISSUES.** Resolve + reply EVERY open issue **ONE BY ONE** via the full dev workflow: plan → cross-model Review Gate on the plan → worktree → implement + tests → PR → cross-model Review Gate on the PR → clear all three feedback channels → merge → **check post-merge CI** per guardrail (h). Prioritize issues that break core tooling, and any CI-regression issue filed in Step 1. This can take many cycles — do NOT stop early, do NOT ask.

5. **DOCS / WIKI.** Update `README.md`, `docs/`, and the wiki whenever code or behavior changed. (Release notes auto-generate from conventional-commit subjects — see `CHANGELOG.md`; no hand-editing needed.)

6. **RELEASE.** Cut a release when warranted via the tag-triggered pipeline.
   - **Pre-release CI gate (do this LAST before tagging).** Re-run the Step 1 CI Health Check on the up-to-date master tip. If ANY required check is red, STOP and fix it first — never release from a red master. Only tag once master is green.
   - From up-to-date master:
   ```bash
   git fetch origin master
   TAG="ver_$(date +%Y%m%d.%H)"          # scheme: ver_YYYYMMDD.HH
   git tag "$TAG" origin/master          # tag the up-to-date master tip (no working-tree checkout)
   git push origin "$TAG"                # fires .github/workflows/release.yml
   ```
   **Decide the version number yourself — never ask.** The workflow builds all platforms (WinForms, CLI ×3 RIDs, Avalonia ×3 RIDs, Android APK) and publishes the GitHub Release with auto-generated grouped notes. The Gitee mirror was removed (#1766) — GitHub is the sole release source; there is no Gitee sync to run. Hold a release when the accumulated change since the last tag is a single same-day chore.
   - **Verify the release CI + artifacts (do NOT declare the release done until this passes).** Watch `release.yml` for the tag to completion, confirm the run `conclusion == success`, then confirm the GitHub Release actually published the FULL expected artifact set:
   ```bash
   gh run list -R laqieer/FEBuilderGBA --workflow release.yml --limit 3 \
     --json headBranch,status,conclusion,databaseId    # find the run for <TAG>
   gh release view "$TAG" -R laqieer/FEBuilderGBA --json isDraft,assets \
     --jq '{isDraft, assetCount:(.assets|length), assets:[.assets[].name]}'
   ```
   Expect `isDraft=false` and the full platform set — WinForms zip, CLI ×3 RIDs, Avalonia ×3 RIDs, Android APK (≈9 assets). If the release run FAILED or any expected artifact is missing, investigate the failing job (`gh run view <run-id> --job <job-id> --log-failed`) and fix / re-run (`gh run rerun <run-id> --failed`) before considering the release complete.

## Mandatory completion loop

After finishing the current issue list, **RE-SCAN** `gh issue list -R laqieer/FEBuilderGBA --state open` AND `gh pr list -R laqieer/FEBuilderGBA --state open`. New issues/PRs may have appeared during processing. Keep resolving until **BOTH open issues AND open PRs are zero**. Only then report done. Never declare completion while any open issue/PR remains.

## Hygiene at the end of every run

- Verify the main worktree is on `master` and clean; prune all implementation + review worktrees (`git worktree remove --force` + `git worktree prune`).
- Confirm `0` open issues / `0` open PRs.
- **Confirm master CI is green** (re-run the Step 1 CI Health Check on the final master tip; a red required check means an unfinished regression — do not declare done).
- Post a concise summary of what was merged/closed/created and the release decision.
