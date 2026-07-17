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
one line rewritten to the exact upstream ``typedef ... va_list;`` text, and
delete the temp copy in a ``finally`` block. On the explicitly selected
MSYS2/MinGW path only, the temporary copy also disables ``__attribute__(...)``
and predefines MinGW's ``__INTRIN_H_`` guard while ``<limits.h>`` and its
system-header chain are expanded. This excludes irrelevant compiler intrinsic
declarations (such as ``__debugbreak``) from the cdef stream; both macros are
restored before any mGBA header is included. The resulting stream normalizes
only typedef aliases whose underlying compiler-only type is
``__builtin_va_list`` into CFFI's ``typedef ... alias;`` syntax, removes
top-level MinGW ``extern/static __inline__`` intrinsic definitions with bounded
brace-aware scanning, and keeps declaration-only forms after dropping just the
extension token. It also token-normalizes the safe parser-only GCC qualifier
class (``__extension__``, restrict, volatile, const, and signed spellings)
outside quoted literals. Balanced ``__attribute__((...))`` expressions are
removed only when every contained attribute is from an ABI-neutral allowlist;
layout-affecting or unknown attributes fail closed. The complete successful
preprocessor stream is capped at 64 MiB and at 16,384 inline blocks,
accommodating the generated MinGW header set while retaining deterministic
resource bounds. POSIX preprocessing is otherwise unchanged.
Any drift from that exact expectation (the line missing, duplicated, already
replaced, an unreadable/oversized/non-UTF-8 file, a missing
``FEBUILDERGBA_MGBA_BUILDER_H``, a missing
``FEBUILDERGBA_MGBA_SOURCE_ROOT``, an invalid MinGW selector, a same-named but
differently-located ``_builder.h``, a drifted ``<limits.h>`` include, too many
compiler-only aliases, or a temp copy that would land inside the pinned source
tree) is a hard, nonzero-exit failure with a short, static, data-free
diagnostic on stderr -- never a silent guess. A failure to delete the temp copy
after a successful preprocessor run is ALSO a hard, nonzero-exit failure
(never silently swallowed) -- see ``main()``.

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

# The bootstrap sets this to ``1`` only for the MSYS2/MinGW build. It enables
# narrowly scoped parser sanitization for compiler-only declarations emitted
# by current MinGW system headers. POSIX builds set ``0`` and retain their
# otherwise-working preprocessor stream.
ENV_MINGW_CDEF = "FEBUILDERGBA_MGBA_MINGW_CDEF"

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
LIMITS_INCLUDE_LINE = "#include <limits.h>"
ATTRIBUTE_DISABLE_LINE = "#define __attribute__(X)"
ATTRIBUTE_RESTORE_LINE = "#undef __attribute__"
INTRIN_GUARD_DISABLE_LINE = "#define __INTRIN_H_"
INTRIN_GUARD_RESTORE_LINE = "#undef __INTRIN_H_"

# A pinned header file is a few hundred bytes; this bound is generous while
# still refusing to buffer an unbounded/adversarial input in memory.
MAX_BUILDER_H_BYTES = 1_048_576
MAX_PREPROCESSED_BYTES = 64 * 1024 * 1024
MAX_BUILTIN_VA_LIST_TYPEDEFS = 16
MAX_MINGW_INLINE_BLOCKS = 16_384
MAX_MINGW_INLINE_BLOCK_LINES = 4096
MAX_MINGW_TOKEN_REPLACEMENTS = 65_536
MAX_MINGW_ATTRIBUTE_REPLACEMENTS = 65_536

MINGW_TOKEN_REPLACEMENTS = {
    b"__extension__": b"",
    b"__restrict": b"",
    b"__restrict__": b"",
    b"__volatile__": b"volatile",
    b"__const__": b"const",
    b"__signed__": b"signed",
}

SAFE_MINGW_ATTRIBUTES = frozenset(
    {
        "__always_inline__",
        "__alloc_size__",
        "__artificial__",
        "__cdecl__",
        "__cold__",
        "__const__",
        "__deprecated__",
        "__dllimport__",
        "__dllexport__",
        "__format__",
        "__format_arg__",
        "__gnu_inline__",
        "__hot__",
        "__leaf__",
        "__malloc__",
        "__noinline__",
        "__nonnull__",
        "__nothrow__",
        "__printf__",
        "__pure__",
        "__returns_nonnull__",
        "__scanf__",
        "__stdcall__",
        "__unused__",
        "__used__",
        "__visibility__",
        "__warn_unused_result__",
        "visibility",
    }
)


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


