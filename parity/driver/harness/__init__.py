"""The parity driver: records Parity Scenario goldens from the unmodified Reference Implementation (ADR-0004).

The driver runs only on a developer machine, never in CI, and is deleted at cutover. See ``parity/README.md``.
"""

from pathlib import Path

# parity/driver/harness/__init__.py -> the repo root is three folders up from this package.
REPO_ROOT = Path(__file__).resolve().parents[3]
SRC = REPO_ROOT / "src"
PARITY = REPO_ROOT / "parity"
SCENARIOS = PARITY / "scenarios"
