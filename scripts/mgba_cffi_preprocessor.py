#!/usr/bin/env python3
"""Standard-library-only ``CPP`` shim for mGBA 0.10.5's Python ``_builder.py``.

This is a **build-time-only** helper. It is never imported by, or shipped
with, the ``febuildergba_playtest`` runtime; it exists solely so
``scripts/install-mgba-playtest.sh`` can point mGBA's pinned ``_builder.py``
at a real C preprocessor while working around one exact, well-scoped parse
failure in the pinned ``_builder.h``, without ever patching the pinned
upstream source.

Why this exists
----------------
mGBA 0.10.5's ``src/platform/python/_builder.py`` preprocesses
``_builder.h`` with the C preprocessor named in the ``CPP`` environment
variable (``cc -E`` by default) and feeds the result to ``cffi.FFI().cdef()``.
The pinned ``_builder.h`` contains a single workaround line,

    #define va_list void*

so CFFI's parser never has to understand the real ``va_list``/
``__builtin_va_list`` GCC builtin. On a newer GCC (observed: UCRT64 GCC
16.1) the system header chain expands to a *separate*, unrelated line

    typedef __builtin_va_list __gnuc_va_list;

which the ``va_list`` macro above does not touch (it is a different
identifier), and CFFI's parser cannot parse that line at all, so
``cffi.FFI().cdef()`` raises ``cffi.CDefError: cannot parse "typedef
__builtin_va_list __gnuc_va_list;"``. Upstream mGBA fixed this with a LATER
commit (``36f321f84889bc69b48541e0519401c091eeaeca``, "Python: Actually fix
build") that replaces that exact ``#define va_list void*`` line with the real
``typedef ... va_list;`` declaration (verified against that commit's diff).
This repository stays pinned to the archived 0.10.5 commit (see
``install-mgba-playtest.sh``'s ``MGBA_COMMIT``) and never cherry-picks or
patches that upstream source tree.

Instead, the bootstrap points ``_builder.py``'s ``CPP`` at THIS script
(``CPP="<venv-python> <this-file>"``). It receives, on ``sys.argv[1:]``,
exactly the preprocessor arguments ``_builder.py`` would otherwise have
appended to ``cc -E`` (include flags, ``-P``, ``-fno-inline``, and finally the
header path). For every input EXCEPT the exact pinned ``_builder.h`` (named
by the ``FEBUILDERGBA_MGBA_BUILDER_H`` environment variable the bootstrap
sets) -- notably ``lib.h`` -- the arguments are forwarded to the real
preprocessor completely unchanged.

Only when the final argument names the exact, canonical, bootstrap-supplied
``_builder.h`` does this script read that file (bounded, strict UTF-8),
require the exact old line above exactly once and the upstream replacement
line's absence, write a TEMPORARY copy outside the source tree with ONLY that
one line rewritten to the exact upstream ``typedef ... va_list;`` text, run
the real preprocessor against that temp copy, and delete the temp copy in a
``finally`` block. Any drift from that exact expectation (the line missing,
duplicated, already replaced, an unreadable/oversized/non-UTF-8 file, a
missing ``FEBUILDERGBA_MGBA_BUILDER_H``, a missing
``FEBUILDERGBA_MGBA_SOURCE_ROOT``, a same-named but differently-located
``_builder.h``, or a temp copy that would land inside the pinned source
tree) is a hard, nonzero-exit failure with a short, static, data-free
diagnostic on stderr -- never a silent guess. A failure to delete the temp
copy after a successful preprocessor run is ALSO a hard, nonzero-exit
failure (never silently swallowed) -- see ``main()``.

This script never mutates the pinned source tree, CFLAGS, CPPFLAGS, or any
CMake-generated build flag; it only ever substitutes the final path argument
of ONE preprocessor invocation with a throwaway temporary file, and that
temporary file is proven (via ``FEBUILDERGBA_MGBA_SOURCE_ROOT``) to be
outside the pinned source tree even if ``TMPDIR``/``TEMP`` is hostile or
misconfigured to point inside it.
"""

from __future__ import annotations

import os
import shlex
import subprocess
import sys
import tempfile
from typing import List, Optional, Sequence

# --- Fixed, documented contract -------------------------------------------

