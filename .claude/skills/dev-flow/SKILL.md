---
name: dev-flow
description: MANDATORY for ANY task that involves code changes, bug fixes, features, docs updates, or PRs in this repo. Activate BEFORE writing any code or creating branches. Enforces the full development workflow.
---

# Development Workflow — Enforced Steps

**You MUST follow these phases in order. Do NOT skip steps. Do NOT write code until Phase 1 is complete.**

Read `DEVELOPMENT-WORKFLOW.md` NOW for full details on each step.

## Phase 1 — Issue & Plan

1. **Check/create GitHub issue** — every task needs an issue. Use `gh issue create -R laqieer/FEBuilderGBA` if none exists.
2. **Post implementation plan** as a comment on the issue.
3. **Trigger the Review Gate** on the plan (pick your branch — see `DEVELOPMENT-WORKFLOW.md` → **Developer & Reviewer Roles**):
   - **Branch A** (Claude Code CLI → Copilot CLI):
   ```bash
   copilot -p "Review the plan comment on issue #<N> in laqieer/FEBuilderGBA. Post your review findings as a comment on the issue. After you finish posting the review, prune any git worktree you created for this review: run 'git worktree prune' and 'git worktree remove --force' any checkout you made under your session-state directory. Include your Copilot CLI version and model at the end." --autopilot --enable-all-github-mcp-tools --allow-all-tools
   ```
   - **Branch B** (Copilot CLI → its own model board): convene the current in-session board per `DEVELOPMENT-WORKFLOW.md` → Developer & Reviewer Roles → Branch B; synthesize; post the consolidated review as an issue comment via `gh`. **Never** `agency cc`.
4. **Iterate** until the Review Gate accepts the plan with no blocking concerns.

**STOP. Do not write code until the plan is accepted.**

## Phase 2 — Branch & Implement (Worktree Only)

5. **Sync remote refs** (safe — no branch switch): `git fetch origin`
6. **Spawn worktree agent** with `isolation: "worktree"`. Inside:
   ```bash
   git checkout -b feat/<short-desc>-<issue> origin/master
   git config user.name "laqieer" && git config user.email "laqieer@126.com"
   ```
7. **Implement** per the accepted plan. No scope creep.
8. **Tests** — add unit tests, run all test suites, ensure passing.
9. **Commit and push** — one logical change per commit, reference issue number.
### Step 9.5 — Capture screenshots (feat/fix PRs ONLY — BEFORE opening PR)

```bash
# Launch app
dotnet run --project FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj -c Release -- --rom roms/FE8U.gba
# One-time setup (run once per clone):
#   dotnet new console -o tools/WinCapture --force -n WinCapture
#   cp tools/capture-window.cs tools/WinCapture/Program.cs
#   cd tools/WinCapture && dotnet add package System.Drawing.Common && dotnet build -c Release
# Navigate to the SPECIFIC affected editor (NOT the main window!)
powershell -Command "Add-Type -AssemblyName UIAutomationClient; <# locate and invoke target editor button #>"
# Capture THAT editor
dotnet run --project tools/WinCapture -c Release -- "Editor Title" pr-screenshots/prN-editor.png
# Commit to pr-screenshots/ on master (via docs PR) or use GitHub asset upload
# Reference via default-branch URLs (`blob/master/pr-screenshots/...?raw=1` or `raw.githubusercontent.com/{owner}/{repo}/master/pr-screenshots/...`) or GitHub asset uploads
```

> **Shell note:** The examples above run in the foreground. To background a process in Git Bash append `&`; in PowerShell use `Start-Process`.

> **Windows-only:** The UIAutomation + WinCapture flow above requires Windows. Non-Windows contributors should use MCP computer-use tools or manual testing with actual application screenshots.

**CRITICAL: The screenshot MUST show the specific editor that was changed, with populated data. NEVER use the generic main Avalonia window.**

## Phase 3 — PR & Review Loop

10. **Open PR** via `gh pr create -R laqieer/FEBuilderGBA`. Include:
    - Summary, plan reference, scope (Closes/Ref), test plan (ALL items `[x]`), screenshots
    - **Screenshots**: MUST show the SPECIFIC affected editor with data for GUI-changing PRs. Non-GUI PRs (Core, CLI, tests only) may use CLI/test output as proof. Generic main window is NOT acceptable for GUI PRs. NEVER fabricate images. NEVER use feature-branch URLs (`blob/{feature-branch}/` or `raw.githubusercontent.com/{owner}/{repo}/{feature-branch}/`). For `docs`/`chore` PRs, screenshots are optional.
    - **Test plan**: ALL items must be `[x]`. Automatable tests MUST be automated — no unchecked "manual later" items.
    - Footer (developer-dependent): Claude Code CLI → `Generated with Claude Code (<model>)`; Copilot CLI → `Copilot CLI: <version>` + `Model: <display-name> (<model-id>)`
