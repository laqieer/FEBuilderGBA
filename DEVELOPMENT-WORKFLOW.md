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

**Always sync master before branching (from the main repo, not a linked worktree):**
```bash
# Run this in the main repo root — not inside a linked worktree
git checkout master && git pull
git checkout -b feat/<short-desc>-<issue>   # or fix/<short-desc>-<issue>
```
For parallel worktree agents: sync master in the main repo first, then create worktrees from it.

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

### 8. Scope Accuracy Check (Before PR)
Before opening the PR, verify:
- [ ] Does the PR fully deliver what the referenced issues require?
- [ ] If partial, use `Ref #N (partial — <what's missing>)` instead of `Closes #N`
- [ ] Don't claim to close issues that require more work beyond this PR

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
<!-- For feat/fix PRs: MANDATORY — replace this comment with actual screenshot(s).
     Acceptable proof: UI screenshot, CLI/terminal output, test run output, or before/after diff.
     For docs/chore PRs: This entire section may be deleted. -->

## Test plan
- [x] <what was tested>
- [ ] <what needs manual verification>

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
- **Screenshots are MANDATORY for `feat` and `fix` PRs** — include screenshot(s) proving the feature or bugfix works. Acceptable evidence: UI screenshot, CLI/terminal output capture, test run output, or before/after diff screenshot. For `docs` and `chore` PRs (documentation, gitignore, CI config, dependency bumps, etc.), screenshots are optional and the Screenshots section may be omitted entirely.

### 10. Copilot CLI PR Review + Resolve ALL Comments
- **Invocation** — trigger review and ensure it posts on the PR:
  ```bash
  copilot -p "Review pull request #<N> in laqieer/FEBuilderGBA. \
  Perform a full code review: check correctness, test coverage, style, potential bugs, and adherence to the plan. \
  Screenshot check: if the PR title starts with 'feat' or 'fix', verify the PR description contains at least one rendered image (Markdown ![...](URL) or HTML <img> tag) proving the feature/bugfix works. \
  Accept valid image sources: GitHub attachments, raw.githubusercontent.com links, relative repo paths, or blob URLs with ?raw=1. \
  Treat a Screenshots section as missing if it contains only placeholder URLs (e.g., 'replace-with-actual-url', 'url', empty URLs), only HTML comments, or no rendered images at all. Flag missing screenshots as a blocking issue for feat/fix PRs. \
  For docs/chore PRs (title starts with 'docs' or 'chore'), screenshots are optional — do NOT flag their absence. \
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
- If working in a worktree, clean it up:
  ```bash
  # 1. Ensure all changes are committed or discarded — git worktree remove fails on a dirty worktree
  # 2. Navigate back to the main repo root (you cannot remove a worktree from inside it)
  cd /path/to/main/repo
  git worktree list             # verify which worktrees exist
  git worktree remove <path>    # remove the linked worktree
  ```
- Switch back to master and sync (from the main repo):
  ```bash
  git checkout master && git pull
  ```

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
**Do:** Always create a feature branch from an up-to-date master: `git checkout master && git pull && git checkout -b feat/...`

### Don't: Guess why merge is blocked
Assuming "needs approval" when the real cause is an outdated branch or unresolved threads wastes time and misses the actual fix.
**Do:** Diagnose systematically — check all causes in order: unresolved threads → CI status → branch up-to-date → review approvals. Use `gh pr view <N> -R laqieer/FEBuilderGBA --json mergeStateStatus,statusCheckRollup` to get the actual block reason.

### Don't: Open a feat/fix PR without screenshots
A `feat` or `fix` PR without visual proof is incomplete. Copilot CLI reviews are expected to flag missing screenshots as a blocking issue for these PR types.
**Do:** For feat/fix PRs, always capture and attach screenshot(s) to the PR description before requesting review. For `docs`/`chore` PRs, screenshots are optional.

---

## QUICK REFERENCE

```
Issue → Plan Comment → Copilot Review → Revise → Accept
  → Branch → Implement → Tests → Push
  → PR → Copilot Review + Bot Comments → Fix All → Resolve Threads
  → Re-review → Signoff → CI Green → Merge → Confirm MERGED
  ↑___________________________________________|  (loop until MERGED)
  → Clean up worktree (if any) → Checkout master & pull
```

**All `gh` commands MUST use `-R laqieer/FEBuilderGBA`.**
**This repo's default branch is `master`.**

**When using Claude Code / Copilot automation:**
- Commits as `laqieer <laqieer@126.com>`
- PR bodies end with `Generated with Claude Code (claude-opus-4-6)`

**Human contributors:** Use your own identity and commit signing workflow.
