# Claude Code + Copilot CLI — Mandatory Development Workflow

You MUST follow this workflow strictly.
Do NOT skip steps.
Do NOT start coding until explicitly allowed.
**The final goal is MERGED. Continue until the PR is merged — do not stop at "ready to merge".**

The human developer owns all final decisions.
Your role is to assist, not override this process.

> **Note:** This repo's default branch is `master`. All branch/rebase commands below use `master` accordingly.

---

## PHASE 1 — ISSUE ANALYSIS & PLAN DRAFTING

### 1. Issue Intake
When a task begins:
- Read the GitHub Issue fully (use `gh issue view <N> -R laqieer/FEBuilderGBA`)
- Identify:
  - **Scope** — what exactly needs to change
  - **Non-goals** — what this issue is NOT about
  - **Constraints** — API compatibility, performance, platform requirements
  - **Acceptance criteria** — how we know it's done
  - **Dependencies** — other issues or PRs that must land first

### 2. Draft Implementation Plan
Produce a structured plan containing:

```markdown
## Plan: <Issue Title> (#N)

### Problem
<1-2 sentences>

### Approach
<High-level strategy>

### Work Units
For each unit:
- **Files to modify/create** (with expected line counts)
- **What changes** (specific, not vague)
- **Edge cases & failure modes**

### File Overlap Analysis
| File | WU1 | WU2 | ... |
|------|-----|-----|-----|
| example.cs | X | | X |

(mark which WUs touch which files — conflicts need sequential handling)

### Test Strategy
- New unit tests (list specific test names)
- Existing tests that must still pass
- E2E coverage needed?

### Scope Boundary
- What this plan does NOT include
- Issues to reference as partial vs. closes

### Rollout Risk
- Breaking changes? Migration needed?
- Can this be reverted cleanly?
```

### 3. Post Plan as Issue Comment
- Post the plan as a **single comment** on the GitHub Issue
- Use: `gh issue comment <N> -R laqieer/FEBuilderGBA --body "$(cat <<'EOF' ... EOF)"`
- Tag with: `Review requested from @copilot`
- This comment is the **source of truth** for implementation

**STOP HERE. Do not write code.**

---

## PHASE 2 — PLAN REVIEW LOOP (Copilot CLI Gate)

### 4. Trigger Copilot CLI Review
- The plan comment MUST be reviewed by Copilot CLI before proceeding
- **Invocation** — Copilot CLI must post its review on GitHub (not just locally):
  ```bash
  copilot -p "Review the plan comment on issue #<N> in laqieer/FEBuilderGBA. \
  Post your review findings as a comment on the issue. \
  Include your Copilot CLI version and model at the end." \
  --autopilot --enable-all-github-mcp-tools --allow-all-tools
  ```
  > **Why `--allow-all-tools`?** Copilot CLI needs both read tools (to fetch the issue/PR) and write tools (to post comments/reviews). `--enable-all-github-mcp-tools` exposes the GitHub MCP tools, and `--allow-all-tools` auto-approves their use so the non-interactive `--autopilot` session can complete without prompts.
