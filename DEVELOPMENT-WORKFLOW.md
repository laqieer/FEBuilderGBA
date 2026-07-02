# Claude Code + Copilot CLI — Mandatory Development Workflow

You MUST follow this workflow strictly.
Do NOT skip steps.
Do NOT start coding until explicitly allowed.
**The final goal is MERGED. Continue until the PR is merged — do not stop at "ready to merge".**

The human developer owns all final decisions.
Your role is to assist, not override this process.

> **Note:** This repo's default branch is `master`. All branch/rebase commands below use `master` accordingly.

---

## Developer & Reviewer Roles (Review Gate)

This workflow is driven by an AI **developer** that must pass an independent **Review Gate** at two points — the
**plan** (Phase 2) and the **PR** (Phase 4). WHO performs the review depends on **which agent is the developer**.

**Self-identify your runtime before every Review Gate:**
- Running as **Claude Code CLI** → use **Branch A**.
- Running as **Copilot CLI** → use **Branch B**.
- A **human** contributor, or identity unclear / mixed handoff → stop and ask the human which gate applies.

> `.claude/skills/dev-flow/SKILL.md` is written in Claude-Code voice ("Multiple Claude Code sessions") but it also
> loads for Copilot CLI sessions. Identify by your **actual runtime**, not by the document's voice.

| Developer | Review Gate consults | Mechanism |
|-----------|----------------------|-----------|
| **Claude Code CLI** | **Copilot CLI** | `copilot -p "..." --autopilot --enable-all-github-mcp-tools --allow-all-tools` (Branch A) |
| **Copilot CLI** | **Its own other models** — an in-session cross-model board | `task` sub-agent per model → synthesize → post via `gh` (Branch B). **Never** `agency cc` / Claude Code. |

### Branch A — developer = Claude Code CLI (consult Copilot CLI)

Run the `copilot -p` invocations exactly as written in **Phase 2 step 4** and **Phase 4 step 10** (kept verbatim,
including `--autopilot --enable-all-github-mcp-tools --allow-all-tools` and the worktree-prune tail). Copilot CLI posts
the review on GitHub as the `Copilot` bot; verify by **bot author** + the 2-line `Copilot CLI:` / `Model:` footer.

### Branch B — developer = Copilot CLI (convene the in-session cross-model board)

Do **not** call `agency cc` / Claude Code. Convene a 3-model board **in-session** and post one consolidated review.

1. **Board roster:** `claude-opus-4.8` (Claude Opus 4.8), `gpt-5.5` (GPT-5.5), `gemini-3.5-flash` (Gemini 3.5 Flash).
   **Independence:** if your own active model is one of these, swap that member for a **named same-tier alternate**
   (`claude-opus-4.8`→`claude-sonnet-5`, `gpt-5.5`→`gpt-5.4`, `gemini-3.5-flash`→`gemini-3.1-pro-preview`) so all
   three reviewers differ from you; keep ≥2 providers (Anthropic / OpenAI / Google). If a roster/alternate model is
   unavailable, substitute another available model from a different provider and note the substitution.
2. **Gather the artifact (full source-of-truth context)** and embed it in each reviewer's prompt:
   - **Plan gate (Phase 2):** the issue title/body + acceptance criteria **and** the plan-comment body (+ URL/ID).
   - **PR gate (Phase 4):** the **accepted plan** comment (+ link), the **issue** body/link, the PR body, the
     changed-files list, `gh pr diff <N> -R laqieer/FEBuilderGBA`, and the test/screenshot evidence.
3. **Spawn** one reviewer per model with the `task` tool (`model` set to each roster id), giving every member the
   **same** criteria the Branch-A `copilot -p` prompt encodes — for the PR gate that is the full rubric: correctness,
   test coverage, screenshot validity + feature-branch-URL rejection, `## GUI Test Report` presence, **all**
   `## Test plan` items `[x]`, and scope creep. Each member labels findings **Blocking** / Non-blocking.
4. **Aggregate (pessimistic veto):** any member's Blocking ⇒ consolidated verdict **Blocked**; approve only when all
   members report zero blocking. Attribute each member's blocking findings **per-model**. As the developer you may
   **not** self-override a board Blocking concern — fix it, or have the board withdraw it; if you believe a finding is
   a false positive, record a **reasoned rebuttal** for the human (who owns final decisions) to adjudicate — never a
   silent override.
