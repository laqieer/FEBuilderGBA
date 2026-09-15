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
the bound `AvaloniaWindowClass`:

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
same-class, distinct, immediate-main-owned setup wizard with the unique visible
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

## Owned-window query topology

UIA ancestry is not native window membership: an owned modal window can be a UIA
descendant of its owner while retaining a distinct `GA_ROOT`. Desktop own-PID
children are therefore seeds, not an exhaustive window list. The real driver
uses `DesktopOwnedTree` with its `AutomationElement` adapter; modeled-adapter
regressions execute that same traversal without UIA/native calls.
Both pure runners enforce 108 named `ownedTreeCases` (the original 94 traversal
cases plus 14 projection/refresh cases), separately from the
unchanged 522 policy, 26 snapshot and 75 startup-observation cases.

The bounded raw walker verifies UIA PID before other node access and resolves
membership through the nearest nonzero HWND in RawView ancestry. Native PID,
live `GA_ROOT`, top-level identity, class and bounded owner-chain validation
remain mandatory. A distinct validated own root is queued separately and its
subtree is pruned from the owner's control search. `GA_ROOTOWNER` is never an
admission shortcut. An invisible valid own seed can still expose a visible owned
window. Consistent root rediscovery is deduplicated; conflicting aliases, cycles
and duplicate control matches refuse the attempt.

Startup, wizard/editor/confirmation discovery, picker host/nested-edit/open
queries, and row/name/status lookups share this boundary. Fixed selectors read
content only after membership checks. The shared production projection helper
freezes its epoch before inspecting any root, including hidden roots that may be
skipped. It checks that same epoch after every visibility read and projection
callback, before deciding readiness, and at final acceptance. An observed change
refuses the sample instead of adopting a newer epoch that could conceal an
earlier omission. Candidate and acceptance-refresh model tests use this actual
orchestration. Existing wizard, picker owner/title,
typed-control, enabled, foreground and action-time checks remain.

Each sample/query has hard limits of eight canonical windows, 4,096 raw-node
visits, depth 32, and 65,536 adapter-issued UIA/Win32 calls. Runtime IDs are bounded
to 32 integers and owner chains to eight. Parent resolution, discovery, matching
and diagnostics share the budget. Counters do not measure provider-internal RPCs.
Calls check existing absolute stage/global deadlines before and after; a blocked
provider still relies on the unchanged joined-worker/runner termination boundary.

HWND comparisons use their documented low 32 significant bits and sign-extended
native-call representation. This normalization is not applied to process handles
or arbitrary pointers, and never replaces native PID/root validation.

`QueryFailure` records at most one fixed stage/selector/predicate and bounded
ownership/counter snapshot. Only previously positively owned HWNDs are included;
foreign handles/PIDs and all names, text, values, paths and runtime IDs are
suppressed. A failure may refresh its known owned handle once, using at most four
remaining budgeted native calls. If cancellation/deadline intervenes, after-state
can be unavailable; diagnostics do not mask the original refusal.

Stale/unavailable roots and ownership failures are not retried or converted to
missing controls. These samples are not atomic and do not prove absence of
same-PID HWND reuse. Attempt `329` remains failed/consumed: its exact offending
selector/HWND was not recorded, so the topology defect is not an attestation of
that attempt's precise cause. A new real desktop attempt requires current SOURCE,
fresh preparation and a separate one-use grant.

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
cases through the real reporting functions. The original 522/26/75/108 and
restage/runtime-binding inventories remain separate.

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

## Helper validation (no private machine configuration)

Use an existing PowerShell 7.5+ installation (major version 7). The three-OS CI job requires the
preinstalled supported host and fails if it is absent; it never installs a host
or silently skips these tests.

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File scripts\OfflinePatchImportProof\test-pure.ps1
python -m unittest scripts.tests.test_offline_patch_import_proof scripts.tests.test_crossplatform_workflow -v
```

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