# The bootstrap sets this to the absolute path of the pinned, extracted
# mGBA source's ``src/platform/python/_builder.h``. This script trusts NO
# other source of truth for "which file is the pinned _builder.h".
ENV_EXPECTED_BUILDER_H = "FEBUILDERGBA_MGBA_BUILDER_H"

# The bootstrap sets this to the absolute path of the pinned, extracted mGBA
# source root (the directory _builder.h's own path is rooted under). This
# script trusts NO other source of truth for "where is the source tree", and
# uses it only to PROVE the temporary overlay copy lands outside that tree
# -- even if TMPDIR/TEMP is hostile or misconfigured to point inside it.
ENV_SOURCE_ROOT = "FEBUILDERGBA_MGBA_SOURCE_ROOT"

# Optional override for the real compiler binary (tokenized, never a raw
# shell string). Unset defaults to plain ``cc``; this script always appends
# ``-E`` itself, mirroring _builder.py's own ``cc -E`` default.
ENV_CC_OVERRIDE = "CC"

BUILDER_H_BASENAME = "_builder.h"

# The exact pinned line this script is allowed to rewrite, and the exact
# upstream replacement it rewrites it to (the literal text of upstream commit
# 36f321f84889bc69b48541e0519401c091eeaeca's own fix). See the module
# docstring and install-mgba-playtest.sh for the citation.
OLD_BUILDER_LINE = "#define va_list void*"
NEW_BUILDER_LINE = "typedef ... va_list;"

# A pinned header file is a few hundred bytes; this bound is generous while
# still refusing to buffer an unbounded/adversarial input in memory.
MAX_BUILDER_H_BYTES = 1_048_576


class PreprocessorError(RuntimeError):
    """A static, data-free diagnostic. Never includes file content."""


def _resolve_compiler_tokens() -> List[str]:
    """Return the real preprocessor's argv prefix (before ``-E`` and args).

    Structural (``shlex.split``), never a shell string handed to ``shell=True``.
    """
    override = os.environ.get(ENV_CC_OVERRIDE)
    if override:
        tokens = shlex.split(override)
        if not tokens:
            raise PreprocessorError(
                f"{ENV_CC_OVERRIDE} is set but tokenizes to an empty command"
            )
        return tokens
    return ["cc"]


def _final_input_path(argv: Sequence[str]) -> str:
    if not argv:
        raise PreprocessorError("no preprocessor arguments were supplied")
    return argv[-1]


def _is_named_builder_h(path: str) -> bool:
    return os.path.basename(path) == BUILDER_H_BASENAME


def _expected_builder_h() -> str:
    expected = os.environ.get(ENV_EXPECTED_BUILDER_H)
    if not expected:
        raise PreprocessorError(f"{ENV_EXPECTED_BUILDER_H} is not set")
    return expected


def _expected_source_root() -> str:
    root = os.environ.get(ENV_SOURCE_ROOT)
    if not root:
        raise PreprocessorError(f"{ENV_SOURCE_ROOT} is not set")
    return root


def _path_is_within(path: str, root: str) -> bool:
    """Return True iff canonical ``path`` is ``root`` itself or nested under it.

    Uses ``os.path.commonpath`` (never a naive string prefix check) so this
    is correct across platform path separators/drives. Both arguments must
    already be canonical (``os.path.realpath``) absolute paths.
    """
    try:
        common = os.path.commonpath([path, root])
    except ValueError:
        # Different drives on Windows, or otherwise incomparable -- not nested.
        return False
    return os.path.normcase(common) == os.path.normcase(root)


def _read_bounded_utf8(path: str) -> str:
    try:
        with open(path, "rb") as handle:
            raw = handle.read(MAX_BUILDER_H_BYTES + 1)
    except OSError as exc:
        raise PreprocessorError(f"cannot read input file: {exc.strerror}") from exc
    if len(raw) > MAX_BUILDER_H_BYTES:
        raise PreprocessorError("input file exceeds the bounded read size")
    try:
        return raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise PreprocessorError(f"input file is not valid UTF-8: {exc}") from exc


