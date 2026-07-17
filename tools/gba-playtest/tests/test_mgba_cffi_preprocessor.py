"""Dependency-free contracts for scripts/mgba_cffi_preprocessor.py.

This is a build-time-only helper (see its module docstring for the full
rationale: it works around a CFFI cdef parse failure in mGBA 0.10.5's pinned
``_builder.h`` on newer GCC toolchains, without ever patching the pinned
upstream source). These tests exercise it directly, in-process, with a faked
bounded-subprocess helper so no real compiler or mGBA source is required.

``capsysbinary`` is used (not a hand-rolled stream fake) so both this
script's plain-text diagnostics (``sys.stderr.write``) and its binary
passthrough (``sys.stdout.buffer.write`` / ``sys.stderr.buffer.write``) are
captured the same way pytest already captures any other process output,
without needing a custom stream double.
"""

import os
import sys

import pytest

TEST_DIR = os.path.dirname(os.path.abspath(__file__))
TOOL_DIR = os.path.dirname(TEST_DIR)
REPO_ROOT = os.path.dirname(os.path.dirname(TOOL_DIR))
SCRIPTS_DIR = os.path.join(REPO_ROOT, "scripts")
if SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, SCRIPTS_DIR)

import mgba_cffi_preprocessor as wrapper  # noqa: E402


class _FakeCompleted:
    def __init__(self, returncode=0, stdout=b"", stderr=b""):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr


@pytest.fixture(autouse=True)
def _no_ambient_cc_override(monkeypatch):
    # Never let the real test-runner environment's CC leak into a test that
    # asserts the plain "cc" default.
    monkeypatch.delenv(wrapper.ENV_CC_OVERRIDE, raising=False)


@pytest.fixture(autouse=True)
def _no_ambient_wrapper_env(monkeypatch):
    # Never let the real test-runner environment's own bootstrap env vars
    # leak into a test that expects them unset/specific.
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    monkeypatch.delenv(wrapper.ENV_SOURCE_ROOT, raising=False)
    monkeypatch.delenv(wrapper.ENV_MINGW_CDEF, raising=False)


# --------------------------------------------------------------------------- #
# Compiler token resolution
# --------------------------------------------------------------------------- #
def test_default_compiler_tokens_are_plain_cc():
    assert wrapper._resolve_compiler_tokens() == ["cc"]


def test_cc_override_is_tokenized_structurally(monkeypatch):
    monkeypatch.setenv(wrapper.ENV_CC_OVERRIDE, "/usr/bin/gcc-13 -pipe")
    assert wrapper._resolve_compiler_tokens() == ["/usr/bin/gcc-13", "-pipe"]


def test_cc_override_with_only_whitespace_fails_closed(monkeypatch):
    monkeypatch.setenv(wrapper.ENV_CC_OVERRIDE, "   ")
    with pytest.raises(wrapper.PreprocessorError, match="empty command"):
        wrapper._resolve_compiler_tokens()


def test_mingw_cdef_flag_accepts_only_zero_or_one(monkeypatch):
    assert wrapper._mingw_cdef_enabled() is False
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "1")
    assert wrapper._mingw_cdef_enabled() is True
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "yes")
    with pytest.raises(wrapper.PreprocessorError, match="must be 0 or 1"):
        wrapper._mingw_cdef_enabled()


# --------------------------------------------------------------------------- #
# Exact single-line rewrite / drift rejection
# --------------------------------------------------------------------------- #
def test_exact_rewrite_replaces_only_the_one_line():
    text = "int a;\n#define va_list void*\nint b;\n"
    rewritten = wrapper._rewrite_builder_h_text(text)
    lines = rewritten.splitlines()
    assert lines == ["int a;", "typedef ... va_list;", "int b;"]
    assert wrapper.OLD_BUILDER_LINE not in rewritten
    assert rewritten.count(wrapper.NEW_BUILDER_LINE) == 1


def test_rewrite_preserves_line_ending_style():
    text = "a;\r\n#define va_list void*\r\nb;\r\n"
    rewritten = wrapper._rewrite_builder_h_text(text)
    assert "typedef ... va_list;\r\n" in rewritten


def test_rewrite_rejects_zero_occurrences():
    with pytest.raises(wrapper.PreprocessorError, match="found 0"):
        wrapper._rewrite_builder_h_text("int a;\nint b;\n")


def test_rewrite_rejects_duplicate_occurrences():
    text = "#define va_list void*\n#define va_list void*\n"
    with pytest.raises(wrapper.PreprocessorError, match="found 2"):
        wrapper._rewrite_builder_h_text(text)


def test_rewrite_rejects_when_upstream_replacement_already_present():
    text = "#define va_list void*\ntypedef ... va_list;\n"
    with pytest.raises(wrapper.PreprocessorError, match="already present"):
        wrapper._rewrite_builder_h_text(text)


def test_mingw_rewrite_scopes_attribute_sanitizer_to_limits_header():
    text = (
        "#define PYCPARSE\n"
        "#define va_list void*\n"
        "#include <limits.h>\n"
        "#include <mgba/flags.h>\n"
    )
    rewritten = wrapper._rewrite_builder_h_text(text, sanitize_mingw=True)
    assert (
        "#define __attribute__(X)\n"
        "#define __INTRIN_H_\n"
        "#include <limits.h>\n"
        "#undef __INTRIN_H_\n"
        "#undef __attribute__\n"
    ) in rewritten
    assert rewritten.count(wrapper.ATTRIBUTE_DISABLE_LINE) == 1
    assert rewritten.count(wrapper.ATTRIBUTE_RESTORE_LINE) == 1
    assert rewritten.count(wrapper.INTRIN_GUARD_DISABLE_LINE) == 1
    assert rewritten.count(wrapper.INTRIN_GUARD_RESTORE_LINE) == 1
    assert rewritten.count(wrapper.LIMITS_BLOCK_BEGIN_LINE) == 1
    assert rewritten.count(wrapper.LIMITS_BLOCK_END_LINE) == 1
    assert (
        rewritten.index(wrapper.LIMITS_BLOCK_BEGIN_LINE)
        < rewritten.index(wrapper.ATTRIBUTE_DISABLE_LINE)
        < rewritten.index(wrapper.INTRIN_GUARD_DISABLE_LINE)
        < rewritten.index(wrapper.LIMITS_INCLUDE_LINE)
        < rewritten.index(wrapper.INTRIN_GUARD_RESTORE_LINE)
        < rewritten.index(wrapper.ATTRIBUTE_RESTORE_LINE)
        < rewritten.index(wrapper.LIMITS_BLOCK_END_LINE)
    )
    assert rewritten.index(wrapper.ATTRIBUTE_RESTORE_LINE) < rewritten.index(
        "#include <mgba/flags.h>"
    )


def test_mingw_rewrite_fails_closed_if_limits_include_drifts():
    text = "#define va_list void*\n"
    with pytest.raises(wrapper.PreprocessorError, match="limits.h"):
        wrapper._rewrite_builder_h_text(text, sanitize_mingw=True)


