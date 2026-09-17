# Offline patch-import desktop proof

This package keeps the feature's actual Windows UI automation and necessary
support in the same feature PR. It does not modify application behavior and does
not depend on another GUI-readiness PR. Pure tests are **not GUI acceptance**.

## Startup observation

Ordinary startup can complete before the transient recovery window is visible to
the bounded observer. `DesktopStartupObservation` is shared by the actual driver
and separately counted pure regressions; it does not change application startup.
Both pure runners enforce all 75 startup-observation cases separately from the
unchanged 522 policy and 26 installed-snapshot cases.
The result records `StartupRoute`, `LoadingObserved`, `LoadingObservedAtMs` and
the legacy, **main-window-only** `AvaloniaWindowClass`:

* `real-main-visible-and-loading-destroyed`: loading was positively observed.
  That observation is sticky. Only the original strict `DesktopPolicy.Handoff`
  accepts later chronology, a distinct main handle, a destroyed loading handle
  and a currently visible main/control. Missing labels or a still-existing
  loading window never fall back to the other route.
* `main-visible-loading-not-observed`: no loading observation was made. This
  reports only the current validated main/control; it does not claim a transient
  handoff or loading-window destruction.

Each observation classifies the complete bounded visible owned-root sample before
deciding. Unknown extra roots block readiness regardless of enumeration order;
duplicate handles/candidates, invalid projections and nonmonotonic polling
observations refuse the attempt. The sole ancillary-root exception is at most one
independently class-bound, distinct, immediate-main-owned setup wizard with the unique visible
`ContentRepoSetupWizard_Close_Button`. A title, owner chain alone, native picker,
missing close control or another root is insufficient. No wizard is closed here:
only the existing owned-control invocation in `OpenEditor` may close it.

Acceptance refreshes the complete sample, preserves candidate main identity/class,
rechecks all accepted native roots and required visible controls, and rechecks
the observed loading handle's destruction. The refresh may share its candidate's
millisecond tick; ordinary polling samples must advance. Main controls need not
be enabled before the existing wizard-close phase. A late loading observation
is sticky too, and a rejected attempt cannot recover through the fast route.

The 45-second startup stage and overall budgets, ownership/ancestry checks,
joined-worker/retained-process custody, real import/rejection, file preservation,
editor screenshot and normal-close requirements are unchanged. These sampled
checks are not an atomic desktop snapshot. Historical `missing-loading-observation`
attempts remain failed and consumed; new runtime proof needs newly authenticated
source, preparation and a separate grant.

## Per-window class identity