def _rewrite_builder_h_text(text: str) -> str:
    """Rewrite exactly one line, failing closed on any ambiguity or drift."""
    lines = text.splitlines(keepends=True)

    def _stripped(line: str) -> str:
        return line.rstrip("\r\n")

    old_matches = [i for i, line in enumerate(lines) if _stripped(line) == OLD_BUILDER_LINE]
    if len(old_matches) != 1:
        raise PreprocessorError(
            "expected exactly one pinned '#define va_list void*' line, found "
            f"{len(old_matches)} (source drift or already patched)"
        )
    if any(_stripped(line) == NEW_BUILDER_LINE for line in lines):
        raise PreprocessorError(
            "the upstream replacement line is already present; refusing an "
            "ambiguous rewrite"
        )
    index = old_matches[0]
    original = lines[index]
    ending = original[len(_stripped(original)):]
    lines[index] = NEW_BUILDER_LINE + ending
    return "".join(lines)


def _write_temp_copy(text: str) -> str:
    """Write ``text`` to a new temp file OUTSIDE the source tree."""
    fd, path = tempfile.mkstemp(prefix="febuildergba-mgba-builder-", suffix=".h")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as handle:
            handle.write(text)
    except BaseException:
        _remove_temp(path)
        raise
    return path


def _remove_temp(path: str) -> None:
    os.remove(path)


def _run_real_preprocessor(argv: List[str]) -> int:
    """Invoke the real preprocessor structurally (never via a shell)."""
    command = _resolve_compiler_tokens() + ["-E"] + argv
    completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    # Preserve compiler stdout EXACTLY -- _builder.py treats it as the
    # preprocessed cdef text. Diagnostics only ever go to stderr.
    sys.stdout.buffer.write(completed.stdout)
    sys.stdout.buffer.flush()
    if completed.stderr:
        sys.stderr.buffer.write(completed.stderr)
        sys.stderr.buffer.flush()
    return completed.returncode


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)

    try:
        final_path = _final_input_path(args)
    except PreprocessorError as exc:
        sys.stderr.write(f"mgba_cffi_preprocessor: {exc}\n")
        return 1

    if not _is_named_builder_h(final_path):
        # Not _builder.h at all (notably lib.h) -- unchanged pass-through.
        return _run_real_preprocessor(args)

    temp_path: Optional[str] = None
    # Fail-closed default: if anything below returns without explicitly
    # setting this to the real preprocessor's exit code, the function must
    # still fail nonzero rather than silently report success.
    result = 1
    try:
        expected = _expected_builder_h()
        final_real = os.path.realpath(final_path)
        expected_real = os.path.realpath(expected)
        if os.path.normcase(final_real) != os.path.normcase(expected_real):
            raise PreprocessorError(
                "an input named _builder.h does not match the expected "
                "canonical pinned path"
            )
        text = _read_bounded_utf8(final_path)
        rewritten = _rewrite_builder_h_text(text)
        # Only required once every other check has already passed -- this
        # keeps every earlier fail-closed diagnostic specific to its own
        # cause, and defers the source-root requirement to the one place it
        # is actually needed: proving the overlay copy we are about to write
        # lands outside the pinned source tree.
        source_root_real = os.path.realpath(_expected_source_root())
        temp_path = _write_temp_copy(rewritten)
        # Prove the overlay copy is outside the pinned source tree even if
        # TMPDIR/TEMP is hostile or misconfigured to point inside it. If it
        # somehow lands inside, refuse to preprocess it; `finally` below
        # still deletes it.
        if _path_is_within(os.path.realpath(temp_path), source_root_real):
            raise PreprocessorError(
                "refusing to preprocess a temporary overlay copy located "
                "inside the pinned mGBA source tree (hostile/misconfigured "
                "TMPDIR?)"
            )
        rewritten_argv = list(args[:-1]) + [temp_path]
        result = _run_real_preprocessor(rewritten_argv)
    except PreprocessorError as exc:
        sys.stderr.write(f"mgba_cffi_preprocessor: {exc}\n")
        result = 1
    finally:
        if temp_path is not None:
            try:
                _remove_temp(temp_path)
            except OSError:
                sys.stderr.write(
                    "mgba_cffi_preprocessor: failed to remove the temporary "
                    "overlay copy\n"
                )
                # Fail closed on a cleanup failure -- but if the real
                # preprocessor already failed (nonzero), preserve that exact
                # original exit code rather than masking it with a generic 1.
                if result == 0:
                    result = 1
    return result


if __name__ == "__main__":
    raise SystemExit(main())
