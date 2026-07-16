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
and runs the same POSIX bootstrap under the UCRT64 **login** shell.
A login shell (`bash -l`) is required because a non-login MSYS2 Bash never
sources `/etc/profile.d`, so `/ucrt64/bin` would be missing from `PATH` and
every probed tool (and the delegated build) could silently resolve to the wrong
toolchain.

The wrapper **never pipes a script to Bash on stdin**. Windows PowerShell 5.1
prepends a UTF-8 BOM to stdin, which Bash reads as literal bytes at the start of
the script (that corrupted `set -e` and the toolchain probe). Instead the
toolchain probe is materialized to a single uniquely named temp file, written
with an explicit BOM-free `UTF8Encoding($false)` and CRLF/CR normalized to LF,
and executed directly as a login-shell script argument (`bash -l <probe-path>`).
The checked-in POSIX bootstrap is likewise executed directly (`bash -l
<script-path> …`) after its Windows path is translated with `cygpath -u`
(required as a tool). No `$OutputEncoding` mutation is performed or relied upon;
the probe temp file is removed by exact literal path in the same `finally` that
restores the process environment. It never downloads or installs the toolchain
and never mutates global PATH/environment.
The native prerequisites are `pkg-config`, libffi, libepoxy, libpng, zlib, and
the FFmpeg development modules (libavcodec, libavfilter, libavformat, libavutil,
libswscale, plus one of libswresample/libavresample — install the MSYS2
`mingw-w64-ucrt-x86_64-ffmpeg` package). FFmpeg's modules are **mandatory, not
optional**, for this pinned Python binding: its CFFI declaration exposes the
e-Reader API (`EReaderScanLoadImageA` and friends) unconditionally, but mGBA
only *compiles* those symbols when `USE_FFMPEG` is on, so an FFmpeg-less build
produces a wheel that fails to import with an undefined-symbol error. The
bootstrap verifies each dependency before configuring mGBA so PNG screenshot
support and the e-Reader API cannot be silently omitted.

Both scripts fetch only the official `mgba-emu/mgba` commit
`26b7884bc25a5933960f3cdcd98bac1ae14d42e2` as a commit archive, verify its
SHA-256 before extraction (no fallback), stamp exact inner Git provenance,
additionally fetch the single official lightweight release tag `0.10.5` (as an
exact `refs/tags/0.10.5` ref, never a branch/HEAD) and fail closed unless it
resolves (`tag^{commit}`) to that SAME pinned commit, install hash-locked build
dependencies, build the display-free binding into an isolated `.mgba-build/`
venv, record the native DLL search directories, run a direct import +
provenance probe, and finish by running `--check` (exact mGBA version **and**
commit). The tag check is verified release metadata layered on top of the
commit pin, not a source selector or fallback: the exact commit fetched and
reset above remains the sole source of truth, and this only confirms that the
official `0.10.5` tag genuinely points at that same commit. The bootstrap
builds a local wheel from that pinned source and installs that exact wheel
offline with dependency resolution disabled; it does not use the legacy,
modern-setuptools-incompatible install target.

On Ubuntu the same native prerequisites are installed as system packages:
`build-essential cmake ninja-build pkg-config libepoxy-dev libffi-dev
libpng-dev zlib1g-dev libavcodec-dev libavfilter-dev libavformat-dev
libavutil-dev libswscale-dev libswresample-dev` (the real-mGBA CI job installs
exactly this set before invoking `install-mgba-playtest.sh`).

Immediately after configuring CMake (`-DUSE_FFMPEG=ON -DUSE_PNG=ON
-DUSE_ZLIB=ON`) and before building, the POSIX bootstrap fails closed unless the
CMake-**generated** feature header (`<build>/include/mgba/flags.h`) contains an
uncommented `#define` for `USE_FFMPEG`, `USE_PNG`, and `USE_ZLIB`. This
deliberately reads that generated header instead of `CMakeCache.txt`: CMake's
`find_feature()` can silently shadow a requested feature back OFF (e.g. if an
FFmpeg module is missing) without an error, and a stale cache entry from a
prior run would not reflect what was actually just configured. Only the
generated header is authoritative here.