Avalonia 11.3.18
[`WindowImpl.CreateWindow`](https://github.com/AvaloniaUI/Avalonia/blob/11.3.18/src/Windows/Avalonia.Win32/WindowImpl.cs#L959-L990)
assigns an instance class name with a fresh GUID before `RegisterClassEx`.
The class is not process-global. Main, loading, wizard, editor and confirmation
windows can legitimately have different `Avalonia-<GUID>` classes; different
handles are also allowed to share a valid class.

One attempt-owned `DesktopRootBindings` ledger is shared by every new tree,
refresh, window/control validation and action boundary. A normalized HWND is
bound only after fresh native alive/PID/`GA_ROOT`, class-kind and owner-chain
checks. Canonical UIA identity is attached after its PID/handle/runtime-ID checks
and stable before/after native facts. Valid Avalonia classes have the exact
45-character prefix/GUID shape; the native-dialog path permits only `#32770`.
Native child `Edit`/`Button` handles are not root-class bindings.

The exact class and canonical identity cannot change for a known handle, even
across new trees or after a window closes. There is no eviction or rebinding.
The separate historical ledger is capped at 32 distinct root handles for this
one attempt; the current catalog/sample remains capped at eight. Overflow
refuses. This is continuity evidence, not an assertion that all same-PID HWND
reuse is detectable. Native PID/root/owner facts are still read afresh.

`ValidateWindow` uses the shared tree validator, not a just-read class passed
back as its own expectation. Actions require a previously canonical root and
revalidate its kind, class, identity, visibility and applicable role owner.
Wizard, confirmation and native picker actions retain their exact expected
owners; the editor remains nonmodal. Picker title/control/foreground rules and
all query/deadline/join/cleanup limits are unchanged. Owner facts may be refreshed
after validation; they are not inferred from class equality.

Startup tracks classes by handle, with a separate `LoadingWindowClass`. A
loading-C to main-A plus wizard-B handoff is valid when all the original
chronology, destruction, uniqueness, ownership and epoch requirements pass.
The legacy `WindowClass`/`DesktopResult.AvaloniaWindowClass` label describes only
the main window and stays null while only loading has been bound. Main
candidate/revalidation identity and same-handle class continuity remain strict.

### Owned-only startup failure diagnostic

`DesktopResult.StartupFailure` is one optional immutable snapshot, separate from
`QueryFailure`. The actual worker failure path invokes the shared
`DesktopStartupFailureCapture` helper for a fixed startup predicate and the most
recent complete current startup sample. A new incomplete capture clears the
sample rather than publishing an older poll as its operands.

At most eight rows contain only canonical handle, last validated own owner (or
zero), the exact bound GUID class or `#32770`, and visible/main/loading/wizard
booleans. Class, canonical identity and owner must agree with already validated
ledger data. The closed reason allowlist, fixed ASCII class/number bounds and
eight-row shape keep the diagnostic below 4 KiB; both pure runners additionally
check actual compact, pretty and nested GUI serialization. There are no
names/text/values/runtime IDs/foreign PIDs/paths or arbitrary exception messages.
The historical ledger is never dumped.

Capture copies immutable values without any new UIA/native query, and latches
one attempt. Unbound, duplicate, unsafe or oversized data and unknown predicates
leave the diagnostic null; diagnostic-source exceptions cannot replace the
original refusal. Query-engine errors still use `QueryFailure`. A snapshot is
descriptive last-validated evidence, not a new live observation, authentication
or success token.

### Class-assumption test migration

The prior 75 startup and 146 ownership names are explicitly mapped, not claimed
byte-identical. Startup replaces `wizard-wrong-class`,
`observed-class-remains-pinned`, and `observed-wizard-class-remains-pinned` with
distinct-window-class positives. The latched `"class"` rejection now mutates
the same observed loading HWND. Ownership replaces
`bound-avalonia-class-is-required` with
`same-hwnd-class-remains-bound-across-trees`. The other names retain their
invariants with a distinct-class wizard in the adapter model. Exact migrated
75/146 names are asserted in both runners.

An additional 57 independently named cases exercise the real shared
policy/driver-validator model across seven root lifetimes, five distinct GUID
classes, two native pickers, cross-query/operation mutations, owner/kind/history
refusals and the actual diagnostic capture helper. Four safe snapshots drive
12 real serializer checks. These are asserted locals, not new runtime report
counters. The existing 522/26 and image/terminal/88/72 inventories remain
unchanged; only the optional nested `StartupFailure` DTO is added.

Attempt `0affa` remains failed/consumed. Its `startup-setup-wizard` result did
not record the compound guard's exact failing operands. The backend source
proves the cross-window class assumption invalid, not the historical owner/class
facts. No old attempt or grant is revived by this correction.

## Owned-window query topology

UIA ancestry is not native window membership: an owned modal window can be a UIA
descendant of its owner while retaining a distinct `GA_ROOT`. Desktop own-PID
children are therefore seeds, not an exhaustive window list. The real driver
uses `DesktopOwnedTree` with its `AutomationElement` adapter; modeled-adapter
regressions execute the production query compiler, candidate collector, ownership
algorithm and startup projection without UIA/native calls. Both pure runners
enforce **146 named `ownedTreeCases`**, their exact ordered-name digest and three
full-graph scale profiles. These are separate from the unchanged 522 policy,
26 snapshot and 75 startup-observation cases.

### Closed candidate queries

The former client `FirstChild`/`NextSibling` descendant walk is replaced, not
given a larger quota. The only desktop search remains own-PID
`FindAll(TreeScope.Children)`, with at most eight returned seeds. Descendant
queries are allowed only below a validated own window or subtree and only for
the closed `DesktopCandidateQuery` compiler. This deliberately revises the old
blanket no-Descendants contract.

Window discovery uses own PID and either the concrete UIA `ControlType.Window`
or a nonzero native handle. Avalonia 11.3.18
[window peers](https://github.com/AvaloniaUI/Avalonia/blob/11.3.18/src/Avalonia.Controls/Automation/Peers/WindowBaseAutomationPeer.cs)
map to Window; the Win32
[root provider](https://github.com/AvaloniaUI/Avalonia/blob/11.3.18/src/Windows/Avalonia.Win32.Automation/RootAutomationNode.cs)
supplies the HWND host provider. Virtual peers must not be assumed to expose
their own HWND; the UIA handle property has an Int32/default-zero contract.
The Windows compiler passes actual `ControlType` objects, not guessed type names
or an integer substituted for that API type.

Control queries preserve the existing fixed selector meanings: exact
AutomationIds; Text plus the fixed loading label; Edit; ListItem; or the fixed
expected row name. ID searches do not prefilter away wrong-type matches:
callers still reject the wrong type. PID is always part of the provider
condition. No caller-supplied name, value, condition or search scope is accepted.
Raw-view, element-only full-reference requests add no cached name/value or
ownership proof.

Provider evaluation of those fixed predicates may inspect properties internally.
Filtering is **not ownership authorization**. Returned nodes are checked for UIA
PID before other client properties, then resolved through fresh nearest-nonzero
RawView ancestry to live native PID/`GA_ROOT`/root-PID facts. Runtime-ID cycles,
depth 32, top-level identity, class and owner-chain checks remain. Same-root
native child handles are not promoted to windows. Only a positively validated
different own root is registered/queued and excluded from the requesting root's
controls; foreign, unavailable or inconsistent candidates fail, never become
"missing" through a catch/skip. This is client boundary pruning, not a claim
that the provider stopped traversing that subtree.

For same-root candidates, fresh bounded raw ancestry also has to reach the
requested subtree before and after selector verification. Duplicate identities,
root echoes, scope escapes, changed identities and nonmatching provider results
refuse. Names/values and actions remain behind ownership checks. No raw ancestry
is cached across queries. Consistent native root rediscovery is deduplicated,
with fresh root providers/facts; conflicting aliases or cross-native-root cycles
refuse. Root relationship edges are rebuilt on catalog refresh.

### Catalog and epoch

Startup, wizard/editor/confirmation, picker host/nested-edit/open, and row/name/
status queries all use this boundary. Catalog refresh includes current own-PID
desktop seeds, all known roots, and newly discovered roots drained to closure.
Find refreshes the catalog before returning and refuses an observed revision
change. Startup refreshes before freezing an epoch and after visibility and
projection work, including hidden roots. Decisions and final acceptance refresh
and check the same frozen epoch. No newer epoch is silently adopted after a
skipped/projected root. Late unmatched roots therefore cannot disappear merely
because they match no control selector. Existing wizard, picker owner/title,
typed-control, enabled, foreground and action-time checks remain.

### What the limits measure

Each tree/sample still has eight canonical windows, **4,096 client candidate/
ancestor observation occurrences**, depth 32, and **65,536 budgeted adapter
UIA/Win32 calls**. Each fresh resolution/scope walk charges every inspected node
again; these are not unique-node counts. There is no per-selector reset.
Runtime IDs stay limited to 32 integers and owner chains to eight.

Each returned candidate collection is checked against **256** and the remaining
node allowance before client iteration/copy; control matches retain their
existing 1..16 bound. An oversized collection refuses rather than truncates.
`FindAll` has no provider-side count limit: its result may already be materialized
before Count is checked. Provider/core traversal, allocation, predicate work and
internal RPCs are not included in Nodes/Calls and have no new hard memory/work
bound. Absolute deadlines are checked before/after adapter calls. A blocked
provider still relies on the unchanged joined-worker/runner termination boundary.

The modeled provider evaluates the shared compiled conditions over a full graph,
not a prepared list of winners. Scale cases contain 10,000 irrelevant text/button
peers, deep raw ancestors through the depth-32 boundary, an owned wizard, native
same-root children and candidate/acceptance refreshes. They assert results and
client budgets, count seed/candidate queries and separately expose modeled
provider work. Model success and real Windows output-only compilation are not
live provider or GUI acceptance.

### Migration of the previous 108 cases

The previous suite was 94 traversal cases plus 14 projection/refresh cases.
All 108 have mapped coverage, but adapter/navigation-dependent bodies are
**not byte-identical**. 103 names retain their invariant; affected fixtures now
exercise returned candidates or explicit raw membership rather than assume that
discovery visits every irrelevant virtual node. Five names are replaced:

| Previous name | Current mapped coverage |
| --- | --- |
| `raw-child-cycle-refused` | `provider-query-root-echo-refused`: a Descendants response cannot echo its root. Raw parent and cross-native-root cycle checks remain separately tested. |
| `raw-sibling-cycle-refused` | `duplicate-provider-candidate-refused`: duplicate result identities refuse. Provider-internal navigation is not claimed client-inspected. |
| `actual-traversal-node-budget-refuses-wide-tree` | `irrelevant-wide-provider-tree-does-not-charge-client-nodes`: irrelevant provider work is excluded. A new deep-candidate case actually exhausts the unchanged 4096 cap. |
| `query-discovered-root-is-drained-before-return` | `query-discovered-root-is-drained-before-refusal`: a newly found root is drained, then invalidates the query epoch. |
| `visibility-change-before-first-projection-read-refuses` | `pre-capture-refresh-includes-newly-visible-root`: changes discovered before freezing are included; changes after freezing still refuse. |

Thirty-eight additional cases cover the compiler, all fixed selectors, candidate
caps, scope/alias/provider faults, deadlines, catalog refresh and scale profiles.
The resulting 146-name inventory is asserted in both runners without adding
runtime JSON fields. Image/terminal and preparation-exit test inventories are
unchanged.

HWND comparisons use their documented low 32 significant bits and sign-extended
native-call representation. This normalization is not applied to process handles
or arbitrary pointers, and never replaces native PID/root validation.

`QueryFailure` records at most one fixed stage/selector/predicate and bounded
ownership/counter snapshot. Only previously positively owned HWNDs are included;
foreign handles/PIDs and all names, text, values, paths and runtime IDs are
suppressed. A failure may refresh its known owned handle once, using at most four
remaining budgeted native calls. If cancellation/deadline intervenes, after-state
can be unavailable; diagnostics do not mask the original refusal.

Four nullable observations describe an unverified Resolve candidate without
identifying it or granting ownership:

| Field | Available value |
| --- | --- |
| `SeedOrdinal` | Current bounded collection position, 1..8; null for direct `Seed(node)` or before collection completes. |
| `SeedResolveKeyEqual` | Whether the already-returned outer seed key equals the first Resolve key after low-32 normalization; null before that return and for raw ancestors. |
| `ResolveAlive` | Boolean returned by the existing budgeted liveness read, not evidence of ownership. |
| `ResolvePidRelation` | `zero`, `owned`, or `foreign`, derived from the single existing native PID return; never the PID itself. |

Refresh clears these fields before collection. A seed gets its ordinal before
its PID/handle checks; Resolve clears probe values on entry and at each raw-parent
frame, retaining only the supplied ordinal. A successful Resolve clears all four
before registration or later scanning. “Returned” includes the adapter's
post-read budget check: a cancellation there leaves that observation null while
preserving earlier completed observations. Failure-time refresh still targets
only a previously positively owned handle and cannot update the new fields.
Legacy ownership context, refusals, call order and budgets do not change.

Both runners separately check 39 named production-model diagnostic cases and 48
named compact/pretty/nested wire cases using 16 actual model `QueryFailure`
objects. Each wire shape has exactly 20 properties, nullable/domain checks,
a 4096-byte UTF-8 cap, and unverified handle/PID/private-text sentinel exclusions.
These are checked locals, not new runtime report counters; the existing
146/522/57/75 case inventories and 12 `StartupFailure` serialization cases remain
separate and unchanged. The model seed adapter makes one fewer call than the
production adapter; exact model traces are not production call-count claims.

The public Windows aggregate includes existing authenticated fixture-worker
launches and retained-process-image native queries. It is not native-free.
Exact-source local execution approval must explicitly cover these bounded test
effects. The second runner remains reachable only through the authenticated
Validate parent and its `ValidatePureChild` dispatch, with the required current
source, configuration and preparation bindings; running the ordinary aggregate
does not execute or prove that second route. The Validate parent requires a
clean committed worktree matching its prepared head/tree, so it cannot validate
an uncommitted test-first candidate by bypassing those source checks.
Direct `validate-helper.ps1`
invocation remains unsupported. No test result authorizes application launch,
desktop readiness, GUI/native probes, recovery, or another proof attempt.

The consumed `c927` attempt passed readiness but refused `query-native-gone`.
The cause of its historical `IsWindow` false result is not established by these
new observations. This contract closes an observation gap only; it neither
repairs a proven GUI cause nor revives `c927` or the consumed `3af`/`877f` attempts.

Stale/unavailable roots and ownership failures are not retried or converted to
missing controls. These samples are not atomic and do not prove absence of
same-PID HWND reuse. Attempt `329` remains failed/consumed: its exact offending
selector/HWND was not recorded, so the topology defect is not an attestation of
that attempt's precise cause. A new real desktop attempt requires current SOURCE,
fresh preparation and a separate one-use grant.

Attempt `d2` also remains failed/consumed. Its `query-node-bound` report measured
4096 occurrences and 17736 calls across two roots; it did not record 4096 unique
controls or the exact UIA shape. This motivates the query/accounting correction,
not a claim about a specific provider defect. No failed attempt is revived and
no further live validation is authorized by pure/model results.

## Retained executable-image observations

`Readiness.cs` contains the one shared `BoundedProcessImage` reader. It borrows
an existing `SafeProcessHandle` and calls
[QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
once with flags zero and a 32,768-character buffer. It never reopens by PID,
enumerates modules, grows/retries a buffer, sleeps until a match, or disposes the
owner's handle. Invalid/closed handles, access denial, native errors, malformed
results and actual mismatches refuse. Full-width process handles are not HWNDs.
The existing single receipt-bound app acquisition remains; its handle and
PID/start-time tuple are bound before querying its image.

All preparation, launcher, runner, desktop and supervision image/cleanup checks
use that reader. Existing per-caller Ordinal/OrdinalIgnoreCase comparisons remain;
no prefix, alias or short-name normalization is added. Only preparation's existing
fast-tool route can report `exited-unobserved`, without claiming an image match.
Cleanup makes at most one separate guarded observation and cannot erase an initial
failure or authorize app cleanup before confirmed runner exit.

Preparation also permits one narrowly bounded post-query exit observation. Only
`prepare-initial` with a typed `native-error` result, error 31, one query, a valid
retained handle and truly null path/character fields can reach it. The diagnostic
must agree and contain no observed image. Error 31 alone is **not exit evidence**.
There is one additional `HasExited` observation on the same original process,
never another image query or PID acquisition. With remaining budgets sampled
before the query, after the query/before this exit check, and after the exit check,
acceptance requires a strict Boolean true and finite
`0 < final <= post-query <= initial` budgets. Exceptions, non-Booleans, a live
process or exhausted/increasing/nonfinite budgets refuse.

This returns `exited-unobserved`, null path and one attempted query, with the
distinct diagnostic code `exited-after-query-error`. Error 31, the query count
and null-image facts remain present. Existing budget fields retain the initial
and final observations; no diagnostic/schema fields are added. The serialization
guard accepts this code only with its exact preparation/error/handle/query,
null-image and bounded-budget shape. It is not an image identity or success token.
Other errors, source exceptions, wrong images followed by exit, other roles and
all cleanup/GUI/supervisor image requirements remain unchanged.

The preparation caller still independently waits for the original process,
requires exit code zero, drains both streams within their bounds/deadlines and
checks the source state before passing. No wait, cleanup or overall budget grows.
Attempt `219` remains failed/consumed: its first Git query reported native error
31 and no path, with cleanup marked not needed but no recorded exit code.
That is consistent with termination, not proof of its precise cause or timing.
Neither that attempt nor an earlier GUI identity is revived by this policy.

Initial/failure and separate cleanup facts are retained in existing owned receipts.
The closed diagnostic is at most 4 KiB: fixed role/method/refusal, native error,
handle validity, query/length/budget facts, path digests, and at most 256 UTF-16
units of a successfully queried **owned executable** path. It contains no failed
buffer, arguments, environment, module list or foreign-process contents.
Diagnostics are not authentication or success tokens. Source/reference pins
precede compilation; imports of supervisor writer libraries do not compile or
query. Reopened sources still require quiescence, not an OS-sandbox claim.

The ten PowerShell observer cases now test one-query behavior; their old
module-null polling/getter-exit expectations are intentionally replaced.
Both pure runners separately execute 32 fake-native cases through the actual
reader, without OS queries. Aggregate also runs 16 diagnostic publication/schema
cases through the real reporting functions. The 522/26/75, current owned-tree,
and restage/runtime-binding inventories remain separate.
Both pure runners additionally assert the exact ordered names of 88 modeled
preparation-exit cases and 72 diagnostic/terminal-publication cases, separately
from those original inventories. They use the actual bridge and writers without
native queries; the constructor fixtures use a true null string, not PowerShell's
empty-string conversion. Counts/name digests are checked locals, not new runtime
JSON fields. New helper imports use the authenticated declaration library and
cached reporting policy (with its existing strict-mode directive); imports do not
compile, run tests, query processes or launch an application.

Attempt `d297` remains failed/consumed: the initial mismatching module path was
not recorded, though later guarded cleanup matched and confirmed retained-child
exit. Module enumeration can return incorrect information while its list is
uninitialized/changing; this explains the mechanism change, not that attempt's
precise cause. No old preparation or grant is restored. Fresh SOURCE, push,
independently granted preparation and separate GUI authorization remain required.

## Reviewable source

`scripts\OfflinePatchImportProof` contains:

| Source | Responsibility |
| --- | --- |
| `Desktop.cs` | The feature-specific ordinary `--rom` → observed-loading/main or main-first → Patch Manager → native picker flow, valid and invalid ZIP assertions, one editor-only PrintWindow, and normal close. |
| `Policy.cs`, `Policy.Tests.cs`, `Readiness.cs` | Own-session admission, bounded dispatch, retained-process cleanup decisions, worker containment, the original 522 policy cases, 26 installed-snapshot checks and 75 startup-observation cases. |
| `RuntimeBinding*`, `ProcessImage*` | Pinned runtime identity and one-read retained-image observation; 22 binding and 10 updated observer cases. Windows-form path checks remain Windows lexical checks on every OS. |
| `prepare.ps1`, `validate-helper.ps1` | Fixed Build/Validate/Inputs operations, pinned local tools, fixture tests and output-only compilation. The existing `scripts\SyntheticProofFixtures` project remains the only ROM fixture generator. |
| `run.ps1`, `launch.ps1` | Ordinary GUI runner and retained-runner/application supervisor. No global input, focus manipulation, PID enumeration, recursive kill, screenshot fallback, or timeout promotion. |
| `Configuration*`, `configuration.example.json` | Bounded data-only inputs, source closure, relocation/no-effect tests, and real staging/writer integration fixtures. |
| `restage\*` | Strict historical preparation verification, exclusive claim, CreateNew copy, physical inventory, receipts, and the 286 original plus 36 later policy regressions. |
| `supervision\*` | One shared fixed-mode supervisor, separate restage adapter, raw bounded logs and production terminal/final writers. |

`scripts\WindowsDesktopProof\PinnedLoader*` is the separate shared loader package,
published in this same PR. It accepts no script text, command path, environment,
or arbitrary arguments. Fixed modes map to fixed source entrypoints.

## PowerShell host and preparation telemetry boundaries

A newly started PowerShell host reads its telemetry opt-out during startup,
before the target script can set it. Its launching process must supply literal
`POWERSHELL_TELEMETRY_OPTOUT=1` before starting the approved external executable.
An update-check setting or a DOTNET opt-out is not equivalent. The already-running
CLI/tool bootstrap remains a bounded trusted, unattested boundary: setting the
flag later cannot retroactively suppress that bootstrap or prove historical
telemetry absence.

Preparation `Run` clears inheritance, populates its fixed child dictionary, then
starts the child. Both `AVALONIA_TELEMETRY_OPTOUT='1'` and
`POWERSHELL_TELEMETRY_OPTOUT='1'` belong in that dictionary, including for the
actual Validate PowerShell children. Separately, `launch.ps1` clears the RunChild
PowerShell runner's environment and must put literal
`POWERSHELL_TELEMETRY_OPTOUT='1'` in its existing dictionary before population and
Start. Its proxies and DOTNET opt-out do not replace that key.

Before child Start, preparation also supplies the exact SDK/VSTest controls
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`,
`DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false`,
`DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=true`, and
`VSTEST_DISABLE_ARTIFACTS_POSTPROCESSING=1`. These suppress the corresponding
certificate, global-tools PATH, workload-integrity and artifact-postprocessing
paths in the inspected runtime; they do not disable every startup side effect.

The loader disables module autoload and explicitly imports Utility, Management
and Security from absolute paths under the pinned host's `Modules` directory
before module commands. The caller must still bind the host-only `PSModulePath`,
telemetry opt-out and an owned `PSModuleAnalysisCachePath` before host startup,
restoring caller process values afterward. Preparation uses a process-prefix
cache; launch and `Get-RChildEnvironment` use their owned scratch caches. The
existing supervisor clears and copies every entry of that returned map.
Aggregate's 21 direct negative hosts share the owned outer cache, rather than
having per-process cache isolation.

Host-only module imports and owned analysis caches do not redirect every
KnownFolder-based input or write. PowerShell may use
`StartupProfileData-NonInteractive`; SDK startup may use the NuGet
`Migrations\1` marker/directory and `NuGet-Migrations` mutex. Any applicable
startup configuration and these normal effects require exact input/effect
admission; a redirected HOME or dead proxy is not evidence of their absence
or network containment. No private configuration content belongs in proof
documentation.

These keys cover those immediate child boundaries, not arbitrary descendants
that may clear their own environments. The app-only environment in `run.ps1` is
unchanged; the existing late supervisor already supplies its PowerShell opt-out.
The full reachable spawn/environment graph must be screened for each route.
Another relevant clearing boundary that loses the flag requires stopping for an
amendment, not silently extending scope.

The Avalonia environment variable is defense in depth, not the primary claim
that task-local discovery or side effects cannot happen.
In the inspected task, ticket discovery precedes the opt-out check and
Community/Trial paths can bypass its early return; the environment variable
alone is therefore insufficient.

The fixed Build common argument array preserves its **12 entries in order**,
including `-m:1`, `-nr:false` and the intentional empty command-line global
property `-p:UsedAvaloniaProducts=`. It appends six false globals, giving
**18 entries**: `ImportUserLocationsByWildcardBeforeMicrosoftCommonProps`,
`ImportUserLocationsByWildcardAfterMicrosoftCommonProps`,
`ImportUserLocationsByWildcardBeforeMicrosoftCommonTargets`,
`ImportUserLocationsByWildcardAfterMicrosoftCommonTargets`,
`ImportUserLocationsByWildcardBeforeMicrosoftCSharpTargets`, and
`ImportUserLocationsByWildcardAfterMicrosoftCSharpTargets`, each spelled
`-p:<name>=false`. These are the six relevant user-wildcard hooks in the
inspected net10 graph, not a general suppression of all MSBuild imports.
Four static Build sites produce six effective MSBuild vectors:
Debug/Release rebuild and four-selected-case test pairs, then application and
generator publish. Existing arguments, selectors, destinations, budgets and
process/source custody remain unchanged. This is not an extra argument for the
separate Inputs-stage `dotnet exec` generator invocation.

Only the Debug/Release `dotnet test` vectors append
`-- xUnit.PreEnumerateTheories=false`, after the common arguments. This defers
theory data enumeration until execution so filtering does not enumerate
unselected MemberData. It does not change the requirement for exactly four
passing selected fixture results in each configuration. The real-framework
discovery regression separately checks deferred nonenumeration, executes the
four selected results, and discovers but never executes its eager positive
control.

The inspected OSS closure's AvaloniaStats target requires nonempty
`UsedAvaloniaProducts`. Keeping it empty suppresses the **entire current task**,
including task-local license-ticket discovery/classification/messages and
possible failures, telemetry writes and collector launch. It is not a dedicated
official suppression API or a blanket claim that licensing behavior is
unchanged. No paid entitlement bypass or private licensing-data access is
authorized. New entitlement coupling or other applicable property use requires
stopping for review, not extending this suppression automatically.

The Python tests inspect the real source structure with comments/string contents
excluded from structural positions. The authenticated loader tests inspect the
actual Run and launch runner hashtables, Build array and call-site ASTs. Actual-source assertions
are separate from controlled in-memory source mutations used to isolate negative
checks; the mutations do not replace the real Run/environment implementation.
No new probe evaluates a Run/Build/Launch body, installed MSBuild/task/DLL,
collector, or license store. The pre-existing early-prefix Run scope probe is
not telemetry-policy evidence; the existing launcher worker-stop probe likewise
does not establish the new opt-out policy.

Python tests cover the actual source, negative source/XML variants, current
project-reference declarations and public nonexecuting AST wiring. The loader's
build-policy inventory requires **128 named structural/AST cases**, covering
actual-source assertions, controls and negative/propagation fixtures. This is
a required inventory, not a claim that a run reached or passed it. The 322
restage cases, 22-member closure, existing report keys and four fixture
selections per configuration do not change. Structural fixtures reject
inherited-only/wrong or comment-only controls, late Clear, missing/unowned
caches, module-bootstrap changes, missing/nonempty/duplicate/conflicting
global flags, misplaced or missing test-only discovery suffixes, missing
common-vector use, and relevant local-property/removal/override forms.
They are not execution of the installed target or a general MSBuild interpreter.

**Source tests alone do not unblock an operational Build.** A fresh complete
execution-surface screen must still examine actual SDK/import/response/default
item/workload chains, analyzers, generators, tasks, project references, package
files and property propagation, including applicable `TreatAsLocalProperty`,
`GlobalPropertiesToRemove`, `RemoveProperties` and later overrides. Dependency,
SDK/import or paid-product changes require renewed review. No claim is made
about whether any historical build transmitted telemetry.

Changed source bytes require fresh source/closure and operational data bindings
before any future Build/Inputs admission. Keep accepted historical Validate
records, ungranted templates and consumed attempts immutable; do not silently
repin or reuse their grants. This policy adds no persistent machine/user setting,
new loader mode, network sandbox, descendant containment or GUI authority.

## Helper validation (no private machine configuration)

Use an existing PowerShell 7.5+ installation (major version 7). The three-OS CI job requires the
preinstalled supported host and fails if it is absent; it never installs a host
or silently skips these tests.

```powershell
python -m unittest scripts.tests.test_offline_patch_import_proof scripts.tests.test_crossplatform_workflow -v
```

The Windows PowerShell command is a separate, prospectively screened invocation
of the freshly pinned absolute host, not a persisted wrapper or private executor.
The following process-only binding saves/restores the launching process values
and removes variables that were originally absent;
it does not write machine/user settings. `$proofScratch` must already name a
fresh owned directory covered by the local-test admission. Use it only after exact-source safety
acceptance and explicit local-test admission, including verification of the host
pin. Other external hosts need their own approved pre-host binding; no workflow
or global setting change is made here.

```powershell
$priorHostEnvironment = @{}
foreach ($name in @('POWERSHELL_TELEMETRY_OPTOUT', 'PSModulePath', 'PSModuleAnalysisCachePath')) {
    $priorHostEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$proofExitCode = 1
try {
    [Environment]::SetEnvironmentVariable('POWERSHELL_TELEMETRY_OPTOUT', '1', 'Process')
    [Environment]::SetEnvironmentVariable('PSModulePath', 'C:\Program Files\PowerShell\7\Modules', 'Process')
    [Environment]::SetEnvironmentVariable('PSModuleAnalysisCachePath', [IO.Path]::Combine($proofScratch, 'ModuleAnalysisCache'), 'Process')
    & 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -NonInteractive -File scripts\OfflinePatchImportProof\test-pure.ps1
    $proofExitCode = $LASTEXITCODE
}
finally {
    foreach ($name in $priorHostEnvironment.Keys) {
        if ($null -eq $priorHostEnvironment[$name]) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $priorHostEnvironment[$name], 'Process')
        }
    }
}
exit $proofExitCode
```

The initial tool host is already initialized and is not retroactively attested
by this literal. Negative-admission PowerShell workers inherit the external
host's flag; clearing workers must independently preserve it. An earlier screen
of an older source tree or launch binding does not admit amended source.

This ordinary command **trusts the reviewed checkout**, not independently approved
source pins. Its outer driver copies the fixed public closure into a fresh owned
fixture, generates test-only pins and invokes `AggregatePure`. Pinned aggregate
dispatch extracts only its authenticated declaration envelope/main; it never
executes the outer driver. Inner loader regressions run only failing AggregatePure
cases, so the successful outer aggregate cannot recurse.

The aggregate creates a fresh test-owned directory under `TestResults`, invokes
the production restager with generated benign bytes and complete synthetic
historical evidence, and checks fresh copy, existing-path refusal, unchanged
donors, hash failure, durable failure output, and the production supervisor's
inventory verifier. The fake ROM/ZIP/runtime filenames contain inert test data;
they are never opened by an application, native helper, archive reader or runtime.
Successful test data is removed; failed evidence is retained.

The restager executes 322 named cases: the original 286 in their original order,
followed by 26 receipt-retention and 10 external-deadline cases. The later cases
and their helper live in `Configuration.Tests.ps1`, keeping the fixed pure
entrypoint below the loader's unchanged 65,536-byte ceiling. Both the runner and
supervisor require the complete combined inventory. Private execution results
are historical evidence, not substitutes for running this public suite.

Windows additionally compiles the actual launcher (`Readiness + Policy`) and
runner (`Readiness + Policy + Desktop`) output-only shapes using existing
PowerShell reference assemblies. It does not load or invoke the resulting desktop
assembly. No full GUI-containing E2E suite is run.

The existing Windows supervised-success integration additionally starts one
authenticated PowerShell worker and checks its retained executable image and the
supervisor's own image. Only this case opts into the fixed 26 current
`PSHOME\ref` compiler DLLs; all negative/inert fixture defaults remain synthetic.
Reference rows and dependent input, metadata and history pins are constructed
together. Eight separately counted fixture checks cover the real/default rows,
coherent provenance and refusal before allocation for missing or altered pins.
Missing references fail; the suite does not install tools or bypass admission.
The Windows aggregate compiles the reader as its own test-owned assembly, matching
the supervisor's standalone source compilation, then compiles the modeled tests
against it. The reader is loaded from bytes into the default assembly context,
so its fixture DLL is not held open when successful test data is deleted. The
aggregate emits its success report only after its fixture cleanup completes.
This prevents duplicate type definitions when the live case loads
the same authenticated reader. The tests invoke the existing internal delegate
seam through reflection; production visibility and native dispatch are unchanged.

This live integration is not a pure/native-free test. A successful Windows
aggregate reports `liveProcessImageIntegrationCases=1` and `native_calls=true`;
other platforms report zero and false, with zero Windows reference-fixture cases.
`gui_authorization` remains false: neither the integration nor output-only
compilation invokes desktop readiness, UIA, application startup, input or capture.
Review the complete source and obtain the applicable safe-to-run acceptance
before running a changed live integration. Its existing single-worker custody,
raw-output limits, time budgets and no-retry rules remain unchanged.

Both production writer adapters are tested with real files and injected clock
observations at 105/315 seconds, including optional verification/completion
expiry. Terminal evidence is written first. Final reporting has one separate
interval of at most five seconds and 1 MiB on the same already-running clock.
This is **not** additional process, EOF, cleanup or retry time. Failure remains
failure even when a final receipt is successfully retained.
The final interval must begin at or after the terminal writer's last admitted
observation on that same clock. Nonfinite/backward observations are refused.
The supervisor checks its external deadline before accepting completed exit/EOF
observations, so a late natural exit remains a failure.

Only the two deliberate depth-warning cases capture their warning stream.
Unexpected warnings remain visible. Loader tests establish a local `r` alias
trap and invoke the real fixed entrypoint without invoking that alias.

## Installed-library preservation

The production snapshot requires exactly the `proof` directory, its descriptor
and payload, and the root `.febuilder-patch-import.json` marker produced by Core.
Despite its extension, that marker uses Core's exact ASCII owner/GUID/FE8U/newline
format. The marker is validated with a bounded read and its authenticated bytes
are included in the same tree fingerprint as the payloads. It is not ignored as
incidental metadata: changing even its valid operation GUID invalidates the
before/after preservation assertion.

The shared non-native snapshot helper is called by the actual desktop scenario.
Both current pure runners execute 26 real-directory snapshot checks separately
from the unchanged 522 original policy cases. Missing or malformed markers,
unexpected paths, substituted directories and oversized payloads fail closed.
The 16-entry, 16-MiB file, no-reparse and exact-inventory limits remain in force.

## Local configuration and provenance

The example is intentionally non-runnable. Replace every placeholder locally,
and pin its exact bytes with SHA256. Never commit real configuration, approval
bodies, run IDs, installed metadata, outputs, ROMs, ZIPs, DLLs or screenshots.
Approval references/hashes are caller-supplied approval metadata: a syntactically
valid URL is not an authorization, and the scripts do not acquire approval.

All configuration objects reject duplicate/case-colliding keys, unknown top-level
fields, incorrect types, missing required fields, noncanonical paths and reparse
ancestry before allocation, compilation or spawning. JSON is bounded to 4 MiB,
depth 32 and 50,000 nodes. A pin is exactly `{path, bytes, sha256}`: canonical
absolute local path, integer length, and 64 lowercase hex digits. Source,
configuration and approval pins are supplied by the caller; none are built in.

The full top-level shape is in `configuration.example.json`. `preparation` may be
null for GUI-only use; `restage` may be null when not restaging. Output/evidence
roots must already exist. Every run-specific destination must be absent.
Source is always resolved relative to the package, not the output or data root.
`sourceManifest` is exactly `{"files":[...]}` with the 20 relative CS/PS1 members
listed by `Assert-ProofSource`, each with exact bytes/SHA256.

Preparation retains the reviewed input/metadata protocols as pinned JSON:

* Inputs: `application_pins_status` = `final-pinned`; `worktree`, `dotnet`,
  `head`, `head_tree`, `accepted_application_source`,
  `reviewed_application_base`, `patch2_submodule`, `fe_info_submodule`;
  `source_sha256` and `reviewed_localization_delta_sha256` relative-path/hash maps;
  `reviewed_documentation_delta_paths` string array and exactly two canonical
  `package_folders`. Commit/tree values are 40 lowercase hex digits.
* Metadata: `dotnetRoot`, `pshome`, `systemRoot`, three-component `sdk`/`runtime`
  versions; pinned `tools`, `runtimeAssemblies`, `compileReferences`; and
  worktree-relative pinned `assets`. Required executable tools must have exactly
  one matching row. Commands, arguments and child environments stay fixed in code.
* Restaging preserves the historical preparation protocol rather than accepting
  an arbitrary directory. `history` is the closed 15-key
  `windows-desktop-restage-source-v1` object: `schema`, `status`, `root`,
  `planReference`, `planGateReference`, `planBoardSha256`, `priorId`,
  `applicationHead`, `applicationTree`, `priorSourceGate`, relative `files`,
  `evidence`, `pureCaseNames`, `limits`, and `externalClosureRule`. A partial
  projection or an extra top-level field is refused. Its `evidence` pin map
  contains `preflight`, `bSource`, `dSource`, `handoff`,
  `metadata`, `inputManifest`, `validate`, `build`, `inputs`, `outputs`,
  `projection`, and `refused`. Those historical pins bind three stage receipts,
  grant/outer links, unchanged HEAD/tree, 99 preflight rows/46 installed pins,
  the 7/10-member historical source closures, generator dependencies, 11
  validation files, seven runtime bindings, two compile outputs, the original
  publish/projection mapping and both four-case Debug/Release TRX files.
  Historical refusal stays failed. Historical files are authenticated data,
  never executed or rewritten. The exact original bytes are authenticated before
  consuming exactly one leading UTF-8 BOM at the XML-only boundary; DTDs and
  external resolvers are prohibited.

Production results include `startedUtc`, `completedUtc` and `elapsedSeconds`.
The supervisor accepts valid timestamp strings or the `DateTime` values produced
by PowerShell's existing JSON decoder. It does not change the decoder or
normalize authenticated input bytes. Calendar timestamps are descriptive;
execution and reporting budgets use the existing monotonic clocks.

Do not edit a frozen manifest or donor to make a refusal pass. Retain the failed
evidence and obtain a new independently approved source/configuration/run binding.

## Entry points

All operational files refuse direct `-File`, call-operator and dot-source execution
with `PinnedProof.UnsupportedDirectRoute`, before importing dependencies. Use the
fixed loader modes below. Their v2 bindings carry `configuration` and
`configurationSha256`; the following lists describe the preserved internal
parameters, not supported direct commands. They are Windows operations, not CI's
pure command.

* `prepare.ps1`: fixed `-Stage Build|Validate|Inputs`, fresh `-Id`, matching
  `-SourceManifestSha256`, `-HelperSourceManifestSha256`,
  `-SourceGateReference`, `-AuthorizationReference`; Inputs also needs the
  passed `-BuildReceiptSha256` and `-ValidationReceiptSha256`.
* `validate-helper.ps1`: parent-only `-Mode Pure|Compile`, `-Id`, and both
  source hashes; it requires the owned Validate attempt and working directory.
* `supervision\NonCopySupervisor.ps1`: `-Mode Pure|ReadOnlyPrerequisites`.
* `supervision\RestageSupervisor.ps1`: a fresh `-NewGuiId`; it creates the
  exclusive claim consumed by `restage\restage.ps1`.
* `launch.ps1`: `-InputManifest`, `-InputManifestSha256`,
  `-SourceManifestSha256`, `-AuthorizationReference`; it owns runner launch.
  `run.ps1` is its fixed child, not an independent cleanup owner.

The launcher permits only one receipt-bound `GetProcessById` adoption followed
by identity validation and retained custody. Runner exit must be confirmed before
app cleanup; at most one nonrecursive kill of each original retained process is
allowed. A killed/timed-out app never counts as graceful-close success.

## Independently pinned bootstrap and bindings

The selected PowerShell host and **PinnedLoader itself must first be authenticated
externally**. A loader cannot establish its own trust by hashing itself after
execution. Its inclusion in the closure binds later child-loader reads; it does
not replace external bootstrap authentication.

`PinnedLoader.ps1` accepts only `Mode`, `CommandBytes`, `CommandSha256`,
`BindingsPath`, `BindingsBytes`, `BindingsSha256`, and mandatory `ClosurePath`,
`ClosureBytes`, `ClosureSha256`. Production pins come from the caller's approved
data, never discovery of current checkout bytes. All nine parameters must be
supplied; there is no unpinned production fallback.

Closure JSON is exactly:

```json
{"schema":"pinned-proof-closure-v1","files":[{"path":"OfflinePatchImportProof\\Configuration.ps1","bytes":1,"sha256":"REPLACE_WITH_APPROVED_SHA256"}]}
```

This abbreviated example is deliberately incomplete. Exactly 22 rows are required:
the existing 20 feature-package CS/PS1 members, prefixed `OfflinePatchImportProof\`,
and `WindowsDesktopProof\PinnedLoader.ps1` plus
`WindowsDesktopProof\PinnedLoader.Tests.ps1`. The fixed inventory is public in
`Get-PinnedProofFiles`. Unknown/missing/duplicate/case-colliding rows or fields,
incorrect types, sizes, hashes, or unsafe/reparse paths are rejected. The existing
configuration's 20-member source manifest remains a separate additional provenance
check; historical 7/10-member preparation evidence is not upgraded or relaxed.

Closure and bindings are each 1..16384 bytes. Each source is 1..1048576 bytes,
with a 22 MiB aggregate ceiling. The selected command remains 1..65536 bytes and
its separately supplied pin must equal its closure row. Reads use exact lengths,
an EOF sentinel and SHA256. JSON and executable AST input use strict UTF-8 without
a BOM. JSON/AST parsing consumes authenticated buffers.

**Every member, including all four C# units and secondary test/policy libraries,
is authenticated before any package code, compilation, output allocation or
downstream spawn.** Admission errors start with `PinnedProof.Authentication:`;
they cannot be promoted to successful operational receipts.

Bindings v1 is rejected. V2 has exactly these 15 fields:

```json
{"schema":"pinned-proof-bindings-v2","mode":"Pure","configuration":null,"configurationSha256":null,"newGuiId":null,"inputManifest":null,"inputManifestSha256":null,"sourceManifestSha256":null,"authorizationReference":null,"id":null,"priorId":null,"helperSourceManifestSha256":null,"sourceGateReference":null,"buildReceiptSha256":null,"validationReceiptSha256":null}
```

Only the following fields may be non-null for each exact mode. `config` below
means `configuration` + `configurationSha256`; all other names are literal.

| Mode | Fixed target | Required non-null data |
| --- | --- | --- |
| Pure | Original restage pure AST, through the loader's fixed `Invoke-PinnedRestagePure` adapter | None |
| AggregatePure | Aggregate envelope/main | None |
| SupervisedPure | NonCopy supervisor, Pure | config |
| ReadOnlyPrerequisites | NonCopy supervisor, ReadOnlyPrerequisites | config |
| Restage | Restage supervisor | config, newGuiId |
| Gui / RunChild | Launcher / runner | config, inputManifest, inputManifestSha256, sourceManifestSha256, authorizationReference |
| Build / Validate | Preparation, corresponding Stage | config, id, sourceManifestSha256, helperSourceManifestSha256, sourceGateReference, authorizationReference |
| Inputs | Preparation, Inputs | Same as Build, plus buildReceiptSha256, validationReceiptSha256 |
| ValidatePureChild / ValidateCompileChild | Validation helper, Pure / Compile | config, id, sourceManifestSha256, helperSourceManifestSha256 |
| PrerequisitesChild | Read-only restager | config, priorId |
| RestageChild | Copying restager | config, priorId, newGuiId |

No operational fields are admitted for Pure/AggregatePure, but their independent
closure pins are mandatory. IDs, hashes, paths and references retain their strict
types/formats. URLs remain metadata, not acquired execution authorization.

Operational files contain a direct-route throw and one declaration-only
`ProofEnvelope`, including a separate fixed named main. The loader imports the
authenticated envelope body into its invocation scope and calls only the fixed
main. Supervisor library imports dot-source cached authenticated bodies with no
arguments, exposing writers without invoking production main. No Boolean,
environment marker, secret or caller script callback constitutes authentication.

Preparation, launcher and supervisor child dispatches reauthenticate the complete
closure before creating closed data-only child bindings. They pass the inherited
closure pins to the public loader, and the fresh child independently repeats
admission. Existing host, attempt/cwd, claim, ID, deadline and custody checks remain.

### Quiescent-source limitation

The authenticated entry/envelope and supervisor-library buffers are not reopened
for execution. Other already-authenticated PowerShell dependencies, C# `Add-Type
-Path` inputs and the child loader are reopened. **All 22 source files and their
path ancestry must remain quiescent throughout execution, including between the
parent's verification and the child's loader open.** The correction refuses
pre-existing tampering; preflight/path checks are neither atomic filesystem
protection nor an OS sandbox. A hostile caller manually evaluating arbitrary code
outside the supported API is not contained.

Old grants, manifests, failed receipts and prepared roots stay historical and
unchanged. A corrected source closure requires fresh approval and bindings.
Passing these non-native tests is not authority for production preparation or
GUI execution; the feature's UI automation remains in this feature PR.