- Copilot CLI checks for:
  - Design gaps or missing components
  - Risky assumptions about existing code
  - Missing test coverage or edge cases
  - Scope creep (claiming to close issues the plan doesn't fully address)
  - Better alternatives or simpler approaches

### 5. Revise Plan Based on Feedback
- **Edit** the original issue comment (don't create new ones):
  ```bash
  # Find the comment ID, then update it
  COMMENT_ID=$(gh api repos/laqieer/FEBuilderGBA/issues/<N>/comments --jq '.[0].id')
  gh api repos/laqieer/FEBuilderGBA/issues/comments/$COMMENT_ID -X PATCH -f body="updated plan"
  ```
- Address every point raised — either fix it or explain why not
- Do NOT write code during revision

### 6. Iterate Until Accepted
Repeat steps 4-5 until Copilot CLI reports **no blocking concerns**.

**Exit condition:** Plan explicitly accepted with no unresolved design issues.

---

## PHASE 3 — IMPLEMENTATION

### 7. Branch & Implement

**MANDATORY: Always use an isolated worktree for new tasks.**
Multiple Claude Code sessions may run in parallel on the same repo. To prevent interference (broken stash, wrong branch checkout), every new implementation task MUST use `isolation: "worktree"` in the Agent tool. Never run `git checkout`, `git stash`, or `git switch` in the main worktree.

**Before spawning the worktree agent**, sync remote refs (safe — doesn't change working tree or branch):
```bash
git fetch origin
```

**Inside the worktree**, create the feature branch from latest remote master:
```bash
git checkout -b feat/<short-desc>-<issue> origin/master
```

Never work directly on master — always create a feature branch.

**Branch naming:** `feat/<short-desc>-<issue>` or `fix/<short-desc>-<issue>`

**Parallel execution rules:**
- Work units with **no file overlap** → launch as parallel worktree agents
- Work units with **file overlap** → implement sequentially or in same agent
- After parallel agents complete, merge changes into a single branch and resolve conflicts before pushing

**Commit discipline:**
- One logical change per commit
- Commit messages reference the issue: `feat: add X (#N)` or `fix: resolve Y (#N)`
- **When using Claude Code automation:** commit as `laqieer <laqieer@126.com>` (human contributors use their own identity)
- Every commit must build and pass tests

**Scope discipline:**
- Follow the accepted plan exactly
- No bonus refactoring, no extra features, no "while I'm here" changes
- If you discover something that needs fixing, file a new issue — don't scope-creep

**Test requirements:**
- New tests for every behavioral change
- Run `dotnet test FEBuilderGBA.Core.Tests/` and `dotnet test FEBuilderGBA.Tests/` before pushing
- Tests mutating CoreState use `[Collection("SharedState")]`
- **Avalonia GUI changes MUST include headless UI tests** in `FEBuilderGBA.Avalonia.Tests/`:
  - Run `dotnet test FEBuilderGBA.Avalonia.Tests/` before pushing
  - Use `[AvaloniaFact]` / `[AvaloniaTheory]` (from `Avalonia.Headless.XUnit`) to instantiate real controls
  - Verify **control properties and rendering state**, not just ViewModel logic
  - Example: test that `Image.Stretch == Stretch.Fill` (catches rendering bugs), not just that a zoom variable changed
  - Unit tests verify code logic; headless tests verify UI behavior. **Both are required for GUI changes.**

### 8. Scope Accuracy Check (Before PR)
Before opening the PR, verify:
- [ ] Does the PR fully deliver what the referenced issues require?
- [ ] If partial, use `Ref #N (partial — <what's missing>)` instead of `Closes #N`
- [ ] Don't claim to close issues that require more work beyond this PR
- [ ] Documentation updated: CLAUDE.md, README.md, --help text, wiki as applicable (see [CONTRIBUTING.md](CONTRIBUTING.md))

### 8.5. GUI Validation (GUI feat/fix PRs)

**When required:** `feat` or `fix` PRs that modify GUI files under `FEBuilderGBA.Avalonia/` or `FEBuilderGBA/` (WinForms). NOT required for `docs`, `chore`, or refactor-only changes even if they touch GUI files. NOT required for changes that only touch `FEBuilderGBA.Core/`, `FEBuilderGBA.CLI/`, `FEBuilderGBA.Tests/`, or other non-GUI projects.

**Preferred method (Windows):** Use `PrintWindow` API + PowerShell `UIAutomationClient` for headless GUI validation. This works even with locked screens and produces real window captures.

To build the capture tool from `tools/capture-window.cs`:
```bash
dotnet new console -o tools/WinCapture --force -n WinCapture
cp tools/capture-window.cs tools/WinCapture/Program.cs
cd tools/WinCapture && dotnet add package System.Drawing.Common && dotnet build -c Release
```

Then capture any window: `dotnet run --project tools/WinCapture -c Release -- "Window Title" output.png`

**Alternative:** MCP computer-use tools (`screenshot`, `click`, `type_text`) when the screen is unlocked.

**Non-Windows contributors:** Use MCP or manual testing with actual application screenshots.

**If no capture method is available**, this step can be skipped with a note in the PR explaining why. The GUI Test Report section should still be present with manual test results.

**Procedure:**
1. Build and launch the GUI app (Avalonia or WinForms) with a test ROM:
   ```bash
   # Avalonia (cross-platform):
   dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj
   cd FEBuilderGBA.Avalonia && dotnet run -- --rom ../roms/FE8U.gba

   # WinForms (Windows, x86):
   msbuild /p:Configuration=Debug /p:Platform=x86 FEBuilderGBA.sln
   ./FEBuilderGBA/bin/Debug/FEBuilderGBA.exe --rom roms/FE8U.gba
   ```
   > **Shell note:** The examples above run in the foreground. To background a process in Git Bash append `&`; in PowerShell use `Start-Process`.
2. Use MCP computer-use tools to exercise the changed feature:
   - `find_window` / `focus_window` to locate and activate the app
   - `screenshot` to capture the current state
   - `click` / `type_text` / `key_press` to interact with UI elements
   - `scroll` to navigate within views
3. Capture screenshots at key states (before/after the change works)
4. Generate a structured test report

**Test Report Format:**
```markdown
## GUI Test Report

### Environment
- ROM: <rom file used>
- Editor/View: <which Avalonia view was tested>

### Steps Performed
1. <describe step taken>
2. <describe step taken>

### Test Results
| Test | Expected | Actual | Status |
|------|----------|--------|--------|
| <describe test> | <expected behavior> | <observed behavior> | PASS / FAIL |

### Verdict
<PASS/FAIL with summary>
```

Include the test report in the PR body under `## GUI Test Report`.

---

## PHASE 4 — PULL REQUEST REVIEW LOOP

### 9. Open Pull Request
```bash
gh pr create -R laqieer/FEBuilderGBA --title "<title>" --body "$(cat <<'EOF'
## Summary
<bullet points>

## Plan Reference
Implements the plan from <link to issue comment>

## Scope
Closes #N
Ref #M (partial — <what remains>)

## Screenshots
<!-- For feat/fix PRs: MANDATORY — replace this comment with REAL GUI screenshot(s).
     Screenshots MUST be captured from the actual running application (e.g., via PrintWindow API
     or MCP computer-use). Fabricated images (e.g., DrawString on Bitmap) are NOT acceptable.
     Image URLs MUST be permanent — use blob/master/pr-screenshots/ paths or GitHub asset uploads.
     NEVER use blob/{feature-branch}/ URLs — they 404 after branch deletion.
     For docs/chore PRs: This entire section may be deleted. -->

## GUI Test Report
<!-- For GUI feat/fix PRs (Avalonia or WinForms): MANDATORY — replace this comment with MCP or manual test results.
     Use the test report format from step 8.5. If MCP was not available, include manual test results instead.
     For non-GUI PRs or docs/chore PRs: This entire section may be deleted. -->

## Test plan
<!-- ALL test cases MUST be checked [x] before requesting review.
     Unchecked items block merge — do not leave items unchecked for "manual later".
     If a test CAN be automated (e.g., via UIAutomation + PrintWindow), it MUST be automated.
     Only items that genuinely require human judgment may remain unchecked. -->
- [x] <what was tested and verified>
- [x] <another verified item — ALL must be checked>

## Known limitations
<anything not covered>

Generated with Claude Code (claude-opus-4-6)
EOF
)"
```

**PR rules:**
- Reference the original Issue AND the accepted plan
- Clearly distinguish `Closes` (fully done) from `Ref` (partial)
- Include test coverage notes and known limitations
- **ALL test plan items MUST be checked `[x]` before requesting review.** Unchecked items `[ ]` block merge. If a test CAN be automated (e.g., PowerShell UIAutomation + PrintWindow for GUI validation), it MUST be automated — do not leave automatable tests as unchecked "manual later" items. Only items that genuinely require human judgment (e.g., subjective visual assessment) may remain unchecked, and these must include a justification.
- **Screenshots are MANDATORY for `feat` and `fix` PRs:**
  - **GUI-changing PRs** (Avalonia or WinForms files modified): include **real GUI screenshot(s)** captured from the actual running application using `PrintWindow` API (`tools/capture-window.cs`), MCP, or manual screen capture. **NEVER fabricate images** (e.g., `System.Drawing.DrawString` on a blank Bitmap is NOT a screenshot).
  - **Non-GUI PRs** (Core, CLI, tests only): CLI terminal output, test run output, or before/after diff screenshots are acceptable proof.
  - **Image URL rules** (all PRs): URLs MUST be permanent — commit to `pr-screenshots/` on master (via a docs PR) or use GitHub asset uploads. **NEVER use `blob/{feature-branch}/` URLs** — they 404 after branch deletion.
  - For `docs` and `chore` PRs, screenshots are optional.

### 10. Copilot CLI PR Review + Resolve ALL Comments
- **Invocation** — trigger review and ensure it posts on the PR:
  ```bash
  copilot -p "Review pull request #<N> in laqieer/FEBuilderGBA. \
  Perform a full code review: check correctness, test coverage, style, potential bugs, and adherence to the plan. \
  Screenshot check: if the PR title starts with 'feat' or 'fix', verify the PR description contains at least one rendered image (Markdown ![...](URL) or HTML <img> tag) proving the change works. \
  For PRs that modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/ WinForms): screenshots MUST show the ACTUAL running application GUI with controls and data visible — NOT fabricated terminal-output images drawn on a blank background. Verify the screenshot content is RELEVANT to the behavior change (e.g., a Class Editor fix should show the Class Editor with populated data). \
  For PRs that only modify non-GUI files (Core, CLI, Tests): CLI terminal output or test run screenshots are acceptable proof. \
  Accept valid image sources: GitHub attachments, raw.githubusercontent.com links, or blob/master/ paths with ?raw=1. \
  REJECT blob/{feature-branch}/ URLs — these break after branch deletion. Flag them as a blocking issue. \
  Treat a Screenshots section as missing if it contains only placeholder URLs, only HTML comments, or no rendered images at all. Flag missing or invalid screenshots as a blocking issue for feat/fix PRs. \
  For docs/chore PRs (title starts with 'docs' or 'chore'), screenshots are optional — do NOT flag their absence. \
  GUI Test Report check: inspect the changed files list — if the PR modifies any GUI file under FEBuilderGBA.Avalonia/ or FEBuilderGBA/ (WinForms) AND the title starts with 'feat' or 'fix', verify the PR description contains a '## GUI Test Report' section with actual test results (a results table with pass/fail entries). \
  Files under FEBuilderGBA.Core/, FEBuilderGBA.CLI/, FEBuilderGBA.Tests/, FEBuilderGBA.Core.Tests/, FEBuilderGBA.E2ETests/, and FEBuilderGBA.SkiaSharp/ are NOT GUI files — do not count them. \
  Treat a GUI Test Report section as missing if it contains only HTML comments, only placeholder text, or no results table. Flag missing GUI test report as a blocking issue for qualifying GUI feat/fix PRs. \
  For PRs that do not modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/), or for docs/chore/refactor PRs, do NOT require a GUI test report. \
  Test plan check: verify the '## Test plan' section has ALL items checked [x]. Flag any unchecked [ ] items as a blocking issue — unchecked test cases mean the PR is not fully validated. If an unchecked item could be automated (e.g., GUI validation via UIAutomation/PrintWindow), flag it as 'should be automated, not left for manual'. Only items requiring genuine human judgment are acceptable as unchecked. \
  Post your review as a pull request review on GitHub. \
  Include your Copilot CLI version and model at the end." \
  --autopilot --enable-all-github-mcp-tools --allow-all-tools
  ```
- Verify the review was posted by Copilot with the required footer:
  ```bash
  # Get the latest Copilot review (filter by bot author and check for footer)
  gh api repos/laqieer/FEBuilderGBA/pulls/<N>/reviews \
    --jq '[.[] | select(.user.login == "Copilot" or .user.login == "copilot" or .user.type == "Bot")] | .[-1].body'
  # The output MUST contain both "Copilot CLI:" and "Model:" lines
  ```

Address feedback in categories:

| Category | Action |
|----------|--------|
| **Code fix needed** | Fix the code, push new commit |
| **Scope overreach** | Update PR body, change `Closes` to `Ref` |
| **Missing feature** | Add it if in plan scope, otherwise note as future work |
| **Dead/conflicting UI** | Remove it (e.g., don't reintroduce removed features) |
| **Needs rebase** | Rebase onto default branch, resolve conflicts, `git push --force-with-lease`, then re-trigger Copilot CLI review |

**After each push, also check for inline comments from the GitHub Copilot bot** (separate from Copilot CLI reviews).

Find all unresolved review threads (use `first: 100` to cover large PRs; paginate if `hasNextPage` is true):
```bash
# Get ALL unresolved thread IDs and their first comment
gh api graphql -f query='{
  repository(owner: "laqieer", name: "FEBuilderGBA") {
    pullRequest(number: <N>) {
      reviewThreads(first: 100) {
        pageInfo { hasNextPage endCursor }
        nodes {
          id
          isResolved
          comments(first: 1) { nodes { path line body } }
        }
      }
    }
  }
}' --jq '(.data.repository.pullRequest.reviewThreads | "hasNextPage=\(.pageInfo.hasNextPage) endCursor=\(.pageInfo.endCursor)"), (.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved == false) | "\(.id) [\(.comments.nodes[0].path):\(.comments.nodes[0].line)] \(.comments.nodes[0].body | split("\n")[0])")'
# If hasNextPage=true, re-run with: reviewThreads(first: 100, after: "<endCursor>")
```

These must ALL be addressed (fix the code) and then resolved:
```bash
# Resolve each thread after addressing the feedback
gh api graphql -f query='mutation { resolveReviewThread(input: {threadId: "<THREAD_ID>"}) { thread { isResolved } } }'
```

### 11. Iterate Until Approved
- Fix ALL issues raised — both Copilot CLI reviews AND GitHub Copilot bot inline comments
- Push fixes as new commits (not amends)
- **After each push, wait for the GitHub Copilot bot auto-review to complete** (typically 2-5 minutes). Verify by checking for a new review with a timestamp after your push:
  ```bash
  gh api repos/laqieer/FEBuilderGBA/pulls/<N>/reviews \
    --jq '[.[] | select(.user.login == "Copilot" or .user.type == "Bot")] | .[-1].submitted_at'
  ```
- Re-run the unresolved threads query from step 10 to catch **newly posted** comments
- Resolve all review threads after addressing them
- Re-trigger Copilot CLI review using the same invocation from step 10
- Repeat until: **no unresolved comments of any kind**

**Exit condition:** Copilot CLI posts a review with no blocking concerns AND includes its version/model footer in this exact format:
```
Copilot CLI: <version>
Model: <display-name> (<model-id>)
```
Example: `Copilot CLI: 1.0.6-0` / `Model: GPT-5.4 (gpt-5.4)`. Both lines must be present at the end of the review body.

---

## PHASE 5 — MERGE COMPLETION LOOP

**This phase is a loop. Continue until the PR state is MERGED.**

### 12. Pre-Merge Checklist
Before attempting merge, verify ALL of these:
- [ ] Copilot CLI posted a review on the PR with **no blocking concerns** and a `Copilot CLI: <version>` + `Model: <name>` footer
- [ ] All GitHub Copilot bot inline comments addressed and threads resolved
- [ ] All **required** CI checks green (build via `check.yml`). E2E checks are informational — they run daily on master via cron, not required for merge.
- [ ] Branch is up to date with master — verify with:
  ```bash
  gh pr view <N> -R laqieer/FEBuilderGBA --json mergeStateStatus --jq .mergeStateStatus
  # If BEHIND or DIRTY: rebase onto master and push
  ```
- [ ] No merge conflicts
- [ ] PR body accurately reflects what was delivered (update if code changes were added during review)

### 13. Attempt Merge
```bash
gh pr merge <N> -R laqieer/FEBuilderGBA --merge
```

**If merge fails**, diagnose and fix the blocker:

| Blocker | Diagnosis | Fix |
|---------|-----------|-----|
| **CI checks pending** | `gh pr checks <N> -R laqieer/FEBuilderGBA` | Wait, or set auto-merge: `gh pr merge <N> -R laqieer/FEBuilderGBA --merge --auto` |
| **CI checks failed** | `gh run view <RUN_ID> -R laqieer/FEBuilderGBA --log-failed` | Fix the failing test/build, push, re-trigger Copilot CLI review |
| **Unresolved conversations** | GraphQL query for unresolved threads (see step 10) | Resolve all threads |
| **Branch outdated** | `gh pr view <N> -R laqieer/FEBuilderGBA --json mergeStateStatus --jq .mergeStateStatus` shows `BEHIND` | `git fetch origin master && git rebase origin/master && git push --force-with-lease`, then re-trigger Copilot CLI review |
| **Merge conflicts** | `gh pr view <N> -R laqieer/FEBuilderGBA --json mergeable` | `git rebase origin/master && git push --force-with-lease`, then re-trigger Copilot CLI review (rebase can introduce changes) |
| **Branch policy violation** | Read the error message carefully | Fix the specific rule violation (missing check, deployment, etc.) |
| **"not mergeable" (unknown)** | Wait 15s — GitHub recalculates merge status | `sleep 15 && gh pr merge <N> -R laqieer/FEBuilderGBA --merge` |

### 14. Confirm Merge
```bash
gh pr view <N> -R laqieer/FEBuilderGBA --json state --jq .state
# MUST output: MERGED
```

**If not MERGED, go back to step 12.** Repeat the checklist → attempt → diagnose → fix loop until the PR is confirmed MERGED.

### 15. Merge Strategy (Multiple PRs)
When merging multiple PRs:

**Serial merge order** — merge one, wait for GitHub to recalculate merge status, then merge the next. This avoids the rebase cascade problem where merging PR A causes PRs B, C, D to all conflict simultaneously.

**Priority order:**
1. Bug fixes first (least likely to cause cascading conflicts)
2. Small features next
3. Large cross-cutting changes last (e.g., dark mode, collapsible sections)

```bash
# Merge one PR
gh pr merge <N> -R laqieer/FEBuilderGBA --merge

# Wait for merge status to recalculate
sleep 15

# Check next PR's merge status before merging
gh pr view <M> -R laqieer/FEBuilderGBA --json mergeable --jq '.mergeable'
# If CONFLICTING: rebase, push, wait, then merge
# If MERGEABLE: merge directly
```

### 16. Post-Merge
- Verify the issue was auto-closed (if `Closes #N` was used)
- Clean up the worktree (all tasks use worktrees — see step 7):
  ```bash
  # 1. Ensure all changes are committed or discarded — git worktree remove fails on a dirty worktree
  # 2. Navigate back to the main repo root (you cannot remove a worktree from inside it)
  cd /path/to/main/repo
  git worktree list             # verify which worktrees exist
  git worktree remove <path>    # remove the linked worktree
  ```
- No need to checkout or pull master — just run `git fetch origin` before creating the next worktree (step 7) to ensure remote refs are current.

---

## ANTI-PATTERNS (Learned from Experience)

### Don't: Batch-merge and hope for the best
Each merge changes master. Merging 5 PRs at once creates 5 rebase cascades.
**Do:** Merge one at a time, rebase the next onto updated master.

### Don't: Claim `Closes #N` for partial work
Copilot CLI will flag this every time.
**Do:** Use `Ref #N (partial — <what remains>)` and be specific.

### Don't: Reintroduce removed features
If PR #91 removed Redo UI, PR #95 must not add it back.
**Do:** Check recently merged PRs for conflicting changes before implementing.

### Don't: Skip the plan phase for "simple" changes
Even "just add a shortcut" can conflict with other work.
**Do:** Always post a plan comment. Small plans are fine — 3 lines is enough.

### Don't: Run parallel agents on overlapping files
Two agents editing `MainWindow.axaml.cs` will create merge conflicts.
**Do:** Use the file overlap analysis table. Overlapping files go in the same agent.

### Don't: Stop at "ready to merge" without confirming MERGED
"All checks pass" and "Copilot signed off" doesn't mean done — the merge itself can fail due to branch policies, ruleset requirements, or race conditions.
**Do:** Always run `gh pr view <N> -R laqieer/FEBuilderGBA --json state --jq .state` and confirm the output is `MERGED`. If not, diagnose and fix.

### Don't: Ignore GitHub Copilot bot inline comments
The Copilot bot (separate from Copilot CLI) posts inline code comments on each push. Unresolved threads block merge when `required_review_thread_resolution` is enabled.
**Do:** After each push, check for inline comments, address them, and resolve the threads via GraphQL.

### Don't: Merge before Copilot CLI posts its signoff on the PR
A local-only review doesn't count — the review must be visible on GitHub.
**Do:** Use `--enable-all-github-mcp-tools --allow-all-tools` so Copilot CLI can post via GitHub MCP tools. Verify with `gh api repos/.../pulls/<N>/reviews`.

### Don't: Force-push without `--force-with-lease`
**Do:** Always use `--force-with-lease` to avoid overwriting someone else's work.

### Don't: Work directly on master
Committing to master means no PR review, no Copilot CLI gate, and no clean revert path.
**Do:** Always create a feature branch in an isolated worktree: `git fetch origin` then spawn a worktree agent that runs `git checkout -b feat/... origin/master` (see step 7).

### Don't: Switch branches or stash in the main worktree
Multiple Claude Code sessions share the same repo. Running `git checkout`, `git stash`, or `git switch` in the main worktree will break other sessions' working state. Attempting to "restore" it is also wrong — you don't know what state the other session expects.
**Do:** ALWAYS use `isolation: "worktree"` in the Agent tool for every new implementation task. This gives you an isolated copy of the repo with zero interference. In the main worktree, only safe ref updates like `git fetch origin` are allowed — never `git checkout`, `git stash`, `git switch`, or `git reset`.

### Don't: Guess why merge is blocked
Assuming "needs approval" when the real cause is an outdated branch or unresolved threads wastes time and misses the actual fix.
**Do:** Diagnose systematically — check all causes in order: unresolved threads → CI status → branch up-to-date → review approvals. Use `gh pr view <N> -R laqieer/FEBuilderGBA --json mergeStateStatus,statusCheckRollup` to get the actual block reason.

### Don't: Skip GUI validation for "obvious" Avalonia or WinForms changes
A style-only XAML change can still break layout. A ViewModel tweak can disconnect a binding. A WinForms designer change can misalign controls.
**Do:** Always run GUI validation for GUI feat/fix PRs. Use `PrintWindow` API + PowerShell `UIAutomationClient` (Windows, works headlessly) or MCP computer-use (when screen is unlocked). The headless tests verify control properties; real screenshots verify the user sees the right thing.

### Don't: Open a feat/fix PR without real GUI screenshots
A `feat` or `fix` PR without visual proof is incomplete. Copilot CLI reviews are expected to flag missing screenshots as a blocking issue for these PR types.
**Do:** For feat/fix PRs, always capture **real GUI screenshots** from the running application using `PrintWindow` API (`tools/capture-window.cs`) or MCP. For `docs`/`chore` PRs, screenshots are optional.

### Don't: Fabricate screenshots
Drawing text on a Bitmap with `System.Drawing.DrawString` (e.g., "VALIDATION PASSED" in green on black) is NOT a screenshot — it proves nothing about the GUI working. It's cheating the review process.
**Do:** Use `PrintWindow` API to capture real window content. It works even with a locked screen. Combine with PowerShell `UIAutomationClient` for headless navigation.

### Don't: Use feature branch blob URLs for screenshot images
`blob/{feature-branch}/file.png?raw=1` becomes a 404 after the branch is deleted post-merge. Every screenshot in the PR breaks permanently.
**Do:** Commit screenshots to `pr-screenshots/` on master (via a docs PR) BEFORE referencing them. Use `blob/master/pr-screenshots/...` URLs. Or use GitHub asset uploads which produce permanent `user-attachments/assets/...` URLs.

### Don't: Declare GUI changes complete with only unit tests
Unit tests verify code logic (e.g., "the zoom variable changed to 2"). They do NOT verify UI behavior (e.g., "the image actually rendered at 2x size"). The `Stretch="None"` zoom bug (issue #183) passed all unit tests but zoom never actually worked — because the Image control ignored Width/Height when Stretch was None.
**Do:** For ANY Avalonia GUI change, write headless UI tests in `FEBuilderGBA.Avalonia.Tests/` using `[AvaloniaFact]`:
```csharp
// BAD — only tests ViewModel logic
Assert.Equal(2, viewModel.Zoom); // Zoom variable is 2, but does the image scale?

// GOOD — tests actual control behavior
var image = control.FindControl<Image>("ImageDisplay");
Assert.Equal(Stretch.Fill, image.Stretch);  // Image WILL scale
Assert.Equal(32, image.Width);              // Image IS scaled to 32px
```
Run `dotnet test FEBuilderGBA.Avalonia.Tests/` before pushing. These tests catch rendering bugs that unit tests fundamentally cannot.

---

## QUICK REFERENCE

```
Issue → Plan Comment → Copilot Review → Revise → Accept
  → Branch → Implement → Tests → Push
  → PR → Copilot Review + Bot Comments → Fix All → Resolve Threads
  → Re-review → Signoff → CI Green → Merge → Confirm MERGED
  ↑___________________________________________|  (loop until MERGED)
  → Clean up worktree
```

**All `gh` commands MUST use `-R laqieer/FEBuilderGBA`.**
**This repo's default branch is `master`.**

**When using Claude Code / Copilot automation:**
- Commits as `laqieer <laqieer@126.com>`
- PR bodies end with `Generated with Claude Code (claude-opus-4-6)`

**Human contributors:** Use your own identity and commit signing workflow.
