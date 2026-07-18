"""FEBuilderGBA deterministic headless playtest engine.

A dependency-free (standard-library only) scenario runner that drives the pinned
mGBA 0.10.5 Python binding through a strict, data-only JSON scenario and emits a
single machine-readable JSON verdict.

The mGBA binding is an optional, explicitly bootstrapped native dependency; this
package never downloads or installs anything at runtime.
"""

from __future__ import annotations

from .model import (
    SCHEMA_VERSION,
    Scenario,
    ScenarioError,
    build_scenario,
    load_scenario,
    parse_json,
)
from .runner import (
    RESULT_SCHEMA_VERSION,
    STATUS_EXIT_CODES,
    Backend,
    BackendError,
    Runner,
    canonical_json,
)

__version__ = "0.1.0"

__all__ = [
    "SCHEMA_VERSION",
    "RESULT_SCHEMA_VERSION",
    "STATUS_EXIT_CODES",
    "Scenario",
    "ScenarioError",
    "Backend",
    "BackendError",
    "Runner",
    "build_scenario",
    "load_scenario",
    "parse_json",
    "canonical_json",
    "__version__",
]