def _mingw_cdef_enabled() -> bool:
    value = os.environ.get(ENV_MINGW_CDEF, "0")
    if value not in ("0", "1"):
        raise PreprocessorError(f"{ENV_MINGW_CDEF} must be 0 or 1")
    return value == "1"


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


def _rewrite_builder_h_text(text: str, *, sanitize_mingw: bool = False) -> str:
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

    if sanitize_mingw:
        limits_matches = [
            i for i, line in enumerate(lines)
            if _stripped(line) == LIMITS_INCLUDE_LINE
        ]
        if len(limits_matches) != 1:
            raise PreprocessorError(
                "expected exactly one pinned limits.h include for MinGW "
                f"sanitization, found {len(limits_matches)}"
            )
        for forbidden in (
            ATTRIBUTE_DISABLE_LINE,
            ATTRIBUTE_RESTORE_LINE,
            INTRIN_GUARD_DISABLE_LINE,
            INTRIN_GUARD_RESTORE_LINE,
        ):
            if any(_stripped(line) == forbidden for line in lines):
                raise PreprocessorError(
                    "MinGW system-header sanitizer line is already present"
                )
        limits_index = limits_matches[0]
        limits_original = lines[limits_index]
        limits_ending = limits_original[len(_stripped(limits_original)):]
        lines[limits_index:limits_index + 1] = [
            ATTRIBUTE_DISABLE_LINE + limits_ending,
            INTRIN_GUARD_DISABLE_LINE + limits_ending,
            limits_original,
            INTRIN_GUARD_RESTORE_LINE + limits_ending,
            ATTRIBUTE_RESTORE_LINE + limits_ending,
        ]
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


def _brace_delta(line: bytes) -> int:
    """Count braces outside quoted C string/character literals."""
    delta = 0
    quote: Optional[int] = None
    escaped = False
    for value in line:
        if quote is not None:
            if escaped:
                escaped = False
            elif value == 0x5C:  # backslash
                escaped = True
            elif value == quote:
                quote = None
            continue
        if value in (0x22, 0x27):  # double/single quote
            quote = value
        elif value == 0x7B:  # {
            delta += 1
        elif value == 0x7D:  # }
            delta -= 1
    return delta


def _mingw_inline_token(line: bytes) -> Optional[bytes]:
    stripped = line.lstrip()
    for storage in (b"extern ", b"static "):
        if not stripped.startswith(storage):
            continue
        remainder = stripped[len(storage):]
        for token in (b"__inline__", b"__inline"):
            if remainder.startswith(token + b" "):
                return token
    return None


def _replace_mingw_tokens(lines: List[bytes]) -> List[bytes]:
    """Normalize parser-only GCC qualifiers outside quoted literals."""
    output: List[bytes] = []
    replacements = 0
    for line in lines:
        rewritten = bytearray()
        index = 0
        quote: Optional[int] = None
        escaped = False
        while index < len(line):
            value = line[index]
            if quote is not None:
                rewritten.append(value)
                if escaped:
                    escaped = False
                elif value == 0x5C:
                    escaped = True
                elif value == quote:
                    quote = None
                index += 1
                continue
            if value in (0x22, 0x27):
                quote = value
                rewritten.append(value)
                index += 1
                continue
            if value == 0x5F or 0x41 <= value <= 0x5A or 0x61 <= value <= 0x7A:
                end = index + 1
                while end < len(line):
                    char = line[end]
                    if not (
                        char == 0x5F
                        or 0x30 <= char <= 0x39
                        or 0x41 <= char <= 0x5A
                        or 0x61 <= char <= 0x7A
                    ):
                        break
                    end += 1
                token = line[index:end]
                replacement = MINGW_TOKEN_REPLACEMENTS.get(token)
                if replacement is not None:
                    replacements += 1
                    if replacements > MAX_MINGW_TOKEN_REPLACEMENTS:
                        raise PreprocessorError(
                            "too many MinGW compiler-token replacements"
                        )
                    rewritten.extend(replacement)
                else:
                    rewritten.extend(token)
                index = end
                continue
            rewritten.append(value)
            index += 1
        output.append(bytes(rewritten))
    return output


