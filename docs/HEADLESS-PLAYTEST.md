# Headless Playtest

`FEBuilderGBA.CLI --playtest` runs a deterministic, data-only scenario through
the pinned mGBA 0.10.5 Python binding and returns one machine-readable JSON
verdict. It is intended for CI and agent verification after static checks such
as `--lint`, `--data-roundtrip`, `--checksum`, and `--diff`.

The feature does not bundle mGBA, a BIOS, a ROM, or a Python runtime. The CLI
never downloads or installs dependencies at command runtime.

## Safety and licensing

- mGBA is fetched only by an explicit bootstrap command from the official
  `mgba-emu/mgba` repository.
- Source is pinned to version `0.10.5`, commit
  `26b7884bc25a5933960f3cdcd98bac1ae14d42e2`, and archive SHA-256
  `9475c26e9fa2f4b30c07ab6636e4b0a5b62e4baee2109ede7b2fecc52edae366`.
- mGBA is MPL-2.0. It is built locally into the ignored
  `tools/gba-playtest/.mgba-build/` directory and is not distributed in this
  repository or the FEBuilderGBA CLI archive.
- The built-in mGBA HLE BIOS is used. Do not commit or upload copyrighted ROMs,
  BIOS files, save states, SRAM, or memory dumps.
- Scenario JSON is data only. It cannot contain commands, imports, expressions,
  Python hooks, Lua, or arbitrary output paths.

## Setup

### Linux and macOS

Install a C compiler, CMake, Python, pkg-config, libffi, libepoxy, libpng, zlib,
and FFmpeg development libraries, then run:

```bash
scripts/install-mgba-playtest.sh
```

Choose a specific Python interpreter with:

```bash
scripts/install-mgba-playtest.sh --python=python3
```

The script prefers Ninja and falls back to Unix Makefiles. CI's Ubuntu package
set is documented in [`tools/gba-playtest/README.md`](../tools/gba-playtest/README.md).

### Windows

mGBA 0.10.5's deprecated Python binding is GCC/MinGW-only; MSVC is unsupported
upstream. Install MSYS2 and the UCRT64 packages listed by the bootstrap, then
run:

```powershell
scripts\install-mgba-playtest.ps1
```

If MSYS2 is not at `C:\msys64`:

```powershell
scripts\install-mgba-playtest.ps1 -Msys2Root C:\path\to\msys64
```

The wrapper delegates to the same POSIX bootstrap inside the UCRT64 login
shell. It does not install MSYS2, alter the global `PATH`, or use MSVC.
Published CLI runners derive the recorded native DLL search manifest from the
selected bootstrap interpreter under `.mgba-build/venv`, so no extra global
`PATH` configuration is required.
The bootstrap also scopes a repository-owned setuptools shim to the native
build commands: Windows rpath arguments are dropped in favor of that DLL
manifest, and the exact versioned Python import library is linked for CFFI's
non-limited API symbols. Installed packages and the pinned mGBA source remain
unchanged.

Both bootstraps finish with an exact version/commit check. Rebuild from scratch
with `--force` on POSIX or `-Force` on Windows.

## Dependency check

Use the Python executable created by the bootstrap:

```bash
FEBuilderGBA.CLI --playtest --check \
  --python=tools/gba-playtest/.mgba-build/venv/bin/python
```

Windows MSYS2 builds may place it under `venv\bin\python.exe` or
`venv\Scripts\python.exe`.

Interpreter precedence is:

1. `--python=<executable>`;
2. `FEBUILDERGBA_PLAYTEST_PYTHON`;
3. the platform candidate list (`python3`/`python`).

`--check` cannot be combined with ROM, scenario, output, artifact, or timeout
arguments.

## Running a scenario

```bash
FEBuilderGBA.CLI --playtest \
  --rom=modded.gba \
  --scenario=scenario.json \
  --out=result.json \
  --artifact-dir=artifacts \
  --python=tools/gba-playtest/.mgba-build/venv/bin/python \
  --timeout=600000
```

| Option | Required | Description |
|---|---:|---|
| `--rom=<file>` | Yes | Input GBA ROM. The runner reads at most 32 MiB and never modifies it. |
| `--scenario=<file>` | Yes | UTF-8 schema-v1 scenario, at most 1 MiB. |
| `--out=<file>` | No | Persist the same JSON verdict emitted on stdout. |
| `--artifact-dir=<dir>` | No | Existing directory used to persist a requested screenshot; without it the PNG is still captured and hashed with `written: false`. |
| `--python=<executable>` | No | Python with the pinned mGBA binding. |
| `--timeout=<ms>` | No | Outer native-process timeout, 1,000-3,600,000; default 600,000. |

The timeout is a harness failure for a stuck native process. It is not the
scenario frame budget and is not a softlock detector.
The .NET boundary also caps stdout and stderr at 1,048,576 characters each and
terminates the runner process tree if either stream exceeds that limit.

## Scenario schema v1

The authoritative parser is
`tools/gba-playtest/febuildergba_playtest/model.py`; the matching JSON Schema is
[`tools/gba-playtest/scenario.schema.json`](../tools/gba-playtest/scenario.schema.json).
Unknown properties, duplicate keys, booleans in integer fields, `NaN`,
`Infinity`, overlapping writes, invalid alignment, and out-of-domain addresses
are rejected.