# --------------------------------------------------------------------------- #
# Preprocessed MinGW builtin typedef normalization
# --------------------------------------------------------------------------- #
def test_preprocessed_limits_block_is_removed_before_normalization():
    data = (
        b"typedef ... va_list;\n"
        + wrapper.LIMITS_BLOCK_BEGIN_LINE.encode("ascii")
        + b"\n"
        + b"void irrelevant_windows_api(void);\n"
        + b"typedef struct _IRRELEVANT { int value; } IRRELEVANT;\n"
        + wrapper.LIMITS_BLOCK_END_LINE.encode("ascii")
        + b"\n"
        + b"int retained_mgba_api(void);\n"
    )
    assert wrapper._normalize_builder_output(
        data,
        require_limits_markers=True,
    ) == (
        b"typedef ... va_list;\n"
        b"int retained_mgba_api(void);\n"
    )


def test_required_limits_markers_fail_closed_when_missing():
    with pytest.raises(wrapper.PreprocessorError, match="limits block markers"):
        wrapper._normalize_builder_output(
            b"int retained;\n",
            require_limits_markers=True,
        )


def test_limits_block_line_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_LIMITS_BLOCK_LINES", 2)
    data = (
        wrapper.LIMITS_BLOCK_BEGIN_LINE.encode("ascii")
        + b"\nint system;\n"
        + wrapper.LIMITS_BLOCK_END_LINE.encode("ascii")
        + b"\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="line bound"):
        wrapper._normalize_builder_output(
            data,
            require_limits_markers=True,
        )


def test_source_filter_keeps_only_overlay_and_pinned_source(
    monkeypatch, tmp_path
):
    source_root = tmp_path / "mgba"
    source_root.mkdir()
    source_header = source_root / "include" / "api.h"
    source_header.parent.mkdir()
    source_header.write_text("", encoding="utf-8")
    sibling = tmp_path / "mgba-evil" / "api.h"
    sibling.parent.mkdir()
    sibling.write_text("", encoding="utf-8")
    overlay = tmp_path / "overlay.h"
    overlay.write_text("", encoding="utf-8")
    system = tmp_path / "system.h"
    system.write_text("", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(source_root))
    marker = lambda path: str(path).replace("\\", "/")

    output = (
        f'# 1 "{marker(overlay)}"\n'.encode()
        + b"int overlay_decl;\n"
        + f'# 1 "{marker(system)}" 1\n'.encode()
        + b"int system_decl;\n"
        + f'# 2 "{marker(source_header)}" 1\n'.encode()
        + b"int mgba_decl;\n"
        + f'# 3 "{marker(sibling)}" 1\n'.encode()
        + b"int sibling_decl;\n"
    )
    assert wrapper._filter_mingw_preprocessed_sources(
        output,
        str(overlay),
    ) == (
        b"int overlay_decl;\n"
        b"int mgba_decl;\n"
    )


def test_source_filter_requires_line_markers(monkeypatch, tmp_path):
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))
    with pytest.raises(wrapper.PreprocessorError, match="no line markers"):
        wrapper._filter_mingw_preprocessed_sources(
            b"int declaration;\n",
            str(tmp_path / "overlay.h"),
        )


def test_source_filter_line_marker_count_is_bounded(monkeypatch, tmp_path):
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))
    monkeypatch.setattr(wrapper, "MAX_PREPROCESSOR_LINE_MARKERS", 1)
    overlay = tmp_path / "overlay.h"
    system = tmp_path / "system.h"
    overlay_marker = str(overlay).replace("\\", "/")
    system_marker = str(system).replace("\\", "/")
    output = (
        f'# 1 "{overlay_marker}"\n'.encode()
        + f'# 1 "{system_marker}"\n'.encode()
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many preprocessor"):
        wrapper._filter_mingw_preprocessed_sources(
            output,
            str(overlay),
        )


def test_mingw_preprocessor_rejects_conflicting_winver(monkeypatch):
    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda *args, **kwargs: pytest.fail("compiler must not run"),
    )
    with pytest.raises(wrapper.PreprocessorError, match="conflicting MinGW"):
        wrapper._run_real_preprocessor(
            ["-D_WIN32_WINNT=0x0A00", "builder.h"],
            normalize_builder_output=True,
        )


def test_builtin_va_list_alias_is_normalized_for_cffi():
    data = b"typedef __builtin_va_list __gnuc_va_list;\nint value;\n"
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __gnuc_va_list;\nint value;\n"
    )


def test_multiple_builtin_va_list_aliases_are_normalized_with_line_endings():
    data = (
        b"typedef __builtin_va_list __gnuc_va_list;\r\n"
        b"typedef __builtin_ms_va_list __gnuc_ms_va_list;\n"
        b"typedef __builtin_sysv_va_list __gnuc_sysv_va_list;\n"
        b"  typedef   __builtin_va_list   compiler_va_list ;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __gnuc_va_list;\r\n"
        b"typedef ... __gnuc_ms_va_list;\n"
        b"typedef ... __gnuc_sysv_va_list;\n"
        b"typedef ... compiler_va_list;\n"
    )


def test_gcc_bfloat16_scalar_alias_is_normalized_for_cffi():
    data = b"typedef __bf16 __bfloat16;\nint retained;\n"
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __bfloat16;\nint retained;\n"
    )


def test_gcc_bfloat16_application_alias_fails_closed():
    data = b"typedef __bf16 application_bfloat;\n"
    with pytest.raises(wrapper.PreprocessorError, match="unsupported"):
        wrapper._normalize_builder_output(data)


def test_overlay_va_list_wins_over_equivalent_gcc_builtin_typedef():
    data = (
        b"typedef ... va_list;\r\n"
        b"typedef __builtin_va_list va_list;\r\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... va_list;\r\n"
        b"\r\n"
        b"int retained;\n"
    )


