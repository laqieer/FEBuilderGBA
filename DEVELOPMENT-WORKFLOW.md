# Claude Code + Copilot CLI — Mandatory Development Workflow

You MUST follow this workflow strictly.
Do NOT skip steps.
Do NOT start coding until explicitly allowed.

The human developer owns all final decisions.
Your role is to assist, not override this process.

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
- Copilot CLI checks for:
  - Design gaps or missing components
  - Risky assumptions about existing code
  - Missing test coverage or edge cases
  - Scope creep (claiming to close issues the plan doesn't fully address)
  - Better alternatives or simpler approaches

### 5. Revise Plan Based on Feedback
- **Edit** the original issue comment (don't create new ones)
- Address every point raised — either fix it or explain why not
- Do NOT write code during revision

### 6. Iterate Until Accepted
Repeat steps 4-5 until Copilot CLI reports **no blocking concerns**.

**Exit condition:** Plan explicitly accepted with no unresolved design issues.

---

## PHASE 3 — IMPLEMENTATION

### 7. Branch & Implement

**Branch naming:** `feat/<short-desc>-<issue>` or `fix/<short-desc>-<issue>`

**Parallel execution rules:**
- Work units with **no file overlap** → launch as parallel worktree agents
- Work units with **file overlap** → implement sequentially or in same agent
- After parallel agents complete, merge changes into a single branch and resolve conflicts before pushing

**Commit discipline:**
- One logical change per commit
- Commit messages reference the issue: `feat: add X (#N)` or `fix: resolve Y (#N)`
- Commit as `laqieer <laqieer@126.com>`
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

### 10. Copilot CLI PR Review
Address feedback in categories:

| Category | Action |
|----------|--------|
| **Code fix needed** | Fix the code, push new commit |
| **Scope overreach** | Update PR body, change `Closes` to `Ref` |
| **Missing feature** | Add it if in plan scope, otherwise note as future work |
| **Dead/conflicting UI** | Remove it (e.g., don't reintroduce removed features) |
| **Needs rebase** | Rebase onto master, resolve conflicts, force push |

### 11. Iterate Until Approved
- Fix all issues raised
- Push fixes as new commits (not amends)
- Re-trigger Copilot CLI review
- Repeat until: **no unresolved Copilot CLI comments**

**Exit condition:** Copilot CLI has no blocking concerns on the latest diff.

---

## PHASE 5 — MERGE & FINALIZATION

### 12. Pre-Merge Checklist
Before merge, verify:
- [ ] All CI checks green (build + E2E for all ROM variants)
- [ ] Branch is up to date with master (rebase if needed)
- [ ] No merge conflicts
- [ ] Copilot CLI review has no unresolved concerns
- [ ] PR body accurately reflects what was delivered

### 13. Merge Strategy
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

### 14. Post-Merge
- Verify the issue was auto-closed (if `Closes #N` was used)
- Pull latest master: `git fetch origin master`

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

### Don't: Force-push without `--force-with-lease`
**Do:** Always use `--force-with-lease` to avoid overwriting someone else's work.

---

## QUICK REFERENCE

```
Issue → Plan Comment → Copilot Review → Revise → Accept
  → Branch → Implement → Tests → Push
  → PR → Copilot Review → Fix → Approve
  → Rebase → CI Green → Merge → Close Issue
```

**All `gh` commands MUST use `-R laqieer/FEBuilderGBA`.**
**All commits as `laqieer <laqieer@126.com>`.**
**All PR bodies end with `Generated with Claude Code (claude-opus-4-6)`.**