11. **Trigger the Review Gate or verify the screenshot-only helper exemption**:
    - The plan gate is never exempt. Skip the PR gate only when every canonical predicate in
      `DEVELOPMENT-WORKFLOW.md` → **Screenshot-only helper PR exemption** is independently verified against the
      current head: REST `author_association` in `OWNER`/`MEMBER`/`COLLABORATOR`, `isCrossRepository == false`, an
      accepted parent plan, `docs:` title, a base-to-head name/status diff containing only added (`A`) paths under
      `pr-screenshots/` that end in lowercase `.png`, mode-`100644` PNG blobs with the correct signature plus
      successful decode and visual inspection, no GitHub closing keyword paired with any issue reference in the
      title/body or commits, an all-checked test plan, and the exact PR-body marker
      `Review-Gate-Exemption: screenshot-only-helper`.
    - The marker alone is never sufficient. If any predicate is absent, ambiguous, or later becomes false, run the
      normal branch below. An eligible PR skips only the independent PR review; safety screening, CI, freshness,
      all-three-channel feedback, merge confirmation, post-merge CI, and cleanup remain mandatory.
    - **Branch A** (Claude Code CLI → Copilot CLI):
    ```bash
    copilot -p "Review pull request #<N> in laqieer/FEBuilderGBA. Perform a full code review: check correctness, test coverage, style, potential bugs, and adherence to the plan. Screenshot check: if the PR title starts with 'feat' or 'fix', verify the PR description contains at least one rendered image (Markdown ![...](URL) or HTML <img> tag) proving the change works. For PRs that modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/ WinForms): screenshots MUST show the ACTUAL running application GUI with controls and data visible — NOT fabricated terminal-output images drawn on a blank background. Verify the screenshot content is RELEVANT to the behavior change (e.g., a Class Editor fix should show the Class Editor with populated data). For PRs that only modify non-GUI files (Core, CLI, Tests): CLI terminal output or test run screenshots are acceptable proof. Accept valid image sources: GitHub attachments, default-branch `raw.githubusercontent.com/{owner}/{repo}/master/...` links, or `blob/master/...` paths with `?raw=1`. REJECT feature-branch URLs (`blob/{feature-branch}/...` or `raw.githubusercontent.com/{owner}/{repo}/{feature-branch}/...`) — these break after branch deletion. Flag them as a blocking issue. Treat a Screenshots section as missing if it contains only placeholder URLs, only HTML comments, or no rendered images at all. Flag missing or invalid screenshots as a blocking issue for feat/fix PRs. For docs/chore PRs (title starts with 'docs' or 'chore'), screenshots are optional — do NOT flag their absence. GUI Test Report check: inspect the changed files list — if the PR modifies any GUI file under FEBuilderGBA.Avalonia/ or FEBuilderGBA/ (WinForms) AND the title starts with 'feat' or 'fix', verify the PR description contains a '## GUI Test Report' section with actual test results (a results table with pass/fail entries). Files under FEBuilderGBA.Core/, FEBuilderGBA.CLI/, FEBuilderGBA.Tests/, FEBuilderGBA.Core.Tests/, FEBuilderGBA.E2ETests/, and FEBuilderGBA.SkiaSharp/ are NOT GUI files — do not count them. Treat a GUI Test Report section as missing if it contains only HTML comments, only placeholder text, or no results table. Flag missing GUI test report as a blocking issue for qualifying GUI feat/fix PRs. For PRs that do not modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/), or for docs/chore/refactor PRs, do NOT require a GUI test report. Test plan check: verify the '## Test plan' section has ALL items checked [x]. Flag any unchecked [ ] items as a blocking issue — no exceptions. Also flag placeholder/template text that was not replaced (e.g., items containing angle brackets like '<what was tested>' or generic boilerplate) — each item must describe a specific test that was actually performed. After you finish posting the review, prune any git worktree you created for this review: run 'git worktree prune' and 'git worktree remove --force' any checkout you made under your session-state directory. Post your review as a pull request review on GitHub. Include your Copilot CLI version and model at the end." --autopilot --enable-all-github-mcp-tools --allow-all-tools
    ```
    - **Branch B** (Copilot CLI → its own model board): convene the current in-session board per `DEVELOPMENT-WORKFLOW.md` → Developer & Reviewer Roles → Branch B, applying the **same** rubric as the Branch-A prompt above (screenshots, GUI Test Report, test-plan all-`[x]`, scope); synthesize; post a `## Cross-Model Review Board` PR comment via `gh`. **Never** `agency cc`.