def test_overlay_va_list_rejects_conflicting_builtin_kind():
    data = (
        b"typedef ... va_list;\n"
        b"typedef __builtin_ms_va_list va_list;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="conflicting"):
        wrapper._normalize_builder_output(data)


def test_overlay_va_list_wins_over_gcc_gnuc_alias_chain():
    data = (
        b"typedef ... va_list;\n"
        b"typedef __builtin_va_list __gnuc_va_list;\n"
        b"typedef __gnuc_va_list va_list;\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... va_list;\n"
        b"typedef ... __gnuc_va_list;\n"
        b"\n"
        b"int retained;\n"
    )


def test_overlay_va_list_rejects_nondefault_gnuc_alias_chain():
    data = (
        b"typedef ... va_list;\n"
        b"typedef __builtin_ms_va_list __gnuc_ms_va_list;\n"
        b"typedef __gnuc_ms_va_list va_list;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="conflicting"):
        wrapper._normalize_builder_output(data)


def test_gnuc_alias_chain_without_overlay_remains_valid():
    data = (
        b"typedef __builtin_va_list __gnuc_va_list;\n"
        b"typedef __gnuc_va_list va_list;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __gnuc_va_list;\n"
        b"typedef __gnuc_va_list va_list;\n"
    )


def test_self_referential_compiler_scalar_alias_fails_closed():
    data = b"typedef __builtin_va_list __builtin_va_list;\n"
    with pytest.raises(wrapper.PreprocessorError, match="cyclic"):
        wrapper._normalize_builder_output(data)


def test_authoritative_time_and_off_t_overrides_blank_system_typedefs():
    data = (
        b"typedef int... time_t;\r\n"
        b"typedef int... off_t;\n"
        b"typedef __time64_t time_t;\r\n"
        b"typedef off64_t off_t;\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef int... time_t;\r\n"
        b"typedef int... off_t;\n"
        b"\r\n"
        b"\n"
        b"int retained;\n"
    )


def test_time_and_off_t_without_authoritative_overrides_are_unchanged():
    data = (
        b"typedef __time64_t time_t;\n"
        b"typedef off64_t off_t;\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_authoritative_time_t_rejects_incompatible_host_source():
    data = (
        b"typedef int... time_t;\n"
        b"typedef char time_t;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="conflicting authoritative"):
        wrapper._normalize_builder_output(data)


def test_duplicate_authoritative_cffi_alias_fails_closed():
    data = (
        b"typedef int... time_t;\n"
        b"typedef int... time_t;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="duplicate authoritative"):
        wrapper._normalize_builder_output(data)


def test_identical_mingw_function_declarations_are_deduplicated():
    data = (
        b"void * memccpy(void *dst,const void *src,int value,size_t size);\n"
        b"  void *  memccpy(void *dst,const void *src,int value,size_t size) ;  \r\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"void * memccpy(void *dst,const void *src,int value,size_t size);\n"
        b"\r\n"
        b"int retained;\n"
    )


def test_distinct_function_declarations_are_not_deduplicated():
    data = (
        b"void * memccpy(void *dst,const void *src,int value,size_t size);\n"
        b"void * memccpy(void *dst,const void *src,long value,size_t size);\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_function_declaration_tokenization_preserves_identifier_boundaries():
    data = (
        b"unsigned long function(void);\n"
        b"unsignedlong function(void);\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_function_declarations_ignore_parameter_names_when_deduplicating():
    data = (
        b"int chmod(const char *, int);\n"
        b"int chmod(const char *filename, int mode);\r\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"int chmod(const char *, int);\n"
        b"\r\n"
    )


def test_parameter_type_changes_are_not_deduplicated():
    data = (
        b"int function(size_t value);\n"
        b"int function(off_t value);\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_tagged_parameter_names_are_ignored_but_tags_are_preserved():
    data = (
        b"int function(struct Context *);\n"
        b"int function(struct Context *context);\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"int function(struct Context *);\n"
        b"\n"
    )


def test_function_declarations_resolve_equivalent_typedef_aliases():
    data = (
        b"typedef unsigned long ULONG;\n"
        b"typedef unsigned long DWORD;\n"
        b"typedef unsigned char UCHAR;\n"
        b"typedef unsigned char BYTE;\n"
        b"unsigned long long VerSetConditionMask("
        b"unsigned long long mask, DWORD type, BYTE condition);\n"
        b"unsigned long long VerSetConditionMask("
        b"unsigned long long mask, ULONG type, UCHAR condition);\r\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef unsigned long ULONG;\n"
        b"typedef unsigned long DWORD;\n"
        b"typedef unsigned char UCHAR;\n"
        b"typedef unsigned char BYTE;\n"
        b"unsigned long long VerSetConditionMask("
        b"unsigned long long mask, DWORD type, BYTE condition);\n"
        b"\r\n"
    )


def test_function_declarations_keep_inequivalent_typedef_aliases():
    data = (
        b"typedef unsigned long DWORD;\n"
        b"typedef unsigned short WORD;\n"
        b"int function(DWORD value);\n"
        b"int function(WORD value);\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_equivalent_redeclared_typedef_alias_remains_resolvable():
    data = (
        b"typedef unsigned long DWORD;\n"
        b"typedef unsigned long ULONG;\n"
        b"typedef DWORD ULONG;\n"
        b"int function(DWORD value);\n"
        b"int function(ULONG value);\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef unsigned long DWORD;\n"
        b"typedef unsigned long ULONG;\n"
        b"typedef DWORD ULONG;\n"
        b"int function(DWORD value);\n"
        b"\n"
    )


def test_simple_typedef_alias_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_SIMPLE_TYPEDEF_ALIASES", 1)
    data = (
        b"typedef unsigned long FIRST;\n"
        b"typedef unsigned long SECOND;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many simple"):
        wrapper._normalize_builder_output(data)


def test_complex_parameter_identifier_is_not_resolved_as_typedef():
    data = (
        b"typedef unsigned long DWORD;\n"
        b"int function(void (*DWORD)(void));\n"
        b"int function(void (*callback)(void));\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_identical_function_pointer_fields_in_distinct_structs_are_preserved():
    data = (
        b"struct First {\n"
        b"  void (*callback)(void);\n"
        b"};\n"
        b"struct Second {\n"
        b"  void (*callback)(void);\n"
        b"};\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_duplicate_function_declaration_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_DUPLICATE_FUNCTION_DECLARATIONS", 1)
    data = (
        b"void function(void);\n"
        b"void function(void);\n"
        b"void function(void);\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many duplicate"):
        wrapper._normalize_builder_output(data)


def test_identical_function_typedefs_and_pointer_aliases_are_deduplicated():
    data = (
        b"typedef void BAD_MEMORY_CALLBACK_ROUTINE(void);\n"
        b"typedef BAD_MEMORY_CALLBACK_ROUTINE *PBAD_MEMORY_CALLBACK_ROUTINE;\n"
        b"  typedef  void BAD_MEMORY_CALLBACK_ROUTINE (void);  \r\n"
        b"typedef BAD_MEMORY_CALLBACK_ROUTINE * PBAD_MEMORY_CALLBACK_ROUTINE;\r\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef void BAD_MEMORY_CALLBACK_ROUTINE(void);\n"
        b"typedef BAD_MEMORY_CALLBACK_ROUTINE *PBAD_MEMORY_CALLBACK_ROUTINE;\n"
        b"\r\n"
        b"\r\n"
    )


def test_distinct_function_typedefs_are_not_deduplicated():
    data = (
        b"typedef void CALLBACK_ROUTINE(void);\n"
        b"typedef int CALLBACK_ROUTINE(void);\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_duplicate_typedef_declaration_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_DUPLICATE_TYPEDEF_DECLARATIONS", 1)
    data = (
        b"typedef void CALLBACK_ROUTINE(void);\n"
        b"typedef void CALLBACK_ROUTINE(void);\n"
        b"typedef void CALLBACK_ROUTINE(void);\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many duplicate MinGW typedef"):
        wrapper._normalize_builder_output(data)


def test_mingw_c_assert_declaration_is_removed():
    data = (
        b"extern void __C_ASSERT__(int [(VALUE == (A + B)) ? 1 : -1]);\r\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"\r\n"
        b"int retained;\n"
    )


def test_mingw_c_assert_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_C_ASSERT_DECLARATIONS", 1)
    data = (
        b"extern void __C_ASSERT__(int [(A) ? 1 : -1]);\n"
        b"extern void __C_ASSERT__(int [(B) ? 1 : -1]);\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many MinGW C_ASSERT"):
        wrapper._normalize_builder_output(data)


def test_mingw_enum_int_casts_preserve_signed_32bit_values():
    data = (
        b"typedef enum {\n"
        b"  NEGATIVE = (int) -1,\n"
        b"  HIGH_BIT = (int) 0x80000000,\n"
        b"  FORCE_UINT = (int)0xFFFFFFFF,\n"
        b"  ORDINARY = (int) 18\n"
        b"} VALUES;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef enum {\n"
        b"  NEGATIVE = -1,\n"
        b"  HIGH_BIT = -2147483648,\n"
        b"  FORCE_UINT = -1,\n"
        b"  ORDINARY = 18\n"
        b"} VALUES;\n"
    )


def test_nonliteral_enum_int_cast_remains_fail_closed():
    data = (
        b"typedef enum {\n"
        b"  VALUE = (int) (A + B),\n"
        b"  SUFFIXED = (int) 1ULL\n"
        b"} VALUES;\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_mingw_enum_int_cast_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_ENUM_INT_CASTS", 1)
    data = (
        b"typedef enum {\n"
        b"  FIRST = (int) 1,\n"
        b"  SECOND = (int) 2\n"
        b"} VALUES;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many MinGW enum"):
        wrapper._normalize_builder_output(data)


def test_duplicate_opaque_compiler_alias_fails_closed():
    data = (
        b"typedef ... va_list;\n"
        b"typedef ... va_list;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="duplicate"):
        wrapper._normalize_builder_output(data)


def test_unrelated_preprocessor_output_is_byte_identical():
    data = b"typedef unsigned long size_t;\n#define VALUE 1\n"
    assert wrapper._normalize_builder_output(data) == data


def test_invalid_builtin_va_list_alias_fails_closed():
    data = b"typedef __builtin_va_list invalid-alias;\n"
    with pytest.raises(wrapper.PreprocessorError, match="invalid"):
        wrapper._normalize_builder_output(data)


def test_compiler_scalar_alias_count_is_bounded():
    data = b"".join(
        f"typedef __builtin_va_list alias_{index};\n".encode("ascii")
        for index in range(wrapper.MAX_OPAQUE_COMPILER_SCALAR_TYPEDEFS + 1)
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


def test_mingw_compiler_qualifier_tokens_are_normalized():
    data = (
        b"__extension__ typedef long long ssize_t;\n"
        b"const char *__restrict value;\n"
        b"__volatile__ __signed__ int flag;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b" typedef long long ssize_t;\n"
        b"const char * value;\n"
        b"volatile signed int flag;\n"
    )


def test_mingw_token_replacement_does_not_touch_quoted_literals():
    data = (
        b'const char *a = "__extension__ __restrict";\n'
        b"const int b = '__const__';\n"
    )
    assert wrapper._normalize_builder_output(data) == data


def test_extension_prefix_before_inline_definition_is_removed_with_body():
    data = (
        b"__extension__ extern __inline__ void helper(void)\n"
        b"{\n"
        b"}\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == b"int retained;\n"


def test_mingw_compiler_token_replacement_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_TOKEN_REPLACEMENTS", 1)
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(
            b"__extension__ int first;\n__extension__ int second;\n"
        )


def test_safe_mingw_function_attributes_are_removed():
    data = (
        b"__attribute__ ((dllimport)) void *"
        b"__attribute__((cdecl)) _memccpy(void *dst);\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b" void * _memccpy(void *dst);\n"
    )


def test_safe_attribute_base_names_generate_bare_and_decorated_spellings():
    for name in wrapper.SAFE_MINGW_ATTRIBUTE_BASE_NAMES:
        assert name in wrapper.SAFE_MINGW_ATTRIBUTES
        assert "__" + name + "__" in wrapper.SAFE_MINGW_ATTRIBUTES
    for unsafe in ("aligned", "packed", "mode", "vector_size"):
        assert unsafe not in wrapper.SAFE_MINGW_ATTRIBUTES
        assert "__" + unsafe + "__" not in wrapper.SAFE_MINGW_ATTRIBUTES


def test_nested_safe_mingw_format_attribute_is_removed():
    data = (
        b"int log_value(const char *format) "
        b"__attribute__((__format__(__printf__, 1, 2)));\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"int log_value(const char *format) ;\n"
    )


def test_observed_mingw_diagnostic_and_linkage_attributes_are_removed():
    data = (
        b"void fatal(const char *format) "
        b"__attribute__((__format__(__gnu_printf__, 1, 2), __noreturn__));\n"
        b"int selected __attribute__((__selectany__));\n"
        b"void warned(void) __attribute__((__warning__(\"diagnostic\")));\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"void fatal(const char *format) ;\n"
        b"int selected ;\n"
        b"void warned(void) ;\n"
    )


def test_redundant_crt_max_align_field_attributes_are_removed():
    data = (
        b"long long __max_align_ll __attribute__((__aligned__(8)));\n"
        b"long double __max_align_ld __attribute__((__aligned__(16)));\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"long long __max_align_ll ;\n"
        b"long double __max_align_ld ;\n"
    )


def test_aligned_attribute_outside_crt_max_align_fields_still_fails_closed():
    data = b"long long application_field __attribute__((__aligned__(8)));\n"
    with pytest.raises(wrapper.PreprocessorError, match="__aligned__"):
        wrapper._normalize_builder_output(data)


def test_aligned_winnt_m128a_becomes_a_cffi_partial_struct():
    data = (
        b"typedef struct __attribute__((__aligned__(16))) _M128A {\n"
        b"  unsigned long long Low;\n"
        b"  long long High;\n"
        b"} M128A,*PM128A;\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef struct  _M128A {\n"
        b"    ...;\n"
        b"} M128A,*PM128A;\n"
        b"int retained;\n"
    )


def test_aligned_winnt_nested_state_struct_becomes_partial():
    data = (
        b"typedef struct __attribute__((aligned(16))) _XSAVE_FORMAT {\n"
        b"  union {\n"
        b"    int first;\n"
        b"    int second;\n"
        b"  } values;\n"
        b"} XSAVE_FORMAT;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef struct  _XSAVE_FORMAT {\n"
        b"    ...;\n"
        b"} XSAVE_FORMAT;\n"
    )


def test_aligned_winnt_split_arm64_context_header_becomes_partial():
    data = (
        b"typedef struct __attribute__((__aligned__(16)))\n"
        b"_ARM64_NT_CONTEXT\n"
        b"{\n"
        b"  unsigned long ContextFlags;\n"
        b"  unsigned long long Registers[31];\n"
        b"} ARM64_NT_CONTEXT,*PARM64_NT_CONTEXT;\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef struct \n"
        b"_ARM64_NT_CONTEXT\n"
        b"{\n"
        b"    ...;\n"
        b"} ARM64_NT_CONTEXT,*PARM64_NT_CONTEXT;\n"
        b"int retained;\n"
    )


def test_aligned_winnt_slist_union_becomes_partial():
    data = (
        b"typedef union __attribute__((__aligned__(16))) _SLIST_HEADER {\n"
        b"  unsigned long long Alignment;\n"
        b"  unsigned long long Region;\n"
        b"} SLIST_HEADER,*PSLIST_HEADER;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef union  _SLIST_HEADER {\n"
        b"    ...;\n"
        b"} SLIST_HEADER,*PSLIST_HEADER;\n"
    )


def test_nonaligned_winnt_sizeof_union_becomes_partial():
    data = (
        b"typedef union _DISPATCHER_CONTEXT_NONVOLREG_ARM64 {\n"
        b"  unsigned char Buffer[(11 * sizeof(long long)) + 64];\n"
        b"  struct { long long values[11]; } state;\n"
        b"} DISPATCHER_CONTEXT_NONVOLREG_ARM64;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef union _DISPATCHER_CONTEXT_NONVOLREG_ARM64 {\n"
        b"    ...;\n"
        b"} DISPATCHER_CONTEXT_NONVOLREG_ARM64;\n"
    )


def test_unknown_split_aligned_struct_still_fails_closed():
    data = (
        b"typedef struct __attribute__((__aligned__(16)))\n"
        b"_APPLICATION_CONTEXT\n"
        b"{\n"
        b"  int value;\n"
        b"} APPLICATION_CONTEXT;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="__aligned__"):
        wrapper._normalize_builder_output(data)


def test_opaque_aligned_system_struct_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_OPAQUE_ALIGNED_STRUCTS", 1)
    data = (
        b"typedef struct __attribute__((aligned(16))) _M128A {\n"
        b"  int value;\n"
        b"} M128A;\n"
        b"typedef struct __attribute__((aligned(16))) _XSAVE_FORMAT {\n"
        b"  int value;\n"
        b"} XSAVE_FORMAT;\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


def test_mingw_simd_vector_typedef_becomes_opaque():
    data = (
        b"typedef long long __m64 "
        b"__attribute__((__vector_size__(8), __may_alias__));\n"
        b"typedef long long __m128i "
        b"__attribute__((vector_size(16), may_alias));\n"
        b"typedef long long __m64_u "
        b"__attribute__((__vector_size__(8), __may_alias__, __aligned__(1)));\n"
        b"typedef int __v2si "
        b"__attribute__((__vector_size__(8)));\n"
        b"typedef float __x86_float_u "
        b"__attribute__((__may_alias__, __aligned__(1)));\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __m64;\n"
        b"typedef ... __m128i;\n"
        b"typedef ... __m64_u;\n"
        b"typedef ... __v2si;\n"
        b"typedef ... __x86_float_u;\n"
    )


def test_duplicate_mingw_vector_typedef_is_blank_once():
    data = (
        b"typedef long long __v8di "
        b"__attribute__((__vector_size__(64)));\n"
        b"  typedef  long long  __v8di "
        b"__attribute__((__vector_size__(64)));  \r\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __v8di;\n"
        b"\r\n"
    )


def test_conflicting_mingw_vector_typedef_fails_closed():
    data = (
        b"typedef long long __v8di "
        b"__attribute__((__vector_size__(64)));\n"
        b"typedef long long __v8di "
        b"__attribute__((__vector_size__(32)));\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="conflicting opaque"):
        wrapper._normalize_builder_output(data)


def test_vector_size_attribute_on_non_simd_type_still_fails_closed():
    data = b"typedef int application_vector __attribute__((vector_size(16)));\n"
    with pytest.raises(wrapper.PreprocessorError, match="vector_size"):
        wrapper._normalize_builder_output(data)


def test_internal_vector_typedef_with_multiple_attribute_groups_becomes_opaque():
    data = (
        b"typedef float __compiler_vec "
        b"__attribute__((may_alias)) __attribute__((aligned(1)));\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __compiler_vec;\n"
    )


def test_multiline_internal_vector_typedef_becomes_opaque():
    data = (
        b"typedef int __v2si\n"
        b"  __attribute__((__vector_size__(8)));\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __v2si;\n"
        b"int retained;\n"
    )


def test_multiline_vector_typedef_with_alias_after_attribute_becomes_opaque():
    data = (
        b"typedef int\n"
        b"  __attribute__((__vector_size__(8)))\n"
        b"  __v2si;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __v2si;\n"
    )


def test_one_line_vector_typedef_with_alias_after_attribute_becomes_opaque():
    data = (
        b"typedef int __attribute__((vector_size(8))) __v2si;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"typedef ... __v2si;\n"
    )


def test_multiline_application_vector_typedef_still_fails_closed():
    data = (
        b"typedef int application_vector\n"
        b"  __attribute__((vector_size(8)));\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="vector_size"):
        wrapper._normalize_builder_output(data)


def test_multiline_nonattribute_typedef_is_byte_identical():
    data = b"typedef unsigned long\n  application_word;\n"
    assert wrapper._normalize_builder_output(data) == data


def test_multiline_internal_typedef_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_MULTILINE_TYPEDEFS", 1)
    data = (
        b"typedef int __first\n"
        b"  __attribute__((vector_size(8)));\n"
        b"typedef int __second\n"
        b"  __attribute__((vector_size(8)));\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


def test_opaque_mingw_simd_typedef_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_OPAQUE_SIMD_TYPEDEFS", 1)
    data = (
        b"typedef long long __m64 __attribute__((vector_size(8)));\n"
        b"typedef long long __m128 __attribute__((vector_size(16)));\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


def test_layout_affecting_mingw_attribute_fails_closed():
    data = b"typedef int packed_int __attribute__((__packed__));\n"
    with pytest.raises(
        wrapper.PreprocessorError,
        match=r"__packed__; context: __packed__,packed_int",
    ):
        wrapper._normalize_builder_output(data)


def test_unsupported_attribute_diagnostic_lists_bounded_sorted_identifiers():
    data = b"int value __attribute__((zeta, alpha));\n"
    with pytest.raises(
        wrapper.PreprocessorError,
        match=(
            r"unsupported MinGW attribute in cdef output: alpha,zeta; "
            r"context: alpha,value,zeta"
        ),
    ):
        wrapper._normalize_builder_output(data)


def test_unsupported_attribute_diagnostic_includes_neighbor_identifiers():
    lines = [
        b"typedef int __vector_prefix\n",
        b"__attribute__((vector_size(8)))\n",
        b"__vector_suffix;\n",
    ]
    with pytest.raises(
        wrapper.PreprocessorError,
        match=(
            r"vector_size; context: __vector_prefix,__vector_suffix,"
            r"vector_size"
        ),
    ):
        wrapper._strip_mingw_attributes(lines)


def test_mingw_attribute_marker_inside_string_is_unchanged():
    data = b'const char *text = "__attribute__((__cdecl__))";\n'
    assert wrapper._normalize_builder_output(data) == data


def test_unterminated_mingw_attribute_fails_closed():
    data = b"void value(void) __attribute__((__cdecl__);\n"
    with pytest.raises(wrapper.PreprocessorError, match="unterminated"):
        wrapper._normalize_builder_output(data)


def test_mingw_attribute_replacement_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_ATTRIBUTE_REPLACEMENTS", 1)
    data = (
        b"void first(void) __attribute__((__cdecl__));\n"
        b"void second(void) __attribute__((__cdecl__));\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


def test_mingw_inline_declaration_drops_only_the_extension_token():
    data = b"extern __inline__ void declaration_only(void);\n"
    assert wrapper._normalize_builder_output(data) == (
        b"extern  void declaration_only(void);\n"
    )


def test_mingw_inline_definition_is_removed_with_asm_string_braces():
    data = (
        b"void __debugbreak(void);\n"
        b"extern __inline__ void __debugbreak(void)\n"
        b"{\n"
        b'  __asm__ __volatile__("int {$}3":);\n'
        b"}\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"void __debugbreak(void);\nint retained;\n"
    )


def test_mingw_inline_definition_handles_nested_code_braces():
    data = (
        b"static __inline int helper(int value)\n"
        b"{\n"
        b"  if (value) {\n"
        b"    return 1;\n"
        b"  }\n"
        b"  return 0;\n"
        b"}\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == b"int retained;\n"


def test_mingw_inline_definition_accepts_token_at_end_of_line():
    data = (
        b"extern __inline__\n"
        b"void intrinsic(void)\n"
        b"{\n"
        b"  return;\n"
        b"}\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == b"int retained;\n"


def test_mingw_inline_declaration_accepts_crlf_after_token():
    data = (
        b"static\t__inline__\r\n"
        b"void declaration(void);\r\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"static\t\r\n"
        b"void declaration(void);\r\n"
    )


def test_inline_body_attributes_are_removed_with_the_definition():
    data = (
        b"extern __inline__ __m128 shuffle(__m128 __A, __m128 __B)\n"
        b"{\n"
        b"  typedef float __v4sf __attribute__((__vector_size__(16)));\n"
        b"  return (__m128) __builtin_shuffle((__v4sf) __A, (__v4sf) __B);\n"
        b"}\n"
        b"int retained;\n"
    )
    assert wrapper._normalize_builder_output(data) == b"int retained;\n"


def test_inline_declaration_attributes_are_sanitized_after_token_removal():
    data = (
        b"extern __inline__ void declaration(void) "
        b"__attribute__((__nothrow__));\n"
    )
    assert wrapper._normalize_builder_output(data) == (
        b"extern  void declaration(void) ;\n"
    )


def test_unterminated_mingw_inline_definition_fails_closed():
    data = b"extern __inline__ void broken(void)\n{\n"
    with pytest.raises(wrapper.PreprocessorError, match="unterminated"):
        wrapper._normalize_builder_output(data)


def test_mingw_inline_definition_count_is_bounded(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_MINGW_INLINE_BLOCKS", 1)
    data = (
        b"extern __inline__ void first(void) {}\n"
        b"extern __inline__ void second(void) {}\n"
    )
    with pytest.raises(wrapper.PreprocessorError, match="too many"):
        wrapper._normalize_builder_output(data)


@pytest.mark.parametrize(
    ("stream", "message"),
    [
        ("stdout", "preprocessed output exceeds"),
        ("stderr", "preprocessor stderr exceeds"),
    ],
)
def test_bounded_subprocess_terminates_on_pipe_overflow(
    monkeypatch, stream, message
):
    monkeypatch.setattr(wrapper, "MAX_PREPROCESSED_BYTES", 1024)
    monkeypatch.setattr(wrapper, "MAX_PREPROCESSOR_STDERR_BYTES", 1024)
    target = "stdout" if stream == "stdout" else "stderr"
    script = (
        "import sys,time;"
        f"stream=sys.{target}.buffer;"
        "stream.write(b'x'*1048576);stream.flush();time.sleep(30)"
    )
    with pytest.raises(wrapper.PreprocessorError, match=message):
        wrapper._run_bounded_subprocess([sys.executable, "-c", script])


def test_bounded_subprocess_preserves_both_streams():
    script = (
        "import sys;"
        "sys.stdout.buffer.write(b'out');"
        "sys.stderr.buffer.write(b'err')"
    )
    completed = wrapper._run_bounded_subprocess(
        [sys.executable, "-c", script]
    )
    assert completed.returncode == 0
    assert completed.stdout == b"out"
    assert completed.stderr == b"err"


def test_bounded_subprocess_retains_resistant_child(monkeypatch):
    monkeypatch.setattr(wrapper, "MAX_PREPROCESSED_BYTES", 1024)
    retained = []

    def fake_retain(process, threads):
        retained.append(process)
        process.kill()
        process.wait()
        for thread in threads:
            thread.join(timeout=5)

    monkeypatch.setattr(
        wrapper,
        "_terminate_preprocessor",
        lambda process: False,
    )
    monkeypatch.setattr(
        wrapper,
        "_retain_preprocessor_for_reaping",
        fake_retain,
    )
    script = (
        "import sys,time;"
        "sys.stdout.buffer.write(b'x'*1048576);"
        "sys.stdout.buffer.flush();time.sleep(30)"
    )
    with pytest.raises(wrapper.PreprocessorError, match="reaper retained"):
        wrapper._run_bounded_subprocess([sys.executable, "-c", script])
    assert len(retained) == 1


def test_preprocessor_reaper_does_not_wait_forever_for_stuck_drain(
    monkeypatch
):
    monkeypatch.setattr(
        wrapper,
        "PREPROCESSOR_REAPER_JOIN_ATTEMPTS",
        2,
    )
    monkeypatch.setattr(
        wrapper,
        "PREPROCESSOR_REAPER_JOIN_TIMEOUT_SECONDS",
        0.01,
    )
    monkeypatch.setattr(
        wrapper,
        "PREPROCESSOR_REAPER_BACKOFF_SECONDS",
        0.01,
    )

    class ExitedProcess:
        def poll(self):
            return 0

    release = wrapper.threading.Event()
    stuck = wrapper.threading.Thread(
        target=release.wait,
        daemon=True,
    )
    stuck.start()
    process = ExitedProcess()
    wrapper._retain_preprocessor_for_reaping(process, (stuck,))

    deadline = wrapper.threading.Event()
    for _ in range(100):
        with wrapper._RETAINED_PREPROCESSORS_LOCK:
            retained = process in wrapper._RETAINED_PREPROCESSORS
        if not retained:
            break
        deadline.wait(0.01)
    release.set()
    stuck.join(timeout=1)

    assert retained is False


def test_successful_preprocessor_output_size_is_bounded(monkeypatch, capsysbinary):
    monkeypatch.setattr(wrapper, "MAX_PREPROCESSED_BYTES", 3)
    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda command: _FakeCompleted(
            returncode=0, stdout=b"four"
        ),
    )
    with pytest.raises(wrapper.PreprocessorError, match="size bound"):
        wrapper._run_real_preprocessor(["lib.h"])
    assert capsysbinary.readouterr().out == b""


# --------------------------------------------------------------------------- #
# main(): pass-through for non-_builder.h inputs (notably lib.h)
# --------------------------------------------------------------------------- #
def test_lib_h_passes_through_completely_unchanged(monkeypatch, capsysbinary):
    recorded = {}

    def fake_run(command):
        recorded["command"] = command
        return _FakeCompleted(returncode=0, stdout=b"PREPROCESSED", stderr=b"")

    monkeypatch.setattr(wrapper, "_run_bounded_subprocess", fake_run)
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)

    argv = ["-P", "-fno-inline", "-Iinclude", "lib.h"]
    assert wrapper.main(argv) == 0
    assert recorded["command"] == ["cc", "-E"] + argv
    captured = capsysbinary.readouterr()
    assert captured.out == b"PREPROCESSED"


def test_pass_through_never_reads_the_expected_env_var(monkeypatch):
    # A non-_builder.h invocation must succeed even if the expected canonical
    # path env var is entirely unset -- the env var is only consulted once the
    # final argument is actually named _builder.h.
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda command: _FakeCompleted(returncode=0),
    )
    assert wrapper.main(["-Iinclude", "lib.h"]) == 0


# --------------------------------------------------------------------------- #
# main(): the exact pinned _builder.h path
# --------------------------------------------------------------------------- #
def test_wrong_located_builder_h_is_rejected(monkeypatch, tmp_path, capsysbinary):
    real = tmp_path / "real" / "_builder.h"
    real.parent.mkdir()
    real.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")

    decoy = tmp_path / "decoy" / "_builder.h"
    decoy.parent.mkdir()
    decoy.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")

    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(real))

    called = []
    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda *a, **k: called.append(1) or _FakeCompleted(),
    )

    assert wrapper.main(["-Iinclude", str(decoy)]) == 1
    assert not called, "the real preprocessor must never run on a rejected input"
    captured = capsysbinary.readouterr()
    assert b"does not match the expected" in captured.err


def test_missing_expected_env_var_fails_closed_for_builder_h(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    monkeypatch.setattr(
        wrapper, "_run_bounded_subprocess", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1
    captured = capsysbinary.readouterr()
    assert wrapper.ENV_EXPECTED_BUILDER_H.encode() in captured.err


def test_no_args_fails_closed(capsysbinary):
    assert wrapper.main([]) == 1
    captured = capsysbinary.readouterr()
    assert b"no preprocessor arguments" in captured.err


def test_source_drift_on_the_real_builder_h_fails_closed(monkeypatch, tmp_path):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text("int a;\n", encoding="utf-8")  # old line missing
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setattr(
        wrapper, "_run_bounded_subprocess", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1


def test_exact_rewrite_end_to_end_uses_a_temp_copy_outside_the_source_tree_and_cleans_up(
    monkeypatch, tmp_path, capsysbinary
):
    src_dir = tmp_path / "src"
    src_dir.mkdir()
    builder_h = src_dir / "_builder.h"
    builder_h.write_text(f"int a;\n{wrapper.OLD_BUILDER_LINE}\nint b;\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(src_dir))

    seen = {}

    def fake_run(command):
        temp_path = command[-1]
        seen["temp_path"] = temp_path
        seen["command"] = command
        with open(temp_path, "r", encoding="utf-8") as handle:
            seen["temp_contents"] = handle.read()
        return _FakeCompleted(returncode=0, stdout=b"OK", stderr=b"")

    monkeypatch.setattr(wrapper, "_run_bounded_subprocess", fake_run)

    rc = wrapper.main(["-P", "-fno-inline", "-Iinclude", str(builder_h)])
    assert rc == 0

    # The real header path was swapped for a temp copy; every other argument
    # (and their order) is preserved.
    assert seen["command"][:-1] == ["cc", "-E", "-P", "-fno-inline", "-Iinclude"]
    temp_path = seen["temp_path"]
    assert temp_path != str(builder_h)
    assert not str(temp_path).startswith(str(src_dir))
    assert wrapper.NEW_BUILDER_LINE in seen["temp_contents"]
    assert wrapper.OLD_BUILDER_LINE not in seen["temp_contents"]

    # The temp file must be removed after the run (in a `finally`).
    assert not os.path.exists(temp_path)

    captured = capsysbinary.readouterr()
    assert captured.out == b"OK"


def test_temp_file_is_cleaned_up_even_when_the_real_preprocessor_fails(
    monkeypatch, tmp_path
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    seen = {}

    def fake_run(command):
        seen["temp_path"] = command[-1]
        return _FakeCompleted(returncode=1, stdout=b"", stderr=b"boom")

    monkeypatch.setattr(wrapper, "_run_bounded_subprocess", fake_run)

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 1
    assert not os.path.exists(seen["temp_path"])


def test_bounded_read_rejects_oversized_input(monkeypatch, tmp_path, capsysbinary):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_bytes(b"x" * (wrapper.MAX_BUILDER_H_BYTES + 1))
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setattr(
        wrapper, "_run_bounded_subprocess", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1
    captured = capsysbinary.readouterr()
    assert b"bounded" in captured.err


def test_non_utf8_input_is_rejected(monkeypatch, tmp_path):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_bytes(b"\xff\xfe\x00#define va_list void*")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setattr(
        wrapper, "_run_bounded_subprocess", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1


# --------------------------------------------------------------------------- #
# main(): source-tree containment proof (hostile/misconfigured TMPDIR)
# --------------------------------------------------------------------------- #
def test_missing_source_root_env_fails_closed(monkeypatch, tmp_path, capsysbinary):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    # ENV_SOURCE_ROOT is deliberately left unset by the autouse fixture.
    monkeypatch.setattr(
        wrapper, "_run_bounded_subprocess", lambda *a, **k: pytest.fail("must not run")
    )

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 1
    captured = capsysbinary.readouterr()
    assert wrapper.ENV_SOURCE_ROOT.encode() in captured.err


def test_hostile_tmpdir_placement_inside_source_root_is_rejected_and_cleaned_up(
    monkeypatch, tmp_path, capsysbinary
):
    src_dir = tmp_path / "src"
    src_dir.mkdir()
    builder_h = src_dir / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(src_dir))

    # Simulate a hostile/misconfigured TMPDIR that lands the "temporary"
    # overlay copy INSIDE the pinned source tree -- this must be refused,
    # even though everything else about the input is otherwise valid.
    hostile_path = str(src_dir / "evil-overlay.h")

    def fake_write_temp_copy(text):
        with open(hostile_path, "w", encoding="utf-8") as handle:
            handle.write(text)
        return hostile_path

    monkeypatch.setattr(wrapper, "_write_temp_copy", fake_write_temp_copy)
    monkeypatch.setattr(
        wrapper, "_run_bounded_subprocess", lambda *a, **k: pytest.fail("must not run")
    )

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 1
    assert not os.path.exists(hostile_path), (
        "a rejected in-tree overlay copy must still be deleted, not left behind"
    )
    captured = capsysbinary.readouterr()
    assert b"inside the pinned" in captured.err


def test_source_root_itself_as_the_temp_location_is_also_rejected():
    # Edge case: the temp file lands EXACTLY at the source root path (not
    # just somewhere nested under it) -- containment must still hold.
    assert wrapper._path_is_within("/some/src", "/some/src") is True
    assert wrapper._path_is_within("/some/src/nested", "/some/src") is True
    assert wrapper._path_is_within("/some/other", "/some/src") is False


# --------------------------------------------------------------------------- #
# main(): fail-closed temp cleanup (never a silently swallowed OSError)
# --------------------------------------------------------------------------- #
def test_cleanup_failure_after_successful_preprocessing_fails_closed(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda command: _FakeCompleted(returncode=0, stdout=b"OK"),
    )
    monkeypatch.setattr(
        wrapper,
        "_remove_temp",
        lambda path: (_ for _ in ()).throw(OSError("boom")),
    )

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 1, "a cleanup failure must fail closed even though the compiler succeeded"
    captured = capsysbinary.readouterr()
    # The compiler's own (successful) output is still relayed exactly.
    assert captured.out == b"OK"
    assert b"failed to remove the temporary" in captured.err


def test_cleanup_failure_preserves_the_original_nonzero_compiler_exit(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda command: _FakeCompleted(
            returncode=7, stdout=b"", stderr=b"compiler boom"
        ),
    )
    monkeypatch.setattr(
        wrapper,
        "_remove_temp",
        lambda path: (_ for _ in ()).throw(OSError("cleanup boom")),
    )

    rc = wrapper.main(["-Iinclude", str(builder_h)])
    assert rc == 7, (
        "the original compiler failure code must be preserved, never masked "
        "by an unrelated cleanup failure"
    )
    captured = capsysbinary.readouterr()
    assert b"failed to remove the temporary" in captured.err


def test_subprocess_is_never_invoked_via_a_shell(monkeypatch, tmp_path):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(wrapper.OLD_BUILDER_LINE + "\n", encoding="utf-8")
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))

    def fake_run(command):
        assert isinstance(command, list)
        assert all(isinstance(part, str) for part in command)
        return _FakeCompleted(returncode=0)

    monkeypatch.setattr(wrapper, "_run_bounded_subprocess", fake_run)
    assert wrapper.main(["-Iinclude", str(builder_h)]) == 0


def test_nonzero_compiler_exit_is_propagated(monkeypatch):
    monkeypatch.setattr(
        wrapper,
        "_run_bounded_subprocess",
        lambda command: _FakeCompleted(returncode=7),
    )
    monkeypatch.delenv(wrapper.ENV_EXPECTED_BUILDER_H, raising=False)
    assert wrapper.main(["-Iinclude", "lib.h"]) == 7


def test_builder_preprocessor_output_normalizes_builtin_va_list(
    monkeypatch, tmp_path, capsysbinary
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(
        wrapper.OLD_BUILDER_LINE + "\n" + wrapper.LIMITS_INCLUDE_LINE + "\n",
        encoding="utf-8",
    )
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "1")
    def fake_run(command):
        overlay = command[-1]
        marker = overlay.replace("\\", "/")
        return _FakeCompleted(
            returncode=0,
            stdout=(
                f'# 1 "{marker}"\n'.encode()
                + wrapper.LIMITS_BLOCK_BEGIN_LINE.encode("ascii")
                + b"\n"
                + wrapper.LIMITS_BLOCK_END_LINE.encode("ascii")
                + b"\n"
                + b"typedef __builtin_va_list __gnuc_va_list;\n"
            ),
        )

    monkeypatch.setattr(wrapper, "_run_bounded_subprocess", fake_run)

    assert wrapper.main(["-Iinclude", str(builder_h)]) == 0
    assert capsysbinary.readouterr().out == b"typedef ... __gnuc_va_list;\n"


def test_mingw_main_preprocesses_a_scoped_attribute_sanitizer(
    monkeypatch, tmp_path
):
    builder_h = tmp_path / "_builder.h"
    builder_h.write_text(
        wrapper.OLD_BUILDER_LINE + "\n"
        + wrapper.LIMITS_INCLUDE_LINE + "\n"
        + "#include <mgba/flags.h>\n",
        encoding="utf-8",
    )
    monkeypatch.setenv(wrapper.ENV_EXPECTED_BUILDER_H, str(builder_h))
    monkeypatch.setenv(wrapper.ENV_SOURCE_ROOT, str(tmp_path))
    monkeypatch.setenv(wrapper.ENV_MINGW_CDEF, "1")
    seen = {}

    def fake_run(command):
        with open(command[-1], "r", encoding="utf-8") as handle:
            seen["text"] = handle.read()
        seen["command"] = command
        marker = command[-1].replace("\\", "/")
        return _FakeCompleted(
            returncode=0,
            stdout=(
                f'# 1 "{marker}"\n'.encode()
                + wrapper.LIMITS_BLOCK_BEGIN_LINE.encode("ascii")
                + b"\n"
                + wrapper.LIMITS_BLOCK_END_LINE.encode("ascii")
                + b"\n"
            ),
        )

    monkeypatch.setattr(wrapper, "_run_bounded_subprocess", fake_run)
    assert wrapper.main(["-Iinclude", str(builder_h)]) == 0
    assert wrapper.MINGW_WINVER_DEFINE in seen["command"]
    assert "-P" not in seen["command"]
    assert (
        wrapper.ATTRIBUTE_DISABLE_LINE + "\n"
        + wrapper.INTRIN_GUARD_DISABLE_LINE + "\n"
        + wrapper.LIMITS_INCLUDE_LINE + "\n"
        + wrapper.INTRIN_GUARD_RESTORE_LINE + "\n"
        + wrapper.ATTRIBUTE_RESTORE_LINE + "\n"
    ) in seen["text"]
