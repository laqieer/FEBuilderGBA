# Dynamic reviewer selection

Ruleset: **`dynamic-R1`**. This is a version-independent selection and evidence policy, not an inventory of approved model IDs. Apply it with the [workflow review gates](../DEVELOPMENT-WORKFLOW.md#review-gates).

Each new board discovers the actual dispatch tool's current choices and builds a ranked candidate list for every required reviewer slot. Within each configured publisher, all eligible comparable live model IDs are ordered newest first; the board assigns distinct configured model IDs across multiple slots. New matching numeric releases and variants become eligible automatically, without source edits, inventory rows, or per-ID maintainer approval. No exact-ID allowlist, maximum version, fixed preferred model, approved-version table, cached candidate list, or unconditional older-model fallback is permitted. Newest is not a claim of highest quality.

## Authority and bootstrap

- Canonical authority: [issue #2153, plan v5](https://github.com/laqieer/FEBuilderGBA/issues/2153#issuecomment-5553800486), normalized SHA-256 `7173e0a1e6cb37714811fe1b9754d72d40a7adb62c12ed7a59e1a48f00ef5180` (UTF-8 comment body, CRLF to LF, `TrimEnd`).
- Separate [explicit maintainer approval](https://github.com/laqieer/FEBuilderGBA/issues/2153#issuecomment-5554161089) by `laqieer`: its **unquoted** text names that exact v5/dynamic-R1 digest and approves live discovery, generic publisher/version interpretation, capability fallback, automatic future matching versions, `configured-only` evidence with unconfirmed backend execution, and v5 section 7's bounded bootstrap.
- A preference statement, issue creation, reviewer approval, agent-generated post under a maintainer account, quoted approval/template, or future intention is not maintainer authorization. Verify the author and the separate explicit approval itself. An agent cannot author or infer its own approval. External documentation may inform the maintainer but cannot authorize a new interpretation.
- Bind each board to the approved plan digest, approval permalink, and this ruleset at a **full commit SHA**, with an immutable file link rather than a moving branch reference. For the initial policy PR, use its full candidate head SHA; the pre-implementation plan gate instead references the approved v5 digest because the ruleset file does not yet exist.
- Material changes to interpretation rules, selection algorithm, or evidence standard require renewed independent review and explicit maintainer approval. An ordinary new ID satisfying unchanged approved rules does not.
- GUI proof remains governed by the connected-application and screenshot requirements in the development workflow. Reviewer selection itself does not resolve or exclude developer identity.

The initial bootstrap is only for #2153's own plan/PR evidence gates: obtain aligned v5 substantive reviews; obtain separate explicit approval before any branch or implementation; then record the formal plan gate against the exact approved revision. Prior advisory reviews count only if their frozen discovery/dispatch/registry evidence meets these rules; otherwise rerun. They never retroactively become execution attestations.

Implement only the three approved documentation files in an isolated worktree, validate, and obtain a freshly discovered PR board and ordinary CI/feedback/merge/post-merge verification. General reuse begins only after the separately reviewed policy PR merges; resume #2152 only after that merge is verified, using fresh reviews. This bootstrap authorizes no unrelated scope or agent-invented exceptions.

Changes here remain **high-risk** under the `.github/` classifier prefix. Pairwise-distinct configured-model-ID counts, applicable security review, read-only/no-exec safety screens, required CI, all three feedback channels, branch freshness, merge confirmation, and post-merge verification remain mandatory. Roll back only through the normal reviewed process, retain audit history, and re-block work that lacks evidence under the restored rule.

## Live discovery and capabilities

1. Capture a separate **current selectable-model list from the actual review-dispatch tool/runtime for every required slot/agent type**—ordinary reviewer R1, ordinary reviewer R2 when required, and `security-review` when required—including exact IDs, available metadata, and raw order when supplied. A live tool schema's explicit model-override choices are acceptable for that slot. Intersect a broader service catalog with what that slot's chosen dispatch tool actually supports.
2. Use a documented inventory interface or the current tool schema for each slot; do not invent an export command. Record source, capture time, serialization/normalization, and snapshot digest per slot. Issue prose, earlier plans, web search results, model self-reports, examples, fixtures, and past snapshots are not selectable-model sources.
3. Require explicit selectability for the chosen text/chat review agent in each slot. Prefer runtime capability fields; when absent, the approved fallback accepts that slot's dispatch-tool explicit model choices **for that agent** as capability evidence. Record that fallback rather than inventing runtime flags.
4. Exclude non-text/non-review specializations and aliases/pickers. Runtime flags take precedence; the fallback also excludes IDs containing a whole hyphen-delimited `auto`, `default`, `latest`, or `picker` token. These exclusions impose no version ceiling.
5. Refresh each slot's discovery for every new board and after material revisions. Immediately before dispatch, recheck selectability for every slot; if any slot's list changes, rebuild all slot rankings and recompute the complete assignment, including any newly exposed eligible version. A removed winner is never dispatched.
6. Freeze every slot's snapshot once dispatched. A later release does not by itself invalidate an already completed review or cause endless restarts; the **next** board uses fresh lists. A post-dispatch failure or identity mismatch blocks that recorded board. Do not silently reroute to another or older model.

## Field resolution and provenance

Prefer runtime fields whose **documented semantics** identify the configured model's publisher and comparable release version. An API transport provider, gateway, client brand, or opaque build/execution identifier is not automatically such a field.

Resolve each field deterministically: use semantically explicit runtime metadata first and fill only missing fields through R1. Keep provenance separately for publisher, family/version key, capability, and raw index. Use `provider_source` / `version_source=approved-rule:dynamic-R1` when the ID rule supplied them, never `runtime-metadata`. Capability fallback identifies the dispatch-tool choices as its source. Developer identity and publisher are not inputs to reviewer eligibility or selection.

Authoritative runtime metadata contradicting the applicable rule is a blocker, not permission to hide the conflict. Missing or unrelated gateway metadata is not itself a contradiction. Reject contradictory publisher mappings, invalid authoritative keys, and conflicting duplicate IDs.

Use one canonical `provider_id` for grouping, stage bias, and publisher-order ties. For R1 publishers, that key is exactly the publisher string in the table below. Runtime identity evidence must unambiguously agree with that key; an unexplained spelling/casing difference or alias is unresolved/conflicting identity, not a second publisher. Do not invent case-folding or alias mappings. A runtime-only new publisher needs a documented, unambiguous canonical key; ambiguity about whether it duplicates another publisher blocks the board. Compare canonical provider keys and model IDs with case-sensitive ordinal equality and ordinal lexical ordering, never locale-dependent comparison. Keep the original runtime value and its provenance in the audit record. Reviewer diversity is determined only by pairwise-distinct configured model IDs.

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
- A publisher with no rankable candidates has no representative. Record the exclusion, then apply ordinary stage preferences to the remaining publishers. Stage bias alone is a preference, not a mandate that a particular publisher exist. If a required slot has no eligible candidate, or too few unique eligible model IDs remain to fill all required slots, block.
- Multiple otherwise eligible, individually versioned families within a publisher that are **mutually incomparable** block that selection and therefore the board. Require sufficient runtime ordering metadata or an explicitly approved comparison rule. Do not arbitrarily discard a competing family or choose another publisher to conceal the ambiguity. This is distinct from excluding an individual entry with no version key at all.

Disclose exclusions/unranked entries and qualify the result as newest **among eligible comparable live entries**, never newest across unverified formats.

## Deterministic board selection

1. Validate the fresh snapshot and field/capability provenance, exclude ineligible entries, and resolve all blocking conflicts. Do not resolve or exclude the developer's identity or publisher.
2. For each required slot, group eligible candidates by publisher. Within each publisher, rank every eligible model ID by its comparable release key, newest first. For R1, compare numeric tuples, not lexical version strings. Raw advertised order is never the primary version preference.
3. For equal numeric versions, order models by each model's own `(candidate_raw_index, model_id lexical)` if the source provides reliable order; otherwise use `model_id lexical` alone. The candidate raw index is the zero-based position in that slot's captured source list, before exclusions. Absence of order is allowed; a contradictory claimed order is not reliable evidence.
4. Assign stage bias: plan `Google=0`, `xAI=1`, others `2`; PR `xAI=0`, `Google=1`, others `2`. For each slot, order publisher groups by `(stage bias, publisher_first_raw_index, provider_id lexical)` with reliable order, otherwise `(stage bias, provider_id lexical)`. `publisher_first_raw_index` is the minimum original zero-based raw index among that publisher's **eligible candidates in that slot**. Excluded entries retain their original raw positions but do not create groups or affect group minima. Never compare release numbers across publishers or use the publisher index as an individual candidate index.
5. Assign required slots in fixed order: ordinary reviewer R1, ordinary reviewer R2 when required, then `security-review` when required. Use deterministic depth-first backtracking over the ranked per-slot lists and choose the first complete assignment with pairwise-distinct configured model IDs across every slot. Publisher reuse is allowed. A single required slot needs only one eligible model ID; there is no multi-reviewer diversity constraint for it.
6. If no complete assignment exists because too few unique eligible model IDs remain, block with exactly `insufficient distinct eligible model IDs`. Do not add reviewers, relax uniqueness, or silently substitute after dispatch. Record each slot's source, snapshot, rankings, and assignment before dispatch; recheck every slot and recompute all rankings and the complete assignment if any slot's discovery changes. A post-dispatch failure or mismatch blocks the recorded board.

## Evidence and gate outcomes

| Evidence | Evidence component of gate |
| --- | --- |
| Approved ruleset and exact revision; valid fresh snapshot; recorded dynamic board; successful completed reviewers; matching requested/task-registry configured IDs; no authoritative contradiction; no actual execution identity supplied | May pass as **`configured-only`**; record `execution_identity=unconfirmed`. |
| All the above, plus authoritative runtime fields explicitly identifying matching actual execution model and publisher | Record **`execution-confirmed`**, with the authoritative source. Registry configuration alone does not meet this standard. |
| Missing approval, invalid/ambiguous selection, insufficient distinct eligible model IDs, no completed matching registry record, requested/configured mismatch, or contradictory authoritative execution identity | **Block**. No silent replacement, downgrade, or acceptance of a reviewer's prose claim as identity evidence. |

Partial authoritative execution identity also **blocks**: if any actual-execution identity field is supplied but a matching actual model-and-publisher pair cannot be established, neither passing row applies. Record the supplied fields and their authoritative source; do not discard them to claim "no execution identity supplied," or use configured R1 publisher interpretation to fabricate actual-execution attestation. This preserves fail-closed handling rather than extending `configured-only` to a new evidence case.

A substantive blocking finding still blocks at either evidence level. Reviewer diversity under R1 means pairwise-distinct **configured model IDs** across the required slots, not independently attested backend diversity; publishers may repeat. Do not describe configured-only selection as confirmed execution or independent backend attestation.

## Per-board audit record

Record the following in the issue/PR Review Board, linking session/CI artifacts where available:

- Tier and stage; exact reviewed plan digest or PR head SHA; ruleset ID, full ruleset commit SHA/immutable file link, approved plan digest, and approval permalink.
- For every required slot, live inventory interface/tool-schema source, capture time, snapshot digest and its serialization/normalization, whether raw order is reliable, and pre-dispatch availability recheck.
- Eligibility exclusions and diagnostics, unranked entries, discarded older releases/tie decisions, per-slot ordered candidate lists, publisher-group indices, and the selected assignment/backtracking trace; sufficient evidence to reproduce the result.
- Ordered selected model IDs and configured publishers for each slot, comparable families/version keys and candidate raw indices when supplied; provenance **per field**, including capability evidence.
- Reviewer IDs and review turn/invocation, requested and task-registry configured IDs, authoritative registry source, successful completion evidence, and substantive verdicts/findings with citations.
- `configured-only` or `execution-confirmed`; actual execution identity and authoritative source when supplied, otherwise explicitly `execution_identity=unconfirmed`.

Retain the full discovery snapshot in session/CI review artifacts when available and sufficient selected/ranking/exclusion evidence in the durable issue/PR record. Exact IDs in snapshots, historical reviews, examples, or test fixtures do not define eligibility. Never load them as the next candidate pool.

## Decision checks

Validate policy changes with the existing Copilot-customization validator/tests, classifier tests, whitespace checks, and list-driven cases below. Both a rules-only change and the full three-file policy change must classify high. These are decision examples, not a production selector script, schema, model inventory, or new application/CI behavior.

| Input or change | Required outcome |
| --- | --- |
| Append an unseen higher minor, major, multi-digit release, patch, or matching variant | Numeric newest wins without rule/source/approval edits; variants of an equal numeric release use the tie-breaker. |
| Remove any selected candidate before dispatch | Refresh each required slot's list, rebuild rankings, and recompute the first complete pairwise-distinct assignment; never dispatch the removed ID. |
| Shuffle unequal-version entries | Same version winner; equal releases follow reliable raw order, or lexical ID when order is absent. |
| Compare `3.10` with `3.9`, or omit minor/patch | Numeric ordering and zero-filled missing components; no cross-provider version comparison. |
| Add/remove an unversioned experimental sibling beside a higher numeric release | Diagnose/exclude only the sibling; numeric winner remains unchanged. |
| Sufficient explicit metadata for a new publisher/format | Eligible without a rule edit, using documented comparable ordering and field provenance. |
| Unknown publisher or publisher with no rankable entries | Diagnose and exclude; apply remaining stage preferences, not a blanket veto on rankable siblings. |
| Required reviewer slots cannot be filled by a complete assignment with pairwise-distinct configured model IDs | Block with exactly `insufficient distinct eligible model IDs`; do not add slots or relax the constraint. |
| Two slots' highest-ranked choices share a model ID, but a later distinct candidate permits a complete assignment | Backtrack deterministically and choose the first complete assignment in candidate-list order. |
| Two or more slots use different eligible model IDs from the same publisher | Allow; publisher diversity is not required. |
| A reviewer model or publisher matches the developer's | Allow; developer identity and publisher are not selection inputs. |
| GUI proof needed while currently connected | Prioritize the real-app run, affected-editor exercise and fresh GitHub-attached screenshot; verify current connectivity and visible responsive application with representative data. A prior connection is not current evidence. |
| GUI proof needed while disconnected | Reconnect and verify the current visible responsive application before capture; failed reconnect or verification blocks completion. Black, stale, lock-screen, unrelated or API-success-only captures cannot substitute. |
| Two eligible versioned families within one publisher lacking common ordering | Block; neither drop a family nor conceal ambiguity with another publisher. |
| Authoritative field contradiction, invalid authoritative key, conflicting duplicate ID, or unresolved publisher spelling/alias | Block; do not relabel a conflict as missing metadata or count two spellings as distinct publishers. |
| Alias/picker or non-review specialization | Exclude before ranking. |
| Missing approval/completed registry record, dispatch mismatch, or partial/conflicting execution identity | Block regardless of a prose APPROVE verdict; matching partial execution fields cannot manufacture a passing evidence case. |
| Stale snapshot, past example/fixture, or cached preferred ID offered as discovery | Reject it as a candidate source; capture the current dispatch list. |
| New release after an already completed valid review | Preserve that frozen review; discover afresh for the next board, without endless restarts. |