5. **Post** the consolidated review yourself (always `-R laqieer/FEBuilderGBA`):
   - Plan gate: `gh issue comment <N> -R laqieer/FEBuilderGBA ...`
   - PR gate: a clearly-labeled `## Cross-Model Review Board` PR comment (`gh pr comment <N> -R laqieer/FEBuilderGBA ...`). A self-authored
     `--comment` review carries no "approved" state, so merge relies on CI + the resolved-feedback checklist (Phase 5),
     not a GitHub approval.

   End the posted review with a board-roster line **immediately above** the mandatory 2-line footer (so
   `.github/copilot-instructions.md` stays satisfied):
   ```
   Review Board: claude-opus-4.8, gpt-5.5, gemini-3.5-flash
   Copilot CLI: <version>
   Model: <display-name> (<model-id>)
   ```
6. **Iterate & verify:** after any material plan/code change, re-run the full board; exit when no member reports
   blocking. Confirm the gate fired by grepping the **PR-comments** endpoint
   (`repos/laqieer/FEBuilderGBA/issues/<N>/comments` — where the `## Cross-Model Review Board` comment lives, **not**
   `pulls/<N>/reviews`) for the `Review Board:` marker.

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

## PHASE 2 — PLAN REVIEW LOOP (Review Gate)

### 4. Trigger the Review Gate
- The plan comment MUST pass the **Review Gate** before proceeding. Pick your branch — see **Developer & Reviewer Roles** above.
- **Branch A (Claude Code CLI → Copilot CLI)** — Copilot CLI must post its review on GitHub (not just locally):
  ```bash
  copilot -p "Review the plan comment on issue #<N> in laqieer/FEBuilderGBA. \
  Post your review findings as a comment on the issue. \
  After you finish posting the review, prune any git worktree you created for this review: run 'git worktree prune' and 'git worktree remove --force' any checkout you made under your session-state directory. \
  Include your Copilot CLI version and model at the end." \
  --autopilot --enable-all-github-mcp-tools --allow-all-tools
  ```
  > **Why `--allow-all-tools`?** Copilot CLI needs both read tools (to fetch the issue/PR) and write tools (to post comments/reviews). `--enable-all-github-mcp-tools` exposes the GitHub MCP tools, and `--allow-all-tools` auto-approves their use so the non-interactive `--autopilot` session can complete without prompts.
- **Branch B (Copilot CLI → in-session board)** — convene the cross-model board per **Developer & Reviewer Roles → Branch B** with the **plan-comment body** (+ issue title/body & acceptance criteria) as the artifact, then post the consolidated review as an issue comment. **Never** `agency cc`.
- The Review Gate checks for:
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
Repeat steps 4-5 until the Review Gate reports **no blocking concerns**.

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

Then capture any window: `dotnet run --project tools/WinCapture -c Release -- "Window Title" pr-screenshots/output.png`

**Alternative:** MCP computer-use tools (`screenshot`, `click`, `type_text`) when the screen is unlocked.

**Non-Windows contributors:** Use MCP or manual testing with actual application screenshots.

**If no capture method is available**, this step can be skipped with a note in the PR explaining why. The GUI Test Report section should still be present with manual test results.

