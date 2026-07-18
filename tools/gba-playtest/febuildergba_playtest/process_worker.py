"""POSIX launcher that establishes a process group before target execution."""

from __future__ import annotations

import os
import sys


def main() -> int:
    if len(sys.argv) < 2:
        return 127
    if os.name != "nt":
        os.setsid()
    os.execvp(sys.argv[1], sys.argv[1:])
    return 127


if __name__ == "__main__":
    raise SystemExit(main())
