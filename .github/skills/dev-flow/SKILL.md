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

For every normal/high plan or PR board, follow [dynamic reviewer selection](../../reviewer-selection.md) and the [workflow review gates](../../../DEVELOPMENT-WORKFLOW.md#review-gates). Capture a fresh list from the actual dispatch tool, resolve fields metadata-first with the approved `dynamic-R1` fallback, and select the newest eligible comparable version per eligible configured publisher, excluding any resolved developer publisher. New matching versions/variants require no per-ID approval or source edit; snapshots and examples are audit outputs, never allowlists.

For unresolved developer identity, use only the bounded available-evidence Auto path: establish Auto from trusted current session-bound evidence, check and record the trusted current identity sources actually available (provenance, relevant values/results and session/time binding where available), and recheck before dispatch. If any source resolves the publisher, exclude it. Otherwise established Auto may proceed without a per-board waiver or exhaustive-source-inventory proof, recording exactly `developer_publisher=unknown`, `developer_exclusion=not_applied`, `reviewer_diversity=among_reviewers_only`, and `developer_independence=unconfirmed`. Normal still requires one eligible reviewer provider; HIGH requires two distinct reviewer providers. A reviewer may share the unknown developer publisher; claim no developer independence or execution attestation. Partial/stale/ambiguous/contradictory authoritative identity, or unresolved publisher without established Auto, blocks. Screenshots, UI/footer, prose/history, self-reports and prior switch notices are not identity authority.

Keep individual unranked/unknown entries excluded with diagnostics, not publisher-wide vetoes. Genuine metadata conflicts or incomparable eligible versioned families still block. Preserve numeric ordering, deterministic ties, stage priorities, and the required distinct-provider counts. Recheck before dispatch; freeze dispatched evidence and never silently substitute after failure.

Record the exact reviewed revision, immutable ruleset/approval references, source/time/snapshot digest, rankings/exclusions, per-field provenance, reviewer IDs, matching requested/registry configured IDs, successful completion, and substantive verdicts. Approved `configured-only` evidence means `execution_identity=unconfirmed`, not backend attestation; execution-confirmed needs authoritative matching execution fields. Missing authority, mismatches, missing completed registry records, or substantive findings block. Material rule/evidence changes require renewed approval; all safeguards and high-risk governance classification remain.

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

Run the smallest existing checks that cover the change, then all directly affected test projects. GUI `feat`/`fix` changes require a connected live application with representative data, exercise of the affected editor, a GUI Test Report, and a fresh GitHub-attached screenshot of that editor. If RDP was disconnected, reconnect and verify the current visible application is responsive before capture; failed reconnect or application verification leaves proof unmet and blocks completion. Black, stale, lock-screen, unrelated or API-success-only captures are not substitutes. Non-GUI changes use tests and CI; screenshots are not required.

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

Classify the actual base-to-head changed paths and run the matching PR gate using the same tier rules as the plan, with fresh dynamic discovery and reviews against the exact head. Any higher actual tier supersedes the plan tier. After material revisions, refresh discovery, rerun affected checks, and obtain current review signoff.

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