**Procedure:**
1. Build and launch the GUI app (Avalonia or WinForms) with a test ROM:
   ```bash
   # Auto-select first .gba ROM from roms/ folder:
   ROM=$(ls roms/*.gba 2>/dev/null | head -1)

   # Avalonia (cross-platform):
   dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj
   cd FEBuilderGBA.Avalonia && dotnet run -- --rom "../$ROM"

   # WinForms (Windows, x86):
   msbuild /p:Configuration=Debug /p:Platform=x86 FEBuilderGBA.sln
   ./FEBuilderGBA/bin/Debug/FEBuilderGBA.exe --rom "$ROM"
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
     CRITICAL: Screenshots MUST show the SPECIFIC affected editor with populated data.
     NEVER use the generic main Avalonia window as proof — it proves nothing about the fix.
     Capture steps:
       1. Launch: ROM=$(ls roms/*.gba 2>/dev/null | head -1) && dotnet run --project FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj -c Release -- --rom "$ROM" &
       2. Navigate: Use PowerShell UIAutomation to click the specific editor button
       3. Capture: dotnet run --project tools/WinCapture -c Release -- "Editor Title" pr-screenshots/screenshot.png
       4. Commit to pr-screenshots/ on master, reference via raw.githubusercontent.com
     Fabricated images are NOT acceptable. Generic main window screenshots are NOT acceptable.
     For docs/chore PRs: This entire section may be deleted. -->

## GUI Test Report
<!-- For GUI feat/fix PRs (Avalonia or WinForms): MANDATORY — replace this comment with MCP or manual test results.
     Use the test report format from step 8.5. If MCP was not available, include manual test results instead.
     For non-GUI PRs or docs/chore PRs: This entire section may be deleted. -->

## Test plan
<!-- ALL items MUST be checked [x] before requesting review. No unchecked items allowed.
     If a test is not yet verified, either verify it now or move it to Known Limitations.
     If a test CAN be automated (e.g., via UIAutomation + PrintWindow), it MUST be automated. -->
- [x] <what was tested and verified>
- [x] <another verified item — ALL must be checked>

## Known limitations
<anything not covered>

<!-- Footer (developer-dependent) — KEEP the block matching your runtime, DELETE the other: -->
<!-- Claude Code CLI developer: -->
Generated with Claude Code (<model>)
<!-- Copilot CLI developer: -->
Copilot CLI: <version>
Model: <display-name> (<model-id>)
EOF
)"
```

**PR rules:**
- Reference the original Issue AND the accepted plan
- Clearly distinguish `Closes` (fully done) from `Ref` (partial)
- Include test coverage notes and known limitations
- **ALL test plan items MUST be checked `[x]` before requesting review.** No unchecked `[ ]` items allowed — they block merge. If a test is not yet verified, verify it before opening the PR or move it to Known Limitations. If a test CAN be automated (e.g., PowerShell UIAutomation + PrintWindow), it MUST be automated.
- **Screenshots are MANDATORY for `feat` and `fix` PRs:**
  - **GUI-changing PRs** (Avalonia or WinForms files modified): include **real GUI screenshot(s)** captured from the actual running application using `PrintWindow` API (`tools/capture-window.cs`), MCP, or manual screen capture. **NEVER fabricate images** (e.g., `System.Drawing.DrawString` on a blank Bitmap is NOT a screenshot).
  - **Non-GUI PRs** (Core, CLI, tests only): CLI terminal output, test run output, or before/after diff screenshots are acceptable proof.
  - **Image URL rules** (all PRs): URLs MUST be permanent — commit to `pr-screenshots/` on master (via a docs PR) or use GitHub asset uploads. **NEVER use feature-branch URLs** (either `blob/{feature-branch}/` or `raw.githubusercontent.com/{owner}/{repo}/{feature-branch}/`) — both 404 after branch deletion.
  - For `docs` and `chore` PRs, screenshots are optional.

