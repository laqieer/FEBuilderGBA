"""Build-only MinGW fixes for pinned mGBA's deprecated Python binding."""

import os
import sys


if os.name == "nt":
    _build_temp = os.environ.get(
        "FEBUILDERGBA_MGBA_SETUPTOOLS_TEMP", ""
    )
    _dllimport_mode = os.environ.get(
        "FEBUILDERGBA_MGBA_MINGW_DLLIMPORT", "0"
    )
    if _dllimport_mode not in ("0", "1"):
        raise RuntimeError("invalid MinGW dllimport overlay selector")

    if _dllimport_mode == "1":
        import cffi

        _dllimport_marker = os.environ.get(
            "FEBUILDERGBA_MGBA_DLLIMPORT_MARKER", ""
        )
        _empty_export = (
            "\n#define static\n"
            "#define inline\n"
            "#define MGBA_EXPORT\n"
            "#include <mgba/flags.h>\n"
        )
        _dllimport_export = (
            "\n#define static\n"
            "#define inline\n"
            "#define MGBA_EXPORT_H\n"
            "#define MGBA_EXPORT __declspec(dllimport)\n"
            "#include <mgba/flags.h>\n"
        )
        _marker_text = (
            "mgba._pylib MGBA_EXPORT=__declspec(dllimport)\n"
        )
        _original_set_source = cffi.FFI.set_source

        def _set_source(self, module_name, source, *args, **kwargs):
            if module_name != "mgba._pylib":
                return _original_set_source(
                    self, module_name, source, *args, **kwargs
                )
            if not isinstance(source, str) or source.count(_empty_export) != 1:
                raise RuntimeError(
                    "pinned mGBA CFFI export preamble drifted"
                )
            if not _dllimport_marker or not os.path.isabs(
                _dllimport_marker
            ):
                raise RuntimeError(
                    "dllimport overlay marker path must be absolute"
                )
            marker_parent = os.path.dirname(_dllimport_marker)
            if not os.path.isdir(marker_parent):
                raise RuntimeError(
                    "dllimport overlay marker parent is missing"
                )
            if not _build_temp or not os.path.isabs(_build_temp):
                raise RuntimeError(
                    "setuptools temp path must be absolute"
                )
            expected_marker_parent = os.path.dirname(
                os.path.realpath(_build_temp)
            )
            if os.path.normcase(os.path.realpath(marker_parent)) != (
                os.path.normcase(expected_marker_parent)
            ):
                raise RuntimeError(
                    "dllimport overlay marker must share the build root"
                )
            if os.path.lexists(_dllimport_marker) and os.path.islink(
                _dllimport_marker
            ):
                raise RuntimeError(
                    "refusing symlinked dllimport overlay marker"
                )

            patched_source = source.replace(
                _empty_export, _dllimport_export, 1
            )
            result = _original_set_source(
                self, module_name, patched_source, *args, **kwargs
            )

            marker_temp = (
                f"{_dllimport_marker}.{os.getpid()}.tmp"
            )
            try:
                with open(
                    marker_temp,
                    "x",
                    encoding="ascii",
                    newline="\n",
                ) as handle:
                    handle.write(_marker_text)
                os.replace(marker_temp, _dllimport_marker)
            finally:
                if os.path.exists(marker_temp):
                    os.remove(marker_temp)
            return result

        cffi.FFI.set_source = _set_source

    from distutils.command.build_ext import build_ext as DistutilsBuildExt
    from distutils.compilers.C.cygwin import (
        MinGW32Compiler as DistutilsMinGW32Compiler,
    )
    from setuptools.command.build_ext import build_ext as SetuptoolsBuildExt
    from setuptools._distutils.compilers.C.cygwin import MinGW32Compiler

    def _no_runtime_library_dir(_self, _directory):
        return []

    MinGW32Compiler.runtime_library_dir_option = _no_runtime_library_dir
    DistutilsMinGW32Compiler.runtime_library_dir_option = (
        _no_runtime_library_dir
    )

    _python_library = (
        f"python{sys.version_info.major}.{sys.version_info.minor}"
    )
    _disable_runtime_pseudo_reloc = (
        "-Wl,--disable-runtime-pseudo-reloc"
    )

    def _patch_build_ext(command_type):
        original_build_extension = command_type.build_extension
        original_get_libraries = command_type.get_libraries

        def build_extension(self, extension):
            if _build_temp:
                if not os.path.isabs(_build_temp):
                    raise RuntimeError(
                        "setuptools temp path must be absolute"
                    )
                os.makedirs(_build_temp, exist_ok=True)
                self.build_temp = _build_temp
            libraries = list(extension.libraries or [])
            if _python_library not in libraries:
                libraries.append(_python_library)
                extension.libraries = libraries
            if (
                _dllimport_mode == "1"
                and extension.name == "mgba._pylib"
            ):
                link_args = list(extension.extra_link_args or [])
                if _disable_runtime_pseudo_reloc not in link_args:
                    link_args.append(_disable_runtime_pseudo_reloc)
                    extension.extra_link_args = link_args
            return original_build_extension(self, extension)

        def get_libraries(self, extension):
            libraries = list(original_get_libraries(self, extension))
            if _python_library not in libraries:
                libraries.append(_python_library)
            return libraries

        command_type.build_extension = build_extension
        command_type.get_libraries = get_libraries

    _patch_build_ext(SetuptoolsBuildExt)
    if DistutilsBuildExt is not SetuptoolsBuildExt:
        _patch_build_ext(DistutilsBuildExt)
