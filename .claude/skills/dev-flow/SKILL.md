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
3. **Trigger Copilot CLI review** of the plan:
   ```bash
   copilot -p "Review the plan comment on issue #<N> in laqieer/FEBuilderGBA. Post your review findings as a comment on the issue. Include your Copilot CLI version and model at the end." --autopilot --enable-all-github-mcp-tools --allow-all-tools
   ```
4. **Iterate** until Copilot CLI accepts the plan with no blocking concerns.

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

## Phase 3 — PR & Review Loop

10. **Open PR** via `gh pr create -R laqieer/FEBuilderGBA`. Include:
    - Summary, plan reference, scope (Closes/Ref), test plan (ALL items `[x]`), screenshots
    - **Screenshots**: GUI PRs require real GUI captures (PrintWindow or MCP); non-GUI PRs accept CLI/test output. NEVER fabricate images. NEVER use `blob/{feature-branch}/` URLs.
    - **Test plan**: ALL items must be `[x]`. Automatable tests MUST be automated — no unchecked "manual later" items.
    - Footer: `Generated with Claude Code (claude-opus-4-6)`
11. **Trigger Copilot CLI review** of the PR:
    ```bash
    copilot -p "Review pull request #<N> in laqieer/FEBuilderGBA. Perform a full code review: check correctness, test coverage, style, potential bugs, and adherence to the plan. Screenshot check: if the PR title starts with 'feat' or 'fix', verify the PR description contains at least one rendered image (Markdown ![...](URL) or HTML <img> tag) proving the change works. For PRs that modify GUI files (FEBuilderGBA.Avalonia/ or FEBuilderGBA/ WinForms): screenshots MUST show the ACTUAL running application GUI — NOT fabricated terminal-output images. For non-GUI PRs: CLI/test output screenshots are acceptable. Accept valid image sources: GitHub attachments, raw.githubusercontent.com links, or blob/master/ paths with ?raw=1. REJECT blob/{feature-branch}/ URLs — they break after branch deletion. For docs/chore PRs, screenshots are optional. GUI Test Report check: if the PR modifies GUI files AND title starts with 'feat' or 'fix', verify a '## GUI Test Report' section with actual results table exists. For non-GUI or docs/chore PRs, do NOT require it. Test plan check: verify the '## Test plan' section has ALL items checked [x]. Flag any unchecked [ ] items as blocking — unchecked test cases mean the PR is not fully validated. If an unchecked item could be automated, flag it as 'should be automated, not left for manual'. Post your review as a pull request review on GitHub. Include your Copilot CLI version and model at the end." --autopilot --enable-all-github-mcp-tools --allow-all-tools
    ```
12. **Check for ALL comments** — both inline threads AND issue-level conversation comments:
    ```bash
    # Inline review threads (Copilot bot + CLI)
    gh api graphql -f query='{ repository(owner:"laqieer",name:"FEBuilderGBA") { pullRequest(number:<N>) { reviewThreads(first:100) { nodes { id isResolved comments(first:1) { nodes { path line body } } } } } } }' --jq '.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved==false) | "\(.id) [\(.comments.nodes[0].path):\(.comments.nodes[0].line)] \(.comments.nodes[0].body | split("\n")[0])"'
    # Issue-level conversation comments (human reviewer feedback — DO NOT IGNORE)
    gh api repos/laqieer/FEBuilderGBA/issues/<N>/comments --jq '.[] | select(.user.login != "github-actions[bot]") | "\(.user.login): \(.body | split("\n")[0])"'
    ```
    **CRITICAL: Always check BOTH.** Human comments appear as issue comments, not inline threads. Ignoring them is a recurring failure mode.
13. **Fix code** for every comment (never dismiss with "acknowledged"), push, wait for re-review.
14. **Repeat 11-13** until no unresolved threads AND no unaddressed conversation comments.

## Phase 4 — Merge

15. **Pre-merge checklist**: CI green, branch up-to-date, no unresolved threads.
16. **Merge**: `gh pr merge <N> -R laqieer/FEBuilderGBA --merge`
17. **Confirm**: `gh pr view <N> -R laqieer/FEBuilderGBA --json state --jq .state` — must be `MERGED`.
18. If not MERGED, diagnose and fix (see DEVELOPMENT-WORKFLOW.md), loop back to step 15.
19. **Clean up worktree** after merge.

## Hard Rules

- **ALL `gh` commands use `-R laqieer/FEBuilderGBA`** — never target upstream
- **ALL commits as `laqieer <laqieer@126.com>`** — never use any other identity in this repo
- **ALL implementation in isolated worktrees** — never `git checkout`/`stash`/`switch` in main worktree
- **Screenshots MANDATORY for feat/fix PRs** — GUI PRs need real GUI captures (PrintWindow/MCP); non-GUI PRs accept CLI/test output. NEVER fabricate images. NEVER use branch blob URLs.
- **ALL test plan items must be `[x]`** — unchecked items block merge. Automate everything automatable.
- **Push immediately after every commit**