def _attribute_identifiers(data: bytes) -> List[str]:
    identifiers: List[str] = []
    index = 0
    quote: Optional[int] = None
    escaped = False
    while index < len(data):
        value = data[index]
        if quote is not None:
            if escaped:
                escaped = False
            elif value == 0x5C:
                escaped = True
            elif value == quote:
                quote = None
            index += 1
            continue
        if value in (0x22, 0x27):
            quote = value
            index += 1
            continue
        if value == 0x5F or 0x41 <= value <= 0x5A or 0x61 <= value <= 0x7A:
            end = index + 1
            while end < len(data):
                char = data[end]
                if not (
                    char == 0x5F
                    or 0x30 <= char <= 0x39
                    or 0x41 <= char <= 0x5A
                    or 0x61 <= char <= 0x7A
                ):
                    break
                end += 1
            try:
                identifiers.append(data[index:end].decode("ascii"))
            except UnicodeDecodeError as exc:
                raise PreprocessorError("invalid MinGW attribute identifier") from exc
            index = end
            continue
        index += 1
    return identifiers


def _strip_mingw_attributes(lines: List[bytes]) -> List[bytes]:
    """Remove only whitelisted ABI-neutral ``__attribute__((...))`` groups."""
    output: List[bytes] = []
    replacements = 0
    marker = b"__attribute__"
    for line in lines:
        rewritten = bytearray()
        index = 0
        quote: Optional[int] = None
        escaped = False
        while index < len(line):
            value = line[index]
            if quote is not None:
                rewritten.append(value)
                if escaped:
                    escaped = False
                elif value == 0x5C:
                    escaped = True
                elif value == quote:
                    quote = None
                index += 1
                continue
            if value in (0x22, 0x27):
                quote = value
                rewritten.append(value)
                index += 1
                continue
            if not line.startswith(marker, index):
                rewritten.append(value)
                index += 1
                continue
            before = line[index - 1] if index else None
            after_index = index + len(marker)
            after = line[after_index] if after_index < len(line) else None
            if (
                before is not None
                and (
                    before == 0x5F
                    or 0x30 <= before <= 0x39
                    or 0x41 <= before <= 0x5A
                    or 0x61 <= before <= 0x7A
                )
            ) or (
                after is not None
                and (
                    after == 0x5F
                    or 0x30 <= after <= 0x39
                    or 0x41 <= after <= 0x5A
                    or 0x61 <= after <= 0x7A
                )
            ):
                rewritten.append(value)
                index += 1
                continue

            cursor = after_index
            while cursor < len(line) and line[cursor] in (0x20, 0x09):
                cursor += 1
            if line[cursor:cursor + 2] != b"((":
                raise PreprocessorError("malformed MinGW attribute expression")
            depth = 0
            attr_quote: Optional[int] = None
            attr_escaped = False
            closing: Optional[int] = None
            scan = cursor
            while scan < len(line):
                char = line[scan]
                if attr_quote is not None:
                    if attr_escaped:
                        attr_escaped = False
                    elif char == 0x5C:
                        attr_escaped = True
                    elif char == attr_quote:
                        attr_quote = None
                    scan += 1
                    continue
                if char in (0x22, 0x27):
                    attr_quote = char
                elif char == 0x28:
                    depth += 1
                elif char == 0x29:
                    depth -= 1
                    if depth == 0:
                        closing = scan
                        break
                scan += 1
            if closing is None or depth != 0:
                raise PreprocessorError("unterminated MinGW attribute expression")
            content = line[cursor + 2:closing - 1]
            identifiers = _attribute_identifiers(content)
            if not identifiers or any(
                identifier not in SAFE_MINGW_ATTRIBUTES
                for identifier in identifiers
            ):
                raise PreprocessorError("unsupported MinGW attribute in cdef output")
            replacements += 1
            if replacements > MAX_MINGW_ATTRIBUTE_REPLACEMENTS:
                raise PreprocessorError("too many MinGW attribute replacements")
            index = closing + 1
        output.append(bytes(rewritten))
    return output