CMake 4 dropped compatibility with `cmake_minimum_required(VERSION < 3.5)`.
Pinned mGBA 0.10.5 still declares `VERSION 3.1`, so a modern MSYS2/Ubuntu
CMake 4 aborts configuration and explicitly recommends
`-DCMAKE_POLICY_VERSION_MINIMUM=3.5`. The bootstrap passes exactly that policy
floor as an **external** configure flag — the upstream pinned source is never
patched — so the exact-commit/SHA provenance is preserved while the build
remains compatible with CMake 4.

On Windows the binding's dependent DLLs (`libmgba`, `libgcc`, `libwinpthread`)
are not resolved via `runtime_library_dirs` when a UCRT64 Python is launched
from PowerShell/.NET. The bootstrap therefore records the build output directory
and the UCRT64 `bin` as native paths in `.mgba-build/mgba-dll-dirs.txt`, and the
runtime adapter registers them with `os.add_dll_directory()` before importing
`mgba` (override with a semicolon-separated Windows path list in
`FEBUILDERGBA_MGBA_DLL_DIRS`). The bootstrap runs a direct import probe before
`--check` so a loader failure is diagnosed distinctly.

**Windows support is not yet claimed.** This MSYS2 UCRT64 path is an *attempted*
supported build gated by a mandatory real Windows MSYS2 CI job; upstream records
the Windows Python binding as unresolved (mgba-emu/mgba#1637) and deprecated. If
the binding cannot link, the bootstrap reports the blocker rather than weakening
any check.

The hash-locked stage-2 build dependency `cffi` is pinned to **2.1.0**, not
2.0.0: cffi 2.0.0 was the first release with Python 3.14 support (needed
because MSYS2 UCRT64 is a rolling distribution currently shipping Python
3.14), but it carries a MinGW-specific atomic-store regression that breaks
UCRT64 builds. cffi 2.1.0 (upstream PR #198) switches that store to GCC/Clang
builtin atomics, fixing the regression while keeping Python 3.14 support, and
is installed with its exact official PyPI sdist SHA-256 hash.

The runtime adapter loads **no save VFile**: the pinned mGBA core safely uses
anonymous in-memory savedata when no save is attached. In mGBA 0.10.5,
save / patch / cheat autoload is not an `mCoreOptions` setting; it requires an
explicit frontend call to `autoload_save()`, `autoload_patch()`, or
`autoload_cheats()`. The adapter makes none of those calls, and the reported
`autoload*` values describe that adapter-level execution policy, so nothing is
ever written beside the ROM (and one native handle is kept out of teardown).
After each replay the CLI runs a **deterministic native
teardown** before persisting or returning any result: `MgbaBackend.close()` is
idempotent, removes the crash callback from the core-owned list to break the
reference cycle, then imports `mgba.core.ffi` and calls `ffi.release(core._core)`
exactly once while the ROM VFile / config / image are still alive (per the CFFI
docs, `ffi.release()` runs the `ffi.gc` destructor immediately and blocks a
second call). The claimed ROM VFile is never closed from Python — the native
core owns and closes it. If cleanup fails, the CLI reports `harness_error` and
never persists or reports a `pass`.

WU1 (this bootstrap/build/import/replay gate) remains **pending validation by
real mGBA 0.10.5 CI** (Ubuntu + MSYS2 UCRT64); nothing here is claimed as a
passing gate until that CI run confirms it. In particular, the **Windows
transport** fix and the **real replay** acceptance both remain **pending the
next native CI oracle run** — they are not yet claimed as fixed or green.

## Tests

```bash
python -m pytest tools/gba-playtest/tests -q
```

## License

This playtest package (the runner, bootstrap scripts, tests, and synthetic ROM
helper) is authored in this repository and is licensed under **GPLv3**, the same
license as the rest of FEBuilderGBA. See the repository `LICENSE`.

The mGBA 0.10.5 emulator is a **separate** dependency under **MPL-2.0**; it is
fetched and built locally from source by the bootstrap scripts only. No mGBA
binaries, ROMs, BIOS files, or save states are bundled in this repository, and
mGBA's MPL-2.0 license does not apply to the GPLv3 code authored here.
