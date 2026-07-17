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
restored before any mGBA header is included. For this MinGW invocation the
wrapper removes GCC's ``-P`` flag, retains line markers internally, and keeps
declarations only from the exact temporary overlay or the canonical pinned
mGBA source root; declarations from host/GCC/Python headers are discarded after
their macros have already expanded into retained mGBA lines. The wrapper also
adds the same ``_WIN32_WINNT=0x0600`` definition used by the native CMake target,
preventing cdef/native compile-environment drift. Exact markers around the
``limits.h`` expansion are required and removed with that host-only block.
The resulting stream normalizes
the complete GCC 16.1 MinGW census of one-line compiler-only scalar aliases
(``__builtin_*_va_list`` and exact ``__bf16``/``__bfloat16``) into CFFI's
``typedef ... alias;`` syntax. It first
token-normalizes the safe parser-only GCC qualifier class
(``__extension__``, restrict, volatile, const, and signed spellings), then
removes top-level MinGW ``extern/static __inline__`` intrinsic definitions
with bounded brace-aware scanning. This ordering drops vendor attributes and
vector typedefs inside intrinsic bodies before attribute parsing; declaration-
only forms remain and lose only the inline token. Balanced
``__attribute__((...))`` expressions are then
removed only when every contained attribute is from an ABI-neutral allowlist;
the allowlist covers the diagnostic/optimizer/linkage attributes observed in
current MinGW headers and derives both bare and ``__name__`` spellings from a
single base-name set, while layout-affecting attributes such as aligned,
packed, or mode remain rejected. The sole alignment exception is MinGW
``max_align_t``'s two fields explicitly aligned to their own natural
``__alignof__`` values; removing those redundant annotations preserves the
x64 layout. Unknown attributes also fail closed. The complete successful
preprocessor stream is capped at 64 MiB and at 16,384 inline blocks,
accommodating the generated MinGW header set while retaining deterministic
resource bounds. POSIX preprocessing is otherwise unchanged.

The exact aligned WinNT/WDK processor-state structs and unions emitted by the
pinned headers (for example ``_M128A``, ``_ARM64_NT_CONTEXT``, and
``_SLIST_HEADER``) are unreferenced by pinned mGBA. Their cdef bodies are
converted to CFFI partial declarations (``...;``), so the real compiler
supplies their aligned layout instead of dropping alignment or teaching
pycparser vendor syntax. Bounded headers split across preprocessor-selected
tag lines are supported; alignment remains rejected everywhere else.

Compiler-defined MinGW/GCC vector aliases are likewise unreferenced by pinned
mGBA. Any one-line compiler-internal typedef (``__...`` alias) carrying
``vector_size`` or ``may_alias`` plus optional alignment attributes becomes an
opaque CFFI typedef, preserving the compiler's real vector size/alignment
without relying on an open-ended alias-name inventory. Ordinary application
typedefs and non-vector alignment attributes remain fail-closed.
The same rule handles bounded multiline declarations by joining only
compiler-internal attribute typedefs into one logical line before validation;
the internal alias may appear either before or after the attribute group.
Ordinary multiline typedefs remain byte-identical.
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
LIMITS_BLOCK_BEGIN_LINE = "typedef int __febuildergba_limits_block_begin;"
LIMITS_BLOCK_END_LINE = "typedef int __febuildergba_limits_block_end;"
MINGW_WINVER_DEFINE = "-D_WIN32_WINNT=0x0600"

# A pinned header file is a few hundred bytes; this bound is generous while
# still refusing to buffer an unbounded/adversarial input in memory.
MAX_BUILDER_H_BYTES = 1_048_576
MAX_PREPROCESSED_BYTES = 64 * 1024 * 1024
MAX_OPAQUE_COMPILER_SCALAR_TYPEDEFS = 64
MAX_MINGW_INLINE_BLOCKS = 16_384
MAX_MINGW_INLINE_BLOCK_LINES = 4096
MAX_MINGW_TOKEN_REPLACEMENTS = 65_536
MAX_MINGW_ATTRIBUTE_REPLACEMENTS = 65_536
MAX_MINGW_MULTILINE_TYPEDEF_LINES = 32
MAX_MINGW_MULTILINE_TYPEDEFS = 512
MAX_DUPLICATE_FUNCTION_DECLARATIONS = 4096
MAX_DUPLICATE_TYPEDEF_DECLARATIONS = 4096
MAX_MINGW_C_ASSERT_DECLARATIONS = 1024
MAX_SIMPLE_TYPEDEF_ALIASES = 16_384
MAX_MINGW_ENUM_INT_CASTS = 4096
MAX_MINGW_LIMITS_BLOCK_LINES = 100_000
MAX_PREPROCESSOR_LINE_MARKERS = 1_000_000

MINGW_TOKEN_REPLACEMENTS = {
    b"__extension__": b"",
    b"__restrict": b"",
    b"__restrict__": b"",
    b"__volatile__": b"volatile",
    b"__const__": b"const",
    b"__signed__": b"signed",
}

# Complete census of one-line compiler-only scalar typedef sources emitted by
# GCC 16.1.0's x86_64 MinGW include directory. The va_list builtins may use
# ordinary aliases; GCC's native BF16 scalar is exposed only through this exact
# Intel API alias. CFFI's ``typedef ... alias;`` delegates their real ABI to the
# compiler instead of teaching pycparser vendor scalar syntax.
OPAQUE_COMPILER_SCALAR_TYPE_ALIASES = {
    b"__builtin_va_list": None,
    b"__builtin_ms_va_list": None,
    b"__builtin_sysv_va_list": None,
    b"__bf16": frozenset({b"__bfloat16"}),
}