def _strip_mingw_inline_blocks(lines: List[bytes]) -> List[bytes]:
    """Drop MinGW compiler-intrinsic inline definitions from CFFI input."""
    output: List[bytes] = []
    index = 0
    stripped_blocks = 0
    while index < len(lines):
        token = _mingw_inline_token(lines[index])
        if token is None:
            output.append(lines[index])
            index += 1
            continue

        signature: List[bytes] = []
        delimiter: Optional[int] = None
        while index < len(lines) and len(signature) < 32:
            line = lines[index]
            signature.append(line)
            body = line.rstrip(b"\r\n")
            semicolon = body.find(b";")
            brace = body.find(b"{")
            if semicolon >= 0 and (brace < 0 or semicolon < brace):
                delimiter = 0x3B  # ;
                break
            if brace >= 0:
                delimiter = 0x7B  # {
                break
            index += 1
        if delimiter is None:
            raise PreprocessorError("unterminated MinGW inline declaration")

        if delimiter == 0x3B:
            output.extend(line.replace(token, b"", 1) for line in signature)
            index += 1
            continue

        stripped_blocks += 1
        if stripped_blocks > MAX_MINGW_INLINE_BLOCKS:
            raise PreprocessorError("too many MinGW inline definition blocks")
        depth = sum(_brace_delta(line) for line in signature)
        index += 1
        consumed = len(signature)
        while depth > 0 and index < len(lines):
            depth += _brace_delta(lines[index])
            index += 1
            consumed += 1
            if consumed > MAX_MINGW_INLINE_BLOCK_LINES:
                raise PreprocessorError("MinGW inline definition exceeds line bound")
        if depth != 0:
            raise PreprocessorError("unterminated MinGW inline definition")
    return output


def _normalize_builder_output(data: bytes) -> bytes:
    """Normalize compiler-only MinGW constructs for CFFI parsing."""
    lines = data.splitlines(keepends=True)
    normalized = 0
    for index, line in enumerate(lines):
        body = line.rstrip(b"\r\n")
        ending = line[len(body):]
        stripped = body.strip()
        if not stripped.endswith(b";"):
            continue
        parts = stripped[:-1].split()
        if len(parts) != 3 or parts[:2] != [b"typedef", b"__builtin_va_list"]:
            continue
        alias = parts[2]
        try:
            alias_text = alias.decode("ascii")
        except UnicodeDecodeError as exc:
            raise PreprocessorError("invalid builtin va_list typedef alias") from exc
        if not alias_text.isidentifier():
            raise PreprocessorError("invalid builtin va_list typedef alias")
        normalized += 1
        if normalized > MAX_BUILTIN_VA_LIST_TYPEDEFS:
            raise PreprocessorError("too many builtin va_list typedef aliases")
        lines[index] = b"typedef ... " + alias + b";" + ending
    lines = _strip_mingw_attributes(lines)
    lines = _replace_mingw_tokens(lines)
    return b"".join(_strip_mingw_inline_blocks(lines))


def _run_real_preprocessor(
    argv: List[str], *, normalize_builder_output: bool = False
) -> int:
    """Invoke the real preprocessor structurally (never via a shell)."""
    command = _resolve_compiler_tokens() + ["-E"] + argv
    completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output = completed.stdout
    if completed.returncode == 0 and len(output) > MAX_PREPROCESSED_BYTES:
        raise PreprocessorError("preprocessed output exceeds the size bound")
    if completed.returncode == 0 and normalize_builder_output:
        output = _normalize_builder_output(output)
    # Preserve compiler stdout EXACTLY -- _builder.py treats it as the
    # preprocessed cdef text, except for the documented compiler-only typedef
    # aliases normalized above. Diagnostics only ever go to stderr.
    sys.stdout.buffer.write(output)
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
        sanitize_mingw = _mingw_cdef_enabled()
        text = _read_bounded_utf8(final_path)
        rewritten = _rewrite_builder_h_text(
            text, sanitize_mingw=sanitize_mingw
        )
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
        result = _run_real_preprocessor(
            rewritten_argv, normalize_builder_output=sanitize_mingw
        )
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
