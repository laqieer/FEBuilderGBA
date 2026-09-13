# Offline patch-import desktop proof

This package keeps the feature's actual Windows UI automation and necessary
support in the same feature PR. It does not modify application behavior and does
not depend on another GUI-readiness PR. Pure tests are **not GUI acceptance**.

## Reviewable source

`scripts\OfflinePatchImportProof` contains:

| Source | Responsibility |
| --- | --- |
| `Desktop.cs` | The feature-specific ordinary `--rom` → loading/main → Patch Manager → native picker flow, valid and invalid ZIP assertions, one editor-only PrintWindow, and normal close. |
| `Policy.cs`, `Policy.Tests.cs`, `Readiness.cs` | Own-session admission, bounded dispatch, retained-process cleanup decisions, worker containment, and 522 pure cases. |
| `RuntimeBinding*`, `ProcessImage*` | Pinned runtime identity and bounded process-image observation; 22 and 10 original cases. Windows-form path checks remain Windows lexical checks on every OS. |
| `prepare.ps1`, `validate-helper.ps1` | Fixed Build/Validate/Inputs operations, pinned local tools, fixture tests and output-only compilation. The existing `scripts\SyntheticProofFixtures` project remains the only ROM fixture generator. |
| `run.ps1`, `launch.ps1` | Ordinary GUI runner and retained-runner/application supervisor. No global input, focus manipulation, PID enumeration, recursive kill, screenshot fallback, or timeout promotion. |
| `Configuration*`, `configuration.example.json` | Bounded data-only inputs, source closure, relocation/no-effect tests, and real staging/writer integration fixtures. |
| `restage\*` | Strict historical preparation verification, exclusive claim, CreateNew copy, physical inventory, receipts, and the 286 archived pure cases. |
| `supervision\*` | One shared fixed-mode supervisor, separate restage adapter, raw bounded logs and production terminal/final writers. |

`scripts\WindowsDesktopProof\PinnedLoader*` is the separate shared loader package,
published in this same PR. It accepts no script text, command path, environment,
or arbitrary arguments. Fixed modes map to fixed source entrypoints.

## Pure validation (no machine configuration)

Use an existing PowerShell 7.5+ installation (major version 7). The three-OS CI job requires the
preinstalled supported host and fails if it is absent; it never installs a host
or silently skips these tests.

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File scripts\OfflinePatchImportProof\test-pure.ps1
python -m unittest scripts.tests.test_offline_patch_import_proof scripts.tests.test_crossplatform_workflow -v
```

The aggregate creates a fresh test-owned directory under `TestResults`, invokes
the production restager with generated benign bytes and complete synthetic
historical evidence, and checks fresh copy, existing-path refusal, unchanged
donors, hash failure, durable failure output, and the production supervisor's
inventory verifier. The fake ROM/ZIP/runtime filenames contain inert test data;
they are never opened by an application, native helper, archive reader or runtime.
Successful test data is removed; failed evidence is retained.

Windows additionally compiles both original output-only shapes using existing
PowerShell reference assemblies. It does not load or invoke the resulting desktop
assembly. No full GUI-containing E2E suite is run.

Both production writer adapters are tested with real files and injected clock
observations at 105/315 seconds, including optional verification/completion
expiry. Terminal evidence is written first. Final reporting has one separate
interval of at most five seconds and 1 MiB on the same already-running clock.
This is **not** additional process, EOF, cleanup or retry time. Failure remains
failure even when a final receipt is successfully retained.

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
  an arbitrary directory: `history` pins the original closure with `priorId`,
  `applicationHead`, `applicationTree`, `priorSourceGate`, relative `files`, and
  an `evidence` pin map containing `preflight`, `bSource`, `dSource`, `handoff`,
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

Do not edit a frozen manifest or donor to make a refusal pass. Retain the failed
evidence and obtain a new independently approved source/configuration/run binding.

## Entry points

All operational entrypoints require `-Configuration` and
`-ConfigurationSha256`. They are Windows operations, not CI's pure command.

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

## Pinned loader bindings

The loader declares only `Mode`, `CommandBytes`, `CommandSha256`, `BindingsPath`,
`BindingsBytes`, `BindingsSha256`. Binding bytes must be 1..16384; command bytes
1..65536. Reads are canonical/non-reparse, bounded with an EOF sentinel,
length/hash checked and strict UTF-8 without a BOM. The same verified command
buffer is parsed with its fixed source filename and invoked; it is not reopened
for execution and package-relative resolution survives.

Bindings have exactly these fields:

```json
{"schema":"pinned-proof-bindings-v1","mode":"Pure","configuration":null,"configurationSha256":null,"newGuiId":null,"inputManifest":null,"inputManifestSha256":null,"sourceManifestSha256":null,"authorizationReference":null}
```

`Pure` invokes the committed restage pure suite and requires every data field to
be null. Other modes require configuration path/hash; Restage alone requires
`newGuiId`; Gui alone requires all four GUI binding fields. Unexpected fields
are refused. The other fixed targets are the matching supervision/launch scripts.
Tests exercise refusal and a real same-buffer invocation, without dynamic
test-mode commands or native/application launch.