# Exact CFFI override declarations carried by pinned ``_builder.h`` after the
# temporary overlay rewrite. These aliases intentionally replace host ABI
# typedefs with compiler-verified partial declarations for CFFI; later simple
# system typedefs for the same alias must therefore be blanked, not redeclared.
AUTHORITATIVE_CFFI_TYPEDEF_LINES = {
    b"typedef ... va_list;": b"va_list",
    b"typedef int... time_t;": b"time_t",
    b"typedef int... off_t;": b"off_t",
}
AUTHORITATIVE_HOST_TYPEDEF_SOURCES = {
    b"time_t": frozenset({b"__time32_t", b"__time64_t"}),
    b"off_t": frozenset({b"_off_t", b"off32_t", b"off64_t"}),
}

SAFE_MINGW_ATTRIBUTE_BASE_NAMES = frozenset(
    {
        "always_inline",
        "alloc_size",
        "artificial",
        "cdecl",
        "cold",
        "const",
        "deprecated",
        "dllimport",
        "dllexport",
        "format",
        "format_arg",
        "gnu_inline",
        "gnu_printf",
        "gnu_scanf",
        "hot",
        "leaf",
        "malloc",
        "ms_printf",
        "ms_scanf",
        "noinline",
        "nonnull",
        "noreturn",
        "nothrow",
        "printf",
        "pure",
        "returns_nonnull",
        "returns_twice",
        "scanf",
        "selectany",
        "stdcall",
        "unused",
        "used",
        "visibility",
        "warning",
        "warn_unused_result",
    }
)
SAFE_MINGW_ATTRIBUTES = frozenset(
    SAFE_MINGW_ATTRIBUTE_BASE_NAMES
    | {"__" + name + "__" for name in SAFE_MINGW_ATTRIBUTE_BASE_NAMES}
)
REDUNDANT_MAX_ALIGN_FIELDS = (b"__max_align_ll", b"__max_align_ld")
REDUNDANT_MAX_ALIGN_IDENTIFIERS = frozenset(
    {"aligned", "__aligned__", "alignof", "__alignof__", "long", "double"}
)
OPAQUE_ALIGNED_SYSTEM_STRUCT_TAGS = frozenset(
    {
        "_ARM64EC_NT_CONTEXT",
        "_ARM64_NT_CONTEXT",
        "_CONTEXT",
        "_M128A",
        "_MEMORY_BASIC_INFORMATION64",
        "_MEMORY_PARTITION_DEDICATED_MEMORY_ATTRIBUTE",
        "_MEMORY_PARTITION_DEDICATED_MEMORY_INFORMATION",
        "_SLIST_ENTRY",
        "_SLIST_HEADER",
        "_XSAVE_AREA",
        "_XSAVE_AREA_HEADER",
        "_XSAVE_FORMAT",
        "MEM_EXTENDED_PARAMETER",
    }
)
OPAQUE_NONALIGNED_SYSTEM_STRUCT_TAGS = frozenset(
    {
        "_DISPATCHER_CONTEXT_NONVOLREG_ARM64",
        "_IMAGE_AUX_SYMBOL_EX",
        "_SE_SID",
        "_SE_TOKEN_USER",
    }
)
OPAQUE_SYSTEM_STRUCT_TAGS = (
    OPAQUE_ALIGNED_SYSTEM_STRUCT_TAGS
    | OPAQUE_NONALIGNED_SYSTEM_STRUCT_TAGS
)
ALIGNED_ATTRIBUTE_IDENTIFIERS = frozenset(
    {"aligned", "__aligned__", "alignof", "__alignof__"}
)
MAX_OPAQUE_ALIGNED_STRUCTS = 64
MAX_OPAQUE_ALIGNED_STRUCT_HEADER_LINES = 8
MAX_OPAQUE_ALIGNED_STRUCT_LINES = 8192
SIMD_ATTRIBUTE_IDENTIFIERS = frozenset(
    {
        "aligned",
        "__aligned__",
        "alignof",
        "__alignof__",
        "may_alias",
        "__may_alias__",
        "vector_size",
        "__vector_size__",
    }
)
VECTOR_DEFINING_ATTRIBUTE_IDENTIFIERS = frozenset(
    {"may_alias", "__may_alias__", "vector_size", "__vector_size__"}
)
MAX_OPAQUE_SIMD_TYPEDEFS = 256
ATTRIBUTE_CONTEXT_IGNORED_IDENTIFIERS = frozenset(
    {
        "__attribute__",
        "char",
        "const",
        "double",
        "enum",
        "extern",
        "float",
        "int",
        "long",
        "short",
        "signed",
        "static",
        "struct",
        "typedef",
        "union",
        "unsigned",
        "void",
        "volatile",
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
            LIMITS_BLOCK_BEGIN_LINE,
            LIMITS_BLOCK_END_LINE,
        ):
            if any(_stripped(line) == forbidden for line in lines):
                raise PreprocessorError(
                    "MinGW system-header sanitizer line is already present"
                )
        limits_index = limits_matches[0]
        limits_original = lines[limits_index]
        limits_ending = limits_original[len(_stripped(limits_original)):]
        lines[limits_index:limits_index + 1] = [
            LIMITS_BLOCK_BEGIN_LINE + limits_ending,
            ATTRIBUTE_DISABLE_LINE + limits_ending,
            INTRIN_GUARD_DISABLE_LINE + limits_ending,
            limits_original,
            INTRIN_GUARD_RESTORE_LINE + limits_ending,
            ATTRIBUTE_RESTORE_LINE + limits_ending,
            LIMITS_BLOCK_END_LINE + limits_ending,
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
    whitespace = b" \t\r\n"
    for storage in (b"extern", b"static"):
        if not stripped.startswith(storage):
            continue
        storage_end = len(storage)
        if (
            storage_end >= len(stripped)
            or stripped[storage_end:storage_end + 1] not in whitespace
        ):
            continue
        remainder = stripped[storage_end:].lstrip(b" \t")
        for token in (b"__inline__", b"__inline"):
            if not remainder.startswith(token):
                continue
            token_end = len(token)
            if (
                token_end == len(remainder)
                or remainder[token_end:token_end + 1] in whitespace
            ):
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
    opaque_vector_typedefs = 0
    opaque_vector_declarations = {}
    marker = b"__attribute__"
    for line_index, line in enumerate(lines):
        rewritten = bytearray()
        opaque_vector_alias: Optional[str] = None
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
            unsupported = sorted(
                {
                    identifier
                    for identifier in identifiers
                    if identifier not in SAFE_MINGW_ATTRIBUTES
                }
            )
            if (
                unsupported
                and set(unsupported) <= REDUNDANT_MAX_ALIGN_IDENTIFIERS
                and any(field in line for field in REDUNDANT_MAX_ALIGN_FIELDS)
            ):
                # MinGW crt/stddef.h defines max_align_t with long-long and
                # long-double fields explicitly aligned to their own natural
                # __alignof__ values. Dropping only those redundant self-
                # alignment annotations preserves the natural x64 layout;
                # every other aligned/packed/mode attribute remains rejected.
                unsupported = []
            elif (
                unsupported
                and set(unsupported) <= ALIGNED_ATTRIBUTE_IDENTIFIERS
                and _opaque_aligned_system_struct_header(
                    lines,
                    line_index,
                    OPAQUE_ALIGNED_SYSTEM_STRUCT_TAGS,
                ) is not None
            ):
                # These exact WinNT/WDK state structs are not referenced by
                # pinned mGBA. Their bodies are converted to CFFI partial
                # structs after attribute removal, so the compiler supplies
                # the real aligned layout without teaching pycparser GCC
                # alignment syntax.
                unsupported = []
            elif (
                unsupported
                and set(unsupported) <= SIMD_ATTRIBUTE_IDENTIFIERS
                and (
                    opaque_vector_alias is not None
                    or set(unsupported) & VECTOR_DEFINING_ATTRIBUTE_IDENTIFIERS
                )
            ):
                alias = _opaque_vector_typedef_alias(line, index)
                if alias is not None:
                    # Compiler-internal vector typedefs are not referenced by
                    # pinned mGBA. Replace the complete declaration with an
                    # opaque CFFI typedef after validating all attribute groups.
                    if opaque_vector_alias not in (None, alias):
                        raise PreprocessorError(
                            "conflicting opaque MinGW vector typedef aliases"
                        )
                    opaque_vector_alias = alias
                    unsupported = []
            if not identifiers or unsupported:
                detail = ",".join(unsupported[:8]) if unsupported else "<empty>"
                context = sorted(
                    {
                        identifier
                        for context_line in lines[
                            max(0, line_index - 2):line_index + 3
                        ]
                        for identifier in _attribute_identifiers(context_line)
                        if identifier not in ATTRIBUTE_CONTEXT_IGNORED_IDENTIFIERS
                    }
                )
                context_detail = ",".join(context[:12]) if context else "<empty>"
                raise PreprocessorError(
                    "unsupported MinGW attribute in cdef output: "
                    + detail
                    + "; context: "
                    + context_detail
                )
            replacements += 1
            if replacements > MAX_MINGW_ATTRIBUTE_REPLACEMENTS:
                raise PreprocessorError("too many MinGW attribute replacements")
            index = closing + 1
        if opaque_vector_alias is None:
            output.append(bytes(rewritten))
        else:
            opaque_vector_typedefs += 1
            if opaque_vector_typedefs > MAX_OPAQUE_SIMD_TYPEDEFS:
                raise PreprocessorError("too many opaque MinGW vector typedefs")
            body = line.rstrip(b"\r\n")
            ending = line[len(body):]
            declaration = _c_declaration_tokens(body.strip())
            prior = opaque_vector_declarations.get(opaque_vector_alias)
            if prior is not None:
                if prior != declaration:
                    raise PreprocessorError(
                        "conflicting opaque MinGW vector typedef"
                    )
                output.append(ending)
                continue
            opaque_vector_declarations[opaque_vector_alias] = declaration
            indent = line[: len(line) - len(line.lstrip())]
            output.append(
                indent
                + b"typedef ... "
                + opaque_vector_alias.encode("ascii")
                + b";"
                + ending
            )
    return output


def _join_mingw_multiline_attribute_typedefs(lines: List[bytes]) -> List[bytes]:
    """Join bounded compiler-internal attribute typedefs into logical lines."""
    output: List[bytes] = []
    index = 0
    joined = 0
    while index < len(lines):
        first = lines[index]
        first_identifiers = _attribute_identifiers(first)
        if (
            "typedef" not in first_identifiers
            or b";" in first
            or b"{" in first
        ):
            output.append(first)
            index += 1
            continue

        block = [first]
        cursor = index + 1
        saw_brace = False
        while cursor < len(lines) and len(block) < MAX_MINGW_MULTILINE_TYPEDEF_LINES:
            block.append(lines[cursor])
            if b"{" in lines[cursor]:
                saw_brace = True
                break
            if b";" in lines[cursor]:
                break
            cursor += 1
        if (
            saw_brace
            or b";" not in block[-1]
            or not any(b"__attribute__" in line for line in block)
        ):
            output.append(first)
            index += 1
            continue

        combined_body = b" ".join(line.strip() for line in block)
        first_attribute = combined_body.find(b"__attribute__")
        if first_attribute < 0:
            output.append(first)
            index += 1
            continue
        if _opaque_vector_typedef_alias(combined_body, first_attribute) is None:
            output.append(first)
            index += 1
            continue

        joined += 1
        if joined > MAX_MINGW_MULTILINE_TYPEDEFS:
            raise PreprocessorError("too many multiline MinGW typedefs")
        body = first.rstrip(b"\r\n")
        ending = first[len(body):] or b"\n"
        indent = first[: len(first) - len(first.lstrip())]
        output.append(indent + combined_body + ending)
        index = cursor + 1
    return output


def _opaque_vector_typedef_alias(line: bytes, attribute_index: int) -> Optional[str]:
    """Return a compiler-internal one-line vector typedef alias, else ``None``."""
    first_attribute = line.find(b"__attribute__")
    if first_attribute < 0 or first_attribute > attribute_index:
        return None
    prefix = line[:first_attribute]
    identifiers = _attribute_identifiers(prefix)
    if (
        "typedef" not in identifiers
        or b"{" in line
        or b"," in prefix
        or not line.rstrip().endswith(b";")
        or not identifiers
    ):
        return None
    candidates = []
    prefix_alias = identifiers[-1]
    if prefix_alias.startswith("__"):
        candidates.append(prefix_alias)

    last_attribute = line.rfind(b"__attribute__")
    if last_attribute >= 0:
        cursor = last_attribute + len(b"__attribute__")
        while cursor < len(line) and line[cursor] in (0x20, 0x09):
            cursor += 1
        if line[cursor:cursor + 2] != b"((":
            return None
        depth = 0
        quote: Optional[int] = None
        escaped = False
        closing: Optional[int] = None
        scan = cursor
        while scan < len(line):
            value = line[scan]
            if quote is not None:
                if escaped:
                    escaped = False
                elif value == 0x5C:
                    escaped = True
                elif value == quote:
                    quote = None
                scan += 1
                continue
            if value in (0x22, 0x27):
                quote = value
            elif value == 0x28:
                depth += 1
            elif value == 0x29:
                depth -= 1
                if depth == 0:
                    closing = scan
                    break
            scan += 1
        if closing is None:
            return None
        suffix_identifiers = _attribute_identifiers(line[closing + 1:])
        if suffix_identifiers and suffix_identifiers[-1].startswith("__"):
            candidates.append(suffix_identifiers[-1])

    unique = sorted(set(candidates))
    if len(unique) != 1:
        return None
    return unique[0]


def _brace_depth_and_close(line: bytes, depth: int) -> tuple:
    """Advance brace depth outside literals; return first outer close index."""
    quote: Optional[int] = None
    escaped = False
    for index, value in enumerate(line):
        if quote is not None:
            if escaped:
                escaped = False
            elif value == 0x5C:
                escaped = True
            elif value == quote:
                quote = None
            continue
        if value in (0x22, 0x27):
            quote = value
        elif value == 0x7B:
            depth += 1
        elif value == 0x7D:
            depth -= 1
            if depth == 0:
                return depth, index
            if depth < 0:
                raise PreprocessorError("unexpected closing brace in cdef output")
    return depth, None


def _opaque_aligned_system_struct_header(
    lines: List[bytes],
    start: int,
    allowed_tags=OPAQUE_SYSTEM_STRUCT_TAGS,
) -> Optional[tuple]:
    """Return ``(tag, opening-line)`` for an exact bounded WinNT typedef."""
    first_identifiers = set(_attribute_identifiers(lines[start]))
    if (
        "typedef" not in first_identifiers
        or not ({"struct", "union"} & first_identifiers)
    ):
        return None

    tags = set()
    end = min(len(lines), start + MAX_OPAQUE_ALIGNED_STRUCT_HEADER_LINES)
    for index in range(start, end):
        line = lines[index]
        tags.update(
            set(_attribute_identifiers(line))
            & allowed_tags
        )
        depth, closing = _brace_depth_and_close(line, 0)
        if depth > 0:
            if closing is not None or len(tags) != 1:
                return None
            return next(iter(tags)), index
        if closing is not None or b";" in line:
            return None
    return None


def _partialize_opaque_aligned_system_structs(lines: List[bytes]) -> List[bytes]:
    """Replace exact unreferenced aligned WinNT struct bodies with ``...;``."""
    output: List[bytes] = []
    index = 0
    converted = 0
    while index < len(lines):
        header = _opaque_aligned_system_struct_header(lines, index)
        if header is None:
            output.append(lines[index])
            index += 1
            continue
        _, opening_index = header
        converted += 1
        if converted > MAX_OPAQUE_ALIGNED_STRUCTS:
            raise PreprocessorError("too many opaque aligned system structs")

        while index <= opening_index:
            output.append(lines[index])
            index += 1

        opening_line = lines[opening_index]
        depth, closing = _brace_depth_and_close(opening_line, 0)
        if depth <= 0 or closing is not None:
            raise PreprocessorError("malformed opaque aligned system struct")
        body = opening_line.rstrip(b"\r\n")
        ending = opening_line[len(body):]
        indent = (
            opening_line[: len(opening_line) - len(opening_line.lstrip())]
            + b"    "
        )
        output.append(indent + b"...;" + ending)

        consumed = 1
        while index < len(lines):
            depth, closing = _brace_depth_and_close(lines[index], depth)
            consumed += 1
            if consumed > MAX_OPAQUE_ALIGNED_STRUCT_LINES:
                raise PreprocessorError(
                    "opaque aligned system struct exceeds line bound"
                )
            if closing is not None:
                output.append(lines[index][closing:])
                index += 1
                break
            index += 1
        else:
            raise PreprocessorError("unterminated opaque aligned system struct")
    return output


def _c_declaration_tokens(data: bytes) -> tuple:
    """Tokenize a quote-free C declaration while ignoring whitespace."""
    tokens = []
    index = 0
    while index < len(data):
        value = data[index]
        if value in b" \t\r\n\v\f":
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
            tokens.append(data[index:end])
            index = end
            continue
        if 0x30 <= value <= 0x39:
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
            tokens.append(data[index:end])
            index = end
            continue
        tokens.append(bytes((value,)))
        index += 1
    return tuple(tokens)


def _strip_mingw_c_assert_declarations(lines: List[bytes]) -> List[bytes]:
    """Remove bounded WinNT compile-time assertion pseudo-functions."""
    output: List[bytes] = []
    removed = 0
    for line in lines:
        body = line.rstrip(b"\r\n")
        ending = line[len(body):]
        tokens = _c_declaration_tokens(body.strip())
        if (
            len(tokens) >= 9
            and tokens[:6]
            == (b"extern", b"void", b"__C_ASSERT__", b"(", b"int", b"[")
            and tokens[-3:] == (b"]", b")", b";")
        ):
            removed += 1
            if removed > MAX_MINGW_C_ASSERT_DECLARATIONS:
                raise PreprocessorError(
                    "too many MinGW C_ASSERT declarations"
                )
            output.append(ending)
            continue
        output.append(line)
    return output


def _normalize_mingw_enum_int_casts(lines: List[bytes]) -> List[bytes]:
    """Fold simple 32-bit ``(int)`` enum constants to signed decimals."""
    output: List[bytes] = []
    enum_depth = 0
    replacements = 0

    def rewrite(line: bytes) -> bytes:
        nonlocal replacements
        rewritten = bytearray()
        index = 0
        whitespace = b" \t\r\n\v\f"
        while index < len(line):
            start = line.find(b"(", index)
            if start < 0:
                rewritten.extend(line[index:])
                break
            rewritten.extend(line[index:start])
            cursor = start + 1
            while cursor < len(line) and line[cursor:cursor + 1] in whitespace:
                cursor += 1
            if line[cursor:cursor + 3] != b"int":
                rewritten.append(line[start])
                index = start + 1
                continue
            cursor += 3
            if (
                cursor < len(line)
                and (
                    line[cursor] == 0x5F
                    or 0x30 <= line[cursor] <= 0x39
                    or 0x41 <= line[cursor] <= 0x5A
                    or 0x61 <= line[cursor] <= 0x7A
                )
            ):
                rewritten.append(line[start])
                index = start + 1
                continue
            while cursor < len(line) and line[cursor:cursor + 1] in whitespace:
                cursor += 1
            if cursor >= len(line) or line[cursor] != 0x29:
                rewritten.append(line[start])
                index = start + 1
                continue
            cursor += 1
            while cursor < len(line) and line[cursor:cursor + 1] in whitespace:
                cursor += 1

            sign = 1
            if cursor < len(line) and line[cursor] in (0x2B, 0x2D):
                if line[cursor] == 0x2D:
                    sign = -1
                cursor += 1
            if line[cursor:cursor + 2].lower() == b"0x":
                cursor += 2
                digit_start = cursor
                while cursor < len(line) and (
                    0x30 <= line[cursor] <= 0x39
                    or 0x41 <= line[cursor] <= 0x46
                    or 0x61 <= line[cursor] <= 0x66
                ):
                    cursor += 1
                if cursor == digit_start:
                    rewritten.append(line[start])
                    index = start + 1
                    continue
                value = int(line[digit_start:cursor], 16) * sign
            else:
                digit_start = cursor
                while cursor < len(line) and 0x30 <= line[cursor] <= 0x39:
                    cursor += 1
                if cursor == digit_start:
                    rewritten.append(line[start])
                    index = start + 1
                    continue
                value = int(line[digit_start:cursor], 10) * sign
            if (
                cursor < len(line)
                and (
                    line[cursor] == 0x5F
                    or 0x30 <= line[cursor] <= 0x39
                    or 0x41 <= line[cursor] <= 0x5A
                    or 0x61 <= line[cursor] <= 0x7A
                )
            ):
                rewritten.append(line[start])
                index = start + 1
                continue

            replacements += 1
            if replacements > MAX_MINGW_ENUM_INT_CASTS:
                raise PreprocessorError("too many MinGW enum integer casts")
            value &= 0xFFFFFFFF
            if value >= 0x80000000:
                value -= 0x100000000
            rewritten.extend(str(value).encode("ascii"))
            index = cursor
        return bytes(rewritten)

    for line in lines:
        line_delta = _brace_delta(line)
        if enum_depth == 0:
            identifiers = set(_attribute_identifiers(line))
            if "enum" in identifiers and b"{" in line:
                enum_depth = max(line_delta, 0)
                output.append(rewrite(line))
                continue
            output.append(line)
            continue
        output.append(rewrite(line))
        enum_depth += line_delta
        if enum_depth < 0:
            raise PreprocessorError("malformed MinGW enum brace depth")
    return output


_C_TYPE_KEYWORDS = frozenset(
    {
        b"_Bool",
        b"bool",
        b"char",
        b"double",
        b"float",
        b"int",
        b"long",
        b"short",
        b"signed",
        b"unsigned",
        b"void",
        b"wchar_t",
    }
)
_C_TYPE_QUALIFIERS = frozenset(
    {b"const", b"restrict", b"volatile", b"_Atomic"}
)
_C_TAG_KEYWORDS = frozenset({b"struct", b"union", b"enum"})


def _is_c_identifier_token(token: bytes) -> bool:
    if not token:
        return False
    first = token[0]
    if not (
        first == 0x5F
        or 0x41 <= first <= 0x5A
        or 0x61 <= first <= 0x7A
    ):
        return False
    return all(
        value == 0x5F
        or 0x30 <= value <= 0x39
        or 0x41 <= value <= 0x5A
        or 0x61 <= value <= 0x7A
        for value in token[1:]
    )


def _canonical_parameter_tokens(tokens: tuple) -> tuple:
    """Remove an ordinary parameter identifier while retaining its type."""
    if not tokens or any(token in (b"(", b")", b"[", b"]") for token in tokens):
        return tokens
    identifier_indexes = [
        index
        for index, token in enumerate(tokens)
        if _is_c_identifier_token(token)
    ]
    if not identifier_indexes:
        return tokens
    candidate_index = identifier_indexes[-1]
    candidate = tokens[candidate_index]
    if candidate in _C_TYPE_KEYWORDS or candidate in _C_TYPE_QUALIFIERS:
        return tokens

    nonqual = [
        tokens[index]
        for index in identifier_indexes
        if tokens[index] not in _C_TYPE_QUALIFIERS
    ]
    if not nonqual:
        return tokens
    if nonqual[0] in _C_TAG_KEYWORDS:
        has_name = len(nonqual) >= 3
    else:
        has_name = len(nonqual) >= 2
    if not has_name:
        return tokens
    return tokens[:candidate_index] + tokens[candidate_index + 1:]


def _resolve_alias_sequence(
    tokens: tuple,
    aliases: dict,
    stack=frozenset(),
) -> tuple:
    resolved = []
    previous = None
    for token in tokens:
        if (
            _is_c_identifier_token(token)
            and previous not in _C_TAG_KEYWORDS
            and token in aliases
            and token not in stack
        ):
            resolved.extend(
                _resolve_alias_sequence(
                    aliases[token],
                    aliases,
                    stack | {token},
                )
            )
        else:
            resolved.append(token)
        previous = token
    return tuple(resolved)


def _collect_simple_typedef_aliases(lines: List[bytes]) -> dict:
    """Collect unambiguous top-level one-line typedef aliases."""
    aliases = {}
    ambiguous = set()
    brace_depth = 0
    observed = 0
    for line in lines:
        line_depth = _brace_delta(line)
        stripped = line.rstrip(b"\r\n").strip()
        tokens = _c_declaration_tokens(stripped)
        if (
            brace_depth == 0
            and line_depth == 0
            and len(tokens) >= 4
            and tokens[0] == b"typedef"
            and tokens[-1] == b";"
            and _is_c_identifier_token(tokens[-2])
            and not any(
                token in tokens[1:-2]
                for token in (
                    b".",
                    b",",
                    b"{",
                    b"}",
                    b"(",
                    b")",
                    b"[",
                    b"]",
                    b"\"",
                    b"'",
                )
            )
        ):
            alias = tokens[-2]
            underlying = tuple(tokens[1:-2])
            if alias not in underlying and alias not in ambiguous:
                observed += 1
                if observed > MAX_SIMPLE_TYPEDEF_ALIASES:
                    raise PreprocessorError(
                        "too many simple MinGW typedef aliases"
                    )
                resolved = _resolve_alias_sequence(underlying, aliases)
                prior = aliases.get(alias)
                if prior is None:
                    aliases[alias] = resolved
                elif prior != resolved:
                    aliases.pop(alias, None)
                    ambiguous.add(alias)
        brace_depth += line_depth
    return aliases


def _canonical_function_declaration(
    data: bytes,
    aliases: Optional[dict] = None,
) -> tuple:
    """Canonicalize one simple prototype, ignoring parameter identifiers."""
    tokens = _c_declaration_tokens(data)
    if len(tokens) < 4 or tokens[-1] != b";":
        return tokens
    try:
        opening = tokens.index(b"(")
    except ValueError:
        return tokens
    if opening < 1 or not _is_c_identifier_token(tokens[opening - 1]):
        return tokens

    depth = 0
    closing = None
    for index in range(opening, len(tokens)):
        token = tokens[index]
        if token == b"(":
            depth += 1
        elif token == b")":
            depth -= 1
            if depth == 0:
                closing = index
                break
    if closing is None or closing != len(tokens) - 2:
        return tokens

    parameters = []
    current = []
    nested = 0
    for token in tokens[opening + 1:closing]:
        if token in (b"(", b"["):
            nested += 1
        elif token in (b")", b"]"):
            nested -= 1
        if token == b"," and nested == 0:
            parameters.append(_canonical_parameter_tokens(tuple(current)))
            current = []
        else:
            current.append(token)
    parameters.append(_canonical_parameter_tokens(tuple(current)))
    has_complex_parameter = any(
        any(token in (b"(", b")", b"[", b"]") for token in parameter)
        for parameter in parameters
    )

    canonical = list(tokens[:opening + 1])
    for index, parameter in enumerate(parameters):
        if index:
            canonical.append(b",")
        canonical.extend(parameter)
    canonical.extend(tokens[closing:])
    result = tuple(canonical)
    if not aliases or has_complex_parameter:
        return result

    opening = result.index(b"(")
    function_name = result[opening - 1]
    return (
        _resolve_alias_sequence(result[:opening - 1], aliases)
        + (function_name, b"(")
        + _resolve_alias_sequence(result[opening + 1:-2], aliases)
        + (b")", b";")
    )


def _dedupe_identical_function_declarations(lines: List[bytes]) -> List[bytes]:
    """Blank repeated one-line function declarations after sanitization."""
    output: List[bytes] = []
    aliases = _collect_simple_typedef_aliases(lines)
    seen = set()
    removed = 0
    brace_depth = 0
    for line in lines:
        body = line.rstrip(b"\r\n")
        ending = line[len(body):]
        stripped = body.strip()
        line_depth = _brace_delta(line)
        if (
            brace_depth != 0
            or line_depth != 0
            or not stripped.endswith(b";")
            or b"(" not in stripped
            or b")" not in stripped
            or b"{" in stripped
            or b"}" in stripped
            or b"\"" in stripped
            or b"'" in stripped
            or stripped.startswith(b"typedef ")
        ):
            output.append(line)
            brace_depth += line_depth
            continue
        canonical = _canonical_function_declaration(stripped, aliases)
        if canonical not in seen:
            seen.add(canonical)
            output.append(line)
            brace_depth += line_depth
            continue
        removed += 1
        if removed > MAX_DUPLICATE_FUNCTION_DECLARATIONS:
            raise PreprocessorError(
                "too many duplicate MinGW function declarations"
            )
        output.append(ending)
        brace_depth += line_depth
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


def _dedupe_identical_typedef_declarations(lines: List[bytes]) -> List[bytes]:
    """Blank repeated top-level one-line typedef declarations."""
    output: List[bytes] = []
    aliases = _collect_simple_typedef_aliases(lines)
    seen = set()
    removed = 0
    brace_depth = 0
    for line in lines:
        body = line.rstrip(b"\r\n")
        ending = line[len(body):]
        stripped = body.strip()
        line_depth = _brace_delta(line)
        if (
            brace_depth != 0
            or line_depth != 0
            or not stripped.startswith(b"typedef ")
            or not stripped.endswith(b";")
            or b"{" in stripped
            or b"}" in stripped
            or b"\"" in stripped
            or b"'" in stripped
        ):
            output.append(line)
            brace_depth += line_depth
            continue
        if b"(" in stripped and b")" in stripped:
            canonical = _canonical_function_declaration(stripped, aliases)
        else:
            canonical = _c_declaration_tokens(stripped)
        if canonical not in seen:
            seen.add(canonical)
            output.append(line)
            brace_depth += line_depth
            continue
        removed += 1
        if removed > MAX_DUPLICATE_TYPEDEF_DECLARATIONS:
            raise PreprocessorError(
                "too many duplicate MinGW typedef declarations"
            )
        output.append(ending)
        brace_depth += line_depth
    return output


def _remove_mingw_limits_block(
    lines: List[bytes],
    *,
    required: bool = False,
) -> List[bytes]:
    """Remove declarations emitted only while expanding pinned ``limits.h``."""
    begin = [
        index
        for index, line in enumerate(lines)
        if line.strip() == LIMITS_BLOCK_BEGIN_LINE.encode("ascii")
    ]
    end = [
        index
        for index, line in enumerate(lines)
        if line.strip() == LIMITS_BLOCK_END_LINE.encode("ascii")
    ]
    if not begin and not end and not required:
        return lines
    if len(begin) != 1 or len(end) != 1 or end[0] <= begin[0]:
        raise PreprocessorError("invalid MinGW limits block markers")
    if end[0] - begin[0] + 1 > MAX_MINGW_LIMITS_BLOCK_LINES:
        raise PreprocessorError("MinGW limits block exceeds line bound")
    return lines[:begin[0]] + lines[end[0] + 1:]


def _normalize_builder_output(
    data: bytes,
    *,
    require_limits_markers: bool = False,
) -> bytes:
    """Normalize compiler-only MinGW constructs for CFFI parsing."""
    lines = _remove_mingw_limits_block(
        data.splitlines(keepends=True),
        required=require_limits_markers,
    )
    lines = _join_mingw_multiline_attribute_typedefs(lines)
    authoritative_aliases = set()
    opaque_aliases = set()
    for line in lines:
        stripped = line.rstrip(b"\r\n").strip()
        authoritative_alias = AUTHORITATIVE_CFFI_TYPEDEF_LINES.get(stripped)
        if authoritative_alias is not None:
            if authoritative_alias in authoritative_aliases:
                raise PreprocessorError(
                    "duplicate authoritative CFFI typedef alias"
                )
            authoritative_aliases.add(authoritative_alias)
        if not stripped.endswith(b";"):
            continue
        parts = stripped[:-1].split()
        if len(parts) != 3 or parts[:2] != [b"typedef", b"..."]:
            continue
        alias = parts[2]
        try:
            alias_text = alias.decode("ascii")
        except UnicodeDecodeError as exc:
            raise PreprocessorError(
                "invalid opaque compiler typedef alias"
            ) from exc
        if not alias_text.isidentifier():
            raise PreprocessorError("invalid opaque compiler typedef alias")
        if alias in opaque_aliases:
            raise PreprocessorError("duplicate opaque compiler typedef alias")
        opaque_aliases.add(alias)

    normalized = 0
    compiler_alias_sources = {}
    for index, line in enumerate(lines):
        body = line.rstrip(b"\r\n")
        ending = line[len(body):]
        stripped = body.strip()
        if not stripped.endswith(b";"):
            continue
        parts = stripped[:-1].split()
        if len(parts) != 3 or parts[0] != b"typedef":
            continue
        source = parts[1]
        allowed_aliases = OPAQUE_COMPILER_SCALAR_TYPE_ALIASES.get(source)
        if source not in OPAQUE_COMPILER_SCALAR_TYPE_ALIASES:
            continue
        alias = parts[2]
        try:
            alias_text = alias.decode("ascii")
        except UnicodeDecodeError as exc:
            raise PreprocessorError(
                "invalid compiler scalar typedef alias"
            ) from exc
        if not alias_text.isidentifier():
            raise PreprocessorError("invalid compiler scalar typedef alias")
        if allowed_aliases is not None and alias not in allowed_aliases:
            raise PreprocessorError("unsupported compiler scalar typedef alias")
        if alias == source:
            raise PreprocessorError("cyclic compiler scalar typedef alias")
        normalized += 1
        if normalized > MAX_OPAQUE_COMPILER_SCALAR_TYPEDEFS:
            raise PreprocessorError("too many compiler scalar typedef aliases")
        if alias in opaque_aliases:
            if source == b"__builtin_va_list" and alias == b"va_list":
                # The temporary pinned-header overlay already declares CFFI's
                # exact ``typedef ... va_list;``. Current GCC headers may emit
                # the equivalent compiler builtin declaration later; retain
                # the authoritative overlay and blank only that exact duplicate.
                lines[index] = ending
                continue
            raise PreprocessorError("conflicting compiler scalar typedef alias")
        lines[index] = b"typedef ... " + alias + b";" + ending
        opaque_aliases.add(alias)
        compiler_alias_sources[alias] = source

    if b"va_list" in authoritative_aliases:
        for index, line in enumerate(lines):
            body = line.rstrip(b"\r\n")
            ending = line[len(body):]
            stripped = body.strip()
            if not stripped.endswith(b";"):
                continue
            parts = stripped[:-1].split()
            if len(parts) != 3 or parts[0] != b"typedef":
                continue
            source, alias = parts[1], parts[2]
            if alias != b"va_list" or source not in compiler_alias_sources:
                continue
            root = source
            seen = set()
            while root in compiler_alias_sources:
                if root in seen:
                    raise PreprocessorError(
                        "cyclic compiler scalar typedef alias"
                    )
                seen.add(root)
                root = compiler_alias_sources[root]
            if root != b"__builtin_va_list":
                raise PreprocessorError(
                    "conflicting compiler scalar va_list alias"
                )
            normalized += 1
            if normalized > MAX_OPAQUE_COMPILER_SCALAR_TYPEDEFS:
                raise PreprocessorError(
                    "too many compiler scalar typedef aliases"
                )
            lines[index] = ending

    for index, line in enumerate(lines):
        body = line.rstrip(b"\r\n")
        ending = line[len(body):]
        stripped = body.strip()
        if (
            b"..." in stripped
            or not stripped.endswith(b";")
            or any(token in stripped for token in (b"{", b"}", b"(", b")", b"[", b"]", b","))
        ):
            continue
        identifiers = _attribute_identifiers(stripped)
        if len(identifiers) < 3 or identifiers[0] != "typedef":
            continue
        alias_text = identifiers[-1]
        alias = alias_text.encode("ascii")
        if (
            alias not in authoritative_aliases
            or alias == b"va_list"
            or not stripped[:-1].rstrip().endswith(alias)
        ):
            continue
        allowed_sources = AUTHORITATIVE_HOST_TYPEDEF_SOURCES.get(alias)
        source = identifiers[1].encode("ascii")
        if (
            allowed_sources is None
            or len(identifiers) != 3
            or source not in allowed_sources
        ):
            raise PreprocessorError(
                "conflicting authoritative host typedef alias"
            )
        normalized += 1
        if normalized > MAX_OPAQUE_COMPILER_SCALAR_TYPEDEFS:
            raise PreprocessorError("too many compiler scalar typedef aliases")
        lines[index] = ending
    lines = _replace_mingw_tokens(lines)
    lines = _strip_mingw_inline_blocks(lines)
    lines = _strip_mingw_attributes(lines)
    lines = _partialize_opaque_aligned_system_structs(lines)
    lines = _strip_mingw_c_assert_declarations(lines)
    lines = _normalize_mingw_enum_int_casts(lines)
    lines = _dedupe_identical_typedef_declarations(lines)
    lines = _dedupe_identical_function_declarations(lines)
    return b"".join(lines)


def _filter_mingw_preprocessed_sources(
    output: bytes,
    overlay_path: str,
) -> bytes:
    """Keep only the temporary overlay and pinned mGBA source declarations."""
    source_root = os.path.realpath(_expected_source_root())
    overlay = os.path.realpath(overlay_path)
    kept: List[bytes] = []
    allowed = False
    markers = 0
    for line in output.splitlines(keepends=True):
        if line.startswith(b"#"):
            markers += 1
            if markers > MAX_PREPROCESSOR_LINE_MARKERS:
                raise PreprocessorError("too many preprocessor line markers")
            try:
                parts = shlex.split(
                    line.decode("utf-8", errors="surrogateescape")
                )
            except ValueError as exc:
                raise PreprocessorError(
                    "malformed preprocessor line marker"
                ) from exc
            if len(parts) >= 3 and parts[0] == "#" and parts[1].isdigit():
                origin = os.path.realpath(parts[2])
                allowed = (
                    os.path.normcase(origin) == os.path.normcase(overlay)
                    or _path_is_within(origin, source_root)
                )
            continue
        if allowed:
            kept.append(line)
    if markers == 0:
        raise PreprocessorError("preprocessor emitted no line markers")
    return b"".join(kept)


def _run_real_preprocessor(
    argv: List[str], *, normalize_builder_output: bool = False
) -> int:
    """Invoke the real preprocessor structurally (never via a shell)."""
    real_argv = [
        arg
        for arg in argv
        if not (normalize_builder_output and arg == "-P")
    ]
    compiler_flags = []
    if normalize_builder_output:
        winver_args = [
            arg
            for arg in real_argv
            if arg.startswith("-D_WIN32_WINNT=")
        ]
        if len(winver_args) > 1 or (
            winver_args and winver_args[0] != MINGW_WINVER_DEFINE
        ):
            raise PreprocessorError(
                "conflicting MinGW _WIN32_WINNT preprocessor definition"
            )
        if not winver_args:
            compiler_flags.append(MINGW_WINVER_DEFINE)
    command = (
        _resolve_compiler_tokens()
        + compiler_flags
        + ["-E"]
        + real_argv
    )
    completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output = completed.stdout
    if completed.returncode == 0 and len(output) > MAX_PREPROCESSED_BYTES:
        raise PreprocessorError("preprocessed output exceeds the size bound")
    if completed.returncode == 0 and normalize_builder_output:
        output = _filter_mingw_preprocessed_sources(output, argv[-1])
        output = _normalize_builder_output(
            output,
            require_limits_markers=True,
        )
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
