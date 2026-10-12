# Dynamic reviewer selection

Ruleset: **`dynamic-R1`**. This is a version-independent selection and evidence policy, not an inventory of approved model IDs. Apply it with the [workflow review gates](../DEVELOPMENT-WORKFLOW.md#review-gates).

Each new board discovers the actual dispatch tool's current choices separately for every required reviewer slot/agent type. It ranks all eligible model IDs within each configured publisher and assigns the **newest among eligible comparable live entries** to each slot, subject to pairwise-distinct configured model IDs when multiple slots are required. New matching numeric releases and variants become eligible automatically, without source edits, inventory rows, or per-ID maintainer approval. No exact-ID allowlist, maximum version, fixed preferred model, approved-version table, cached candidate list, or unconditional older-model fallback is permitted. Newest is not a claim of highest quality.

## Authority and bootstrap

- Canonical authority: [issue #2153, plan v5](https://github.com/laqieer/FEBuilderGBA/issues/2153#issuecomment-5553800486), normalized SHA-256 `7173e0a1e6cb37714811fe1b9754d72d40a7adb62c12ed7a59e1a48f00ef5180` (UTF-8 comment body, CRLF to LF, `TrimEnd`).
- Separate [explicit maintainer approval](https://github.com/laqieer/FEBuilderGBA/issues/2153#issuecomment-5554161089) by `laqieer`: its **unquoted** text names that exact v5/dynamic-R1 digest and approves live discovery, generic publisher/version interpretation, capability fallback, automatic future matching versions, `configured-only` evidence with unconfirmed backend execution, and v5 section 7's bounded bootstrap.
- A preference statement, issue creation, reviewer approval, agent-generated post under a maintainer account, quoted approval/template, or future intention is not maintainer authorization. Verify the author and the separate explicit approval itself. An agent cannot author or infer its own approval. External documentation may inform the maintainer but cannot authorize a new interpretation.
- Bind each board to the approved plan digest, approval permalink, and this ruleset at a **full commit SHA**, with an immutable file link rather than a moving branch reference. For the initial policy PR, use its full candidate head SHA; the pre-implementation plan gate instead references the approved v5 digest because the ruleset file does not yet exist.
- Material changes to interpretation rules, selection algorithm, or evidence standard require renewed independent review and explicit maintainer approval. An ordinary new ID satisfying unchanged approved rules does not.
- [Issue #2209's separately approved plan](https://github.com/laqieer/FEBuilderGBA/issues/2209#issuecomment-6099515591), SHA-256 `f0f352978b9e249f7b595d4674f6fedd0a70e4fb98cd60a6760628c0b5c14b83` (raw UTF-8 comment body), has separate [explicit maintainer authorization](https://github.com/laqieer/FEBuilderGBA/issues/2209#issuecomment-6116969474) bound to that exact digest. It supersedes only developer-identity/publisher-exclusion and Auto-mode gating for reviewer selection, and changes multi-reviewer diversity to configured model-ID uniqueness among reviewer slots. It does not revert unrelated interface or evidence guidance from #2196, and it is not a claim of backend independence.

The initial bootstrap is only for #2153's own plan/PR evidence gates: obtain aligned v5 substantive reviews; obtain separate explicit approval before any branch or implementation; then record the formal plan gate against the exact approved revision. Prior advisory reviews count only if their frozen discovery/dispatch/registry evidence meets these rules; otherwise rerun. They never retroactively become execution attestations.

Implement only the three approved documentation files in an isolated worktree, validate, and obtain a freshly discovered PR board and ordinary CI/feedback/merge/post-merge verification. General reuse begins only after the separately reviewed policy PR merges; resume #2152 only after that merge is verified, using fresh reviews. This bootstrap authorizes no unrelated scope or agent-invented exceptions.

Changes here remain **high-risk** under the `.github/` classifier prefix. Pairwise-distinct configured reviewer model IDs when multiple slots are required, applicable security review, read-only/no-exec safety screens, required CI, all three feedback channels, branch freshness, merge confirmation, and post-merge verification remain mandatory. Roll back only through the normal reviewed process, retain audit history, and re-block work that lacks evidence under the restored rule.

## Live discovery and capabilities

1. Verify the **current selectable-model choices from the actual review-dispatch tool/runtime** for every required reviewer slot/agent type, including exact IDs, available metadata, and raw order when supplied. A live tool schema's explicit model-override choices are acceptable. If multiple slots use the same dispatch tool and have identical current selector choices, capture one immutable snapshot and reference its digest for every applicable slot while resolving capability and eligibility separately. If a tool or agent type exposes a different list, capture a separate snapshot; never merge distinct lists.
2. Use a documented inventory interface or the current tool schema; do not invent an export command. Record source, capture time, serialization/normalization, and snapshot digest. Issue prose, earlier plans, web search results, model self-reports, examples, fixtures, and past snapshots are not selectable-model sources.
3. Require explicit selectability for the chosen text/chat review agent. Prefer runtime capability fields; when absent, the approved fallback accepts the dispatch tool's explicit model choices **for that agent** as capability evidence. Record that fallback rather than inventing runtime flags. Keep the original raw enum order and shared snapshot serialization unchanged for every slot referencing the same snapshot.
4. Exclude non-text/non-review specializations and aliases/pickers. Runtime flags take precedence; the fallback also excludes IDs containing a whole hyphen-delimited `auto`, `default`, `latest`, or `picker` token. These exclusions impose no version ceiling.
5. Refresh discovery for every new board and after material revisions. Immediately before dispatch, recheck selectability for every required slot and recompute each affected slot's candidates and the whole deterministic assignment if fresh discovery changes a selection, including exposing a newer eligible version. When slots reference one shared snapshot, refresh it once and apply the resulting list to each applicable slot. Never substitute one model locally while retaining the rest of a stale multi-slot assignment; if no complete pairwise-distinct assignment remains, block before dispatch.
6. Freeze the snapshot once dispatched. A later release does not by itself invalidate an already completed review or cause endless restarts; the **next** board uses a fresh list. A post-dispatch failure or identity mismatch blocks that recorded board. Do not silently reroute to another or older model.

## Field resolution and provenance

Prefer runtime fields whose **documented semantics** identify the configured model's publisher and comparable release version. An API transport provider, gateway, client brand, or opaque build/execution identifier is not automatically such a field.

Resolve each candidate field deterministically: use semantically explicit runtime metadata first and fill only missing fields through R1. Keep provenance separately for publisher, family/version key, capability, and raw index. Use `provider_source` / `version_source=approved-rule:dynamic-R1` when the ID rule supplied them, never `runtime-metadata`. Capability fallback identifies the dispatch-tool choices as its source.

Authoritative runtime metadata contradicting the applicable rule is a blocker, not permission to hide the conflict. Missing or unrelated gateway metadata is not itself a contradiction. Reject contradictory publisher mappings, invalid authoritative keys, and conflicting duplicate IDs.

Reviewer selection does not depend on identifying the implementer's configured publisher or model, resolving Auto mode, or excluding the implementer's candidate. An implementer model may also be selected as a reviewer model. A model/session switch before or after implementation does not block or invalidate the board; once dispatched, reviewer IDs, discovery evidence, and the exact reviewed revision remain frozen. Continue to distinguish configured reviewer identity from routed or actual execution identity: this rule does not supply execution attestation or relax blocking for partial or contradictory authoritative reviewer execution identity.

Use one canonical `provider_id` for grouping, stage bias, and publisher-order ties. For R1 publishers, that key is exactly the publisher string in the table below. Runtime identity evidence must unambiguously agree with that key; an unexplained spelling/casing difference or alias is unresolved/conflicting identity, not a second publisher. Do not invent case-folding or alias mappings. A runtime-only new publisher needs a documented, unambiguous canonical key; ambiguity about whether it duplicates another publisher blocks the board. Compare canonical provider keys and model IDs with case-sensitive ordinal equality and ordinal lexical ordering, never locale-dependent comparison. Keep the original runtime value and its provenance in the audit record.

### Approved publisher and version grammar

These are literal prefixes and generic release families, **not approved model IDs**:

| Literal ID prefix | Configured publisher | Comparable release family |
| --- | --- | --- |
| `gpt-` | `OpenAI` | `gpt` |
| `gemini-` | `Google` | `gemini` |
| `grok-` | `xAI` | `grok` |
| `mai-code-` | `Microsoft` | `mai-code` |

After the matched prefix, require a numeric major, optional `.minor`, optional `.patch`, and zero or more hyphen-prefixed alphanumeric variant tokens. Require a full-string match:

```text
^<literal-prefix>(?<major>[0-9]+)(?:\.(?<minor>[0-9]+))?(?:\.(?<patch>[0-9]+))?(?:-[A-Za-z0-9]+)*$
```

Missing minor/patch components are zero. Compare `(major, minor, patch)` **numerically**, so `3.10 > 3.9`. Variant tokens add no release-version component; equal numeric releases use the tie-breaker below. There is no upper bound on numeric components and no special approval for future major/minor/patch releases or variants.

R1 deliberately replaces the unconditional ban on interpreting names with a maintainer-approved deterministic interpretation of **live selectable IDs**. Unapproved guesses and reviewer self-identification remain disallowed.

### Unknown, unranked, and incomparable entries

New publishers or naming formats need no rule edit when the runtime supplies sufficient explicit identity, eligibility, and comparable-version metadata. Only compare releases within a publisher using a documented comparable scheme; do not invent ordering across unrelated families, date encodings, or opaque labels.

- An unidentified publisher is ineligible, with a visible diagnostic; do not guess its identity.
- An individual ID without a usable release-version key is **unranked and ineligible**, with a diagnostic. Exclude only that ID, not its entire publisher. An unversioned experimental/codename sibling must not prevent a newly released higher numeric version from winning; adding/removing the unranked sibling cannot change that winner.
- A publisher with no rankable candidates has no eligible model IDs. Record the exclusion, then apply ordinary stage preferences to the remaining publishers. Stage bias alone is a preference, not a mandate that a particular publisher exist.
- Multiple otherwise eligible, individually versioned families within a publisher that are **mutually incomparable** block that selection and therefore the board. Require sufficient runtime ordering metadata or an explicitly approved comparison rule. Do not arbitrarily discard a competing family or choose another publisher to conceal the ambiguity. This is distinct from excluding an individual entry with no version key at all.

Disclose exclusions/unranked entries and qualify the result as newest **among eligible comparable live entries**, never newest across unverified formats.

## Deterministic board selection

1. For each required reviewer slot and agent type (ordinary R1, ordinary R2 when required, and `security-review` when required), capture and validate a fresh list from the actual dispatch tool, with field/capability provenance. Exclude only per-ID aliases/pickers, non-review specializations, unidentified publishers, and IDs without usable comparable version keys, each with diagnostics. Do not resolve or exclude an implementer model or publisher.
2. Within each publisher and each slot, preserve every eligible model ID and sort by comparable release key newest first. For R1, this is the numeric tuple above, not lexical version ordering. Raw advertised order is not the primary version preference. Multiple otherwise eligible individually versioned families within a publisher that are mutually incomparable block the whole board.
3. Define each candidate's `candidate_raw_index` as its zero-based position in that slot's unfiltered captured list; exclusions never compact or renumber these positions. For equal numeric versions, order candidates by `(candidate_raw_index, model_id lexical)` if the source provides reliable order; otherwise use `model_id lexical` alone. Absence of order is allowed; a contradictory claimed order is not reliable evidence.
4. Assign stage bias: plan `Google=0`, `xAI=1`, others `2`; PR `xAI=0`, `Google=1`, others `2`. For each slot, define `publisher_first_raw_index` as the minimum `candidate_raw_index` among that slot's eligible candidates for the publisher; excluded IDs never contribute to this minimum. Order publisher groups by `(stage bias, publisher_first_raw_index, provider_id lexical)` with reliable raw order, otherwise `(stage bias, provider_id lexical)`. Never compare release numbers across publishers. Use case-sensitive ordinal lexical ordering.
5. Assign slots in fixed order: ordinary R1, ordinary R2 if required, then `security-review` if required. From each slot's ranked candidates, choose the lexicographically first complete assignment whose configured model IDs are pairwise distinct, using deterministic depth-first/backtracking. Repeated publishers and matching the implementer's model are allowed. One required slot has no diversity constraint. If multiple required slots cannot be assigned distinct IDs, block with exactly `insufficient distinct eligible model IDs`; do not add slots, relax uniqueness, or silently substitute.
6. Record ordered selections before dispatch and recheck availability and current slot lists. Recompute the whole board if a refreshed list changes the selection, including exposing a newer eligible release. Freeze reviewer IDs, snapshot evidence, and exact revision at dispatch; later releases or model/session Auto switches do not invalidate the frozen board. A post-dispatch failure or mismatch blocks rather than silently substituting another ID.

## Evidence and gate outcomes

| Evidence | Evidence component of gate |
| --- | --- |
| Approved ruleset and exact revision; valid fresh snapshot; recorded dynamic board; successful completed reviewers; matching requested/task-registry configured IDs; no authoritative contradiction; no actual execution identity supplied | May pass as **`configured-only`**; record `execution_identity=unconfirmed`. |
| All the above, plus authoritative runtime fields explicitly identifying matching actual execution model and publisher | Record **`execution-confirmed`**, with the authoritative source. Registry configuration alone does not meet this standard. |
| Missing approval, invalid/ambiguous selection, too few distinct eligible model IDs for multiple required slots, no completed matching registry record, requested/configured mismatch, or contradictory authoritative reviewer execution identity | **Block**. No silent replacement, downgrade, or acceptance of a reviewer's prose claim as identity evidence. |

Partial authoritative execution identity also **blocks**: if any actual-execution identity field is supplied but a matching actual model-and-publisher pair cannot be established, neither passing row applies. Record the supplied fields and their authoritative source; do not discard them to claim "no execution identity supplied," or use configured R1 publisher interpretation to fabricate actual-execution attestation. This preserves fail-closed handling rather than extending `configured-only` to a new evidence case.

A substantive blocking finding still blocks at either evidence level. When multiple slots are required, diversity under R1 means pairwise-distinct configured reviewer **model IDs**, not distinct publishers or independently attested backend diversity. Do not describe configured-only selection as confirmed execution or independent backend attestation.

## Per-board audit record

Record the following in the issue/PR Review Board, linking session/CI artifacts where available:

- Tier and stage; exact reviewed plan digest or PR head SHA; ruleset ID, full ruleset commit SHA/immutable file link, approved plan digest, and approval permalink.
- Live inventory interface/tool-schema source for each slot, capture time, snapshot digest and its serialization/normalization (referencing one digest for slots using the same immutable snapshot), whether raw order is reliable, and pre-dispatch availability recheck.
- Eligibility exclusions and diagnostics, all eligible per-slot candidate IDs in ranked order, tie decisions, and any publisher with no eligible candidates; sufficient ranking evidence to reproduce the assignment.
- Required slots and agent types; each slot's ordered candidate publisher groups and eligible model IDs, comparable families/version keys, each candidate's `candidate_raw_index`, and each group's `publisher_first_raw_index`; provenance **per field**, including capability evidence.
- Assigned reviewer model IDs and configured publishers, fixed slot order, pairwise-uniqueness result when multiple slots are required, and the deterministic backtracking choices needed to reproduce the assignment.
- Reviewer IDs and review turn/invocation, requested and task-registry configured IDs, authoritative registry source, successful completion evidence, and substantive verdicts/findings with citations.
- `configured-only` or `execution-confirmed`; reviewer actual execution identity and authoritative source when supplied, otherwise explicitly `execution_identity=unconfirmed`. Do not record or require implementer identity for reviewer selection.

Retain the full discovery snapshot in session/CI review artifacts when available and sufficient selected/ranking/exclusion evidence in the durable issue/PR record. Exact IDs in snapshots, historical reviews, examples, or test fixtures do not define eligibility. Never load them as the next candidate pool.

## Decision checks

Validate policy changes with the existing Copilot-customization validator/tests, classifier tests, whitespace checks, and list-driven cases below. Both a rules-only change and the full three-file policy change must classify high. These are decision examples, not a production selector script, schema, model inventory, or new application/CI behavior.

| Input or change | Required outcome |
| --- | --- |
| Append an unseen higher minor, major, multi-digit release, patch, or matching variant | Numeric newest wins without rule/source/approval edits; variants of an equal numeric release use the tie-breaker. |
| A selected model is removed or fresh discovery changes a slot before dispatch | Rerank affected slot candidates and recompute the entire deterministic assignment; dispatch only if the full assignment remains available and pairwise distinct, otherwise block. |
| Shuffle unequal-version entries | Same version winner; equal releases follow reliable raw order, or lexical ID when order is absent. |
| Compare `3.10` with `3.9`, or omit minor/patch | Numeric ordering and zero-filled missing components; no cross-provider version comparison. |
| Add/remove an unversioned experimental sibling beside a higher numeric release | Diagnose/exclude only the sibling; numeric winner remains unchanged. |
| Sufficient explicit metadata for a new publisher/format | Eligible without a rule edit, using documented comparable ordering and field provenance. |
| Unknown publisher or publisher with no rankable entries | Diagnose and exclude the affected IDs; apply remaining publisher ranking without a blanket veto on rankable siblings. |
| Reviewer-count policy is updated | Preserve existing tier assignment and required slot counts; never add reviewers solely for diversity. |
| One reviewer slot is required | Select the highest-ranked candidate for that slot; impose no multi-reviewer diversity requirement. |
| Multiple reviewer slots are required, including security review | Assign pairwise-distinct configured model IDs across all required slots; publisher repeats are allowed. |
| Ordinary and security-review lists share a publisher but offer distinct eligible IDs | Permit the shared publisher; assign distinct model IDs if a complete assignment exists. |
| The implementer's configured model is also an eligible reviewer candidate | Allow it; implementer/reviewer equality is not a diversity key or exclusion. |
| A naive first candidate prevents later slots, but another complete assignment exists | Deterministic depth-first/backtracking selects the lexicographically first complete assignment in fixed slot order. |
| Multiple slots are required and no complete assignment has pairwise-distinct model IDs | Block with exactly `insufficient distinct eligible model IDs`; do not add slots or relax uniqueness. |
| Developer identity is unresolved or session Auto changes before/after implementation | Do not require identity/mode resolution and do not invalidate or change a frozen board. |
| GUI proof needed while currently connected | Prioritize the real-app run, affected-editor exercise and fresh GitHub-attached screenshot; verify current connectivity and visible responsive application with representative data. A prior connection is not current evidence. |
| GUI proof needed while disconnected | Reconnect and verify the current visible responsive application before capture; failed reconnect or verification blocks completion. Black, stale, lock-screen, unrelated or API-success-only captures cannot substitute. |
| Two eligible versioned families within one publisher lacking common ordering | Block; neither drop a family nor conceal ambiguity with another publisher. |
| Authoritative field contradiction, invalid authoritative key, conflicting duplicate ID, or unresolved publisher spelling/alias | Block the whole board; do not relabel a conflict as missing metadata or count two spellings as distinct publishers. |
| Alias/picker, non-review specialization, unidentified publisher, or individually unranked ID | Exclude that ID before assignment and record a diagnostic. |
| Missing approval/completed registry record, dispatch mismatch, or partial/conflicting authoritative reviewer execution identity | Block regardless of a prose APPROVE verdict; matching partial execution fields cannot manufacture a passing evidence case. |
| Stale snapshot, past example/fixture, or cached preferred ID offered as discovery | Reject it as a candidate source; capture the current dispatch list. |
| New release after an already completed valid review | Preserve that frozen review; discover afresh for the next board, without endless restarts. |
| Multiple slots have eligible candidates under one publisher | Preserve every eligible ID, newest first; do not collapse the publisher to one model before assignment. |
| Reliable raw order is available for a slot's candidates | Order publisher groups by `(stage bias, publisher_first_raw_index, provider_id lexical)`, where `publisher_first_raw_index` is the minimum `candidate_raw_index` among eligible candidates only; each `candidate_raw_index` is its original zero-based position in that slot's unfiltered snapshot, never compacted after exclusions. Within a publisher, use numeric version newest-first, then each candidate's own `(candidate_raw_index, model ID)` for equal versions. |
| Reliable raw order is absent | Order publisher groups by stage bias then publisher ID; equal numeric versions use model ID lexical order. All lexical comparisons are case-sensitive ordinal. |
| The initial candidate IDs collide across required reviewer slots | Use the fixed slot order and deterministic backtracking to find the lexicographically first complete distinct-ID assignment, if one exists. |
| Stale rule text requires developer identity, Auto activation, developer-publisher exclusion, or distinct reviewer publishers | Remove that gating; retain reviewer-specific discovery, provenance, exact-revision, registry, completion, execution-identity, security, CI, feedback, and freshness safeguards. |
