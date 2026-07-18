# febuildergba-playtest

Deterministic, headless GBA playtest engine for FEBuilderGBA (issue #1932).

This is a **standard-library-only** Python package that drives the pinned
**mGBA 0.10.5** Python binding through a strict, data-only JSON scenario and
emits a single machine-readable JSON verdict. It is the runner used by the
canonical `FEBuilderGBA.CLI --playtest` command and the agent-harness
`playtest` wrapper.

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
FEBuilderGBA.CLI --playtest --check --python /path/to/playtest-python
FEBuilderGBA.CLI --playtest --rom rom.gba --scenario scenario.json \
    --python /path/to/playtest-python \
    [--out result.json] [--artifact-dir out/]
```

The direct `python -m febuildergba_playtest` entrypoint remains available for
bootstrap diagnostics. The user/agent contract is the .NET CLI surface above;
see [`docs/HEADLESS-PLAYTEST.md`](../../docs/HEADLESS-PLAYTEST.md).

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
toolchain (Python, GCC, CMake, MSYS /usr/bin/make, Git, curl, tar) is already installed,
and runs the same POSIX bootstrap under the UCRT64 **login** shell.
A login shell (`bash -l`) is required because a non-login MSYS2 Bash never
sources `/etc/profile.d`, so `/ucrt64/bin` would be missing from `PATH` and
every probed tool (and the delegated build) could silently resolve to the wrong
toolchain.

Windows configures mGBA with the CMake **MSYS Makefiles** generator and requires
the MSYS `/usr/bin/make` command; it does not use Ninja. Non-MSYS hosts prefer
Ninja and fall back to Unix Makefiles.

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

On MinGW/UCRT64, both `libmgba.dll` and the CFFI extension are linked with
high-entropy VA disabled while ordinary dynamic-base ASLR remains enabled.
This keeps MinGW's required 32-bit runtime pseudo-relocations within range
without disabling ASLR or modifying the pinned source. The bootstrap verifies
both PE images with `objdump` and launches ten fresh import/provenance probes
so randomized module placement is exercised before the binding is accepted.

On Ubuntu the same native prerequisites are installed as system packages:
`build-essential cmake ninja-build pkg-config libepoxy-dev libffi-dev
libpng-dev zlib1g-dev libavcodec-dev libavfilter-dev libavformat-dev
libavutil-dev libswscale-dev libswresample-dev gdb` (the real-mGBA CI job
installs exactly this set before invoking `install-mgba-playtest.sh`; `gdb` is
used only by the diagnostic step described below, never by the bootstrap or
the runtime).

Both real-mGBA CI jobs (Ubuntu and Windows/MSYS2 UCRT64) run the same
dependency-free, no-artifact native phase smoke once, after the pinned binding
build and before the unchanged six-replay synthetic proof. It covers
construction, load, reset, memory read, held-A input, frames, crash queries,
PNG encoding, and deterministic close without writing a ROM, scenario, result,
screenshot, or proof file. On Ubuntu, if that phase smoke step fails, a
following diagnostic-only step re-runs just its `--child` process directly
under `gdb --batch -ex run -ex "thread apply all bt full"` and prints the
native backtrace to the job log; it never runs otherwise (`if: failure() &&
steps.native_smoke.outcome == 'failure'`), is capped at five minutes, never
uses `continue-on-error`, and never writes or uploads any artifact.

Newer GCC toolchains (observed: MSYS2 UCRT64 GCC 16.1) changed how the system
header chain declares `__gnuc_va_list`, which breaks CFFI's cdef parser
(`cffi.CDefError: cannot parse "typedef __builtin_va_list __gnuc_va_list;"`)
even though the pinned mGBA 0.10.5 `_builder.h` already carries a `#define
va_list void*` workaround for the unrelated `va_list` identifier. Upstream
mGBA fixed this with a LATER commit
(`36f321f84889bc69b48541e0519401c091eeaeca`, "Python: Actually fix build")
that replaces that exact line with the real `typedef ... va_list;`
declaration (this is the literal text of that commit's own fix, verified
against its diff). This bootstrap stays pinned to the archived 0.10.5 commit
above and does **not** cherry-pick or patch that upstream source tree, its
CFLAGS, its CPPFLAGS, or any CMake-generated flag. Instead it redirects
`_builder.py`'s own `CPP` preprocessor hook, for exactly the two CMake build
invocations that can run `_builder.py` (the default build and the
`mgba-py-bdist` target), at a repository-owned, dependency-free wrapper
(`scripts/mgba_cffi_preprocessor.py`) — and the bootstrap refuses to use that
wrapper at all if it is a symlink or otherwise not a plain regular file. That
wrapper is launched by mGBA's native Python subprocess API; under MSYS2 the
venv interpreter, wrapper, expected header, and source-root paths are therefore
converted with `cygpath -w` before entering `CPP` or the wrapper environment
(POSIX hosts retain their original paths). This avoids handing native Windows
Python an unlaunchable `/c/...` executable path. The
wrapper recognizes ONLY the exact pinned `_builder.h` (by canonical path),
requires its old workaround line exactly once and the upstream replacement's
absence, rewrites *only* that one line in a temporary copy, preprocesses that
copy, and deletes it in a `finally` block. On MSYS2 only, the temporary header
also disables `__attribute__(...)` and predefines MinGW's `__INTRIN_H_` guard
while `<limits.h>` expands. This excludes irrelevant compiler intrinsic
declarations such as `__debugbreak`; both macros are restored before mGBA
headers. The wrapper removes `-P` for this internal preprocessing pass, uses
GCC line markers to retain declarations only from the exact temporary overlay
or canonical pinned mGBA source root, then drops the markers before returning
the cdef stream. Host/GCC/Python declarations are excluded only after their
macros have expanded into retained mGBA lines. It also injects the native
target's exact `_WIN32_WINNT=0x0600` define, eliminating cdef/native compile
drift. Exact begin/end declarations around the `limits.h` expansion are
required, bounded, and removed. The wrapper then normalizes only preprocessed
compiler-only scalar typedefs from the complete GCC 16.1.0 MinGW header census
(`__builtin_*_va_list` plus the exact `__bf16` to `__bfloat16` API alias) to
CFFI's `typedef ... <alias>;` syntax (bounded and identifier-validated).
If GCC also emits `typedef __builtin_va_list va_list;`, the wrapper retains the
single authoritative `typedef ... va_list;` from the exact temporary header
overlay and blanks only that equivalent duplicate. The same finite provenance
check handles GCC's actual two-step
`__builtin_va_list -> __gnuc_va_list -> va_list` chain; MS/SysV roots, cycles,
other alias conflicts, and duplicate opaque declarations fail closed.
Pinned `_builder.h` also owns the exact CFFI partial overrides
`typedef int... time_t;` and `typedef int... off_t;`; later simple host typedefs
for those two aliases are blanked only when their source is from the finite
UCRT set (`__time32_t`/`__time64_t` and `_off_t`/`off32_t`/`off64_t`), with
line endings preserved. Incompatible sources fail closed, while the same host
typedefs remain untouched when the authoritative override is absent.
Unrecognized BF16 aliases fail closed. It first
token-normalizes safe parser-only GCC qualifiers (`__extension__`, restrict,
volatile, const, and signed spellings), then removes bounded top-level MinGW
`extern/static __inline__` intrinsic definitions with brace-aware scanning.
The inline token may end its preprocessed line (as emitted by
`__INTRINSICS_USEINLINE`) or be followed by the return type on that line.
That ordering discards body-local vector typedefs/attributes before attribute
parsing; declaration-only forms remain and lose only the inline token.
Balanced `__attribute__((...))` expressions are then removed only
when every contained attribute is on an ABI-neutral allowlist derived from the
current MinGW diagnostic/optimizer/linkage attributes. Both bare (`dllimport`)
and decorated (`__dllimport__`) spellings are generated from the same base-name
set; unknown or layout-affecting attributes (`aligned`, `packed`, `mode`, etc.)
fail closed. The only alignment exception is MinGW `max_align_t`'s long-long
and long-double fields aligned to their own natural `__alignof__` values, where
removing the redundant annotation preserves the UCRT64 x64 layout. Exact
aligned WinNT/WDK processor-state structs and unions unused by pinned mGBA
(such as `_M128A`, `_ARM64_NT_CONTEXT`, and `_SLIST_HEADER`) are converted to
CFFI partial declarations (`...;`), so the real compiler provides their
aligned layout. Bounded headers split across up to eight preprocessor-selected
tag lines are supported; alignment remains rejected outside that finite
context. A second finite tag set partializes unused non-aligned WinNT unions
whose array sizes contain `sizeof` expressions that CFFI cannot evaluate
(`_DISPATCHER_CONTEXT_NONVOLREG_ARM64`, `_SE_SID`, `_SE_TOKEN_USER`, and
`_IMAGE_AUX_SYMBOL_EX`). WinNT's single compile-time `__C_ASSERT__` pseudo-
function declaration is removed separately with its own bound. Compiler-defined
MinGW/GCC vector aliases are also
unused by pinned mGBA; any one-line compiler-internal typedef (`__...` alias)
carrying `vector_size` or `may_alias` plus optional alignment attributes becomes
an opaque CFFI typedef. This preserves the compiler's real vector layout without
maintaining an open-ended alias-name list, while application typedefs and
non-vector alignment attributes still fail closed. Repeated vector aliases are
blanked only when their complete declaration token stream is identical (as in
GCC's repeated AVX-512 `__v8di` definition); a different type/size declaration
for the same alias fails closed.
Bounded multiline compiler-internal attribute typedefs are joined into one
logical declaration before the same validation, supporting aliases before or
after the attribute group; ordinary multiline typedefs remain byte-identical.
After all token/attribute/layout transforms, byte-equivalent one-line function
declarations are deduplicated with a lightweight C-token comparison that
ignores whitespace and ordinary parameter identifiers while retaining type
keywords, tag names, punctuation, and identifier boundaries. A bounded map of
unambiguous, top-level, one-line typedef aliases is recursively resolved for
signature comparison, so ABI-equivalent WinNT spellings (`DWORD`/`ULONG`,
`BYTE`/`UCHAR`) compare equal while different underlying types remain distinct.
This
covers MinGW headers that expose the same C23 and legacy old-name prototype
(for example `memccpy`, named-vs-unnamed `chmod` parameters, or
`VerSetConditionMask` with equivalent WinNT typedef spellings) under overlapping
feature guards; distinct signatures, struct-scoped fields, quoted declarations,
complex declarators, and multiline declarations remain untouched. Both typedef
collection and declaration removal counts are bounded.
An adjacent bounded pass applies the same top-level/token rules to repeated
one-line typedef declarations, covering duplicate function typedefs and their
pointer aliases (for example `BAD_MEMORY_CALLBACK_ROUTINE`) while retaining
different typedef signatures and all multiline/structured declarations.
Simple `(int)` casts on literal WinNT enum values are folded to their exact
signed 32-bit decimal value (including `0x80000000` and `0xFFFFFFFF`), because
CFFI cannot evaluate enum casts; nonliteral cast expressions remain unchanged
and fail closed, and the replacement count is bounded.
Successful preprocessor output is
capped at 64 MiB and 16,384 inline blocks (current MinGW headers contain
hundreds, not merely a handful), so the full generated header set fits without
removing resource bounds. POSIX output is otherwise unchanged. The wrapper fails
closed (nonzero exit, static diagnostic) on any drift, ambiguity, mismatch,
excessive aliases/inline blocks, or cleanup failure
(a failed deletion is never silently swallowed; if the real preprocessor also
already failed, that original nonzero exit code is preserved rather than
masked). The temporary copy's location is itself proven to be outside the
pinned source tree — via a dedicated, bootstrap-supplied source-root path —
so even a hostile or misconfigured `TMPDIR`/`TEMP` cannot land the overlay
inside the pinned source; if it would, the wrapper refuses to preprocess it
(and still deletes it). Every other preprocessor input (notably
`lib.h`) passes through completely unchanged.

Immediately after configuring CMake (`-DUSE_FFMPEG=ON -DUSE_PNG=ON
-DUSE_ZLIB=ON`) and before building, the POSIX bootstrap fails closed unless the
CMake-**generated** feature header (`<build>/include/mgba/flags.h`) contains an
uncommented `#define` for `USE_FFMPEG`, `USE_PNG`, and `USE_ZLIB`. This
deliberately reads that generated header instead of `CMakeCache.txt`: CMake's
`find_feature()` can silently shadow a requested feature back OFF (e.g. if an
FFmpeg module is missing) without an error, and a stale cache entry from a
prior run would not reflect what was actually just configured. Only the
generated header is authoritative here.

The Python binding and native renderer are pinned to the same ordinary
**32-bit `color_t` ABI** (`COLOR_16_BIT=OFF`, `COLOR_5_6_5=OFF`). These names
are generated-header variables, not ordinary libmgba target compile
definitions: setting them `ON` made CFFI allocate a 16-bit `Image` while the
native software renderer still wrote 32-bit pixels, overflowing the framebuffer
during `GBAVideoSoftwareRendererInit`. The bootstrap verifies both macros stay
undefined in generated `flags.h` before building.

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
`mgba`. A published CLI's copied runner derives that manifest from the selected
bootstrap interpreter at `.mgba-build/venv/{bin|Scripts}/python.exe`, so the
one-command setup remains sufficient after publish; override with a
semicolon-separated Windows path list in `FEBUILDERGBA_MGBA_DLL_DIRS`. The
bootstrap runs a direct import probe before `--check` so a loader failure is
diagnosed distinctly.

Pinned mGBA still passes `runtime_library_dirs` to setuptools, which modern
MinGW setuptools rejects because Windows has no rpath. During the two native
build commands only, the bootstrap sets `PYTHONPATH` to the repository-owned
`setuptools-shim/sitecustomize.py`. On Windows that shim makes MinGW's
runtime-directory option a no-op (the recorded DLL manifest above is the actual
runtime strategy) and adds the exact `python<major>.<minor>` import library in
addition to setuptools' stable-ABI `python3` library, so non-limited CFFI symbols
such as `Py_CompileStringExFlags` link correctly. Both setuptools/distutils
module identities are patched because MSYS2 can load either. The shim is
also given a canonical, non-symlinked build-temp directory under the out-of-
source CMake tree, avoiding MinGW's repeated absolute-source object path from
exceeding Windows path limits during `bdist_wheel`. The shim is command-scoped,
regular-file/symlink/containment checked, does not edit mGBA or installed
packages, and is a no-op on POSIX.

**Windows support is validated for this pinned toolchain.** The dedicated
MSYS2 UCRT64 CI job bootstraps the binding, runs native phase smoke, executes
six deterministic direct replays, and proves the published .NET CLI path plus
its expected exit-2 result. Upstream still records the Python binding as
deprecated and does not support MSVC (mgba-emu/mgba#1637), so this claim is
deliberately limited to the pinned GCC/UCRT64 build verified here.

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

The native gate is complete and runs on every relevant change. Ubuntu and
MSYS2 UCRT64 both build/import the exact pinned binding, pass native phase
smoke, execute the protected six-replay oracle, and prove the published CLI
surface (including the expected machine-readable exit `2`). Failure artifacts
remain available for diagnosis, including Ubuntu gdb-on-failure output.

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
