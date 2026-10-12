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


@operation("pc-info")
def pc_info(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""PC-1..PC-5: ``helpers.PCInfo()`` on the host block's OS, memory and registry.

	The ``get_cpu():``/``get_gpu():`` exception lines are the rendered log, so they sit under ``display``.
	"""
	from helpers import PCInfo  # noqa: PLC0415

	capture = capture_logs()
	pc = PCInfo()
	return {
		"pc": {
			"usingWine": pc.using_wine,
			"os": pc.os,
			"ram": pc.ram,
			"cpu": pc.cpu,
			"gpu": pc.gpu,
			"vram": pc.vram,
			"display": {"log": log_lines(capture.records, "helpers")},
		},
	}


class _StopAfterManager(Exception):  # noqa: N818 - a control-flow sentinel, not an error
	"""Raised where ``GameInfo.find_path`` leaves the mod manager part for the game-path part (the next slice)."""


def _path_text(golden: GoldenContext, value: Any) -> str | None:
	"""``str(path)`` as the reference renders it (backslashes, T-7 collapsing), with the temp root as ``<ROOT>``.

	A path outside the root on the root's own drive (B-6: ``%BASE_DIR%/mods`` lands on the drive root) starts with
	``<DRIVE>`` instead of the drive letter, so the golden doesn't depend on which drive the temp folder is on.
	"""
	if value is None:
		return None
	text = golden.text(str(value))
	drive = golden.root.drive
	if drive and not text.startswith("<ROOT>") and text.startswith(drive):
		text = "<DRIVE>" + text[len(drive) :]
	return text


def _setting_value(golden: GoldenContext, value: Any) -> Any:
	"""One ``mo2_settings`` value, typed: ``{"path": ...}``, ``{"text": ...}``, a bool, or a list of strings."""
	from pathlib import PurePath  # noqa: PLC0415

	if isinstance(value, PurePath):
		return {"path": _path_text(golden, value)}
	if isinstance(value, bool):
		return value
	if isinstance(value, str):
		return {"text": value}
	if isinstance(value, list) and all(isinstance(v, str) for v in value):
		return list(value)
	msg = f"Unexpected mo2_settings value {value!r}"
	raise TypeError(msg)


def _error(golden: GoldenContext, error: Exception) -> dict[str, Any]:
	"""The golden form of a failure: the messages the reference wrote for the user (MO2-1, MO2-3, MO2-8) verbatim, with
	the temp root as ``<ROOT>``, and only the kind for the rest, whose wording isn't part of Behaviour Parity
	(GLOSSARY.md, Failure Outcome).
	"""
	import psutil  # noqa: PLC0415

	if isinstance(error, UnicodeDecodeError):
		return {"kind": "decode"}
	if isinstance(error, psutil.AccessDenied):
		return {"kind": "accessDenied"}
	if isinstance(error, FileNotFoundError):
		return {"kind": "fileNotFound", "message": golden.text(str(error))}
	if isinstance(error, ValueError):
		return {"kind": "value", "message": golden.text(str(error))}
	raise error


@operation("mod-manager")
def mod_manager(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""MM-1 and MO2-1..MO2-9, MO2-11: ``utils.find_mod_manager()``, then the mod manager part of
	``GameInfo.find_path`` (portable, instance and fallback INI lookup) on a stand-in ``GameInfo``.

	``game_info.is_fo4_dir`` is where ``find_path`` moves on to the game path, so it raises a sentinel that ends the
	operation there. A failure is recorded under ``error`` along with whatever was detected before it.
	"""
	from types import SimpleNamespace  # noqa: PLC0415

	import game_info  # noqa: PLC0415
	import utils  # noqa: PLC0415

	def stop(_path: Any) -> bool:
		raise _StopAfterManager

	game_info.is_fo4_dir = stop
	result: dict[str, Any] = {"manager": None}
	try:
		manager = utils.find_mod_manager()
	except Exception as e:  # noqa: BLE001 - recorded, or re-raised by _error
		result["error"] = _error(golden, e)
		return result
	if manager is None:
		return result

	result["manager"] = {
		"name": manager.name,
		"exePath": _path_text(golden, manager.exe_path),
		"version": str(manager.version),
	}
	stand_in = SimpleNamespace(manager=manager, game_path=None)
	try:
		game_info.GameInfo.find_path(stand_in)  # type: ignore[arg-type]
	except _StopAfterManager:
		pass
	except Exception as e:  # noqa: BLE001 - recorded, or re-raised by _error
		result["error"] = _error(golden, e)
		return result

	if manager.name != "Mod Organizer":
		result["mo2"] = None
		return result
	result["mo2"] = {
		"iniPath": _path_text(golden, manager.ini_path),
		"portable": manager.portable,
		"portableTxtPath": _path_text(golden, manager.portable_txt_path),
		"gamePath": _path_text(golden, manager.game_path),
		"selectedProfile": manager.selected_profile,
		"stagePath": _path_text(golden, manager.stage_path),
		"overwritePath": _path_text(golden, manager.overwrite_path),
		"profilesPath": _path_text(golden, manager.profiles_path),
		"skipFileSuffixes": list(manager.skip_file_suffixes),
		# A set, so its iteration order is hash order (T-15).
		"skipDirectories": golden.unordered(manager.skip_directories),
		# Insertion order is what OVW-4 renders, so the dict is kept as an ordered list of entries.
		"settings": [{"key": k, "value": _setting_value(golden, v)} for k, v in manager.mo2_settings.items()],
	}
	return result
