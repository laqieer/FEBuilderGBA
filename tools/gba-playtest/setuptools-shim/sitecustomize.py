"""Build-only MinGW fixes for pinned mGBA's deprecated Python binding."""

import os
import sys


if os.name == "nt":
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
    _build_temp = os.environ.get(
        "FEBUILDERGBA_MGBA_SETUPTOOLS_TEMP", ""
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