12. **Check for ALL feedback** — across all three channels (issue comments + review bodies + inline threads):
    ```bash
    # Inline review threads (Copilot bot + CLI)
    gh api graphql -f query='{ repository(owner:"laqieer",name:"FEBuilderGBA") { pullRequest(number:<N>) { reviewThreads(first:100) { nodes { id isResolved comments(first:1) { nodes { path line body } } } } } } }' --jq '.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved==false) | "\(.id) [\(.comments.nodes[0].path):\(.comments.nodes[0].line)] \(.comments.nodes[0].body | split("\n")[0])"'
    # Issue-level conversation comments
    gh api repos/laqieer/FEBuilderGBA/issues/<N>/comments --jq '.[] | select(.user.login != "github-actions[bot]") | "\(.user.login): \(.body | split("\n")[0])"'
    # Top-level PR review bodies (separate from inline threads)
    gh api repos/laqieer/FEBuilderGBA/pulls/<N>/reviews --jq '.[] | "\(.user.login) [\(.state)]: \(.body | split("\n")[0])"'
    ```
    **CRITICAL: Always check ALL THREE channels** (issue comments + review bodies + inline threads). Ignoring any channel is a recurring failure mode.
13. **Fix code** for every comment (never dismiss with "acknowledged"), push, wait for re-review.
14. **Repeat 11-13** until no unaddressed feedback across all three channels. For an exempt helper PR, re-verify
    every canonical predicate after each push; any eligibility loss triggers the full Review Gate.

## Phase 4 — Merge

15. **Pre-merge checklist**: normal Review Gate signoff or a current independently verified screenshot-only helper
    exemption; CI green; branch up-to-date; all feedback addressed (all three channels: issue comments, review
    bodies, inline threads).
16. **Merge**: `gh pr merge <N> -R laqieer/FEBuilderGBA --merge`
17. **Confirm**: `gh pr view <N> -R laqieer/FEBuilderGBA --json state --jq .state` — must be `MERGED`.
18. If not MERGED, diagnose and fix (see DEVELOPMENT-WORKFLOW.md), loop back to step 15.
19. **Prune ALL stale worktrees** after merge (standing step — prevents disk-space leaks): remove your own implementation worktree(s) AND merged-PR Copilot CLI review checkouts under `~/.copilot/session-state/*/files/pr*`:
    ```bash
    cd /path/to/main/repo
    git worktree list                   # inspect every registered worktree FIRST
    git worktree remove --force <path>  # own implementation worktree(s)
    git worktree remove --force <path>  # merged-PR Copilot CLI review checkouts
    git worktree prune                  # drop stale registrations
    ```
    **WARNING:** NEVER `rm -rf` a path not explicitly confirmed as a linked worktree (a buggy loop can delete the main checkout). Prefer `git worktree remove --force`. Only `rm -rf -- <path>` an exact, verified ORPHAN checkout under `~/.copilot/session-state/<id>/files/pr*` after confirming it is NOT the main checkout, current dir, or an active/unmerged review checkout — never the whole `session-state/<id>` dir, never a glob-derived path you didn't first print.

## Hard Rules

- **ALL `gh` commands use `-R laqieer/FEBuilderGBA`** — never target upstream
- **ALL commits as `laqieer <laqieer@126.com>`** — never use any other identity in this repo
- **ALL implementation in isolated worktrees** — never `git checkout`/`stash`/`switch` in main worktree
- **Screenshots MANDATORY for feat/fix PRs** — GUI PRs need real GUI captures (PrintWindow/MCP); non-GUI PRs accept CLI/test output. NEVER fabricate images. NEVER use feature-branch URLs (blob or raw).
- **ALL test plan items must be `[x]`** — unchecked items block merge. Automate everything automatable.
- **PR Review Gate may be skipped ONLY for the canonical screenshot-only helper exemption** — re-verify every
  predicate; the PR-body marker alone never authorizes a bypass.
- **Push immediately after every commit**
- **Prune ALL stale worktrees after every merge** — own implementation worktree(s) AND merged-PR Copilot CLI review checkouts under `~/.copilot/session-state/*/files/pr*` (`git worktree remove --force` + `git worktree prune`); never `rm -rf` an unconfirmed path
