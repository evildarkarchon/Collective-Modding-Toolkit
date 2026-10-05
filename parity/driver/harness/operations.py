"""The operations a scenario can name in ``scenario.json``: each runs one path of the Reference Implementation and
returns its golden. A C# test runs the matching Core or App call and compares against that golden.

To add one, write a function taking the started ``Session`` and a ``GoldenContext`` and returning a JSON-able dict,
and register it with ``@operation("<name>")``. Read results that only exist after the GUI is built off the widgets,
not by re-deriving them (ADR-0004).
"""

import logging
import sys
from collections.abc import Callable
from typing import Any

from harness.golden import GoldenContext
from harness.reference import Session

Operation = Callable[[Session, GoldenContext], dict[str, Any]]
OPERATIONS: dict[str, Operation] = {}


def operation(name: str) -> Callable[[Operation], Operation]:
	"""Registers an operation under ``name``."""

	def register(function: Operation) -> Operation:
		OPERATIONS[name] = function
		return function

	return register


class _Capture(logging.Handler):
	"""Collects every log record, at every level."""

	def __init__(self) -> None:
		super().__init__(logging.DEBUG)
		self.records: list[logging.LogRecord] = []

	def emit(self, record: logging.LogRecord) -> None:
		self.records.append(record)


def capture_logs() -> _Capture:
	"""Starts capturing the root logger at DEBUG, so records below the app's configured level are seen too."""
	handler = _Capture()
	root = logging.getLogger()
	root.setLevel(logging.DEBUG)
	root.addHandler(handler)
	return handler


def log_lines(records: list[logging.LogRecord], logger_name: str) -> list[dict[str, Any]]:
	"""The rendered log lines one logger wrote. Tracebacks are left out; only the fact of one is recorded, since the
	error type and wording are not part of Behaviour Parity (GLOSSARY.md, Failure Outcome).
	"""
	lines = []
	for record in records:
		if record.name != logger_name:
			continue
		line: dict[str, Any] = {"level": record.levelname, "message": record.getMessage()}
		if record.exc_info:
			line["exception"] = True
		lines.append(line)
	return lines


@operation("download-source")
def download_source(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""SET-2: the Download Source lookup, which ``app_settings`` runs at import time against ``sys._MEIPASS``.

	The outcome is read from which of the three log calls fired; ``rawValue`` is the logged (stripped) value, which
	is ``None`` when the read failed.
	"""
	if "app_settings" in sys.modules:
		msg = "app_settings was imported before the operation; the lookup must run at this import"
		raise RuntimeError(msg)

	capture = capture_logs()
	import app_settings  # noqa: PLC0415

	records = [r for r in capture.records if r.name == "app_settings"]
	outcomes = {
		"Settings : Download source: '%s'": "valid",
		"Settings : Invalid download source: '%s'": "invalid",
		"Settings : Failed to detect download source.": "readFailed",
	}
	matched = [r for r in records if r.msg in outcomes]
	if len(matched) != 1:
		msg = f"Expected exactly one Download Source log record, got {[r.msg for r in records]}"
		raise RuntimeError(msg)
	record = matched[0]
	outcome = outcomes[record.msg]  # type: ignore[index]

	return {
		"downloadSource": {
			"source": app_settings.download_source,
			"outcome": outcome,
			"rawValue": None if outcome == "readFailed" else record.args[0],  # type: ignore[index]
			"display": {"log": log_lines(records, "app_settings")},
		},
	}


@operation("main-window")
def main_window(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""SHELL-6/SHELL-7: the real ``CMChecker`` built on the withdrawn root, with its window facts read back off Tk."""
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415

	root = session.root
	assert root is not None
	checker = CMChecker(root, AppSettings())
	root.update_idletasks()

	notebook = checker.tabs[Tab.Overview].master
	geometry = root.wm_geometry()
	size = geometry.split("+", 1)[0]
	width, height = (int(n) for n in size.split("x"))
	resizable_width, resizable_height = root.wm_resizable()
	return {
		"window": {
			"title": root.wm_title(),
			"width": width,
			"height": height,
			"resizable": bool(resizable_width or resizable_height),
			"tabs": [notebook.tab(tab_id, "text") for tab_id in notebook.tabs()],  # type: ignore[union-attr]
		},
	}
