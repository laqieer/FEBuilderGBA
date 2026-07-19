# FEBuilderGBA.CLI Reference

Comprehensive reference for the cross-platform `FEBuilderGBA.CLI` command-line tool.

The most commonly used commands have full subsections under [Commands](#commands); the
remaining commands are listed in compact form under
[Additional commands](#additional-commands); decomp-project-only commands are under
[Decomp project mode](#decomp-project-mode). The complete option list with inline help is
always available via `FEBuilderGBA.CLI --help`.

**Source:** `FEBuilderGBA.CLI/Program.cs`,
`FEBuilderGBA.CLI/Program.Playtest.cs`, `FEBuilderGBA.CLI/RomLoader.cs`

## Usage

```
FEBuilderGBA.CLI [command] [options]
```

Arguments use `--key=value` syntax. Boolean flags use `--flag` (no value).
The short alias `-h` is equivalent to `--help`.

When invoked with no arguments, the CLI prints help and exits with code 0.

> **Android note:** The Android port is a **GUI build** (Avalonia single-view app), not a CLI command — there are no Android-specific CLI flags. See [ANDROID.md](ANDROID.md) and [CROSS_PLATFORM.md](CROSS_PLATFORM.md) for how the cross-platform builds are produced and run.

---

## Global Options

These options can be combined with any ROM-based command.

| Option | Description |
|---|---|
| `--rom=<path>` | Path to the GBA ROM file to operate on. Required by most commands. |
| `--force-version=<VER>` | Override automatic ROM version detection. Values: `FE6`, `FE7J`, `FE7U`, `FE8J`, `FE8U`. |

---

## Commands

### `--help`, `-h`

Print usage information and exit.

```
FEBuilderGBA.CLI --help
FEBuilderGBA.CLI -h
```

**Exit code:** always 0.

---

### `--version`

Print version, copyright, and license information.

```
FEBuilderGBA.CLI --version
```

Output format:
```
FEBuilderGBA.Core Version:YYYYMMDD.HH
Copyright: 2017-
License: GPLv3
```

**Exit code:** always 0.

---

### `--playtest`

Run a deterministic, data-only scenario through the optional, source-pinned
mGBA 0.10.5 Python binding.

```bash
FEBuilderGBA.CLI --playtest --check --python=/path/to/python
FEBuilderGBA.CLI --playtest --rom=rom.gba --scenario=scenario.json \
  --out=result.json --artifact-dir=artifacts --python=/path/to/python
```

| Option | Required | Description |
|---|---|---|
| `--check` | Alternative | Verify the pinned mGBA version/commit; cannot be combined with run options. |
| `--rom=<path>` | Run | Input ROM, read-only, at most 32 MiB. |
| `--scenario=<path>` | Run | Strict UTF-8 schema-v1 scenario. |
| `--out=<path>` | No | Persist the same JSON verdict emitted on stdout. |
| `--artifact-dir=<dir>` | No | Existing directory used to persist a requested final-frame PNG; capture and SHA-256 evidence still occur without it. |
| `--python=<executable>` | No | Interpreter override; then `FEBUILDERGBA_PLAYTEST_PYTHON`, then platform candidates. |
| `--timeout=<ms>` | No | Native process timeout, 1,000-3,600,000; default 600,000. |

Stdout is exactly one JSON object. `runFrames` is exact normal completion; a
watchdog is the scenario-level softlock signal, while `--timeout` is a separate
stuck-process failure. The fixed startup contract disables sync/frameskip,
mutes audio, uses HLE BIOS, keeps savedata on anonymous in-memory backing
without attaching a save VFile, and disables save/patch/cheat autoload.
The .NET process boundary caps stdout and stderr at 1,048,576 characters per
stream and terminates the runner tree on overflow.

**Exit codes:** `0` pass/check OK; `1` usage, setup, dependency, I/O, or harness
failure; `2` ROM guard, assertion, crash, or softlock verification failure.

See [Headless Playtest](HEADLESS-PLAYTEST.md) for setup, schema, memory domains,
result fields, determinism boundaries, and CI proof.

---

### `--makeups=<path>`

Create a UPS patch by diffing a modified ROM against the original.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **modified** ROM. |
| `--fromrom=<path>` | Yes | Path to the **original** (vanilla) ROM. |

```
FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba
```

**Exit code:** 0 on success, 1 if files are missing or arguments are incomplete.

---

### `--applyups=<path>`

Apply a UPS patch to a ROM and write the patched result.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **original** ROM to patch. |
| `--patch=<path>` | Yes | Path to the `.ups` patch file. |

The `=<path>` value of `--applyups` is the **output** file path.

```
FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups
```

**Exit code:** 0 on success, 1 on error. CRC mismatch prints a warning but still writes output.

---

### `--lint`

Run FELint checks on a ROM and report errors/warnings.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM to lint. |
| `--force-version=<VER>` | No | Override version detection. |

```
FEBuilderGBA.CLI --lint --rom=rom.gba
FEBuilderGBA.CLI --lint --rom=rom.gba --force-version=FE8U
```

**Exit code:** 0 if no errors found (warnings are OK), 1 if any ERROR-severity issues exist.

---

### `--disasm=<path>`

Disassemble the ROM's code sections to a text file.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM to disassemble. |
| `--force-version=<VER>` | No | Override version detection. |

The `=<path>` value is the output `.asm` file.

```
FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba
```

**Exit code:** 0 on success, 1 on error.

---

### `--decreasecolor`

Quantize an image's palette to a limited number of colors (for GBA-compatible graphics).

| Option | Required | Description |
|---|---|---|
| `--in=<path>` | Yes | Input image file (PNG, etc.). |
| `--out=<path>` | Yes | Output image file. |
| `--paletteno=<n>` | No | Maximum palette colors. Default: **16**; range 2–256, or 1–256 with `--noReserve1stColor`. |
| `--noScale` | No | Do not scale RGB values to GBA 5-bit range (0-31). |
| `--noReserve1stColor` | No | Do not reserve palette slot 0 for transparency. |
| `--ignoreTSA` | No | Ignore TSA 8x8 tile deduplication constraints. |
| `--json` | No | Emit one JSON object on stdout for success or failure. Errors still return a non-zero exit code. |

```
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16 --noScale --noReserve1stColor
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16 --json
```

This command does **not** require a ROM; it operates purely on image files.

**Exit code:** 0 on success, 1 on error.

---

### `--pointercalc`

Search for pointer references across two ROMs. Finds where addresses in the source ROM map to in the target ROM.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Source ROM. |
| `--target=<path>` | Yes | Target ROM to search in. |
| `--address=<hex_list>` | Yes | Comma-separated hex addresses (e.g., `0x100,0x200`) or path to a file containing addresses. |
| `--tracelevel=<n>` | No | Search depth level. Higher values search more bytes per address. Default search length is 16 bytes; tracelevel N sets it to `max(4, N*4)`. |

```
FEBuilderGBA.CLI --pointercalc --rom=source.gba --target=target.gba --address=0x100,0x200
```

This command does **not** perform full ROM initialization — it reads the raw binary data directly.

**Exit code:** always 0 (even if no matches found).

---

### `--rebuild`

Rebuild (defragment) a modified ROM using the vanilla ROM as a reference.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **modified** ROM. |
| `--fromrom=<path>` | Yes | Path to the **vanilla** (original) ROM. |

```
FEBuilderGBA.CLI --rebuild --rom=modified.gba --fromrom=original.gba
```

**Exit code:** 0 on success, 1 on error.

---

### `--export-buildfile`

Export a deterministic, git-friendly **buildfile recipe** describing the complete binary
delta from a clean ROM to a modded ROM. The recipe is authoritative and lossless: every
target byte is owned exactly once by the clean baseline, a declared extension fill, or a
single payload range. No source ROM path or full ROM is ever copied into the project.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | Path to the **modded** plain regular ROM file (kept in memory; never mutated). |
| `--clean=<path>` | Yes | Path to the **clean/baseline** plain regular ROM file of the **same version**. Its SHA-256 is the reproducibility identity. |
| `--out=<dir>` | Yes | New project directory. **Must not already exist.** |
| `--force-version=<VER>` | No | Applies **only** to loading the modded ROM; clean/modded version identity is still enforced. |
| `--with-source` | No | Also emit an advisory, non-authoritative `source/` projection (opt-in). |

```
FEBuilderGBA.CLI --export-buildfile --rom=modified.gba --clean=original.gba --out=project/
```

Output layout:

```
project/
  buildfile.json   # canonical machine-readable manifest (schema v1) — the ONLY build authority
  main.event       # derived Event Assembler installer (PUSH / ORG / FILL Amount Value / #incbin / POP)
  README.md        # generated layout + authority notes (no absolute paths)
  data/            # one raw payload per range: <index>_<offset>_<length>.bin
  source/          # optional advisory projection (only with --with-source, non-composable)
```

Authority model: `buildfile.json` + `data/` are the sole build authority (consumed by
issue #1936, which applies/verifies a recipe). Payload ranges come directly from an authoritative,
bounded byte-level diff (`maxGap=0`: unchanged bytes are never folded into a payload); a decomp-diff
classifier then advisorily annotates each authoritative range with a best-effort category/confidence/
suggestion, falling back to a stable `unknown`/low-confidence/manual-review record for any single
range whose classifier faults — a classification failure never drops, reorders, resizes, or otherwise
changes the authoritative ranges, and never aborts the export. To bound worst-case fragmentation
(e.g. a large alternating-byte diff), the exporter rejects a diff producing more than 16,384 distinct
changed ranges with an explicit, path-free resource-safety error **before** any payload/manifest file
is materialized; ordinary mods are far below this limit. `main.event` is a derived interoperability
surface; it emits extension bytes with ColorzCore's byte-wise `FILL Amount Value` form
(`FILL <length> <fillByte>`). A non-skippable structural test pins that two-operand parser contract;
a gated real-ColorzCore test additionally rebuilds the declared extension fill and payload bytes
exactly when a complete bundled EA toolchain is available. The installed-patch inventory in the manifest is advisory (its
`config/patch2/{version}`
directory is resolved by existence only and enumerated under a guard, so a missing, empty,
slow, or unreadable patch library yields `unavailable` and never aborts the export; a directory that
exists but cannot actually be enumerated — permissions, I/O, path failures — is distinguished from a
directory that is simply absent, and any enumeration/parameter/relative-path failure is reported in
the manifest as a stable, fixed, path-free reason string — never the raw exception message or the
absolute patch library/patch file path). Each individual `PATCH_*.txt` FINAL entry (the definition
file itself, not its ancestor directories, which remain out of scope for this guarantee) is opened
no-follow and must be an exact regular file: a symlink, reparse point, or other non-regular-file
type is refused before a single byte is read (closing an information-disclosure path where a
final-entry symlink pointing outside the patch library could otherwise leak an external target
file's content into the advisory inventory), and a Browser host fails closed rather than falling
back to an unsafe open — both cases degrade the same way as any other expected filesystem fault
(stable, path-free reason; authoritative recipe/payload export unaffected). The shared bounded
reader itself never swallows a missing entry: `FileNotFoundException`/`DirectoryNotFoundException`
propagate and each caller decides. A missing patch ROOT during INITIAL discovery is a successful
empty listing; a definition file that was already discovered but is missing or faulting when the
bounded METADATA pass opens it degrades the WHOLE advisory inventory to `unavailable` with a
generic, path-free filesystem reason (NOT success-empty and NOT a resource-budget reason); only
the later bounded raw-PARAMETER pass maps a missing file to a successful, empty parameter list.
The advisory patch inventory (every installed/unknown
record plus its nested raw parameters) and the manifest's `warnings` share ONE internal
16,384-item resource-safety budget; the exporter is a *bounded producer* — patch-file discovery,
per-record, and per-parameter counting are all checked against the remaining budget **before**
anything is materialized, and a warning appended late (e.g. by the optional source projection,
after the patch inventory budget was already reserved) is re-checked against the same shared
total. Exceeding the budget at any point degrades the **entire** advisory patch inventory to
`unavailable` with the same stable, path-free reason — never a partial/truncated installed list,
and the authoritative recipe/payloads/manifest still export successfully. Independently of that
item-count budget, every individual `PATCH_*.txt` definition read during metadata discovery and
raw-parameter capture is byte-bounded: each file is capped at **16 MiB**, rejected on the raw byte
count **before** any line is decoded. A `FileStream.Length` reported above 16 MiB — including an
oversized or sparse-huge-reported length — is rejected **up front, before any read is issued**;
this initial length check is distinct from, and precedes, the separate cap+1 probe. Once that
length check is passed, a bounded read of at most cap+1 bytes on the same handle still catches a
file that **grows beyond the cap after that check**, again before the surplus byte is ever
written or decoded — so no partial line is ever decoded or returned on either kind of rejection.
The reader also enforces exact length-drift detection on that same handle: accepted bytes
reaching decode must equal the `FileStream.Length` captured at open time **exactly**. Any byte
read past that captured length is rejected before it is written into the accepted buffer or
decoded — even when the running total is still comfortably within the 16 MiB cap — and reaching
genuine end-of-file with **fewer** bytes than that captured length (a premature EOF) is rejected
the same way. `bytesRead` stays monotonic and visible to the caller on either rejection; a false
length-drift result immediately degrades/clears/stops the enclosing advisory pass, so no later
file in that pass can exploit any remaining aggregate byte budget. This is a length-drift check,
not an immutable-snapshot guarantee: an in-place mutation that leaves the file's length
unchanged, or bytes appended strictly **after the final observed EOF**, are both outside what
this check can detect — an append visible **before** that EOF is instead read as surplus and
rejected the same way.
The reader never allocates a buffer sized to the full 16 MiB cap for
every file: reads are issued as fixed **64 KiB** `ArrayPool`-rented chunk requests, and the
initial accepted in-memory storage capacity is sized from that already-validated `FileStream.Length`
(so at most 16 MiB, never the unvalidated raw/sparse-reported value) and grows
**only** as bytes are actually accepted — never blindly pre-allocated to the full cap for a small
file — so a library of thousands of mostly-small files does not incur cumulative large-buffer
allocation. Raw lines are
also counted against an independent raw-line cap **during** decoding, not after a full file is
materialized into a line list — so a within-byte-budget file consisting of massive numbers of
tiny/blank lines cannot force an unbounded line list either; no partial line list is ever produced
or returned on any breach. The metadata-discovery pass and the raw-parameter-capture pass each also
track their own running total against a **separate 64 MiB aggregate cap per pass** (128 MiB
worst case if both passes are near their cap at once); breaching either the per-file or the
aggregate byte cap degrades the entire advisory patch inventory to `unavailable` the same way the
16,384-item budget does — never a partial/truncated installed list. Accepted bytes are decoded
with the same BOM-detecting, non-strict-UTF-8 (replacement-fallback) semantics as the legacy
`File.ReadLines`/`File.ReadAllLines` APIs, so any file within budget is **decoded**
byte-identically to the unbounded legacy path. The one deliberate behavioral difference is
install-marker **classification**: this bounded metadata scan classifies a file-backed `$FGREP`
install marker (one the shared resolver would resolve by opening the external file it names) as
`unknown` **before** any filename access, whereas the public/unbounded Patch Manager, CLI, patch
scanner, and rebuild paths still resolve it (shipped patches depend on that legacy behavior); the
bounded raw advisory params read from the patch definition itself are unchanged. This strict
exporter inventory is separate from the legacy
Patch Manager/CLI enumeration path, which logs an unreadable individual definition and retains all
other successfully parsed patches. The optional `source/` projection is a non-composable
best-effort produced only by the built-in synchronous `RebuildProducerCore` path. It first writes
to an exporter-private scratch tree while **no publish stage exists**. The exporter opens the root
once and captures descendants handle-relatively (`openat` on Unix; `NtCreateFile` with a held
`RootDirectory` on Windows), rejecting links and non-regular nodes without following replaceable
ancestor pathnames. The immutable snapshot is limited to 32,768 entries, 256 MiB of file data,
16 MiB per text file, and bounded path depth/metadata. Pre-capture manifest parsing uses that
16 MiB text ceiling, rejects more than 32,768 sidecar directives, bounds line count/length, and
only opens sidecars before their bodies are read by the bounded handle-relative capture. The
snapshot preserves empty directories, accepts/removes a UTF-8 BOM, rejects UTF-16/32 BOMs, and
normalizes strict UTF-8 text to LF,
strips only the exporter-owned scratch path,
and leaves binary bytes unchanged. Invalid UTF-8, a limit breach, or any capture fault downgrades
only the advisory projection to `error`.

Scratch is deleted and verified gone before the private atomic-publish stage is reserved. Near the
publication boundary, the snapshot is materialized into a brand-new `source/`; no runner-owned
inode or hard link is ever published. The internal source-swap fault seam validates the mutated
candidate, then discards every candidate inode and rematerializes the immutable snapshot; a regular
hard-link replacement therefore cannot survive into the published tree. A late unsafe link/node
replacement is removed and recorded consistently as a projection error in `README.md` and
`buildfile.json`; inability to remove it aborts the export. Refusal/error cleanup and authoritative-
output failures likewise verify stage/scratch removal and report any residual temporary path. The
projector is synchronous by contract; as with every user-owned output, a separate process running
as the same OS identity can modify files after publication, so consumers must still treat loose
project files as mutable.

Independently of the advisory item-count and per-patch-file byte budgets above, the exporter also
guarantees the published `buildfile.json` itself can never exceed the **same 16 MiB** cap its own
`--build-buildfile` consumer enforces (below) — one shared constant neither side maintains a
separate copy of, so the two can never drift apart. Before both the initial manifest write and any
later rewrite triggered by a late materialization-error warning, the exact UTF-8 byte count of the
serialized manifest (including its trailing newline — the identical text that is actually written
to disk) is measured against that cap. Within budget, nothing changes. Over budget with an
available advisory patch inventory, the **entire** inventory (never a partial/truncated list, and
never any authoritative range/payload/identity/extension/projection field) degrades to
`unavailable` with its own stable, path-free reason, and the manifest is re-measured; if it is
still over budget afterward — or there was no inventory left to degrade — the export fails before
either file is published, and the same existing stage/scratch cleanup applies. This closes a
producer/consumer gap: previously only the consumer enforced a manifest size bound, so a
serialized manifest could exceed it — a defect BOTH sides now share exactly one immutable
constant to prevent (this guarantee covers only the exporter's own serialization size; the
consumer's other structural/identity/payload/filesystem validations, and any post-publication
mutation of the published project, are unaffected and remain independently enforced).
If
the target extends the clean ROM, the exporter picks the
most frequent extension byte (lowest byte on ties) as the fill and emits only sparse override
ranges, so a large mostly-`FF`/`00` extension never becomes a giant payload. Emulator/playtest
validation is issue #1932.

**Rejections (exit 1, no partial output):** an unknown command-specific option (e.g. a typo
like `--with-soruce`); missing `--rom`/`--clean`/`--out`; a `--rom` or `--clean` value that
contains a parent-directory (`..`) path segment (rejected up front — `Path.GetFullPath` collapses
`..` lexically before symlinks are resolved, which can diverge from the physical filesystem, so
the exporter fails closed; ordinary and `.`-relative paths are fine); a valueless
`--force-version` or `--force-version=` (rejected globally before command I/O); a nonexistent input ROM;
`--rom` and `--clean` that resolve to the **same physical file** — each input is canonicalized to
its realpath (following symlinks/junctions **including ancestor links**) for preflight, so
`C:\real\mod.gba` and `C:\link\mod.gba` (with
`C:\link → C:\real`) are rejected as one file, while two *distinct* ROMs that merely share a
benign symlinked ancestor such as macOS `/var → /private/var` are accepted; comparison is
case-insensitive on Windows/macOS; on Windows, any device-namespace spelling (`\\?\`, `\\.\`,
`\??\`, or an equivalent slash variant) is rejected at the shared normalization boundary before
filesystem inspection, ROM loading, or output creation, including extended-drive and extended-UNC
forms—use a standard drive or UNC path instead; a symlink/junction output parent directory
(single-entry check
— the atomic publish only needs the stage and destination to share one real immediate parent); a
pre-existing `--out`; a clean/modded version mismatch; a modded ROM shorter than clean or larger
than 32 MiB; or either ROM input not being a plain regular file. Each final ROM entry is opened
no-follow, validated on the exact handle, bounded by that handle's length, and read completely
through the same handle before parsing. The two authoritative opened handles are identity-compared
again (128-bit `FileIdInfo` plus volume on Windows, with the legacy 64-bit file index used only when
that capability is unavailable; `(device,inode)` on Unix), so a regular-file replacement or hard-
link alias cannot collapse the inputs after preflight; a FIFO/device cannot block or produce an
unbounded read, and pathname replacement cannot redirect the later byte load. Browser builds fail
closed before pathname access because they do not expose the required native no-follow open and
opened-file identity primitives. A non-canonical
(but same-version) clean baseline is an explicit warning, not a
rejection. The `--out` path is normalized (full-path + trailing-separator trim, roots preserved)
before all checks, so `--out=project/` and `--out=project` behave identically. Global switches
(`--help`, `--version`) still take precedence over the verb in either order. A final-component
symlink is allowed as long as it resolves to a *distinct* physical file from the other input.
Windows additionally compares volume serial + 128-bit file ID, with the older 64-bit handle
identity as a capability fallback for FAT/FAT32/exFAT and other filesystems that do not expose
`FileIdInfo`. Hard links, mounted-drive aliases, and local-drive/UNC aliases of the same file are
therefore rejected even when their resolved path strings differ; Unix hard links are rejected by
the final opened `(device,inode)` comparison. The macOS path preflight remains conservatively
case-insensitive.

**Exit code:** 0 on success, 1 on error.

---

### `--build-buildfile`

Independently **rebuild a ROM** from a schema-v1 buildfile recipe. The reconstruction is
driven **solely** by `buildfile.json` + `data/`: this verb never executes or parses
`main.event`, invokes ColorzCore, consumes `source/`, or trusts the advisory patch inventory,
projection metadata, or warnings — none of those surfaces can influence a single output byte.
It never mutates the clean ROM or any project file.

| Option | Required | Description |
|---|---|---|
| `--clean=<path>` | Yes | Clean/baseline ROM. Must exactly match the recipe's declared clean size + CRC32 + SHA-256 + version. |
| `--project=<dir>` | Yes | The recipe directory (must contain `buildfile.json` + `data/`). |
| `--out=<path>` | Yes | New destination ROM. **Must not already exist or be inside `project/data`**; published atomically, never overwriting. |

```
FEBuilderGBA.CLI --build-buildfile --clean=original.gba --project=project/ --out=rebuilt.gba
```

The consumer opens `buildfile.json` as an exact no-follow regular file, bounds it to 16 MiB (the
SAME shared cap `--export-buildfile` now also enforces on every manifest it publishes — see
above — so an exporter-produced manifest is never rejected here SOLELY because exporter-owned
serialization exceeded that shared cap; post-publication mutation and every other structural/
identity/payload/filesystem validation described below remain independently enforced regardless
of origin. A hand-edited or third-party-produced buildfile.json is not exempt and is rejected
outright if it exceeds it),
requires strict UTF-8 (an optional UTF-8 BOM only), rejects duplicate JSON property names, and
requires `schemaVersion == 1`. Every schema-v1 object has an exact member allowlist: unknown
members and wrong-typed optional/advisory members are rejected, while validated advisory values
remain non-authoritative. It then enforces the full recipe contract before allocating the target:
exact clean identity (size/CRC32/SHA-256/canonical flag) and version equal to the clean ROM's; a
target no smaller than clean and no larger than 32 MiB; `dataDirectory == "data"`;
extension present exactly when the target extends clean (`start == clean size`,
`length == target - clean`, canonical `0xNN` fill); at most 16,384 ranges with contiguous
zero-based indices, exact `totalRanges`, strictly ordered non-touching positive ranges,
checked in-bounds offset arithmetic, canonical `gbaAddress` (`0x08000000 + offset`),
`changedBytes == length`, unique canonical `data/{index:D4}_{offset:X6}_{length}.bin` payload
paths, canonical CRC/SHA spellings, and an exact `totalChangedBytes` total. `data/` is captured
through the same handle-relative, no-follow snapshot reader used by the exporter (relative names
are mapped back under the `data/` prefix), rejecting symlinks/reparse points/devices,
subdirectories, missing payloads, extra payloads, case-colliding names, any payload whose
length or SHA-256 does not match the manifest, and any claimed changed byte equal to its clean
or extension-fill baseline. The target is then reconstructed as clean bytes +
the declared extension fill + the validated payloads in manifest order (a payload may override
the fill or span the clean/extension boundary), and its recomputed CRC32/SHA-256 must match the
recipe's declared target before anything is published.

The project root and output parent are physically resolved before manifest/data access. Every
output-parent ancestor is compared to authoritative project `data/` by filesystem entry identity
(device/inode on Unix; volume + file ID on Windows), so alternate drive/UNC/mount aliases are
rejected along with ordinary descendants. The boundary and project-root identity are checked
again immediately before publication, so a successful build cannot poison the next build with an
undeclared payload. The verified ROM is published to `--out` by writing an exclusively-reserved,
bounded fixed-ASCII-name staging file in the destination's own parent directory, flushing it
durably, fully closing the handle, and then committing it with an atomic **no-replace** rename
(`MoveFileExW` without replace on Windows,
`renameat2(RENAME_NOREPLACE)` on Linux, `renamex_np(RENAME_EXCL)` on macOS). A destination that
appears after the pre-check is preserved and the build fails; on any failure the staging file is
removed and its removal verified.

The identity recheck detects same-path replacement during reconstruction. As with other pathname-
based desktop tools, a privileged concurrent remap in the narrow interval after the final identity
check and before the staging-file open is outside this command's guarantee; the publication layer
still rejects reparse-point parents, destination replacement, and no-replace rename races.

`patches.installed` (with its nested `params` arrays) and `warnings` share the SAME internal
16,384-item resource-safety budget the exporter uses. Unlike the exporter (which *degrades* an
oversized patch library to `unavailable`), the consumer treats an over-cap combined total as a
**structural validation failure**: each array's declared length is checked against the shared
running budget *before* that array is enumerated, so a hostile or corrupt buildfile.json cannot
force an unbounded number of advisory records/params/warnings into memory — the build is rejected
before a single over-budget POCO/list entry is materialized. Staging-file cleanup (after a failed
publish) and the staging create-collision check never use `File.Exists`, which can silently
report "gone" for a directory/reparse-point replacement or for an inspection fault; instead a
shared tri-state attribute inspection classifies each outcome and fails closed, with the exact
path, whenever existence cannot be positively confirmed.

**Rejections (exit 1, no output):** an unknown command-specific option; missing
`--clean`/`--project`/`--out`; a `--clean` or `--project` value containing a `..` segment; a
Windows device-namespace path; a nonexistent clean ROM or project directory; a clean ROM larger
than 32 MiB or not a plain regular file; a pre-existing `--out`; an output parent physically at
or below project `data/`; any structural/identity/size/range/path/hash/bounds violation above; or
a rebuilt ROM whose recomputed identity does not match the recipe's declared target. Global
switches (`--help`, `--version`) take precedence over the verb in either order.

**Exit code:** 0 on success, 1 on any usage/validation/identity/I/O/publication error.

---

### `--buildfile-roundtrip`

Prove a modded ROM is **reproducible** from its recipe: internally export the recipe (with the
advisory `source/` projection **off**), independently rebuild from it in a private atomically
reserved scratch tree, and compare the rebuilt bytes to the already-opened `--rom` bytes — the
**sole** drift oracle. No project or output is *intentionally* published; this is not an absolute
guarantee that no scratch can remain (e.g. a crash before cleanup runs, or external interference) —
see the cleanup-failure exit code below.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | The **modified** ROM to prove reproducible (the export target and the comparison oracle). |
| `--clean=<path>` | Yes | Clean/baseline ROM of the **same version** (must be a different physical file from `--rom`). |
| `--force-version=<VER>` | No | Applies only to loading the modified `--rom`. |

```
FEBuilderGBA.CLI --buildfile-roundtrip --rom=modified.gba --clean=original.gba
```

`--rom` and `--clean` use the exact regular-file, bounded-read, parent-traversal, and
device-namespace ingestion contract of `--export-buildfile` (including opened-handle identity
rejection of the same file). The verb reserves a private bounded-name scratch parent atomically,
passes an absent child project path to the exporter, reconstructs from the published project with
the production consumer, then attempts to remove the scratch tree and verifies it is gone. A byte
or length difference is reproducibility **drift** (exit 2) reporting the first-difference offset
(or first length-difference offset) between the rebuilt and expected bytes. A **declared-target-
identity-only** drift — the rebuilt bytes match `--rom` exactly, but the recipe's own declared
target crc32/sha256 do not match the recomputed ones — is also exit 2, but instead reports the
declared-vs-actual crc32/sha256 hashes; it has no byte offset to report. Neither case is a usage
error. Scratch-cleanup failure is a hard error (exit 1) even after an otherwise exact comparison,
and it reports the residual scratch path from the underlying deletion failure.

**Exit codes:** **0** = the rebuilt ROM is byte-for-byte identical to `--rom`; **2** = a completed
round-trip whose rebuilt bytes drift from `--rom` (or whose declared target identity drifts); **1**
= usage, validation, I/O, or scratch-cleanup error (the latter reports the residual scratch path).

---

### `--songexchange`

Copy a song from one ROM to another.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | **Destination** ROM (will be modified in-place). |
| `--fromrom=<path>` | Yes | **Source** ROM to copy the song from. |
| `--fromsong=<hex>` | Yes | Song ID in the source ROM (hex, e.g., `0x1A`). |
| `--tosong=<hex>` | Yes | Song slot in the destination ROM (hex, e.g., `0x1A`). |
| `--force-version=<VER>` | No | Override version detection for the destination ROM. |

```
FEBuilderGBA.CLI --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=0x1A --tosong=0x1A
```

**Warning:** This command modifies the destination ROM file in-place.

**Exit code:** 0 on success, 1 on error.

---

### `--convertmap1picture`

Convert an image to GBA map tile data and TSA (Tile Set Arrangement).

| Option | Required | Description |
|---|---|---|
| `--in=<path>` | Yes | Input image file. Dimensions must be multiples of 8; conversion is limited to 15 opaque colors plus reserved transparent palette slot 0, and 1024 unique tiles. |
| `--outImg=<path>` | No* | Raw 4bpp tile bytes (32 bytes per tile), not an encoded image regardless of extension. |
| `--outTSA=<path>` | No* | Output TSA data file (LZ77-compressed). |
| `--outPal=<path>` | No* | Matching GBA RGB555 palette data (up to 32 bytes). |
| `--json` | No | Emit one JSON object on stdout for success or failure. Errors still return a non-zero exit code. |

*At least one output is required. Multiple outputs are written transactionally so aliases cannot
overwrite another requested artifact while reporting success.

```
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin --outPal=palette.bin
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin --outPal=palette.bin --json
```

This command does **not** require a ROM.

**Exit code:** 0 on success, 1 on error.

---

### `--generate-random-map`

Generate FEBuilderGBA CSV map output through a trusted local FEMapCreator CLI.

| Option | Required | Description |
|---|---|---|
| `--femapcreator=<path>` | Yes | Trusted absolute local FEMapCreator program path. The CLI never downloads or bundles FEMapCreator; it launches a Windows `.exe`, a managed `.dll`, or a native file carrying an executable bit on Unix. |
| `--tileset=<name>` | Yes | FEMapCreator tileset name to pass to `generate`. |
| `--width=<int>` | Yes | Output map width in tiles. Range: **1..64** so the generated CSV round-trips through FEBuilderGBA's map CSV parser. |
| `--height=<int>` | Yes | Output map height in tiles. Range: **1..64** so the generated CSV round-trips through FEBuilderGBA's map CSV parser. |
| `--out=<path>` | Yes | Output FEBuilderGBA CSV file. It must not alias the selected FEMapCreator program or discovered tileset assets, and is written atomically only after generation, bounded MAR parsing, and CSV serialization all succeed. |
| `--assets-dir=<path>` | No | Optional absolute FEMapCreator assets-root override passed through to `--assets-dir`. |
| `--seed=<int>` | No | Deterministic generation seed. If omitted, the CLI generates one locally and reports the effective value. |
| `--algorithm=<name>` | No | FEMapCreator algorithm: `experimental`, `legacy`, or `hybrid`. Default: **`experimental`**. |
| `--json` | No | Emit one JSON object on stdout for success or failure. Errors still return a non-zero exit code. |

```
FEBuilderGBA.CLI --generate-random-map --femapcreator=C:\tools\FEMapCreator.exe --tileset=Grassland --width=15 --height=10 --out=map.csv
FEBuilderGBA.CLI --generate-random-map --femapcreator=C:\tools\FEMapCreator.exe --tileset=Grassland --width=15 --height=10 --seed=42 --algorithm=hybrid --out=map.csv --json
```

This command does **not** require a ROM and does **not** mutate ROM state. It shells out only to
the exact FEMapCreator path you provided, reads the resulting headerless signed-LE `.mar` once
into its exact bounded size, converts
(`tileIndex * 32`) into FEBuilderGBA MAR values (`chipsetIndex * 4`), serializes the map through
`MapExportCsv.Serialize`, and publishes the final CSV through `AtomicFileSetWriterCore`. A
non-zero FEMapCreator exit includes bounded stderr (or stdout when stderr is blank) in the
reported error.

**Exit code:** 0 on success, 1 on error.

---

### `--translate`

Dump ROM text to a TSV file, or import translated text from a TSV file back into the ROM.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM file. |
| `--out=<path>` | No | Export: write all text entries to this TSV file. |
| `--in=<path>` | No | Import: read text entries from this TSV file and write them to the ROM. |
| `--force-version=<VER>` | No | Override version detection. |

**Three modes:**
1. **Info mode** (no `--out`, no `--in`): prints ROM version and text entry count.
2. **Export mode** (`--out`): dumps all text to a TSV file.
3. **Import mode** (`--in`): reads TSV and writes text back to the ROM. The ROM file is saved in-place.

```
# Info mode
FEBuilderGBA.CLI --translate --rom=rom.gba

# Export
FEBuilderGBA.CLI --translate --rom=rom.gba --out=texts.tsv

# Import
FEBuilderGBA.CLI --translate --rom=rom.gba --in=texts.tsv
```

**Exit code:** 0 on success, 1 on error (including if zero entries were written during import).

---

### `--translate_batch`

Batch translation workflow: export all text, then optionally import translated text.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | Yes | ROM file. |
| `--out=<path>` | No | Export TSV path. Defaults to `<rom_name>.tsv` if omitted. |
| `--in=<path>` | No | If provided, import translated text from this TSV after exporting. |
| `--force-version=<VER>` | No | Override version detection. |

```
FEBuilderGBA.CLI --translate_batch --rom=rom.gba --out=texts.tsv --in=translated.tsv
```

The export always runs. The import only runs if `--in` is provided. The ROM is saved in-place after import.

**Exit code:** 0 on success, 1 on error.

---

### `--lastrom`

Load the last-used ROM from the application config file.

| Option | Required | Description |
|---|---|---|
| `--force-version=<VER>` | No | Override version detection. |

```
FEBuilderGBA.CLI --lastrom
```

Reads `Last_Rom_Filename` from `config/config.xml`, loads the ROM, and prints version info.

**Exit code:** 0 on success, 1 if no last ROM is configured or the file is missing.

---

### `--force-detail`

Acknowledge the detailed editor mode flag. This is primarily used by the Avalonia GUI to skip easy-mode. In the CLI, it simply prints a message and exits.

```
FEBuilderGBA.CLI --force-detail
```

**Exit code:** always 0.

---

### `--test`, `--testonly`

Run built-in self-test diagnostics. Checks config loading, ROM loading, text system, event scripts, and image service.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` | No | If provided, runs additional ROM-specific tests (load, init, text, event scripts). |
| `--force-version=<VER>` | No | Override version detection. |

`--testonly` behaves identically to `--test` but prints an additional "exiting" message.

```
FEBuilderGBA.CLI --test --rom=rom.gba
FEBuilderGBA.CLI --testonly --rom=rom.gba
FEBuilderGBA.CLI --test
```

**Exit code:** 0 if all tests pass, 1 if any test fails.

---

### `--rom-info`

Print ROM metadata to stdout: file path, size, ASCII title and game code (from the
header), detected version, CRC32, header checksum (actual + expected + `VALID`/`INVALID`
status), and a `Mode:` line.

| Option | Required | Description |
|---|---|---|
| `--rom=<path>` **or** `--project=<dir>` | Yes (one) | The ROM file, or a decomp project whose *built* ROM is read. |
| `--force-version=<VER>` | No | Override version detection (`--rom` mode only). |

With `--rom` the last line is `Mode: Rom`. With `--project` it loads the project's built
ROM and the last lines are `Mode: Decomp (preview ROM …)` plus a `Symbols:` breakdown.

```
FEBuilderGBA.CLI --rom-info --rom=rom.gba
FEBuilderGBA.CLI --rom-info --project=decomp/
```

**Exit code:** 0 on success, 1 if neither `--rom` nor `--project` is given, the file is
missing, or the ROM is not a recognized GBA Fire Emblem ROM.

---

## Additional commands

The commands below are documented in compact form — one entry per command with its
required and optional flags. Each is verified against its `Run*` handler in
`FEBuilderGBA.CLI/Program.cs`. The full option list with inline help is available via
`FEBuilderGBA.CLI --help`. Unless noted, ROM-based commands accept the global
`--force-version=<VER>` modifier.

### ROM info / validation

- **`--checksum`** — Validate the GBA ROM header checksum. Requires `--rom`. Prints
  title, game code, and the actual/expected checksum bytes. **Exit:** 0 valid, 2 invalid,
  1 on file/usage error.
- **`--repair-header`** — Recompute and write the correct GBA header checksum in-place.
  Requires `--rom`. **Exit:** 0 (already valid or repaired), 1 on file/usage error.
- **`--diff`** — Compare two ROMs byte-by-byte. Requires `--rom` and `--rom2`; optional
  `--out=<path>` writes a TSV of the differing ranges (a summary always prints to stdout).
  Operates on raw files (no ROM init). **Exit:** 0 on success, 1 on file/usage error.
- **`--translate-roundtrip`** — Export all ROM text, write it back, re-export, and compare
  for losslessness (works on a temp copy; the source ROM is untouched). Requires `--rom`;
  optional `--out=<base>` saves `<base>.export1.tsv` and `<base>.export2.tsv`. **Exit:**
  0 lossless, 2 if any text entry mismatches, 1 on file/usage error.
- **`--data-roundtrip`** — Verify direct struct read/write stability on a temp copy: read
  table values, write the same in-memory values back, re-read, and compare. It does not
  serialize through TSV/CSV/EA/JSON/C. Requires `--rom`; optional `--table=<name>` (default
  `all`). **Exit:** 0 stable, 2 if any table mismatches, 1 on file/usage error.
- **`--lint-oam`** — Validate battle-animation OAM data at an address. Requires `--rom`
  and `--addr=<hex>`; optional `--length=<int>` (0 = auto, default). **Exit:** 0 clean,
  1 if issues are found or on usage error.

### Data

- **`--export-data`** — Export a struct table to TSV/CSV/EA/JSON/C. Requires `--rom` and
  `--table=<name>` (any name from `--list-tables`, or `all`); optional `--out=<path>`
  (defaults to `<rom>.<table>.<ext>`; with `--table=all` it is a base path written as
  `<out>.<table>.<ext>`) and `--format=<tsv|csv|ea|json|c>` (default `tsv`; an unsupported
  value is rejected with an error before any output is written). `--format=json`
  serializes rows as a JSON array of objects: the public key is `Index` (never the internal
  `_Index`), followed by one key per struct field; every value is a JSON **string** holding
  the same hex/text representation as TSV/CSV (e.g. `"0x0A"`) — see
  [`febuilder-cli-as-llm-backend.md`](febuilder-cli-as-llm-backend.md). `--format=c` emits a
  self-contained GNU11 (devkitARM `arm-none-eabi-gcc`-compatible) C translation unit per
  table — a one-byte-packed row `struct`, a 4-byte-aligned array object, a
  `_Static_assert(sizeof(...) == <resolved stride>, ...)`, deterministic
  `struct FEBuilder_<StructName>`/`gFEBuilder_<table>`/`gFEBuilder_<table>Count` names,
  compiler-visible full-width ordinal array designators (`[0x000] =`, `[0x100] =`, ...),
  and a real zero-row GNU array + `Count = 0` symbol pair for a version-absent/empty table — see
  [`febuilder-cli-as-decomp-c-backend.md`](febuilder-cli-as-decomp-c-backend.md) for the full
  contract. `c` is **export-only** (not accepted by `--import-data`) and requires
  `arm-none-eabi-gcc`/host `gcc` with `-std=gnu11` to actually compile the output — FEBuilderGBA
  does not invoke a compiler itself. Optional `--c-symbol=<identifier>` (single-table `--format=c`
  only) overrides the emitted data-array symbol with a strictly-validated, non-keyword C
  identifier that starts with a letter (leading underscores are implementation-reserved at file
  scope; `<stdint.h>` typedef/macro names such as `uint8_t` and `UINT8_MAX` collide with the
  generated prologue; both classes are rejected and invalid values are never silently sanitized);
  matching preprocessor macros (including GNU toolchain built-ins such as `linux`/`unix`) are
  neutralized by generated post-include `#ifdef`/`#undef` guards; the count symbol becomes
  `<identifier>Count`; combining `--c-symbol` with `--table=all`, or with any format other than `c`,
  is rejected before the ROM is loaded or any output file is created. **Exit:** 0 on
  success, 1 on usage/unknown-table/unsupported-format/invalid-`--c-symbol`/C-layout-validation
  error.
- **`--import-data`** — Import a struct table from TSV or JSON and save the ROM in-place.
  Requires `--rom`, `--table=<name>`, and `--in=<path>`. `--format=c` (export-only) is not
  accepted here; only `tsv`/`json` are. JSON input is used when
  `--format=json` is passed explicitly, or automatically when `--in` has a `.json`
  extension (and `--format` is omitted); otherwise the input is parsed as TSV. An explicit
  `--format` value other than `tsv`/`json` is rejected with an error before the ROM is even
  loaded. The JSON document is fully validated (root must be an array; every row an object;
  every property value a JSON string — numbers/booleans/nulls/arrays/objects are rejected;
  no row may repeat the same property name, including `Index`, twice) **before** any ROM
  write; a malformed document fails with a specific error and leaves the ROM untouched. The
  public `Index` key is required and strictly parsed (0x-hex, `$`-hex, or plain decimal,
  optionally followed by a space and a label) back to the internal row index — unlike TSV
  import, a garbage/overflowing/negative `Index` is rejected outright rather than silently
  aliased to row 0. Export preserves the complete row index for every registered table, so
  row 256 is emitted as `0x0100` rather than wrapping to `0x00`. A second, struct/count-aware
  preflight then runs — still **before** any
  ROM write — that TSV import does not perform: every non-`Index` property name must be a
  known field of the resolved table's struct (an unknown/typo'd name, e.g. `Wieght` instead
  of `Weight`, is rejected with the row number and property name — a field simply absent
  from a row is still allowed, for partial updates); every field value must strictly parse
  as a complete `0x`-hex/`$`-hex/plain-decimal token — no trailing tokens, no bare prefixes,
  no negatives, no overflow — and fit the field's byte/word/dword/pointer width (accepted
  values are normalized to a canonical lowercase-`0x` hexadecimal form first, including
  accepted decimal input, so the full unsigned field range reaches the writer safely); no two rows may
  target the same `Index`; and every `Index` must be within `[0, entryCount)` for the
  resolved table (rejected here instead of relying on the writer's silent per-row skip).
  **Exit:** 0 on success, 1 on usage/unknown-table/unsupported-format/malformed-input error.
- **`--resolve-names`** — Resolve entity IDs to names. Requires `--rom`, `--kind=<unit|class|item|song>`,
  and `--ids=<comma-list>`. Prints `id<TAB>name` per ID. **Exit:** 0 on success, 1 on
  usage/unknown-kind error.
- **`--list-tables`** — Print every exportable struct table name (one per line). No ROM
  required. **Exit:** 0.
- **`--export-map-settings`** — Export all chapter/map settings as a TSV of raw struct
  words. Requires `--rom` and `--out=<path.tsv>`. **Exit:** 0 on success, 1 on usage error.

### Graphics

- **`--render-portrait`** — Render a unit's portrait to a PNG. Requires `--rom`,
  `--unit-id=<id>`, and `--out=<path.png>`. **Exit:** 0 on success, 1 on usage/render error.
- **`--export-portrait-all`** — Export every portrait in the table to `portrait_NNN.png`
  files. Requires `--rom` and `--out=<dir>`. **Exit:** 0 on success, 2 if some portraits
  failed to render, 1 on usage error.
- **`--import-portrait`** — Import a PNG into a portrait slot and save the ROM in-place.
  Requires `--rom`, `--portrait-id=<id>` (alias `--unit-id`), and `--in=<path.png>`.
  **Exit:** 0 on success, 1 on usage/import error.
- **`--import-portrait-all`** — Batch-import PNGs from a directory; the leading
  `{id}_`/`{id}`/`0x{id}` of each filename selects the slot. Requires `--rom` and
  `--dir=<directory>`. **Exit:** 0 if all imports succeed, 1 if any failed.
- **`--generate-font`** — Render characters to a 16×16-per-glyph PNG via SkiaSharp. No ROM.
  Requires `--out=<path.png>`; optional `--text=<chars>` (default `A`), `--font-file=<path>`
  (`.ttf`/`.otf`; system default if omitted), `--font-size=<float>` (default 12), and
  `--vertical-offset=<int>` (clamped -8..8). **Exit:** 0 on success, 1 on usage/font error.
- **`--export-palette`** — Export a GBA palette to a file. Requires `--rom`, `--addr=<hex>`,
  and `--out=<path>` whose extension picks the format (`.pal` JASC, `.act` ACT, `.gpl` GIMP,
  `.txt` hex, `.gbapal` raw); optional `--colors=<int>` (1..256, default 16). **Exit:** 0 on
  success, 1 on usage/format error.
- **`--import-palette`** — Import a palette file into the ROM (format auto-detected from
  content/extension). Requires `--rom`, `--addr=<hex>`, and `--in=<path>`. **Exit:** 0 on
  success, 1 on usage/format error.

### Audio

- **`--export-midi`** — Export a ROM song to a `.mid` file. Requires `--rom`,
  `--song-id=<hex>`, and `--out=<path.mid>`. **Exit:** 0 on success, 1 on usage/range error.
- **`--import-midi`** — Import a MIDI file into a ROM song slot and save in-place. Requires
  `--rom`, `--song-id=<hex>`, and `--in=<path.mid>`. **Exit:** 0 on success, 1 on
  usage/range error.

### Events

- **`--disasm-event`** — Disassemble an event/procs/AI script starting at an address.
  Requires `--rom` and `--addr=<hex>`; optional `--type=<event|procs|ai>` (default `event`)
  and `--out=<path>` (prints to stdout if omitted). **Exit:** 0 on success, 1 on
  usage/unknown-type error.
- **`--compile-event`** — Compile an `.event` script with the bundled/configured
  EA/ColorzCore and write the modified ROM. Requires `--rom` and `--in=<path.event>`;
  optional `--out=<path>` (defaults to overwriting the input ROM). **Exit:** 0 on success,
  1 if the tool is missing or compilation fails.
- **`--import-battle-anime`** — Import a battle animation from a `.txt` script or FEditor
  `.bin` (format auto-detected) and save the ROM in-place. Requires `--rom`,
  `--animation-id=<id>` (0-based), and `--in=<path>`. **Exit:** 0 on success, 1 on
  usage/range error.
- **`--export-battle-anime`** — Export a battle animation to a `.txt` script + PNGs, or to
  an animated GIF. Requires `--rom`, `--animation-id=<id>` (0-based), and `--out=<path>`;
  optional `--gif` (GIF instead of `.txt`+PNG) and `--section=<0..11>` (GIF only,
  default 0). **Exit:** 0 on success, 1 on usage/range error.

### Patches

- **`--list-patches`** — List the patches available for the ROM's version and their install
  status. Requires `--rom`; optional `--patch-name=<substring>` filter. **Exit:** 0 on
  success, 1 if the version/patch directory cannot be resolved.
- **`--apply-patch`** — Apply a `PATCH_*.txt` BIN patch (checks dependencies, writes a
  `.backup`, restores on failure). Requires `--rom` and `--patch-file=<path>`. **Exit:** 0
  on success, 1 on unsatisfied dependencies / apply failure / usage error.
- **`--uninstall-patch`** — Restore the original bytes for fixed-address `BIN:0xADDR=file`
  entries using a clean ROM (writes a `.backup`; FREEAREA/JUMP/EA/CLEAR directives can't be
  reversed and warn). Requires `--rom`, `--patch-file=<path>`, and `--original-rom=<clean.gba>`.
  **Exit:** 0 on success, 1 if no BIN ranges or on usage error.
- **`--list-resources`** — List the FE-Repo / FE-Repo-Music submodule resource categories
  and file counts. No ROM. Optional `--category=<name>` substring filter. **Exit:** 0.

### Utility

- **`--expand-table`** — Grow a pointer-based ROM data table by one entry (writes a
  `.backup`, restores on failure). Requires `--rom`, `--pointer=<hex>`, `--entry-size=<int>`,
  and `--count=<int>` (the current row count, required for safety). **Exit:** 0 on success,
  1 on usage/expand error.
- **`--merge3`** — Three-way merge of ROM files. Requires `--base`, `--mine`, `--theirs`,
  and `--out` (all paths). **Exit:** 0 clean merge, **2** merged **with conflicts** (the
  merged ROM is still written; conflicting ranges default to `--mine`'s bytes), 1 on
  file/usage error.
- **`--freespace`** — Scan and report runs of free space (`0x00`/`0xFF`). Requires `--rom`;
  optional `--min-size=<int>` (default 16). **Exit:** 0 on success, 1 on usage error.
- **`--hex-dump`** — Dump ROM bytes in xxd-style hex+ASCII. Requires `--rom` and
  `--addr=<hex>`; optional `--length=<int>` (default 256). **Exit:** 0 on success, 1 on
  usage error.
- **`--search-text`** — Search the decoded text of every ROM text entry (case-insensitive).
  Requires `--rom` and `--query=<text>`; optional `--limit=<1-500>` bounds printed matches while
  the final summary still reports the exact total. **Exit:** 0 on success, 1 on usage error.
- **`--text-refs`** — List every ROM entry that references a text ID (via the text-ref table
  registry). Requires `--rom` and `--text-id=<hex|dec>`. **Exit:** 0 on success, 1 on usage
  error.
- **`--lz77`** — LZ77 compress or decompress a file. No ROM. Requires `--in`, `--out`, and
  exactly one of `--compress`/`--decompress`. **Exit:** 0 on success, 1 on usage/data error.

For the decomp-only `--export-battle-anim-decomp` source exporter, see
[`--export-battle-anim-decomp`](#--export-battle-anim-decomp) in the Decomp project mode
section below.

---

## Decomp project mode

These commands operate on a **decomp project directory** (one containing a `febuilder.project.json`
manifest and a buildable C/JSON source tree) instead of a single `.gba` file. The classic ROM mode
(`--rom=<path>`) is unchanged; the decomp family below adds a `--project=<dir>` mode that loads the
project's *built* ROM as a preview and can rewrite the owning source elements (a churn-free diff)
rather than mutating the ROM directly. A typical workflow is: open the project (`--project`) → resolve
addresses to symbols (`--resolve-addr`) → classify FEBuilder edits (`--migrate-diff`) → rewrite the
owning source (`--write-source`) / export assets (`--export-asset`) → rebuild (`--build-project`).

### `--project=<dir>`

Open a decomp project directory and load its built ROM for preview. Used standalone,
`--project=<dir>` is treated as a `--rom-info` alias — it opens the project, loads its built/preview
ROM, and prints the ROM metadata + a Mode line (decomp vs classic; the decomp Mode line is
`Mode: Decomp (preview ROM …)`, flagging that the ROM is a source-backed build preview). It also
combines with the decomp commands below (`--resolve-addr`, `--migrate-diff`, `--write-source`,
`--export-asset`, `--build-project`). Classic ROM mode (`--rom=<path>`) is unchanged.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (containing `febuilder.project.json`). |

```
FEBuilderGBA.CLI --rom-info --project=decomp/
```

**Exit code:** when run standalone it behaves as `--rom-info` (0 on success, 1 on load failure);
otherwise determined by the decomp command it is combined with.

---

### `--resolve-addr=<hex>`

Resolve an address to a decomp project symbol (requires `--project`). Layers the project's
`.map` / ELF / `.sym` / JSON symbols over the shipped symbol set; this is also what powers the
Pointer Tool's "What is this address?" lookup.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory whose symbols are layered over the shipped set. |
| `--resolve-addr=<hex>` | Yes | The address to resolve (e.g., `0x08012345`). |

Output: it first prints `addr=0x........`. **When a symbol resolves**, it additionally prints
`symbol=...`, `source=<map|elf|sym|json|shipped>`, and `offset=+0x..`. On the no-symbol path (or an
internal error) it instead prints `symbol=(none)` (preceded by `addr=` if it got that far). The
`source=` / `offset=` lines do **not** always print — only when a symbol is actually found. This
command never throws.

```
FEBuilderGBA.CLI --resolve-addr=0x08012345 --project=decomp/
```

**Exit code:** always 0.

---

### `--migrate-diff`

Decomp **diff-to-source migration assistant**: classifies the changes between the project's
built/baseline ROM and a FEBuilder-edited ROM by symbol / category / source / confidence. This is
**advisory and read-only** — it never writes the ROM or any source file.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (its built ROM is the baseline). |
| `--rom2=<editedRom>` | Yes | The FEBuilder-edited ROM, compared against the project's built/baseline ROM. |
| `--out=<report.tsv>` | No | Write the classified report (range / symbol / category / source / confidence) as TSV. |
| `--max-gap=<int>` | No | Small-gap merge distance for range coalescing. Default: **16**. |

```
FEBuilderGBA.CLI --migrate-diff --project=decomp/ --rom2=edited.gba --out=migrate.tsv
```

**Exit code:** 1 on a usage fault (missing `--project`/`--rom2`, file not found, project/preview-ROM
load failure) **or when a requested `--out` report cannot be written**; otherwise 0. The analysis
itself is advisory and never mutates anything.

---

### `--write-source`

Rewrite the owning **C/JSON source element** for a structured table entry instead of mutating the
ROM. This produces a churn-free, minimal diff and marks the project as "needs rebuild".

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (the table must declare a source owner in `tables[]`). |
| `--table=<name>` | Yes | Structured table: `items`, `units` (alias `characters`), `classes`, `map_settings`/`chapter`, `support_units`/`support_attributes`/`support_talks`. |
| `--id=<n>` | Yes | Entry index, in array order. |
| `--field=<name>` | Yes | C/JSON field to change. **REPEATABLE, ordered** — pair each `--field` with a following `--value`. |
| `--value=<int>` | Yes | New value for the preceding `--field` (`0x` hex or decimal; signed fields take the two's-complement magnitude). **REPEATABLE.** |
| `--out-diff=<path>` | No | Write a before/after of the changed source element. |

Unsupported / pointer-like fields fall back to ROM-only / manual handling. Shops (variable-length
lists) are written by `--write-shop` instead (see below).

```
FEBuilderGBA.CLI --write-source --project=decomp/ --table=items --id=1 --field=might --value=0x0A
FEBuilderGBA.CLI --write-source --project=decomp/ --table=units --id=1 --field=hp --value=18 --field=pow --value=7
```

**Exit code:** 0 on success, non-zero on usage / write fault.

---

### `--write-shop`

Rewrite an owning variable-length **`u16` `ITEM_NONE`-terminated shop LIST** in source in place
(#1347), instead of mutating the preview ROM. The whole `{…}` initializer body is **re-serialized**
to the requested item vector + a fresh terminator. This is NOT a minimal-token splice: the body is
reformatted to a canonical style and any per-item comments inside the braces are dropped (the bytes
OUTSIDE the `{…}` body are untouched). When the body already matches that canonical form, the rewrite
is a byte-identical no-op (no churn).

**Symbolic `ITEM_*` lists (#1354):** when the existing source list uses `ITEM_*` macro names (the
canonical FE8U `worldmap_shop_data.c` **item-id-only** form, e.g. `{ ITEM_SWORD_IRON, ITEM_NONE, }`),
the body is re-serialized **symbolically** with the resolved `ITEM_*` macro names instead of raw hex.
The id↔macro map is parsed from the project's constants header — an **enum** (auto-incrementing, with
explicit-literal anchors) or `#define` table, typically `include/constants/items.h`. Discovery
precedence: the owner's project-relative **`constantsHeader`**, then the manifest top-level
**`artifacts.itemConstants`**, then the conventional default **`include/constants/items.h`**. An
EXPLICIT path (`constantsHeader`/`artifacts.itemConstants`) that is absolute / escapes the project
root / missing / unparseable makes the resolver **unavailable** and does NOT fall back to the default
(wrong-universe danger). Symbolic lists are **item-id-only**: each `--items` entry's **quantity must
be `0`** (a non-zero quantity is an actionable refusal — keep quantity 0 or migrate to a raw-hex list)
and an id with no `ITEM_*` constant is refused. A list of plain hex literals still re-serializes to a
raw-hex vector + a `0x0000` terminator.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory (the manifest must declare a `u16-list` list-owner for the shop's symbol). |
| `--symbol=<name>` | One of | Look up the list-owner directly by name (skips the address resolver). |
| `--shop-addr=<hex>` | One of | Shop item-list **ROM offset**; resolved to a list symbol via the project `.map`/`.elf`/`.sym` + a manifest list-owner (strict exact-or-span-covering match). |
| `--items=<csv>` | Yes | New list as `id:qty` pairs (e.g. `0x01:5,0x02:3`); `id`/`qty` hex-or-dec `0..255`, `id != 0`; an **empty** `--items=` empties the shop (just the terminator). For a **symbolic** owner, the quantity must be `0` (item-id-only) — e.g. `0x01:0,0x14:0`. |

A list containing an **unknown or ambiguous** macro element (one the constants header does not resolve
unambiguously) is REFUSED (no-clobber) — export it to a raw-hex list (`--export-asset --kind=shop`) or
edit it by hand. With no decomp symbol AND no manifest list-owner, the command reports **not owned**
(exit 2) and you degrade to `--export-asset --kind=shop`.

```
FEBuilderGBA.CLI --write-shop --project=decomp/ --symbol=ItemList_WM_FluornArmory --items=0x01:5,0x02:3
FEBuilderGBA.CLI --write-shop --project=decomp/ --shop-addr=0xB2A18 --items=0x16:1
FEBuilderGBA.CLI --write-shop --project=decomp/ --symbol=ItemList_WM_Ide_Armory --items=0x01:0,0x14:0   # symbolic ITEM_* (item-id-only)
```

**Exit code:** `0` on success (or clean no-op); `2` for any advisory / no-write outcome — not owned,
ROM-only, manual, **unsupported field** (macro/no-clobber refusal), **rejected** (sourceFile path
escapes the project root), **malformed manifest**, or **not decomp mode**; `1` for a usage / parse
fault (and the unexpected-error / source-not-found cases).

---

### `--export-asset`

Export a ROM asset to a decomp source-tree path. Use the existing dedicated commands
(`--export-midi`, `--render-portrait` / `--export-portrait-all`, `--export-battle-anime`) for
music, portraits, and battle animations.

| Option | Required | Description |
|---|---|---|
| `--kind=<kind>` | Yes | Asset kind: `graphics`, `palette`, `map` (always LZ77-decompressed), `mapchange` (raw u16 map-change overlay), `mapanime2pal` (raw u16 map tile-animation-2 palette), `objtiles` (LZ77-decompressed 4bpp OBJ tile payload), `mapchipconfig` (LZ77-decompressed chipset TSA/config payload), `text`, `shop`. |
| `--out=<path>` | Yes | Output path (project-relative when `--project`; absolute or relative when `--rom`). |
| `--rom=<path>` **or** `--project=<dir>` | Yes | Source ROM, or a decomp project whose built ROM is read (one is required). |
| `--addr=<hex>` | Cond. | ROM address of the asset (required for `graphics`, `palette`, `map`, `mapchange`, `mapanime2pal`, `objtiles`, `mapchipconfig`). For `mapchange` it is the `change_mar` offset (the record `+8` change pointer, dereferenced); for `mapanime2pal` it is the anime-2 entry `+0` palette pointer, dereferenced; for `objtiles` it is the **DEREFERENCED** OBJ LZ77 stream address (NOT `RomInfo.map_obj_pointer`); for `mapchipconfig` it is the **DEREFERENCED** config LZ77 stream address (the CONFIG-PLIST pointer dereferenced, NOT `RomInfo.map_config_pointer`; FE7 split layouts use a separate per-plist `--addr`). |
| `--palette-addr=<hex>` | Cond. | ROM address of the palette data (required for `graphics`). |
| `--width=<int>` | Cond. | Image width in pixels (required for `graphics`; overlay width for `mapchange`, record `+3`). |
| `--height=<int>` | Cond. | Image height in pixels (required for `graphics`; overlay height for `mapchange`, record `+4`). |
| `--count=<int>` | Cond. | Number of `u16` colors for `mapanime2pal` (anime-2 entry `+5`), 1..255. |
| `--colors=<int>` | No | Palette colors (for `palette` and `graphics`). Default: **16**. |
| `--bpp=<int>` | No | Bits per pixel for `graphics`: `4` or `8`. Default: **4**. |
| `--compressed` | No | (graphics only) the source tiles at `--addr` are LZ77-compressed (flag). |

The `mapchange` kind (#1355) exports the **raw uncompressed map-change OVERLAY tile data block** — a flat
`width*height` array of `u16` LE config-descriptor indices. It is **NOT** the `.mar` tile layout (no `<<3`
shift, no LZ77) and **NOT** the 12-byte change-RECORD chain (terminator / flagID / PLIST metadata). The body
is copied byte-for-byte from the (already-uncompressed) ROM; `srcAddr` in the `.change.json` sidecar is
provenance metadata only. Re-import / round-trip / byte-exact ROM verify are below.

The `mapanime2pal` kind (#1360) is the structural **twin** of `mapchange`: it exports the **raw uncompressed
map tile-animation-2 PALETTE data block** — a flat array of `count` 15-bit GBA colors (`count*2` bytes) reached
by each anime-2 entry's `+0` pointer. It uses a single `--count` descriptor (1..255) instead of width/height; no
`<<3` shift, no LZ77. It is **NOT** the anime-2 ENTRY/PLIST table. The CLI takes EXPLICIT `--addr`/`--count` (no
entry-index auto-resolve); `srcAddr` in the `.mapanime2pal.json` sidecar is provenance metadata only. Re-import /
round-trip / byte-exact ROM verify are below.

The `mapchipconfig` kind (#1375) is the structural **twin** of `objtiles`: it exports the **LZ77-decompressed
chipset TSA/config payload** — a single LZ77 stream reached by one dereferenced CONFIG-PLIST pointer (WF
`ImageUtilMap.UnLZ77ChipsetData`). The source body is the **DECOMPRESSED** bytes, NOT a byte-pinned LZ77 stream
(FEBuilder's packer is non-canonical, so the build re-compresses). `--addr` is the **DEREFERENCED** config LZ77
stream address (NOT `RomInfo.map_config_pointer`; FE7 split layouts use a separate per-plist `--addr`). No
`--width`/`--height`/`--count` — the decompressed length comes from the sidecar (`febuilder-mapchipconfig-lz77`).
It is **NOT** the anime-1/anime-2 entry tables, **NOT** the map-change record chain, **NOT** the `.mar` layout.
Re-import / round-trip / read-only decompress-and-byte-compare ROM verify are below.

```
FEBuilderGBA.CLI --export-asset --kind=palette --rom=rom.gba --addr=0x5524 --out=gfx/palette.pal
FEBuilderGBA.CLI --export-asset --kind=graphics --project=decomp/ --addr=0x123000 --width=64 --height=64 --palette-addr=0x124000 --out=gfx/tiles.png
FEBuilderGBA.CLI --export-asset --kind=map --rom=rom.gba --addr=0x200000 --out=map/chapter1.mar
FEBuilderGBA.CLI --export-asset --kind=mapchange --rom=rom.gba --addr=0x300000 --width=15 --height=10 --out=map/chapter1.change
FEBuilderGBA.CLI --export-asset --kind=mapanime2pal --rom=rom.gba --addr=0x400000 --count=16 --out=map/chapter1.mapanime2pal
FEBuilderGBA.CLI --export-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --out=map/chapter1.objtiles
FEBuilderGBA.CLI --export-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --out=map/chapter1.mapchipconfig
FEBuilderGBA.CLI --export-asset --kind=mapanime1gfx --rom=rom.gba --addr=0x600000 --length=512 --out=map/chapter1.mapanime1gfx
FEBuilderGBA.CLI --export-asset --kind=text --rom=rom.gba --out=text/
```

The `mapanime1gfx` (#1389) kind exports the **map tile-animation-1 per-entry GRAPHICS block** — a RAW
UNCOMPRESSED 4bpp tile-byte block sized by the entry's `+2` `u16` length, reached by each anime-1 entry's
`+4` pointer (the inverse of anime-2's `+0`). It is the structural TWIN of `mapchange`/`mapanime2pal`
(RAW, length-sized), **NOT** the LZ77 `objtiles`/`mapchipconfig` pattern: the WF read/import/rebuild paths
treat this block as raw `ImageToByte16Tile` 4bpp bytes (a rebuild `IMG` block), never an LZ77 stream.
`--addr` is the **DEREFERENCED** anime-1 entry `+4` graphics pointer and `--length` is the entry `+2` byte
length. It is **NOT** the anime-1 ENTRY/PLIST table (pointer-per-row, no clean source owner — stays guarded,
tracked by #1389) and **NOT** the `.mar` layout. Re-import / round-trip / read-only RAW byte-compare ROM
verify are below.

**Exit code:** 0 on success, non-zero on usage / export fault.

---

### `--export-voicegroup`

Export a FEBuilder voicegroup (a GBA M4A / MusicPlayer2000 **instrument set**) as reviewable decomp
**source macro assembly** for `sound/voicegroups/voicegroupNNN.s`, using the macros from
`fireemblem8u`'s `asm/macros/music_voice.inc` (#1362). **READ-ONLY** — it reads the preview ROM to
produce a source artifact and **never mutates the ROM**, allocates no free space, and creates no
samples. This is an **export / source-helper**, NOT a byte-pinned ROM round-trip and NOT a full M4A
re-assembler.

| Option | Required | Description |
|---|---|---|
| `--out=<voicegroupNNN.s>` | Yes | Output `.s` path (project-relative + root-confined when `--project`; absolute or relative when `--rom`). |
| `--voicegroup-addr=<hex>` **or** `--song-id=<n>` | Yes (exactly one) | The voicegroup base (ROM offset/pointer), or a song id whose header (`+4`) voicegroup pointer is resolved from the song table. |
| `--rom=<path>` **or** `--project=<dir>` | Yes | Source ROM, or a decomp project whose built ROM is read (one is required). |
| `--number=<N>` | No | The `NNN` used in the label / `.global` / comment. Default: the `--song-id`, else `0`. |

**Supported voice types** are emitted as the exact `music_voice.inc` macros:
`voice_directsound` (0x00) / `voice_directsound_no_resample` (0x08) / `voice_directsound_alt` (0x10),
`voice_square_1` (0x01/0x09), `voice_square_2` (0x02/0x0A), `voice_programmable_wave` (0x03/0x0B),
`voice_noise` (0x04/0x0C), `voice_keysplit` (0x40), `voice_keysplit_all` (0x80).

**Conservatively handled (no guessed symbols):** a `voice_keysplit` / `voice_keysplit_all` (drum)
sub-voicegroup / keysplit-table pointer, and a `voice_directsound` sample pointer, are emitted as
**valid raw `0x08XXXXXX` macro arguments** plus an "unresolved pointer" diagnostic — the sub-table is
**not** inlined (a documented manual step) and no decomp symbol is invented. An **unknown / `0x18`**
voice type becomes a **commented placeholder + diagnostic**, never a wrong macro. All provenance /
unresolved notes are collected into a trailing comment block (never inline between macro args). Wire
the emitted `voicegroupNNN.s` into the decomp build via `songs.mk` / a song's `-G` voicegroup number.

```
FEBuilderGBA.CLI --export-voicegroup --rom=rom.gba --song-id=1 --out=sound/voicegroups/voicegroup001.s
FEBuilderGBA.CLI --export-voicegroup --project=decomp/ --voicegroup-addr=0x207470 --number=42 --out=sound/voicegroups/voicegroup042.s
```

**Exit code:** 0 on success; 1 usage error; 2 export fault / path rejected / song not found; 3 internal read-only-invariant violation.

---

### `--export-battle-anim-decomp`

Export a FEBuilder/FEditor-decoded battle animation as reviewable decomp **source macro
assembly** (`banim_<TAG>_motion.s`) plus per-team `.pal` palette sidecars and a `.json`
registration manifest, using the `fireemblem8u` banim macros (#1363). **READ-ONLY** — it
reads the preview ROM and never mutates it (a before/after SHA-256 of the ROM bytes guards
the invariant).

| Option | Required | Description |
|---|---|---|
| `--out=<banim_<TAG>_motion.s>` | Yes | Output `.s` path (project-root-confined when `--project`; absolute or relative when `--rom`). Sidecars are written alongside. |
| `--animation-id=<n>` **or** `--banim-addr=<hex>` | Yes (exactly one) | 0-based animation index in the ROM table, or the ROM offset/pointer of the 32-byte animation record. |
| `--rom=<path>` **or** `--project=<dir>` | Yes | Source ROM, or a decomp project whose built ROM is read (one is required). |
| `--tag=<name>` | No | Label tag for the emitted symbols. Default: `anim<NNN>`. |
| `--number=<N>` | No | Animation number used in the default tag. Default: `--animation-id`, else `0`. |

```
FEBuilderGBA.CLI --export-battle-anim-decomp --rom=rom.gba --animation-id=1 --out=banim/banim_anim001_motion.s
FEBuilderGBA.CLI --export-battle-anim-decomp --project=decomp/ --banim-addr=0x12A4F0 --tag=eirika --out=banim/banim_eirika_motion.s
```

**Exit code:** 0 on success; 1 usage error; 2 export fault / path rejected / animation not
found; 3 internal read-only-invariant violation.

---

### `--import-asset` / `--roundtrip-asset` / `--verify-asset` (map / mapchange / mapanime2pal / objtiles / mapchipconfig / mapanime1gfx)

Re-import an edited map asset to a raw uncompressed blob, prove a body round-trips, or verify a map-change
overlay / anime-2 palette block / OBJ tileset / chipset config byte-for-byte against the ROM. The `map` (`.mar`
layout) variants are documented under `--export-asset` above; the `mapchange` (#1355) variants are:

| Command | Reads ROM? | Description |
|---|---|---|
| `--import-asset --kind=mapchange --in=<x.change> --out=<x.bin>` | No | Identity copy of the validated `.change` body to a raw blob (NO `[w][h]` header, NO `>>3` shift, NO LZ77). Requires the `.change.json` sidecar. |
| `--roundtrip-asset --kind=mapchange --in=<x.change>` | No | Structure-exact identity proof (`body.Length == width*height*2`, read from the sidecar). Exit 0 lossless, 2 mismatch. |
| `--verify-asset --kind=mapchange --in=<x.change> --addr=<hex> --width --height (--rom\|--project)` | **Yes (read-only)** | Byte-exact ROM-backed mismatch proof — the ONLY ROM-backed verification path. Exit 0 byte-identical, 2 mismatch/fault, 1 usage error. |

```
FEBuilderGBA.CLI --import-asset --kind=mapchange --in=map/chapter1.change --out=map/chapter1.change_raw.bin
FEBuilderGBA.CLI --roundtrip-asset --kind=mapchange --in=map/chapter1.change
FEBuilderGBA.CLI --verify-asset --kind=mapchange --rom=rom.gba --addr=0x300000 --width=15 --height=10 --in=map/chapter1.change
```

The `mapanime2pal` (#1360) variants mirror `mapchange` exactly, swapping the width/height descriptor for a single
`--count`:

| Command | Reads ROM? | Description |
|---|---|---|
| `--import-asset --kind=mapanime2pal --in=<x.mapanime2pal> --out=<x.bin>` | No | Identity copy of the validated palette body to a raw blob (NO header, NO `>>3` shift, NO LZ77). Requires the `.mapanime2pal.json` sidecar. |
| `--roundtrip-asset --kind=mapanime2pal --in=<x.mapanime2pal>` | No | Structure-exact identity proof (`body.Length == count*2`, read from the sidecar). Exit 0 lossless, 2 mismatch. |
| `--verify-asset --kind=mapanime2pal --in=<x.mapanime2pal> --addr=<hex> --count=<int> (--rom\|--project)` | **Yes (read-only)** | Byte-exact ROM-backed mismatch proof — the ONLY ROM-backed verification path. Exit 0 byte-identical, 2 mismatch/fault, 1 usage error. |

```
FEBuilderGBA.CLI --import-asset --kind=mapanime2pal --in=map/chapter1.mapanime2pal --out=map/chapter1.mapanime2pal_raw.bin
FEBuilderGBA.CLI --roundtrip-asset --kind=mapanime2pal --in=map/chapter1.mapanime2pal
FEBuilderGBA.CLI --verify-asset --kind=mapanime2pal --rom=rom.gba --addr=0x400000 --count=16 --in=map/chapter1.mapanime2pal
```

The `objtiles` (#1371) variants prove **decompressed-payload equivalence**, NOT compressed-stream byte identity
(FEBuilder's LZ77 packer is non-canonical, so the decomp build re-compresses the decompressed body). The source
body is the LZ77-DECOMPRESSED 4bpp payload; `--verify-asset` re-decompresses the live ROM block and byte-compares
(READ-ONLY). `--addr` is the **DEREFERENCED** OBJ LZ77 stream address (FE7 obj2 secondary tileset is a separate
stream/`--addr`, never concatenated). No `--width`/`--height`/`--count` — the decompressed length comes from the sidecar:

| Command | Reads ROM? | Description |
|---|---|---|
| `--import-asset --kind=objtiles --in=<x.objtiles> --out=<x.bin>` | No | Identity copy of the validated decompressed body to a raw blob (NO LZ77 compression). Requires the `.objtiles.json` sidecar. |
| `--roundtrip-asset --kind=objtiles --in=<x.objtiles>` | No | Structure-exact identity proof (`body.Length == length`, read from the sidecar). Exit 0 lossless, 2 mismatch. |
| `--verify-asset --kind=objtiles --in=<x.objtiles> --addr=<hex> (--rom\|--project)` | **Yes (read-only)** | LZ77-decompresses the ROM block at `--addr` and byte-compares vs the file body. Exit 0 byte-identical, 2 mismatch/fault, 1 usage error. |

```
FEBuilderGBA.CLI --import-asset --kind=objtiles --in=map/chapter1.objtiles --out=map/chapter1.objtiles_raw.bin
FEBuilderGBA.CLI --roundtrip-asset --kind=objtiles --in=map/chapter1.objtiles
FEBuilderGBA.CLI --verify-asset --kind=objtiles --rom=rom.gba --addr=0x400000 --in=map/chapter1.objtiles
```

The `mapchipconfig` (#1375) variants mirror `objtiles` exactly (its structural twin): the source body is the
LZ77-DECOMPRESSED chipset TSA/config payload; `--verify-asset` re-decompresses the live ROM block and byte-compares
(READ-ONLY). `--addr` is the **DEREFERENCED** config LZ77 stream address (NOT `RomInfo.map_config_pointer`; FE7
split layouts use a separate per-plist `--addr`). No `--width`/`--height`/`--count` — the decompressed length comes
from the sidecar (`febuilder-mapchipconfig-lz77`). NOT the anime-1/anime-2 entry tables, NOT the map-change record chain:

| Command | Reads ROM? | Description |
|---|---|---|
| `--import-asset --kind=mapchipconfig --in=<x.mapchipconfig> --out=<x.bin>` | No | Identity copy of the validated decompressed body to a raw blob (NO LZ77 compression). Requires the `.mapchipconfig.json` sidecar. |
| `--roundtrip-asset --kind=mapchipconfig --in=<x.mapchipconfig>` | No | Structure-exact identity proof (`body.Length == length`, read from the sidecar). Exit 0 lossless, 2 mismatch. |
| `--verify-asset --kind=mapchipconfig --in=<x.mapchipconfig> --addr=<hex> (--rom\|--project)` | **Yes (read-only)** | LZ77-decompresses the ROM block at `--addr` and byte-compares vs the file body. Exit 0 byte-identical, 2 mismatch/fault, 1 usage error. |

```
FEBuilderGBA.CLI --import-asset --kind=mapchipconfig --in=map/chapter1.mapchipconfig --out=map/chapter1.mapchipconfig_raw.bin
FEBuilderGBA.CLI --roundtrip-asset --kind=mapchipconfig --in=map/chapter1.mapchipconfig
FEBuilderGBA.CLI --verify-asset --kind=mapchipconfig --rom=rom.gba --addr=0x500000 --in=map/chapter1.mapchipconfig
```

The `mapanime1gfx` (#1389) variants are the RAW-block TWIN of `mapchange`/`mapanime2pal` (NOT the LZ77
`objtiles`/`mapchipconfig` decompress pattern): the source body is the RAW UNCOMPRESSED 4bpp graphics block;
`--verify-asset` reads the live ROM block and byte-compares **without** decompression (READ-ONLY). `--addr` is
the **DEREFERENCED** anime-1 entry `+4` graphics pointer and `--length` is the entry `+2` byte length (the RAW
block is not self-delimiting, so the length is REQUIRED — it does NOT come from a sidecar-only path on verify):

| Command | Reads ROM? | Description |
|---|---|---|
| `--import-asset --kind=mapanime1gfx --in=<x.mapanime1gfx> --out=<x.bin>` | No | Identity copy of the validated RAW body to a blob (NO header, NO `>>3` shift, NO LZ77). Requires the `.mapanime1gfx.json` sidecar. |
| `--roundtrip-asset --kind=mapanime1gfx --in=<x.mapanime1gfx>` | No | Structure-exact identity proof (`body.Length == length`, read from the sidecar). Exit 0 lossless, 2 mismatch. |
| `--verify-asset --kind=mapanime1gfx --in=<x.mapanime1gfx> --addr=<hex> --length=<int> (--rom\|--project)` | **Yes (read-only)** | Byte-exact RAW ROM-backed mismatch proof (no decompression). Exit 0 byte-identical, 2 mismatch/fault, 1 usage error. |

```
FEBuilderGBA.CLI --import-asset --kind=mapanime1gfx --in=map/chapter1.mapanime1gfx --out=map/chapter1.mapanime1gfx_raw.bin
FEBuilderGBA.CLI --roundtrip-asset --kind=mapanime1gfx --in=map/chapter1.mapanime1gfx
FEBuilderGBA.CLI --verify-asset --kind=mapanime1gfx --rom=rom.gba --addr=0x600000 --length=512 --in=map/chapter1.mapanime1gfx
```

The `portrait-package` (#1374) variants are a **multi-file DIRECTORY** write-back / round-trip (NOT a file `--in`):
a portrait package is a 128×112 composite sheet PNG + an optional name-matched JASC `.pal` sidecar (the same
package the `--validate-asset --kind=portrait-package` validator checks). Both paths are ROM-free and never mutate
the preview ROM. The import refuses unless the destination is an **unambiguous owner**; the round-trip requires an
explicit `--expect` baseline (the oracle — no self-compare). Residual: there is **no ROM byte-pin** (no canonical
ROM→128×112-sheet builder exists, so the preview ROM is never the source of truth for a portrait package):

| Command | Reads ROM? | Description |
|---|---|---|
| `--import-asset --kind=portrait-package --path=<srcDir> --out=<destDir> [--allow-main-only] [--overwrite] [--project=<dir>]` | No | Validate the source package then identity-copy the sheet PNG + matched sidecar into an unambiguous owner dir. A clean/empty dest writes; an existing single-package owner needs `--overwrite` (else `OWNER_EXISTS`); a multi-PNG/different-layout dest is refused (`AMBIGUOUS_OWNER`). `--out` is project-root-confined when `--project` is given. Exit 0 ok, 2 validation/owner/path fault, 1 usage. |
| `--roundtrip-asset --kind=portrait-package --path=<srcDir> --expect=<baselineDir> [--allow-main-only]` | No | Validate BOTH dirs then prove the source sheet + sidecar are byte-identical to the REQUIRED baseline (the oracle). Exit 0 byte-identical, 2 mismatch/validation fault, 1 usage. |

```
FEBuilderGBA.CLI --import-asset --kind=portrait-package --path portraits/src/eirika/ --out portraits/eirika/ --project=decomp/
FEBuilderGBA.CLI --roundtrip-asset --kind=portrait-package --path portraits/src/eirika/ --expect portraits/eirika/
```

---

### `--build-project`

Run the decomp project's declared build command (requires `--project`; the manifest
`febuilder.project.json` must declare a `build` section). **Without `--yes` it is a dry-run** (prints
the command, exits 0); pass `--yes` to actually execute. It never runs the build implicitly. Captures
the build's stdout/stderr.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Yes | Decomp project directory containing `febuilder.project.json` with a `build` section. |
| `--yes` | No | Execute the build command for real. **Without `--yes`, the command is a DRY-RUN** — it prints the resolved build command and exits without building. |
| `--reload` | No | After a successful build, reload the built ROM into CoreState and print version info. |
| `--timeout=<ms>` | No | Build timeout in milliseconds. Default: **600000** (10 minutes). |

```
FEBuilderGBA.CLI --build-project --project=decomp/ --reload --yes
```

**Exit code:** 0 on a successful build or a dry-run (no `--yes`); 1 on a usage fault, a failed build,
or a failed `--reload`; 2 when the project has not opted into FEBuilder-managed builds (no/disabled
`build` section in `febuilder.project.json`).

---

### `--decomp-audit`

Print the maintained decomp **round-trip coverage matrix** (#1150) — which FEBuilder editor/action is source-backed, exporter-migrated, preview-only, manual, or ROM-only. READ-ONLY; never loads a ROM.

| Option | Required | Description |
|---|---|---|
| `--format=<tsv\|md>` | No | Output format for the **table**: `tsv` (default) or `md` (GitHub markdown table). Ignored when `--summary` is set. |
| `--summary` | No | Print the per-tier coverage **summary** (counts per tier + `Total` + explicit `Unclassified = N` + the master-ahead-of-release note) instead of the table. Takes precedence over `--format` (the summary is always plaintext). |
| `--out=<path>` | No | Write the matrix/summary to a file (otherwise printed to stdout). |

```
FEBuilderGBA.CLI --decomp-audit
FEBuilderGBA.CLI --decomp-audit --format=md --out=docs/decomp-coverage.md
FEBuilderGBA.CLI --decomp-audit --summary
```

The matrix is **complete relative to the maintained audit inventory**
(`DecompRoundTripAuditCore.ExpectedDecompEditors`) — a maintained classification, **not**
exhaustive byte-level runtime round-trip proof. The full decomp feature set + release status
(currently on `master`, ahead of any tagged release) is enumerated in
[DECOMP-FEATURE-INVENTORY.md](DECOMP-FEATURE-INVENTORY.md).

**Exit code:** 0 (1 on a write fault).

---

### `--nmm-to-manifest`

Parse a No$gba memory map (`.nmm`, the `--export-data … STRUCT/NMM` sibling) into a decomp manifest `tables[]` entry JSON (#1150). A **schema aid, not a writability path**: pointer / var-length / odd-size fields survive flagged `"unsupported": true` (never dropped). No ROM.

| Option | Required | Description |
|---|---|---|
| `--in=<x.nmm>` | Required | Input `.nmm` file. |
| `--table=<name>` | No | Table name for the emitted entry (default `table`). |
| `--out=<path>` | No | Write the JSON to a file (otherwise stdout); warnings go to stderr. |

```
FEBuilderGBA.CLI --nmm-to-manifest --in=items.nmm --table=items --out=items.tables.json
```

**Exit code:** 0 on parse-ok, 1 on usage/file-not-found, 2 when the NMM header is unusable.

---

### `--manifest-to-nmm`

Emit `.nmm` text for a manifest table owner (#1150), reusing the FormatNMM grammar. Pointer/var fields are flagged unsafe via stderr warnings. No ROM mutation.

| Option | Required | Description |
|---|---|---|
| `--project=<dir>` | Required | Decomp project directory whose manifest declares the table owner. |
| `--table=<name>` | Required | Table name to export to `.nmm`. |
| `--out=<path>` | No | Write the `.nmm` to a file (otherwise stdout). |

```
FEBuilderGBA.CLI --manifest-to-nmm --project=decomp/ --table=items --out=items.nmm
```

**Exit code:** 0 on success, 1 on usage/load fault, 2 when the table has no owner in the manifest.

---

### `--validate-asset`

Structurally validate a decomp IMPORT asset on disk (#1150) **before** wiring it into a build. READ-ONLY; **NEVER loads a ROM**. Indexed PNG → color type 3 / tile alignment / palette size / in-range indices; JASC `.pal` → header/count/color triples; `.mar` → length == w*h*2 and the `<<3` low-3-bits-zero invariant (validated against the `.mar.json` sidecar); `.change` map-change overlay (`--kind=mapchange`, #1355) → REQUIRED `.change.json` sidecar declaring `format "febuilder-mapchange-u16"` + dims 1..255, even length, `length == width*height*2` (NO `<<3` invariant — overlay indices are raw u16); `.mapanime2pal` map tile-animation-2 palette (`--kind=mapanime2pal`, #1360) → REQUIRED `.mapanime2pal.json` sidecar declaring `format "febuilder-mapanime2-pal-u16"` + count 1..255, even length, `length == count*2` (raw u16 colors; `count == 0` is an intentional refusal — a meaningful source asset must have ≥1 color); `.objtiles` OBJ tileset decompressed payload (`--kind=objtiles`, #1371) → REQUIRED `.objtiles.json` sidecar declaring `format "febuilder-objtiles-lz77"` + a positive `length`, and `body.Length == length` (the decompressed 4bpp payload; NO ROM read, NO LZ77); `.mapchipconfig` chipset TSA/config decompressed payload (`--kind=mapchipconfig`, #1375) → REQUIRED `.mapchipconfig.json` sidecar declaring `format "febuilder-mapchipconfig-lz77"` + a positive `length`, and `body.Length == length` (the decompressed chipset config payload, structural twin of `objtiles`; NO ROM read, NO LZ77).

The `portrait-package` kind (#1350) is a **multi-file PACKAGE validator** over a DIRECTORY: it requires exactly one composite sheet PNG, reuses the single-PNG structural checks, then verifies the canonical 128×112 slot geometry (mini/eye/mouth slots fit; a 96×80 main-mug-only sheet is `INCOMPLETE_PACKAGE` unless `--allow-main-only`), the 4bpp (≤16-color) portrait palette cap, and **palette consistency** between the sheet's embedded PLTE and an optional JASC `.pal` sidecar (count + per-entry RGB). It still never loads the ROM.

| Option | Required | Description |
|---|---|---|
| `--kind=<kind>` | Required | Asset kind: `graphics`, `palette`, `portrait`, `icon`, `map`, `mapchange`, `mapanime2pal`, `objtiles`, `mapchipconfig`, `portrait-package`. |
| `--in=<srcAsset>` | Required (single-file kinds) | Input asset file (PNG / `.pal` / `.mar` / `.change` / `.mapanime2pal` / `.objtiles` / `.mapchipconfig`). |
| `--path=<dir>` | Required for `--kind=portrait-package` | Package directory (one 128×112 sheet PNG + optional JASC `.pal`). |
| `--allow-main-only` | Optional (`portrait-package`) | Accept a 96×80 main-mug-only sheet (warn instead of error). |
| `--project=<dir>` | Optional (`portrait-package`) | Confine `--path` to the decomp project root (rejects absolute / escaping paths; **never loads the preview ROM**). |

```
FEBuilderGBA.CLI --validate-asset --kind=graphics --in=gfx/tiles.png
FEBuilderGBA.CLI --validate-asset --kind=palette --in=gfx/palette.pal
FEBuilderGBA.CLI --validate-asset --kind=portrait-package --path portraits/eirika/
```

Each finding prints as `ERROR [CODE] msg` (stderr) or `WARN [CODE] msg` (stdout) plus a summary line.

**Exit code:** 0 on no errors (warnings allowed), 2 on errors (or a `--project` containment / detection failure for `portrait-package`), 1 on usage / bad-kind.

---

## Summary Table

| Command | `--rom` | `--fromrom` | `--in` | `--out` | Other required | ROM init |
|---|---|---|---|---|---|---|
| `--help` / `-h` | — | — | — | — | — | No |
| `--version` | — | — | — | — | — | No |
| `--playtest` | Required for run | — | — | Optional | `--scenario` for run, or `--check` | No |
| `--makeups=<path>` | Required | Required | — | — | — | No |
| `--applyups=<path>` | Required | — | — | — | `--patch` | No |
| `--lint` | Required | — | — | — | — | Full |
| `--disasm=<path>` | Required | — | — | — | — | Full |
| `--decreasecolor` | — | — | Required | Required | — | No |
| `--pointercalc` | Required | — | — | — | `--target`, `--address` | No |
| `--rebuild` | Required | Required | — | — | — | No |
| `--export-buildfile` | Required | — | — | Required | `--clean` | Full |
| `--build-buildfile` | — | — | — | Required | `--clean`, `--project` | Partial |
| `--buildfile-roundtrip` | Required | — | — | — | `--clean` | Full |
| `--songexchange` | Required | Required | — | — | `--fromsong`, `--tosong` | Partial |
| `--convertmap1picture` | — | — | Required | — | one or more of `--outImg`/`--outTSA`/`--outPal` | No |
| `--generate-random-map` | — | — | — | Required | `--femapcreator`, `--tileset`, `--width`, `--height` | No |
| `--translate` | Required | — | Optional | Optional | — | Full |
| `--translate_batch` | Required | — | Optional | Optional | — | Full |
| `--lastrom` | — | — | — | — | — | Full |
| `--force-detail` | — | — | — | — | — | No |
| `--test` / `--testonly` | Optional | — | — | — | — | Conditional |
| `--rom-info` | Optional | — | — | — | `--rom` or `--project` | Full |
| `--project=<dir>` | — | — | — | — | — (standalone, runs as `--rom-info`) | Project |
| `--resolve-addr=<hex>` | — | — | — | — | `--project` | Project |
| `--migrate-diff` | — | — | — | Optional | `--project`, `--rom2` | Project |
| `--write-source` | — | — | — | — | `--project`, `--table`, `--id`, `--field`, `--value` | Project |
| `--write-shop` | — | — | — | — | `--project`, `--items`, one of `--symbol`/`--shop-addr` | Project |
| `--export-asset` | Optional | — | — | Required | `--kind` (+ `--rom` or `--project`) | Project |
| `--validate-asset` | — | — | Required (single-file kinds) | — | `--kind` (+ `--path` for `portrait-package`; optional `--project`) | No (project root only for `portrait-package` containment) |
| `--build-project` | — | — | — | — | `--project` (`--yes` to execute) | Project |
| `--decomp-audit` | — | — | — | Optional | — | No |
| `--nmm-to-manifest` | — | — | Required | Optional | — | No |
| `--manifest-to-nmm` | — | — | — | Optional | `--project`, `--table` | Project |

**ROM init levels:**
- **No** — command operates on raw files, no ROM object needed.
- **Partial** — loads ROM for metadata (song table pointer) but not full init.
- **Full** — calls `RomLoader.InitEnvironment()` + `RomLoader.LoadRom()` + `RomLoader.InitFull()` (Huffman, text, event scripts, caches).
- **Project** — opens a decomp project via `--project` and loads its *built* ROM (full init); commands that also accept `--rom` fall back to a plain ROM load with that flag.
- **Conditional** — depends on whether `--rom` is provided.

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success (or no errors for `--lint`). |
| `1` | Error: missing arguments, file not found, operation failed, or lint found ERROR-severity issues. |
| `2` | Advisory / validation / behavioral-verification outcome — no harness failure, but the requested condition was not satisfied. Examples: a `--playtest` ROM-guard/assertion/crash/softlock failure, a `--translate-roundtrip` / `--data-roundtrip` / `--roundtrip-asset` mismatch, `--buildfile-roundtrip` byte or declared-target drift, a `--checksum` INVALID header, `--export-portrait-all` rendering some portraits, a `--write-shop` not-owned / ROM-only / refused list, a `--verify-asset` byte mismatch, a `--merge3` merged **with conflicts** (output still written), or a `--build-project` with no enabled build section. |
| `3` | Internal read-only-invariant violation — a READ-ONLY exporter (`--export-voicegroup`, `--export-battle-anim-decomp`) detected that the in-memory ROM was mutated and aborted without writing. |

---

## Argument Parsing

Arguments are parsed into a `Dictionary<string, string>`:

- `--key=value` → key = `"--key"`, value = `"value"`
- `--flag` (no `=`) → key = `"--flag"`, value = `""` (empty string)
- `-h` → mapped to `--help`
- Positional arguments (no `--` prefix) are ignored.
- Duplicate keys: last value wins.

`--playtest` adds a stricter command-local pass over the original ordered argv:
unknown options, positionals, duplicates, empty values, missing values, and
values attached to flag-only options are rejected before Python is launched.

---

## Examples

```bash
# Show help
FEBuilderGBA.CLI --help

# Show version
FEBuilderGBA.CLI --version

# Create a UPS patch
FEBuilderGBA.CLI --makeups=patch.ups --rom=modified.gba --fromrom=original.gba

# Apply a UPS patch
FEBuilderGBA.CLI --applyups=output.gba --rom=original.gba --patch=patch.ups

# Lint a ROM
FEBuilderGBA.CLI --lint --rom=rom.gba
FEBuilderGBA.CLI --lint --rom=rom.gba --force-version=FE8U

# Disassemble
FEBuilderGBA.CLI --disasm=output.asm --rom=rom.gba

# Reduce image colors
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16
FEBuilderGBA.CLI --decreasecolor --in=input.png --out=output.png --paletteno=16 --noScale --noReserve1stColor

# Pointer search
FEBuilderGBA.CLI --pointercalc --rom=source.gba --target=target.gba --address=0x100,0x200

# Rebuild ROM
FEBuilderGBA.CLI --rebuild --rom=modified.gba --fromrom=original.gba

# Song exchange
FEBuilderGBA.CLI --songexchange --rom=dest.gba --fromrom=source.gba --fromsong=0x1A --tosong=0x1A

# Convert image to map tiles
FEBuilderGBA.CLI --convertmap1picture --in=map.png --outImg=tiles.bin --outTSA=tsa.bin --outPal=palette.bin

# Generate a random map CSV through FEMapCreator
FEBuilderGBA.CLI --generate-random-map --femapcreator=C:\tools\FEMapCreator.exe --tileset=Grassland --width=15 --height=10 --out=map.csv

# Export text
FEBuilderGBA.CLI --translate --rom=rom.gba --out=texts.tsv

# Import text
FEBuilderGBA.CLI --translate --rom=rom.gba --in=texts.tsv

# Batch translate
FEBuilderGBA.CLI --translate_batch --rom=rom.gba --out=texts.tsv --in=translated.tsv

# Load last ROM
FEBuilderGBA.CLI --lastrom

# Self-test
FEBuilderGBA.CLI --test --rom=rom.gba
FEBuilderGBA.CLI --testonly --rom=rom.gba

# Decomp project mode
FEBuilderGBA.CLI --rom-info --project=decomp/
FEBuilderGBA.CLI --resolve-addr=0x08012345 --project=decomp/
FEBuilderGBA.CLI --migrate-diff --project=decomp/ --rom2=edited.gba --out=migrate.tsv
FEBuilderGBA.CLI --write-source --project=decomp/ --table=items --id=1 --field=might --value=0x0A
FEBuilderGBA.CLI --export-asset --kind=graphics --project=decomp/ --addr=0x123000 --width=64 --height=64 --palette-addr=0x124000 --out=gfx/tiles.png
FEBuilderGBA.CLI --build-project --project=decomp/ --reload --yes
```