```json
{
  "schemaVersion": 1,
  "name": "synthetic-input-check",
  "runFrames": 3,
  "expectedRomSha256": "0000000000000000000000000000000000000000000000000000000000000000",
  "expectedGameCode": "TEST",
  "keys": [
    { "frame": 0, "keys": ["A"] },
    { "frame": 2, "keys": [] }
  ],
  "writes": [
    {
      "frame": 0,
      "domain": "iwram",
      "address": "0x10",
      "width": 32,
      "value": "0x12345678"
    }
  ],
  "assertions": [
    {
      "domain": "wram",
      "address": 0,
      "width": 8,
      "op": "equals",
      "value": 0,
      "label": "released marker"
    }
  ],
  "watchdogs": [
    {
      "domain": "wram",
      "address": 0,
      "width": 8,
      "maxStallFrames": 120,
      "label": "event progress"
    }
  ],
  "screenshot": { "basename": "final.png" }
}
```

The SHA-256 above is intentionally a placeholder. Replace it with the exact
hash of a ROM you are authorized to test.

Addresses and unsigned values may be decimal integers or `0x`-prefixed
strings with one to eight hex digits. Screenshot basenames are portable
single components of 1-128 ASCII characters from `[A-Za-z0-9._-]`; they
cannot end in `.`, use `.`/`..`, or use Windows device stems such as `NUL`,
`CON`, `COM1`, or `LPT1`.

### Frame and input semantics

- Frames are zero-based.
- Each `keys` event sets the complete pressed-key set from that frame onward.
- Keys are `A`, `B`, `SELECT`, `START`, `RIGHT`, `LEFT`, `UP`, `DOWN`, `R`,
  and `L`.
- Opposite directions in one key state are rejected.
- `runFrames` means run exactly that many frames unless a crash or watchdog
  fails first. Reaching the frame count is normal completion.

### Memory domains and offsets

Addresses are byte offsets inside a domain, not GBA bus addresses.

| Domain | Bus base | Size | Read | Write |
|---|---:|---:|:---:|:---:|
| `wram` | `0x02000000` | `0x40000` | Yes | Yes |
| `iwram` | `0x03000000` | `0x8000` | Yes | Yes |
| `io` | `0x04000000` | `0x400` | Yes | No |
| `palette` | `0x05000000` | `0x400` | Yes | No |
| `vram` | `0x06000000` | `0x18000` | Yes | No |
| `oam` | `0x07000000` | `0x400` | Yes | No |
| `sram` | `0x0E000000` | `0x10000` | Yes | No |

Widths are 8, 16, or 32 bits and must be naturally aligned. Schema-v1 writes
are deliberately limited to WRAM/IWRAM; a scenario can use them to seed a
ROM-specific RNG location without embedding game offsets in the generic
engine. Reads from volatile IO registers can have emulator-defined side
effects; prefer stable RAM progress markers.

### Assertions and watchdogs

Assertions run after the final frame:

- `equals` / `notEquals` use `value`;
- `changed` compares against the value captured before emulation;
- `inclusiveRange` uses `min` and `max`.

A watchdog reports `softlock` only when its selected value remains unchanged
for `maxStallFrames`. This is scenario-defined progress evidence, not a
universal softlock oracle. A game may be healthy while a poorly chosen value is
stable, or stalled while an unrelated value changes.

## Deterministic startup contract

Before reset, the adapter:

- disables audio and video synchronization;
- sets frameskip to zero and mutes audio;
- uses the built-in HLE BIOS, not an external BIOS;
- leaves savedata on mGBA's anonymous in-memory backing and attaches no save file or save VFile;
- never calls save, patch, or cheat autoload APIs;
- configures a video buffer only when a screenshot is requested.

The effective configuration is recorded in `startupConfig`. Determinism is
claimed for the same ROM, scenario, pinned mGBA build/configuration, and host
architecture. Cross-architecture PNG byte identity is not claimed.

## Result and exit codes

Stdout contains exactly one compact JSON object. It excludes timestamps,
durations, interpreter paths, temporary paths, absolute artifact paths, raw ROM
bytes, and RAM dumps.

| Exit | Meaning | Typical statuses |
|---:|---|---|
| `0` | Verification passed | `pass`, `check_ok` |
| `1` | Usage, dependency, setup, I/O, or harness failure | `scenario_error`, `dependency_error`, `harness_error`, `check_failed` |
| `2` | Behavioral verification failure | `rom_mismatch`, `assertion_failed`, `crash`, `softlock` |

Results include `resultSchemaVersion`, `status`, `exitCode`, ROM SHA-256,
requested/executed frames, assertion/watchdog evidence, pinned mGBA provenance,
and `startupConfig`. Optional fields include ROM guard evidence, screenshot
hash/evidence, scenario name, and a bounded sanitized note.

## Agent harness and MCP

The Python agent harness exposes the same operation:

```bash
cli-anything-febuildergba --json playtest \
  --rom modded.gba \
  --scenario scenario.json \
  --python tools/gba-playtest/.mgba-build/venv/bin/python
```

When `--rom` is omitted, the harness may use its global/session ROM. An explicit
ROM never inherits unrelated session state.

Playtesting is intentionally not an MCP tool in schema v1. It launches an
optional native emulator and can be long-running; MCP remains a fixed,
bounded allowlist with no generic command runner. Agents can invoke the
explicit Click or .NET CLI command instead.

## CI proof

`.github/workflows/gba-playtest.yml` is a separate, non-required workflow, so it
does not replace the required Windows `build` context. A started workflow is
fail-hard: native build, phase smoke, direct six-replay oracle, published-CLI
replay, expected exit-2 assertion failure, output equality, screenshot hashes,
or save-side-effect failures make the workflow red.

The synthetic ROM is assembled from repository-authored bytes at test time,
uses a zeroed Nintendo-logo region, and is never uploaded. Proof artifacts
contain only JSON and PNG files.
