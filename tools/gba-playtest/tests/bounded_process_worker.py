"""Windows target launcher gated until the parent assigns its Job Object."""

from __future__ import annotations

import ctypes
import subprocess
import sys
from ctypes import wintypes


def main() -> int:
    if len(sys.argv) < 2:
        return 127
    if sys.stdin.buffer.read(1) != b"\x01":
        return 127
    try:
        process = subprocess.Popen(
            sys.argv[1:],
            stdin=subprocess.DEVNULL,
        )
    except OSError:
        return 127
    return process.wait()


if __name__ == "__main__":
    exit_code = main()
    if sys.platform == "win32":
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.ExitProcess.argtypes = [wintypes.UINT]
        kernel32.ExitProcess.restype = None
        kernel32.ExitProcess(exit_code & 0xFFFFFFFF)
    raise SystemExit(exit_code)
