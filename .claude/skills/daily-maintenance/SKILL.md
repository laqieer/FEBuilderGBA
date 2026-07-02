---
name: daily-maintenance
description: Run the full autonomous daily-maintenance routine for the laqieer/FEBuilderGBA fork end-to-end and unattended — open PRs → discussions → issues → docs/wiki → release, then loop until zero open issues AND zero open PRs. Activate when the user says "daily maintenance", "run the routine", "maintain the project", "auto-maintain", or when the scheduled maintenance prompt fires. Never ask questions — make the best safe decision and proceed, no matter how long it takes.
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

**(e) Review Gate (developer-aware).** Follow `DEVELOPMENT-WORKFLOW.md`; activate the `dev-flow` skill BEFORE any code change. For Copilot-CLI-authored code the Review Gate is the **in-session cross-model board** (`claude-opus-4.8`, `gpt-5.5`, `gemini-3.5-flash`): launch one reviewer per model (`code-review` for PRs, `rubber-duck` for plans), synthesize, and post the consolidated `## Cross-Model Review Board` review via `gh`. Never `agency cc` / Claude Code. Instruct `gemini` to verify any claimed test/build failure on a clean PR-head build (it has produced stale-build false positives).

**(f) Pre-Commit Checklist.** Tests + docs for every code change. For `feat`/`fix` GUI PRs (Avalonia/WinForms), capture a **real** GUI screenshot — the automation-free path is `dotnet run --project FEBuilderGBA.Avalonia -- --rom roms/FE8U.gba --screenshot-all` (headless `RenderTargetBitmap` yields 0-byte files here); or `PrintWindow`/`tools/WinCapture`. NEVER fabricate images; NEVER use feature-branch URLs (master raw URLs only). Screenshots are OPTIONAL for `docs`/`chore` PRs.

**(g) Clear ALL feedback before merge.** Check ALL THREE channels — unresolved inline review threads (incl. the `copilot-pull-request-reviewer` bot), PR-level conversation comments, and top-level review bodies. Fix every point (never just "acknowledged"), reply, and resolve threads. The repo ruleset blocks merge unless: CI green, branch up-to-date with base (`gh pr update-branch` after other merges land), AND all conversations resolved.

## Steps

1. **OPEN PRs.** For each open PR: run the cross-model Review Gate; post the consolidated comment. Then:
   - Ready + no concerns → **merge** (`gh pr merge <N> -R laqieer/FEBuilderGBA --merge --delete-branch`).
   - Not acceptable → **close** with a reason posted as a comment.
   - Needs author work → **leave open** with actionable feedback.
   - `maintainerCanModify` + small fix → **edit the branch** then merge.

2. **DISCUSSIONS.** Review all open discussions + NEW replies. Reply where useful; check Google Docs / image links in them. Create issues to track surfaced bugfixes / feature requests. **DO NOT write code here.** Close old discussions with no follow-up needed.

3. **ISSUES.** Resolve + reply EVERY open issue **ONE BY ONE** via the full dev workflow: plan → cross-model Review Gate on the plan → worktree → implement + tests → PR → cross-model Review Gate on the PR → clear all three feedback channels → merge. Prioritize issues that break core tooling. This can take many cycles — do NOT stop early, do NOT ask.

4. **DOCS / WIKI.** Update `README.md`, `docs/`, and the wiki whenever code or behavior changed. (Release notes auto-generate from conventional-commit subjects — see `CHANGELOG.md`; no hand-editing needed.)

5. **RELEASE.** Cut a release when warranted via the tag-triggered pipeline. From up-to-date master:
   ```bash
   TAG="ver_$(date +%Y%m%d.%H)"      # scheme: ver_YYYYMMDD.HH
   git tag "$TAG" <master-sha>
   git push origin "$TAG"            # fires .github/workflows/release.yml
   ```
   **Decide the version number yourself — never ask.** The workflow builds all platforms (WinForms, CLI ×3 RIDs, Avalonia ×3 RIDs, Android APK) and publishes the GitHub Release with auto-generated grouped notes. The Gitee mirror was removed (#1766) — GitHub is the sole release source; there is no Gitee sync to run. Hold a release when the accumulated change since the last tag is a single same-day chore.

## Mandatory completion loop

After finishing the current issue list, **RE-SCAN** `gh issue list --state open` AND `gh pr list --state open`. New issues/PRs may have appeared during processing. Keep resolving until **BOTH open issues AND open PRs are zero**. Only then report done. Never declare completion while any open issue/PR remains.

## Hygiene at the end of every run

- Verify the main worktree is on `master` and clean; prune all implementation + review worktrees (`git worktree remove --force` + `git worktree prune`).
- Confirm `0` open issues / `0` open PRs.
- Post a concise summary of what was merged/closed/created and the release decision.
