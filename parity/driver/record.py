"""Records Parity Scenario goldens from the unmodified Reference Implementation (ADR-0004).

Usage, from the repo root (``uv sync`` first; Python 3.14, Windows, NTFS temp folder):

    uv run python parity/driver/record.py                      # every scenario
    uv run python parity/driver/record.py settings/*           # glob over <module>/<scenario> IDs
    uv run python parity/driver/record.py --check settings/*   # re-run and fail if a golden would change

Each scenario runs in a fresh child process with ``PYTHONHASHSEED=0``, because the Reference Implementation keeps
module-level state and iterates string sets in hash order (T-15). Goldens are re-recorded only before cutover, through
a "mirror this change" issue; CI never runs this.
"""

import argparse
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

from harness import REPO_ROOT
from harness.golden import GoldenContext, dumps
from harness.scenario import discover, load, materialize


def _tokenize(value: Any, golden: GoldenContext) -> Any:
	"""Replaces the temp root inside every string of a recorded dialog or HTTP call."""
	if isinstance(value, str):
		return golden.text(value)
	if isinstance(value, list):
		return [_tokenize(v, golden) for v in value]
	if isinstance(value, dict):
		return {k: _tokenize(v, golden) for k, v in value.items()}
	return value


def run_child(scenario_id: str) -> str:
	"""Runs one scenario in this (child) process and returns its golden text."""
	if os.environ.get("PYTHONHASHSEED") != "0" or sys.flags.hash_randomization:
		msg = "Goldens must be recorded with PYTHONHASHSEED=0; run record.py without --child"
		raise SystemExit(msg)

	# Imported here so that the parent process never imports the Reference Implementation.
	from harness.operations import OPERATIONS  # noqa: PLC0415
	from harness.reference import Session  # noqa: PLC0415

	scenario = load(scenario_id)
	if scenario.operation not in OPERATIONS:
		msg = f"{scenario_id}: unknown operation {scenario.operation!r}; known: {', '.join(sorted(OPERATIONS))}"
		raise SystemExit(msg)

	machine = materialize(scenario)
	session: Session | None = None
	try:
		session = Session(machine, "record")
		session.start()
		golden = GoldenContext(machine.root)
		result = OPERATIONS[scenario.operation](session, golden)
		if session.dialogs.unused:
			msg = f"{scenario_id}: scripted dialog answers were never asked for: {session.dialogs.unused}"
			raise SystemExit(msg)
		# Every dialog shown and every HTTP request made is part of the result, when there are any.
		if session.dialogs.calls:
			result["dialogs"] = _tokenize(session.dialogs.calls, golden)
		if session.http.calls:  # type: ignore[attr-defined]
			result["httpRequests"] = _tokenize(session.http.calls, golden)  # type: ignore[attr-defined]
		return dumps(result)
	finally:
		if session is not None and session.root is not None:
			session.root.destroy()
		os.chdir(REPO_ROOT)
		machine.cleanup()


def main() -> int:
	"""Records (or, with ``--check``, verifies) the selected scenarios."""
	parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	parser.add_argument("patterns", nargs="*", help="glob patterns over <module>/<scenario> IDs (default: all)")
	parser.add_argument("--check", action="store_true", help="fail if any golden would change; write nothing")
	parser.add_argument("--child", metavar="ID", help=argparse.SUPPRESS)
	args = parser.parse_args()

	if args.child:
		sys.stdout.write(run_child(args.child))
		return 0

	scenarios = discover(args.patterns or None)
	if not scenarios:
		sys.stderr.write("No scenario matches.\n")
		return 1

	env = {**os.environ, "PYTHONHASHSEED": "0", "PYTHONUTF8": "1"}
	failed = 0
	for scenario in scenarios:
		child = subprocess.run(
			[sys.executable, str(Path(__file__).resolve()), "--child", scenario.id],
			capture_output=True,
			text=True,
			encoding="utf-8",
			env=env,
			check=False,
		)
		if child.returncode != 0:
			failed += 1
			sys.stderr.write(f"FAILED  {scenario.id}\n{child.stderr}\n")
			continue

		current = scenario.golden_path.read_text("utf-8") if scenario.golden_path.is_file() else None
		if args.check:
			changed = current != child.stdout
			failed += changed
			sys.stdout.write(f"{'CHANGED ' if changed else 'same    '}{scenario.id}\n")
		else:
			# newline="\n": goldens are written with LF whatever the platform, so re-recording is byte-stable.
			scenario.golden_path.write_text(child.stdout, "utf-8", newline="\n")
			sys.stdout.write(f"{'same    ' if current == child.stdout else 'written '}{scenario.id}\n")

	return 1 if failed else 0


if __name__ == "__main__":
	sys.exit(main())
