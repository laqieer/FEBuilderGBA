# febuildergba-playtest

Deterministic, headless GBA playtest engine for FEBuilderGBA (issue #1932, WU1).

This is a **standard-library-only** Python package that drives the pinned
**mGBA 0.10.5** Python binding through a strict, data-only JSON scenario and
emits a single machine-readable JSON verdict. It is the runner half of the
`FEBuilderGBA.CLI --playtest` feature; the .NET CLI integration lands in a later
work unit.

## Runtime policy

* The runtime **never downloads or installs anything**. The optional native
  mGBA binding is provided only by the explicit bootstrap scripts under
  `scripts/`.
* Scenarios are **data only** — no command strings, imports, expressions,
  hooks, or host paths.
* The engine writes only to WRAM/IWRAM, reads only from a non-ROM domain
  allowlist, keeps screenshots inside a caller-owned artifact directory, and
  never copies ROM/RAM bytes into its output.

## Layout

| Path | Purpose |
| --- | --- |
| `febuildergba_playtest/model.py` | Strict scenario parser/validator (`schemaVersion: 1`). |
| `febuildergba_playtest/runner.py` | Deterministic runner + backend protocol + stable JSON. |
| `febuildergba_playtest/mgba_backend.py` | Pinned mGBA 0.10.5 adapter (delayed import). |
| `febuildergba_playtest/__main__.py` | CLI entrypoint: one JSON object on stdout. |
| `scenario.schema.json` | JSON Schema documenting the accepted scenario shape. |
| `requirements-mgba-build.txt` | Hash-locked Python build inputs for the bootstrap. |
| `tests/` | Dependency-free unit tests (no mGBA, no proprietary ROM). |

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Pass / `--check` succeeded. |
| `1` | Setup, usage, dependency, or harness failure. |
| `2` | Behavioral verification failure (assertion, crash, softlock, ROM guard). |

## Usage

```
python -m febuildergba_playtest --check
python -m febuildergba_playtest --rom rom.gba --scenario scenario.json \
    [--out result.json] [--artifact-dir out/]
```

## Setup (explicit, one command)

```powershell
scripts\install-mgba-playtest.ps1        # Windows (MSYS2 UCRT64 wrapper)
```

```bash
scripts/install-mgba-playtest.sh         # POSIX
```

On Windows the mGBA 0.10.5 Python binding is a GCC/MinGW-only build (its
`_builder.py` hardcodes GCC-style preprocessing and CMake feeds `-I` flags into
CFFI; the MSVC path is unsupported upstream — mgba-emu/mgba#1637, closed
not-planned). The PowerShell wrapper therefore does **not** use MSVC: it locates
a user-installed [MSYS2](https://www.msys2.org) root (from `-Msys2Root`, the
`MSYS2_ROOT` environment variable, or `C:\msys64`), verifies the UCRT64
toolchain (Python, GCC, CMake, Ninja/Make, Git, curl, tar) is already installed,
and runs the same POSIX bootstrap under the UCRT64 login shell. It never
downloads or installs the toolchain and never mutates global PATH/environment.

Both scripts fetch only the official `mgba-emu/mgba` commit
`26b7884bc25a5933960f3cdcd98bac1ae14d42e2` as a commit archive, verify its
SHA-256 before extraction (no fallback), stamp exact inner Git provenance,
install hash-locked build dependencies, build the display-free binding into an
isolated `.mgba-build/` venv, record the native DLL search directories, run a
direct import + provenance probe, and finish by running `--check` (exact mGBA
version **and** commit).

On Windows the binding's dependent DLLs (`libmgba`, `libgcc`, `libwinpthread`)
are not resolved via `runtime_library_dirs` when a UCRT64 Python is launched
from PowerShell/.NET. The bootstrap therefore records the build output directory
and the UCRT64 `bin` as native paths in `.mgba-build/mgba-dll-dirs.txt`, and the
runtime adapter registers them with `os.add_dll_directory()` before importing
`mgba` (override with `FEBUILDERGBA_MGBA_DLL_DIRS`). The bootstrap runs a direct
import probe before `--check` so a loader failure is diagnosed distinctly.

**Windows support is not yet claimed.** This MSYS2 UCRT64 path is an *attempted*
supported build gated by a mandatory real Windows MSYS2 CI job; upstream records
the Windows Python binding as unresolved (mgba-emu/mgba#1637) and deprecated. If
the binding cannot link, the bootstrap reports the blocker rather than weakening
any check.

## Tests

```bash
python -m pytest tools/gba-playtest/tests -q
```

## License

mGBA is MPL-2.0 and is built locally from source by the bootstrap scripts; no
mGBA binaries, ROMs, BIOS files, or save states are bundled in this repository.