### 10. Review Gate: PR Review + Resolve ALL Comments
Pick your branch — see **Developer & Reviewer Roles** above.
- **Branch A (Claude Code CLI → Copilot CLI)** — **Invocation:** trigger review and ensure it posts on the PR:
  ```bash
  copilot -p "Review pull request #<N> in laqieer/FEBuilderGBA. \
  Perform a full code review: check correctness, test coverage, style, potential bugs, and adherence to the plan. \
  Screenshot check: if the PR title starts with 'feat' or 'fix', verify the PR description contains at least one rendered image (Markdown ![...](URL) or HTML <img> tag) proving the change works. \
  For PRs that modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/ WinForms): screenshots MUST show the ACTUAL running application GUI with controls and data visible — NOT fabricated terminal-output images drawn on a blank background. Verify the screenshot content is RELEVANT to the behavior change (e.g., a Class Editor fix should show the Class Editor with populated data). \
  For PRs that only modify non-GUI files (Core, CLI, Tests): CLI terminal output or test run screenshots are acceptable proof. \
  Accept valid image sources: GitHub attachments, default-branch `raw.githubusercontent.com/{owner}/{repo}/master/...` links, or `blob/master/...` paths with `?raw=1`. \
  REJECT feature-branch URLs (`blob/{feature-branch}/...` or `raw.githubusercontent.com/{owner}/{repo}/{feature-branch}/...`) — these break after branch deletion. Flag them as a blocking issue. \
  Treat a Screenshots section as missing if it contains only placeholder URLs, only HTML comments, or no rendered images at all. Flag missing or invalid screenshots as a blocking issue for feat/fix PRs. \
  For docs/chore PRs (title starts with 'docs' or 'chore'), screenshots are optional — do NOT flag their absence. \
  GUI Test Report check: inspect the changed files list — if the PR modifies any GUI file under FEBuilderGBA.Avalonia/ or FEBuilderGBA/ (WinForms) AND the title starts with 'feat' or 'fix', verify the PR description contains a '## GUI Test Report' section with actual test results (a results table with pass/fail entries). \
  Files under FEBuilderGBA.Core/, FEBuilderGBA.CLI/, FEBuilderGBA.Tests/, FEBuilderGBA.Core.Tests/, FEBuilderGBA.E2ETests/, and FEBuilderGBA.SkiaSharp/ are NOT GUI files — do not count them. \
  Treat a GUI Test Report section as missing if it contains only HTML comments, only placeholder text, or no results table. Flag missing GUI test report as a blocking issue for qualifying GUI feat/fix PRs. \
  For PRs that do not modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/), or for docs/chore/refactor PRs, do NOT require a GUI test report. \
  Test plan check: verify the '## Test plan' section has ALL items checked [x]. Flag any unchecked [ ] items as a blocking issue — no exceptions. Also flag placeholder/template text that was not replaced (e.g., items containing angle brackets like '<what was tested>' or generic boilerplate) — each item must describe a specific test that was actually performed. \
  After you finish posting the review, prune any git worktree you created for this review: run 'git worktree prune' and 'git worktree remove --force' any checkout you made under your session-state directory. \
  Post your review as a pull request review on GitHub. \
  Include your Copilot CLI version and model at the end." \
  --autopilot --enable-all-github-mcp-tools --allow-all-tools
  ```
- **Branch B (Copilot CLI → in-session board)** — convene the cross-model board per **Developer & Reviewer Roles → Branch B**, passing `gh pr diff <N> -R laqieer/FEBuilderGBA` + the PR body + changed-files list + the **accepted plan** comment + the **issue** body as the artifact, and applying the **same** rubric the Branch-A prompt above encodes (screenshot validity + feature-branch-URL rejection, `## GUI Test Report`, `## Test plan` all-`[x]`, scope creep). Post the consolidated verdict as a clearly-labeled `## Cross-Model Review Board` PR comment. **Never** `agency cc`.
- **Verify the Review Gate posted on the PR:**
  - **Branch A** — a `Copilot`-bot review carrying the required footer:
    ```bash
    # Get the latest Copilot review (filter by bot author and check for footer)
    gh api repos/laqieer/FEBuilderGBA/pulls/<N>/reviews \
      --jq '[.[] | select(.user.login == "Copilot" or .user.login == "copilot" or .user.type == "Bot")] | .[-1].body'
    # The output MUST contain both "Copilot CLI:" and "Model:" lines
    ```
  - **Branch B** — the developer-posted `## Cross-Model Review Board` PR comment (a `User`, not a bot), detected by the `Review Board:` marker on the **comments** endpoint:
    ```bash
    gh api repos/laqieer/FEBuilderGBA/issues/<N>/comments \
      --jq '[.[] | select(.body | contains("Review Board:"))] | .[-1].body'
    # The output MUST contain "Review Board:" plus the "Copilot CLI:" / "Model:" footer lines
    ```

Address feedback in categories:

| Category | Action |
|----------|--------|
| **Code fix needed** | Fix the code, push new commit |
| **Scope overreach** | Update PR body, change `Closes` to `Ref` |
| **Missing feature** | Add it if in plan scope, otherwise note as future work |
| **Dead/conflicting UI** | Remove it (e.g., don't reintroduce removed features) |
| **Needs rebase** | Rebase onto default branch, resolve conflicts, `git push --force-with-lease`, then re-trigger the Review Gate |

**After each push, check for ALL feedback across all three channels:**

**CRITICAL: Reviewers (human, Copilot bot, Copilot CLI) may post feedback in THREE places: issue-level comments, inline review threads, AND top-level PR review bodies. You MUST check all three or you will miss feedback.**

**Step A — Check issue-level conversation comments:**
```bash
gh api repos/laqieer/FEBuilderGBA/issues/<N>/comments --jq '.[] | select(.user.login != "github-actions[bot]") | "\(.user.login): \(.body | split("\n")[0])"'
```

**Step A2 — Check top-level PR review bodies** (separate from inline threads):
```bash
gh api repos/laqieer/FEBuilderGBA/pulls/<N>/reviews --jq '.[] | "\(.user.login) [\(.state)]: \(.body | split("\n")[0])"'
```
Address every comment from all three channels before proceeding.

**Step B — Find all unresolved inline review threads** (use `first: 100`; paginate if `hasNextPage` is true):
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
- Fix ALL issues raised — from all three channels: issue comments, PR review bodies, AND inline threads
- Push fixes as new commits (not amends)
- **After each push, wait for the GitHub Copilot bot auto-review to complete** (typically 2-5 minutes). Verify by checking for a new review with a timestamp after your push:
  ```bash
  gh api repos/laqieer/FEBuilderGBA/pulls/<N>/reviews \
    --jq '[.[] | select(.user.login == "Copilot" or .user.type == "Bot")] | .[-1].submitted_at'
  ```
- Re-run ALL THREE checks from step 10 (issue comments + review bodies + inline threads) to catch **newly posted** feedback
- Resolve all review threads after addressing them
- Re-trigger the Review Gate using the same branch mechanism from step 10
- Repeat until: **all inline review threads resolved AND no unaddressed feedback in issue comments or PR review bodies**

**Exit condition (by branch):**
- **Branch A** — Copilot CLI posts a review with no blocking concerns AND includes its version/model footer in this exact format:
  ```
  Copilot CLI: <version>
  Model: <display-name> (<model-id>)
  ```
  Example: `Copilot CLI: 1.0.6-0` / `Model: GPT-5.4 (gpt-5.4)`. Both lines must be present at the end of the review body.
- **Branch B** — the developer posts a `## Cross-Model Review Board` PR comment with no member reporting blocking, carrying a `Review Board:` roster line immediately above the same 2-line `Copilot CLI:` / `Model:` footer.

---

## PHASE 5 — MERGE COMPLETION LOOP

**This phase is a loop. Continue until the PR state is MERGED.**

### 12. Pre-Merge Checklist
Before attempting merge, verify ALL of these:
- [ ] The **Review Gate** posted its signoff on the PR with **no blocking concerns** — **Branch A:** a `Copilot`-bot review with the `Copilot CLI: <version>` + `Model: <name>` footer; **Branch B:** a `## Cross-Model Review Board` comment with the `Review Board:` roster line + the `Copilot CLI:` / `Model:` footer, no member blocking
- [ ] All feedback addressed across all three channels (issue comments, PR review bodies, inline threads)
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
| **CI checks failed** | `gh run view <RUN_ID> -R laqieer/FEBuilderGBA --log-failed` | Fix the failing test/build, push, re-trigger the Review Gate |
| **Unresolved feedback** | Run all three checks from step 10 (issue comments + review bodies + inline threads) | Address all feedback and resolve threads |
| **Branch outdated** | `gh pr view <N> -R laqieer/FEBuilderGBA --json mergeStateStatus --jq .mergeStateStatus` shows `BEHIND` | `git fetch origin master && git rebase origin/master && git push --force-with-lease`, then re-trigger the Review Gate |
| **Merge conflicts** | `gh pr view <N> -R laqieer/FEBuilderGBA --json mergeable` | `git rebase origin/master && git push --force-with-lease`, then re-trigger the Review Gate (rebase can introduce changes) |
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
- **Prune ALL stale worktrees** (standing post-merge step — prevents disk-space leaks). This includes your own implementation worktree(s) AND any merged-PR Copilot CLI review checkouts under `~/.copilot/session-state/*/files/pr*` (their PRs are merged → safe to remove):
  ```bash
  # 1. Ensure all changes are committed or discarded — git worktree remove fails on a dirty worktree
  # 2. Navigate back to the main repo root (you cannot remove a worktree from inside it)
  cd /path/to/main/repo
  git worktree list                       # inspect every registered worktree FIRST
  git worktree remove --force <path>      # remove your own implementation worktree(s)
  git worktree remove --force <path>      # remove merged-PR Copilot CLI review checkouts (~/.copilot/session-state/*/files/pr*)
  git worktree prune                      # drop stale registrations
  ```
  **WARNING:** When scripting bulk removal, NEVER `rm -rf` a path you have not explicitly confirmed is a linked worktree — a buggy loop can delete the main checkout. Prefer `git worktree remove --force` for registered worktrees. Only fall back to `rm -rf -- <path>` for an exact, verified ORPHAN checkout directory under `~/.copilot/session-state/<id>/files/pr*` — after printing/confirming the path and verifying it is NOT the main checkout, NOT your current working directory, and NOT an active/unmerged review checkout. NEVER `rm -rf` the whole `~/.copilot/session-state/<id>` directory (it may hold unrelated active session state), and NEVER `rm -rf` a glob-derived path that was not first printed and confirmed.
- No need to checkout or pull master — just run `git fetch origin` before creating the next worktree (step 7) to ensure remote refs are current.

---

## Gap-sweep refresh

The `--gap-sweep-*` family (issue [#374](https://github.com/laqieer/FEBuilderGBA/issues/374)) produces static-analysis reports under
`docs/avalonia-gaps/`. Baselines are committed on phase PRs and on fix PRs
that explicitly close one or more gaps. Between baselines, the advisory CI
job (`gap-sweep-advisory` in `.github/workflows/check.yml`) regenerates the
static-analysis reports on every PR and uploads them as workflow artifacts +
posts a job summary — those artifact runs do NOT re-commit.

### When to re-baseline

Re-commit a baseline when ANY of:
- A new sweep phase ships
- A fix PR removes ≥1 entry from the previous baseline
- A scanner heuristic improves (false-positive/false-negative correction)

### Local regeneration

```powershell
# All static-analysis sweeps in one shot:
dotnet run --project FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj -c Release `
    -- --gap-sweep-all --out=docs/avalonia-gaps/$(Get-Date -Format yyyy-MM-dd)
# Individual sweeps:
dotnet run --project FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj -c Release `
    -- --gap-sweep-density --out=docs/avalonia-gaps/$(Get-Date -Format yyyy-MM-dd)-density-sweep.md
```

### CI advisory job

`gap-sweep-advisory` runs on every PR with `continue-on-error: true`. It does
NOT block merge today; once the baseline has been clean for ≥2 weekly
refreshes the maintainer flips it to blocking via a follow-up PR.

---

## ANTI-PATTERNS (Learned from Experience)

### Don't: Batch-merge and hope for the best
Each merge changes master. Merging 5 PRs at once creates 5 rebase cascades.
**Do:** Merge one at a time, rebase the next onto updated master.

### Don't: Claim `Closes #N` for partial work
Copilot CLI (Branch A) or the cross-model board (Branch B) will flag this every time.
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
"All checks pass" and "the Review Gate signed off" doesn't mean done — the merge itself can fail due to branch policies, ruleset requirements, or race conditions.
**Do:** Always run `gh pr view <N> -R laqieer/FEBuilderGBA --json state --jq .state` and confirm the output is `MERGED`. If not, diagnose and fix.

### Don't: Ignore non-inline feedback
Reviewers (human, Copilot bot, Copilot CLI) post feedback in three places: issue-level comments, top-level PR review bodies, and inline threads. Checking only inline threads misses the other two channels.
**Do:** After each push, check all three channels (step 10: issue comments, review bodies, inline threads). Address all feedback before re-triggering review or attempting merge.

### Don't: Merge before the Review Gate posts its signoff on the PR
A local-only review doesn't count — the review must be visible on GitHub.
**Do:** Branch A — use `--enable-all-github-mcp-tools --allow-all-tools` so Copilot CLI can post via GitHub MCP tools (verify with `gh api repos/.../pulls/<N>/reviews`). Branch B — post the `## Cross-Model Review Board` comment yourself via `gh` (verify the `Review Board:` marker on the issue/PR comments endpoint).

### Don't: Force-push without `--force-with-lease`
**Do:** Always use `--force-with-lease` to avoid overwriting someone else's work.

### Don't: Work directly on master
Committing to master means no PR review, no Review Gate, and no clean revert path.
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
A `feat` or `fix` PR without visual proof is incomplete. Review Gate reviews (either branch) are expected to flag missing screenshots as a blocking issue for these PR types.
**Do:** For feat/fix PRs, always capture **real GUI screenshots** from the running application using `PrintWindow` API (`tools/capture-window.cs`) or MCP. For `docs`/`chore` PRs, screenshots are optional.

### Don't: Fabricate screenshots
Drawing text on a Bitmap with `System.Drawing.DrawString` (e.g., "VALIDATION PASSED" in green on black) is NOT a screenshot — it proves nothing about the GUI working. It's cheating the review process.
**Do:** Use `PrintWindow` API to capture real window content. It works even with a locked screen. Combine with PowerShell `UIAutomationClient` for headless navigation.

### Don't: Use the generic main Avalonia window as a screenshot
The main FEBuilderGBA hub window with category buttons proves nothing about whether a specific editor fix works. A PR fixing MapTerrain needs a MapTerrain screenshot, not the main menu.
**Do:** Navigate to the SPECIFIC affected editor using UIAutomation (`powershell Add-Type -AssemblyName UIAutomationClient; $btn.GetCurrentPattern([InvokePattern]::Pattern).Invoke()`), then capture THAT editor window with `tools/WinCapture`. Every feat/fix screenshot must show populated data in the changed editor.

### Don't: Use feature-branch URLs (blob or raw) for screenshot images
`blob/{feature-branch}/file.png?raw=1` or `raw.githubusercontent.com/{owner}/{repo}/{feature-branch}/file.png` becomes a 404 after the branch is deleted post-merge. Every screenshot in the PR breaks permanently.
**Do:** Commit screenshots to `pr-screenshots/` on master (via a docs PR) BEFORE referencing them. Use `blob/master/pr-screenshots/...` URLs or `raw.githubusercontent.com/{owner}/{repo}/master/pr-screenshots/...` URLs. Or use GitHub asset uploads which produce permanent `user-attachments/assets/...` URLs.

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

## Autonomous Daily Maintenance Routine

An unattended agent (GitHub Copilot CLI) runs a **daily maintenance routine** over this fork. It executes end-to-end without asking for confirmation, following every rule in this document. The routine has five steps:

1. **Open PRs** — cross-model review each; post a consolidated review comment. Merge if ready with no concerns; close with a posted reason if unacceptable; leave open with actionable feedback if author work is needed; edit the branch directly if `maintainerCanModify` and a small change makes it mergeable.
2. **Discussions** — review all open discussions and any new replies; reply; check Google Docs / image links; create issues to track surfaced bugfixes/feature-requests (no coding in this step).
3. **Issues** — resolve **and reply to every open issue, one by one**, via the full workflow above (plan → Review Gate → worktree → tests → PR → Review Gate → all-channel comment resolution → merge). Prioritize issues that break core tooling (in-app bug reporter, build, CLI).
4. **Docs / wiki** — update README, `docs/`, and the wiki whenever code or behavior changed.
5. **Release** — cut a tag-triggered release when warranted.

### Non-negotiable rules for the routine

- **Never stop early, never ask.** Resolve every open issue no matter how long it takes; make the best safe decision and proceed. Only merge/close on clearly-met criteria — otherwise leave the item open with feedback.
- **Mandatory completion loop.** After clearing the current issue list, **re-scan** `gh issue list -R laqieer/FEBuilderGBA --state open` (and `gh pr list -R laqieer/FEBuilderGBA --state open`). New issues/PRs frequently appear *during* processing (e.g. from ongoing user testing). Keep resolving until **both open issues and open PRs are zero**. Never declare the routine complete while any open issue or PR remains.
- **All three PR feedback channels** must be cleared before merge: unresolved inline review threads (including the `copilot-pull-request-reviewer` bot), PR-level comments, and top-level review bodies. Fix every point, reply, and resolve the threads.

---

## QUICK REFERENCE

```
Issue → Plan Comment → Review Gate → Revise → Accept
  → Branch → Implement → Tests → Push
  → PR → Review Gate + Bot Comments → Fix All → Resolve Threads
  → Re-review → Signoff → CI Green → Merge → Confirm MERGED
  ↑___________________________________________|  (loop until MERGED)
  → Prune ALL stale worktrees (own + Copilot review checkouts)
```

**All `gh` commands MUST use `-R laqieer/FEBuilderGBA`.**
**This repo's default branch is `master`.**

**When using Claude Code / Copilot automation:**
- Commits as `laqieer <laqieer@126.com>`
- PR-body footer is **developer-dependent**:
  - **Claude Code CLI:** `Generated with Claude Code (<model>)`
  - **Copilot CLI:** `Copilot CLI: <version>` then `Model: <display-name> (<model-id>)`

**Human contributors:** Use your own identity and commit signing workflow.
