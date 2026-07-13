#!/usr/bin/env python3
"""Cross-platform launcher for the FEBuilderGBA MCP stdio server (issue #1942).

Works without an editable/pip install: it bootstraps ``sys.path`` to include
this directory (``agent-harness/``) so ``cli_anything.febuildergba`` resolves,
then defers to the same dependency-free JSON-RPC stdio loop that is installed
as the ``cli-anything-febuildergba-mcp`` console script (see ``setup.py``).

Usage:
    python agent-harness/febuildergba_mcp.py [--session-file PATH]

Referenced from ``.mcp.json`` as:
    "command": "python", "args": ["./agent-harness/febuildergba_mcp.py"]
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from cli_anything.febuildergba.mcp_server import main  # noqa: E402


if __name__ == "__main__":
    main()
