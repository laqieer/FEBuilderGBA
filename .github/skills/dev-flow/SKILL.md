---
name: dev-flow
description: MANDATORY for code, configuration, documentation, workflow, or PR changes in this repository. Enforces issue planning, risk-based independent review, isolated implementation, validation, PR feedback resolution, merge, and cleanup.
---

# FEBuilderGBA Development Flow

Invoke this skill before creating a branch or changing repository files. The source of truth for detailed edge cases is `DEVELOPMENT-WORKFLOW.md`; do not load that whole file unless a specific step requires it.

## 1. Issue and accepted plan

1. Every change needs a fork issue: `gh ... -R laqieer/FEBuilderGBA`.
2. Post one implementation-plan comment containing scope, non-goals, files, tests, risk, and rollback.
3. Classify the planned paths with `python scripts/classify_review_risk.py <paths...>`. Missing or invalid input is `high`.
4. Complete the plan gate:
   - `low`: deterministic classifier/checklist.
   - `normal`: one reviewer using a different model provider.
   - `high`: two reviewers from distinct providers; add `security-review` when security-relevant.
5. Post the consolidated verdict with `Review Tier`, classifier result, reviewer IDs when required, and the runtime footer. Do not implement until no blocking concerns remain.

Reviewers fetch issue/plan content from identifiers in their own isolated context. Never paste full bodies, diffs, logs, or images into the coordinator prompt. Reports are findings plus citations, normally under 4 KiB and never over 8 KiB.

## 2. Isolated implementation

1. In the shared main checkout, only `git fetch origin` is allowed.
2. Create an isolated worktree from `origin/master`; never switch, stash, reset, or implement in the main checkout.
3. Configure commits as `laqieer <laqieer@126.com>`.
4. Implement only the accepted scope. Use test-first development for behavior and script changes.
5. Preserve repository invariants:
   - Shared ROM logic belongs in `FEBuilderGBA.Core`.
   - New GUI features target Avalonia; WinForms receives bug fixes only.
   - ROM mutations use pointer-aware APIs and undo scopes.
   - Headless command routing remains before GUI initialization.

## 3. Validation and commit

Run the smallest existing checks that cover the change, then all directly affected test projects. GUI changes require actual application validation and a real screenshot of the affected editor. Non-GUI changes use tests and CI; screenshots are not required.

Update README/docs only when behavior, interfaces, setup, or contributor workflow changes.

Before the first commit, attempt:

```powershell
pre-commit install --hook-type commit-msg --install-hooks
```

Never install or bypass the unauthenticated ggshield hook. Commit one logical change with:

```text
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
Copilot CLI: <version>
Model: <display-name> (<model-id>)
```

Push immediately after every commit.

## 4. Pull request and review

Open the PR against `laqieer/FEBuilderGBA` with:

- Summary and accepted-plan URL.
- Accurate `Closes` or `Ref` scope.
- All test-plan items checked `[x]`.
- `## GUI Test Report` plus a real GitHub-attachment screenshot only for GUI `feat`/`fix` changes.
- Runtime footer.

Classify the actual base-to-head changed paths and run the matching PR gate using the same tier rules as the plan. Any higher actual tier supersedes the plan tier.

Check and clear all three feedback channels after every push:

1. PR conversation comments.
2. Top-level review bodies.
3. Unresolved inline review threads.

Fix valid findings, reply, resolve threads, rerun affected tests, and repeat review after material changes.

## 5. Merge and cleanup

Before merge require:

- Current no-blocking review signoff.
- Required CI green.
- Branch up to date and conflict-free.
- All three feedback channels clear.
- PR body still accurate.

Merge, verify the PR state is `MERGED`, then check post-merge `master` CI. Remove the exact verified worktree with `git worktree remove --force`, prune registrations, and delete the merged local branch. Never recursively delete an unresolved or glob-derived path.

## Mandatory GitHub footer

Every Copilot-authored GitHub post and commit message ends with:

```text
Copilot CLI: <version>
Model: <display-name> (<model-id>)
```
