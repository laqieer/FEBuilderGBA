"""Pytest bootstrap: make the package and test helpers importable without install.

Adds the ``tools/gba-playtest`` directory (containing ``febuildergba_playtest``)
and the ``tests`` directory (containing ``synthetic_gba``) to ``sys.path`` so the
dependency-free suite runs from a plain checkout.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_TESTS = os.path.join(_HERE, "tests")

for path in (_HERE, _TESTS):
    if path not in sys.path:
        sys.path.insert(0, path)
