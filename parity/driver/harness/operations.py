"""The operations a scenario can name in ``scenario.json``: each runs one path of the Reference Implementation and
returns its golden. A C# test runs the matching Core or App call and compares against that golden.

To add one, write a function taking the started ``Session`` and a ``GoldenContext`` and returning a JSON-able dict,
and register it with ``@operation("<name>")``. Read results that only exist after the GUI is built off the widgets,
not by re-deriving them (ADR-0004).
"""

import base64
import itertools
import logging
import operator
import sys
from collections.abc import Callable
from pathlib import Path
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


def file_snapshot(path: Path) -> dict[str, str] | None:
	"""A file's bytes after an operation (T-10): ``None`` if it isn't a file, ``{"text": ...}`` if it decodes as strict
	UTF-8, else ``{"base64": ...}``. Line endings and every other byte are kept, so a C# writer is compared byte-exact.
	"""
	if not path.is_file():
		return None
	data = path.read_bytes()
	try:
		return {"text": data.decode("utf-8")}
	except UnicodeDecodeError:
		return {"base64": base64.b64encode(data).decode("ascii")}


@operation("settings-load")
def settings_load(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""SET-1, SET-3..SET-6: ``AppSettings()`` against ``settings.json`` in the scenario's working directory.

	The Download Source (SET-2) is read when ``app_settings`` is imported, before the capture starts, so only the
	settings load's own log lines are recorded. They are captured at every level: which of them reach
	``cm-toolkit.log`` is the log file's business (SHELL-2). ``values`` is the loaded dict and ``settingsFile`` the
	file left behind, after any resave.
	"""
	import app_settings  # noqa: PLC0415

	capture = capture_logs()
	settings = app_settings.AppSettings()
	return {
		"settings": {
			"values": dict(settings.dict),
			"display": {"log": log_lines(capture.records, "app_settings")},
		},
		"settingsFile": file_snapshot(Path(app_settings.SETTINGS_PATH)),
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
		# Expected: the sentinel ends find_path once the mod manager part is done; the rest is recorded below.
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


class _Tooltips:
	"""Stands in for ``tktooltip.ToolTip`` where a tab module uses it: records each widget's tooltip text instead of
	binding hover events, so the golden can read it back by widget.
	"""

	def __init__(self) -> None:
		self.by_widget: dict[str, str] = {}

	def __call__(self, widget: Any, msg: str, *_args: Any, **_kwargs: Any) -> None:
		self.by_widget[str(widget)] = msg

	def of(self, widget: Any) -> str | None:
		"""The tooltip bound to ``widget``, or ``None``."""
		return self.by_widget.get(str(widget))


def _grid_children(master: Any) -> list[Any]:
	"""``master``'s gridded children in (row, column) order: the order the user reads them in."""
	children = [c for c in master.winfo_children() if c.winfo_manager() == "grid"]
	return sorted(children, key=lambda c: (int(c.grid_info()["row"]), int(c.grid_info()["column"])))


def _image_path(cmc: Any, widget: Any) -> str | None:
	"""The assets-relative path of the image a label shows, looked up in the ``cmc`` stub's image cache."""
	# A ttk image option is an image spec (a list: the default image, then state/image pairs); the first is shown.
	specs = widget.tk.splitlist(widget.cget("image"))
	if not specs:
		return None
	name = str(specs[0])
	for path, image in cmc._images.items():  # noqa: SLF001 - the stub's own cache
		if str(image) == name:
			return path
	return None


def _build_tab(session: Session, tab_class: Any, **state: Any) -> tuple[Any, Any]:
	"""Builds one tab on a ``cmc`` stub through ``CMCTabFrame.load``, as selecting it would. Returns (tab, cmc)."""
	from tkinter import ttk  # noqa: PLC0415

	cmc = session.make_cmc(**state)
	notebook = ttk.Notebook(session.root)
	tab = tab_class(cmc, notebook)
	tab.load()
	return tab, cmc


@operation("tools-tab")
def tools_tab(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""TOOL-1, TOOL-2, NET-4: the real ``ToolsTab``, read back off its widgets, column by column.

	Each button is invoked with ``webbrowser`` and the two utility windows replaced by recorders, so ``opens`` is what
	the button actually does: ``{"url": ...}`` or ``{"window": ...}``. ``tooltip`` is the button's hover text and
	``info`` the info icon's (``None`` without one). The whole golden is rendered UI, compared in full by the App test.
	"""
	from tabs import _tools  # noqa: PLC0415, PLC2701 - the module the patched names are used in

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	tooltips = _Tooltips()
	opened: list[dict[str, str]] = []
	_tools.ToolTip = tooltips
	_tools.webbrowser = ModuleProxy(_tools.webbrowser, open=lambda url, *_a, **_k: opened.append({"url": url}))
	_tools.Downgrader = lambda *_a, **_k: opened.append({"window": "Downgrader"})
	_tools.ArchivePatcher = lambda *_a, **_k: opened.append({"window": "ArchivePatcher"})

	tab, cmc = _build_tab(session, _tools.ToolsTab)
	columns = []
	for frame in _grid_children(tab):
		rows: dict[int, dict[str, Any]] = {}
		for widget in _grid_children(frame):
			row = rows.setdefault(int(widget.grid_info()["row"]), {})
			if widget.winfo_class() == "TButton":
				row["button"] = widget
			else:
				row["info"] = widget

		buttons = []
		for row in rows.values():
			button = row["button"]
			before = len(opened)
			button.invoke()
			info = row.get("info")
			buttons.append({
				"text": str(button.cget("text")),
				"enabled": not button.instate(["disabled"]),
				"opens": opened[before] if len(opened) > before else None,
				"tooltip": tooltips.of(button),
				"info": None if info is None else {"image": _image_path(cmc, info), "tooltip": tooltips.of(info)},
			})
		columns.append({"title": str(frame.cget("text")), "buttons": buttons})

	return {"toolsTab": {"columns": columns}}


@operation("settings-tab")
def settings_tab(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""SETT-1, SET-7: the real ``SettingsTab`` over ``AppSettings()`` loaded from the scenario's working directory.

	``settingsTab`` is the frames and radios as built: text, value, tooltip and whether each is selected. Then every
	radio is clicked in turn, the last one twice (``invoke``, as a click does), and each click records which radios are selected after
	it, the ``settings.json`` it left behind and the ``app_settings`` log lines it wrote. The settings load's own lines
	are not captured; the ``settings-load`` scenarios cover them.
	"""
	import app_settings  # noqa: PLC0415
	from tabs import _settings  # noqa: PLC0415, PLC2701 - the module the patched name is used in

	tooltips = _Tooltips()
	_settings.ToolTip = tooltips
	settings = app_settings.AppSettings()
	capture = capture_logs()
	tab, _ = _build_tab(session, _settings.SettingsTab, settings=settings)

	frames = _grid_children(tab)
	radios = [radio for frame in frames for radio in frame.pack_slaves()]

	def selected() -> list[str]:
		return [str(radio.cget("text")) for radio in radios if radio.instate(["selected"])]

	built = [
		{
			"title": str(frame.cget("text")),
			"radios": [
				{
					"text": str(radio.cget("text")),
					"value": str(radio.cget("value")),
					"selected": radio.instate(["selected"]),
					"tooltip": tooltips.of(radio),
				}
				for radio in frame.pack_slaves()
			],
		}
		for frame in frames
	]

	clicks = []
	# The last radio is clicked twice: clicking the radio that is already selected still runs its command and saves.
	for radio in [*radios, radios[-1]]:
		before = len(capture.records)
		radio.invoke()
		clicks.append({
			"radio": str(radio.cget("text")),
			"selected": selected(),
			"settingsFile": file_snapshot(Path(app_settings.SETTINGS_PATH)),
			"log": log_lines(capture.records[before:], "app_settings"),
		})

	return {"settingsTab": {"frames": built}, "clicks": clicks}


@operation("about-tab")
def about_tab(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""ABOUT-1, NET-4, THR-3: the real ``AboutTab``, read back off its widgets.

	``webbrowser``, the clipboard and ``after`` are replaced by recorders for the operation, so the real system
	clipboard is never touched and the 3 s revert runs on demand. Each link row records its logo, what its open button
	opens, and what its copy button copies, the button's state once clicked, the ``after`` delay, and its state once
	that callback has run.
	"""
	import tkinter as tk  # noqa: PLC0415

	from tabs import _about  # noqa: PLC0415, PLC2701 - the module the patched name is used in

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	opened: list[str] = []
	clipboard: list[str] = []
	scheduled: list[tuple[int, Any]] = []
	_about.webbrowser = ModuleProxy(_about.webbrowser, open=lambda url, *_a, **_k: opened.append(url))
	patched = {
		"clipboard_clear": lambda _self, **_k: clipboard.clear(),
		"clipboard_append": lambda _self, text, **_k: clipboard.append(text),
		"after": lambda _self, ms, func=None, *args: scheduled.append((ms, lambda: func(*args))),
	}
	originals = {name: getattr(tk.Misc, name) for name in patched}
	try:
		for name, replacement in patched.items():
			setattr(tk.Misc, name, replacement)
		tab, cmc = _build_tab(session, _about.AboutTab)
		# (row, column) order: the title (0, 0), the text frame spanning both rows of column 1, then the icon (1, 0).
		title, text_frame, icon = _grid_children(tab)
		text, *link_frames = _grid_children(text_frame)

		def state(button: Any) -> dict[str, Any]:
			return {"text": str(button.cget("text")), "enabled": not button.instate(["disabled"])}

		links = []
		for frame in link_frames:
			logo, _separator, open_button, copy_button = _grid_children(frame)
			copy_text = str(copy_button.cget("text"))
			open_button.invoke()
			copy_button.invoke()
			clicked = state(copy_button)
			delay, revert = scheduled.pop()
			revert()
			links.append({
				"logo": _image_path(cmc, logo),
				"open": {"text": str(open_button.cget("text")), "opens": opened.pop()},
				"copy": {
					"text": copy_text,
					"copies": "".join(clipboard),
					"clicked": clicked,
					"revertAfterMs": delay,
					"reverted": state(copy_button),
				},
			})
	finally:
		for name, original in originals.items():
			setattr(tk.Misc, name, original)

	return {
		"aboutTab": {
			"title": str(title.cget("text")),
			"icon": _image_path(cmc, icon),
			"text": str(text.cget("text")),
			"links": links,
		},
	}


@operation("game-info")
def game_info_operation(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""GP-1..GP-4 and INI-1..INI-4: the real ``GameInfo`` constructor, which finds the mod manager, then the game path
	(MO2 ``gamePath``, the CWD, the Steam and GOG registry keys, then the scripted dialogs), then reads the game INIs.

	A deliberate ``sys.exit()`` (the "Fallout 4 Not Found" and "Game not found" flows) is recorded as ``exitCode``, and
	a failure as ``error`` (see ``_error``). Otherwise ``game`` holds the paths as ``str(path)`` (T-7), the two INI
	dicts exactly as parsed (B-14), the language and the BA2 suffixes. The dialogs shown are added by ``record.py``.
	"""
	from tkinter import StringVar  # noqa: PLC0415

	import game_info  # noqa: PLC0415

	try:
		game = game_info.GameInfo(StringVar(session.root), StringVar(session.root))
	except SystemExit as e:
		# sys.exit() carries None, which the process reports as exit status 0.
		return {"exitCode": 0 if e.code is None else e.code}
	except Exception as e:  # noqa: BLE001 - recorded, or re-raised by _error
		return {"error": _error(golden, e)}

	return {
		"game": {
			"gamePath": _path_text(golden, game.game_path),
			"dataPath": _path_text(golden, game.data_path),
			"f4sePath": _path_text(golden, game.f4se_path),
			"settings": game.game_settings,
			"prefs": game.game_prefs,
			"language": str(game.language),
			"ba2Suffixes": list(game.ba2_suffixes),
		},
	}


def _tk_colour(widget: Any, colour: str) -> str | None:
	"""A Tk colour name or spec as ``#RRGGBB`` (``winfo_rgb`` gives 16-bit channels), or ``None`` if unset."""
	if not colour:
		return None
	red, green, blue = widget.winfo_rgb(colour)
	return f"#{red >> 8:02X}{green >> 8:02X}{blue >> 8:02X}"


def _tk_font(widget: Any) -> dict[str, Any]:
	"""A label's ``font`` option as given (not ``font actual``, which depends on the fonts installed): the family, the
	point size, and whether the style words include ``bold`` and ``underline``.
	"""
	family, size, *styles = widget.tk.splitlist(widget.cget("font"))
	words = " ".join(str(s) for s in styles).split()
	return {"family": str(family), "size": int(size), "bold": "bold" in words, "underline": "underline" in words}


@operation("update-check")
def update_check(session: Session, golden: GoldenContext) -> dict[str, Any]:  # noqa: ARG001
	"""UPD-1..UPD-4, NET-1, NET-2: ``CMChecker.check_for_updates`` on a ``cmc`` stub, with ``AppSettings()`` loaded from
	the scenario's working directory for ``update_source`` and ``requests`` serving the scenario's ``http/`` folder.

	``updateCheck`` is what each source's check returned (``None`` when it wasn't run or found no update) and the
	``utils`` log lines it wrote. ``banner`` is the update frame read back off its widgets, or ``None`` when none was
	built: the frame's column weights, then each label in column order with its text, image, colours (as ``#RRGGBB``),
	font, padding, cursor, tooltip, and what clicking it opens. An exception escaping the check is recorded as
	``error``: it escapes ``CMChecker.__init__`` too, so startup fails (SHELL-13) and only that fact is compared.
	"""
	import app_settings  # noqa: PLC0415
	import cm_checker  # noqa: PLC0415

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	tooltips = _Tooltips()
	opened: list[str] = []
	returned: dict[str, str | None] = {"nexusVersion": None, "githubVersion": None}
	cm_checker.ToolTip = tooltips
	cm_checker.webbrowser = ModuleProxy(cm_checker.webbrowser, open=lambda url, *_a, **_k: opened.append(url))

	def recording(check: Callable[[], str | None], key: str) -> Callable[[], str | None]:
		def run() -> str | None:
			returned[key] = check()
			return returned[key]

		return run

	cm_checker.check_for_update_nexus = recording(cm_checker.check_for_update_nexus, "nexusVersion")
	cm_checker.check_for_update_github = recording(cm_checker.check_for_update_github, "githubVersion")

	settings = app_settings.AppSettings()
	capture = capture_logs()
	cmc = session.make_cmc(settings=settings)
	result: dict[str, Any] = {}
	try:
		cm_checker.CMChecker.check_for_updates(cmc)  # type: ignore[arg-type]
	except Exception:  # noqa: BLE001 - only the fact of it is part of Behaviour Parity
		result["error"] = {"kind": "startupFailure"}

	result["updateCheck"] = {**returned, "display": {"log": log_lines(capture.records, "utils")}}

	root = cmc.root
	frames = root.grid_slaves(row=0, column=0)
	if not frames:
		result["banner"] = None
		return result

	(frame,) = frames
	root.update_idletasks()
	columns = int(frame.grid_size()[0])
	labels = []
	for label in _grid_children(frame):
		before = len(opened)
		label.event_generate("<Button-1>")
		padding = label.cget("padding")
		labels.append({
			"column": int(label.grid_info()["column"]),
			"sticky": str(label.grid_info()["sticky"]),
			"text": str(label.cget("text")),
			"image": _image_path(cmc, label),
			"foreground": _tk_colour(label, str(label.cget("foreground"))),
			"background": _tk_colour(label, str(label.cget("background"))),
			"font": _tk_font(label),
			# An unset padding is the theme's, and sv_ttk sets none for TLabel: measured, it is the same as padding=0.
			"padding": [int(p) for p in label.tk.splitlist(padding)] if str(padding) else [0],
			"cursor": str(label.cget("cursor")) or None,
			"tooltip": tooltips.of(label),
			"opens": opened[before] if len(opened) > before else None,
		})

	result["banner"] = {
		"style": str(frame.cget("style")),
		"columnWeights": [int(frame.grid_columnconfigure(i)["weight"]) for i in range(columns)],
		"rowWeight": int(frame.grid_rowconfigure(0)["weight"]),
		"labels": labels,
	}
	return result


# The loggers whose lines an Overview load or refresh writes: the tab lifecycle (helpers), the gathering
# (tabs._overview) and its resets (game_info).
_OVERVIEW_LOGGERS = {"helpers", "tabs._overview", "game_info"}


def _label_text(widget: Any) -> str:
	"""A label's shown text: its ``textvariable``'s value when it has one (which overrides ``text``), else ``text``."""
	variable = str(widget.cget("textvariable"))
	return str(widget.getvar(variable)) if variable else str(widget.cget("text"))


def _grid_at(master: Any, row: int, column: int) -> Any:
	"""The gridded child of ``master`` whose cell starts at (``row``, ``column``), or ``None``."""
	for child in _grid_children(master):
		info = child.grid_info()
		if int(info["row"]) == row and int(info["column"]) == column:
			return child
	return None


def _overview_problem(golden: GoldenContext, problem: Any) -> dict[str, Any]:
	"""One ``cmc.overview_problems`` entry as the Scanner reads it (SCN-R2..R5): its group (``type``), the tree item
	text (``path.name`` for a ``ProblemInfo``, the plain ``path`` string for a ``SimpleProblemInfo``), the openable path
	(``None`` for a plain string, which the details pane can't open), and the details texts.
	"""
	from helpers import ProblemInfo  # noqa: PLC0415

	if isinstance(problem, ProblemInfo):
		item_text, path = problem.path.name, _path_text(golden, problem.path)
	else:
		item_text, path = problem.path, None
	return {
		"type": str(problem.type),
		"itemText": item_text,
		"path": path,
		"relativePath": golden.text(str(problem.relative_path)),
		"mod": problem.mod,
		"summary": golden.text(problem.summary),
		"solution": None if problem.solution is None else str(problem.solution),
		"extraData": problem.extra_data,
	}


@operation("overview-tab")
def overview_tab(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""OVW-1..OVW-8, OVW-B1..B8, OVW-P1..P6, PC-6, MM-2, T-4, T-9, B-8, B-13: the real ``CMChecker`` (so PC info, the
	``StringVar``s and ``GameInfo`` are built as at startup) with its Overview tab loaded, read back off the widgets,
	then refreshed through its button and read again.

	The Modules and Archives frames are the next Overview slice's (#52): their gathering is skipped, so they build
	from empty state and add nothing, and they aren't read. ``overview`` holds the gathered binaries, Install Type,
	Address Library and the problems in list order; its ``display`` is the top block and the Binaries frame as shown,
	including what clicking the MO2 info icon, the game path and the Downgrade Manager button does, each value's hover
	text and the text restored on leave, and the log lines written. ``refresh`` is the same after the refresh button.
	"""
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview  # noqa: PLC0415, PLC2701 - the module the patched names are used in

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	tooltips = _Tooltips()
	opened: list[dict[str, Any]] = []
	_overview.ToolTip = tooltips
	_overview.os = ModuleProxy(_overview.os, startfile=lambda path, *_a, **_k: opened.append({"folder": _path_text(golden, path)}))
	_overview.AboutWindow = lambda _root, _cmc, width, height, title, text: opened.append({
		"window": "AboutWindow",
		"width": width,
		"height": height,
		"title": title,
		"text": golden.text(text),
	})
	_overview.Downgrader = lambda *_a, **_k: opened.append({"window": "Downgrader"})
	_overview.OverviewTab.get_info_modules = lambda _self, *, refresh=False: None  # type: ignore[method-assign]  # noqa: ARG005
	_overview.OverviewTab.get_info_archives = lambda _self: None  # type: ignore[method-assign]

	root = session.root
	assert root is not None
	checker = CMChecker(root, AppSettings())
	tab = checker.tabs[Tab.Overview]
	capture = capture_logs()
	# Processing events delivers <<NotebookTabChanged>> for the first tab, which loads it as at startup.
	root.update()
	if not tab.is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	def click(widget: Any) -> dict[str, Any] | None:
		before = len(opened)
		widget.event_generate("<Button-1>")
		return opened[before] if len(opened) > before else None

	def read(first_record: int) -> dict[str, Any]:
		frame_top = tab.pack_slaves()[0]
		manager_icon = _grid_at(frame_top, 0, 1)
		manager_label = _grid_at(frame_top, 0, 2)
		path_label = _grid_at(frame_top, 1, 2)
		version_label = _grid_at(frame_top, 2, 2)
		os_icon = _grid_at(frame_top, 3, 1)
		specs = _grid_children(_grid_at(frame_top, 3, 2))
		refresh_button = _grid_at(frame_top, 0, 3)
		top = {
			"labels": _label_text(_grid_at(frame_top, 0, 0)),
			"modManager": {
				"text": _label_text(manager_label),
				"color": str(manager_label.cget("foreground")),
				"tooltip": tooltips.of(manager_label),
			},
			"modManagerIcon": None if manager_icon is None else {
				"image": _image_path(checker, manager_icon),
				"tooltip": tooltips.of(manager_icon),
				"opens": click(manager_icon),
			},
			"gamePath": {
				"text": golden.text(_label_text(path_label)),
				"color": str(path_label.cget("foreground")),
				"tooltip": tooltips.of(path_label),
				"opens": click(path_label),
			},
			"version": {"text": _label_text(version_label), "color": str(version_label.cget("foreground"))},
			"specs": [{"text": _label_text(s), "color": str(s.cget("foreground"))} for s in specs],
			"osWarning": None if os_icon is None else {"image": _image_path(checker, os_icon), "tooltip": tooltips.of(os_icon)},
			"refresh": {"image": _image_path(checker, refresh_button), "tooltip": tooltips.of(refresh_button)},
		}

		frame = tab.frame_info_binaries
		names = _grid_at(frame, 0, 0)
		rows = len(checker.game.file_info)
		values = []
		for row in range(rows):
			label = _grid_at(frame, row, 1)
			text = str(label.cget("text"))
			label.event_generate("<Enter>")
			hover = str(label.cget("text"))
			label.event_generate("<Leave>")
			values.append({"text": text, "color": str(label.cget("foreground")), "hover": hover, "left": str(label.cget("text"))})
		address_label = _grid_at(frame, rows, 1)
		button = _grid_at(frame, rows + 1, 0)
		before = len(opened)
		button.invoke()
		binaries = {
			"title": str(frame.cget("text")),
			"names": str(names.cget("text")),
			"rows": values,
			"addressLibrary": {
				"label": str(_grid_at(frame, rows, 0).cget("text")),
				"text": str(address_label.cget("text")),
				"color": str(address_label.cget("foreground")),
				"tooltip": tooltips.of(address_label),
			},
			"button": {"text": str(button.cget("text")), "opens": opened[before] if len(opened) > before else None},
		}

		game = checker.game
		return {
			"installType": str(game.install_type),
			"binaries": [
				{
					"name": name,
					"file": _path_text(golden, info["File"]),
					"version": None if info["Version"] is None else list(info["Version"]),
					"versionString": info["VersionString"],
					"hash": info["Hash"],
					"installType": str(info["InstallType"]),
				}
				for name, info in game.file_info.items()
			],
			"addressLibrary": _path_text(golden, game.address_library),
			"problems": [_overview_problem(golden, p) for p in checker.overview_problems],
			"display": {
				"top": top,
				"binaries": binaries,
				"log": log_lines_of(golden, capture.records[first_record:], _OVERVIEW_LOGGERS),
			},
		}

	loaded = read(0)
	before_refresh = len(capture.records)
	_grid_at(tab.pack_slaves()[0], 0, 3).invoke()
	return {"overview": loaded, "refresh": read(before_refresh)}


def log_lines_of(golden: GoldenContext, records: list[logging.LogRecord], logger_names: set[str]) -> list[dict[str, Any]]:
	"""``log_lines`` for several loggers at once, interleaved in the order they were written, with the temp root in
	each message as ``<ROOT>``.
	"""
	lines = []
	for record in records:
		if record.name in logger_names:
			for line in log_lines([record], record.name):
				line["message"] = golden.text(line["message"])
				lines.append(line)
	return lines


def _tk_offsets(content: str) -> list[int]:
	"""For each Tk character offset into ``content`` (and the end), the Python string index it falls at.

	Tk 8.6 counts in UTF-16 code units, so a non-BMP character such as ``💭`` is two Tk characters but one Python one.
	An offset between the halves of such a pair maps to the index after the character.
	"""
	indices: list[int] = []
	for index, char in enumerate(content):
		indices.append(index)
		if ord(char) > 0xFFFF:
			indices.append(index + 1)
	indices.append(len(content))
	return indices


def _text_runs(text: Any) -> list[dict[str, Any]]:
	"""A Tk ``Text`` widget's content as runs in reading order: each tagged range with its tag's foreground, and the
	untagged text between them with ``None``. The trailing newline Tk always keeps is left out.

	Tag ranges are counted in Tk characters and mapped to string indices through ``_tk_offsets``: counting them as string
	indices drifted by one after each non-BMP character.
	"""
	content = text.get("1.0", "end-1c")
	index_at = _tk_offsets(content)
	starts: list[tuple[int, int, str | None]] = []
	for tag in text.tag_names():
		if tag == "sel":
			continue
		ranges = text.tag_ranges(tag)
		for start, end in zip(ranges[::2], ranges[1::2], strict=True):
			first = int(text.count("1.0", start, "chars")[0] or 0) if text.compare(start, ">", "1.0") else 0
			length = int(text.count(start, end, "chars")[0] or 0)
			starts.append((index_at[first], index_at[first + length], str(text.tag_cget(tag, "foreground")) or None))
	runs: list[dict[str, Any]] = []
	at = 0
	for first, last, colour in sorted(starts):
		if first > at:
			runs.append({"text": content[at:first], "color": None})
		runs.append({"text": content[first:last], "color": colour})
		at = last
	if at < len(content):
		runs.append({"text": content[at:], "color": None})
	return runs


def _log_pane_runs(text: Any) -> list[dict[str, Any]]:
	"""A ``Logger`` pane's content as ``_text_runs``, with each emoji and the space after it as one run (§9.5).

	``log_message`` tags ``len("💭 ")`` = 2 Tk characters, which for ``💭`` is its surrogate pair alone, so Tk leaves the
	space after it untagged where it tags the space after ``✅`` and ``❌``. A space's colour can't be seen, so it is
	recorded with its emoji, as ``_log_pane_lines`` does.
	"""
	runs = _text_runs(text)
	for run, after in itertools.pairwise(runs):
		if run["color"] is not None and after["color"] is None and not run["text"].endswith(" ") and after["text"].startswith(" "):
			run["text"] += " "
			after["text"] = after["text"][1:]
	return [run for run in runs if run["text"]]


@operation("downgrader-window")
def downgrader_window(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""DG-1, DG-2, DG-3: the real ``Downgrader`` opened over the real ``CMChecker`` once its Overview tab has loaded (as
	at startup, so ``cmc.game``'s Install Type, which the labels read, comes from the Overview's binaries), read back off
	its widgets. Nothing is patched: the Patch All run is the next slice's.

	``downgrader`` holds the detected version of each of the six files in table order (``current_versions``), the game's
	Install Type, the window title and size, and the derived ``CRCs_by_type``. Its ``display`` is the window as shown:
	both Current frames (names label and each coloured value), the Desired Version radios and which is selected, the
	Options checkboxes with their values and tooltips, the Patch All and About buttons (and what About opens), the log
	pane's text as coloured runs, the progress bar, and the log lines written while opening. The Overview's Modules and
	Archives gathering is skipped as in ``overview-tab``: the Downgrader doesn't read them.
	"""
	import downgrader  # noqa: PLC0415
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview  # noqa: PLC0415, PLC2701 - the module the patched names are used in

	tooltips = _Tooltips()
	opened: list[dict[str, Any]] = []
	downgrader.ToolTip = tooltips
	downgrader.AboutWindow = lambda _parent, _cmc, width, height, title, text: opened.append({
		"window": "AboutWindow",
		"width": width,
		"height": height,
		"title": title,
		"text": golden.text(text),
	})
	_overview.ToolTip = _Tooltips()
	_overview.OverviewTab.get_info_modules = lambda _self, *, refresh=False: None  # type: ignore[method-assign]  # noqa: ARG005
	_overview.OverviewTab.get_info_archives = lambda _self: None  # type: ignore[method-assign]

	root = session.root
	assert root is not None
	checker = CMChecker(root, AppSettings())
	# Processing events delivers <<NotebookTabChanged>> for the first tab, which loads it as at startup.
	root.update()
	if not checker.tabs[Tab.Overview].is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	capture = capture_logs()
	window = downgrader.Downgrader(root, checker)
	root.update_idletasks()

	def current(frame: Any) -> dict[str, Any]:
		names = _grid_at(frame, 0, 0)
		values = sorted(
			(c for c in _grid_children(frame) if int(c.grid_info()["column"]) == 1),
			key=lambda c: int(c.grid_info()["row"]),
		)
		return {
			"title": str(frame.cget("text")),
			"names": str(names.cget("text")),
			"values": [{"text": str(v.cget("text")), "color": str(v.cget("foreground"))} for v in values],
		}

	frame_desired = _grid_at(window, 0, 1)
	radios = [
		{
			"text": str(radio.cget("text")),
			"selected": str(window.getvar(str(radio.cget("variable")))) == str(radio.cget("value")),
		}
		for radio in _grid_children(frame_desired)
	]
	frame_options = _grid_at(window, 2, 1)
	options = [
		{
			"text": str(check.cget("text")),
			"checked": bool(window.getvar(str(check.cget("variable")))),
			"tooltip": tooltips.of(check),
		}
		for check in _grid_children(frame_options)
	]
	about = _grid_at(window, 2, 2)
	before = len(opened)
	about.invoke()
	# The size the modal asks for in its wm_geometry: in record mode it is never mapped, so Tk would report 1x1.
	width, height = window.width, window.height

	return {
		"downgrader": {
			"gameInstallType": str(checker.game.install_type),
			"versions": [{"file": name, "installType": str(t)} for name, t in window.current_versions.items()],
			"crcsByType": {str(t): list(crcs) for t, crcs in downgrader.Downgrader.CRCs_by_type.items()},
			"display": {
				"title": window.wm_title(),
				"width": width,
				"height": height,
				"game": current(window.frame_game),
				"creationKit": current(window.frame_ck),
				"desiredVersion": {"title": str(frame_desired.cget("text")), "radios": radios},
				"options": {"title": str(frame_options.cget("text")), "checks": options},
				"patch": {"text": str(window.button_patch.cget("text")), "enabled": not window.button_patch.instate(["disabled"])},
				"about": {"text": str(about.cget("text")), "opens": opened[before] if len(opened) > before else None},
				"log": _log_pane_runs(window.logger._text),  # noqa: SLF001 - the pane's only widget
				"progress": {"value": float(window.progress_var.get()), "maximum": float(window.progress_bar.cget("maximum"))},
				"processingData": window.processing_data,
				"logLines": log_lines_of(golden, capture.records, {"downgrader", "logger"}),
			},
		},
	}


def unordered_runs(golden: GoldenContext, items: list[Any], in_run: Callable[[Any], bool]) -> list[Any]:
	"""``items`` with each run of consecutive items that ``in_run`` picks replaced by one unordered marker.

	The Overview checks its enabled archives in set order (T-15), so the problems and log lines written in that loop
	come in hash order, while everything around them is ordered.
	"""
	result: list[Any] = []
	run: list[Any] = []
	for item in [*items, None]:
		if item is not None and in_run(item):
			run.append(item)
			continue
		if run:
			# Sorted, so a re-recording is byte-stable: the hash order changes with the temp root in every path.
			result.append(golden.unordered(sorted(run, key=str)))
			run = []
		if item is not None:
			result.append(item)
	return result


def _is_archive_problem(problem: dict[str, Any]) -> bool:
	"""An ``Invalid Archive`` problem, which only the archive loop adds (OVW-P15..P18)."""
	return problem["type"] == "Invalid Archive"


def _is_archive_check(line: dict[str, Any]) -> bool:
	"""The archive loop's per-archive log line."""
	return line["message"].startswith("Archives: Checking ")


@operation("overview-full")
def overview_full(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""OVW-M1..M10, OVW-A1..A6, OVW-C1, OVW-C2, OVW-P7..P18, T-2, T-3, T-8, T-15, B-7, B-15, B-17: the real ``CMChecker``
	with its Overview tab loaded, nothing stubbed, then refreshed through its button.

	``overview`` holds what the gathering left in ``GameInfo`` (modules, archives, counts, the Install Type) and the
	problem list. Python sets come out as unordered arrays, and so do the archive loop's problems and log lines (T-15).
	Its ``display`` is the Archives and Modules frames read off their widgets, including what the Archive Patcher
	button and the HEDR info icon open (the real ``TreeWindow``, read back and closed), and the log lines. ``loaded`` is
	false when the load raised (B-15, T-1): ``error`` is then the failure and ``display`` the loading label left up, and
	there is nothing to refresh. The warnings shown are the golden's ``dialogs``, added by ``record.py``.
	"""
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview  # noqa: PLC0415, PLC2701 - the module the patched names are used in

	tooltips = _Tooltips()
	opened: list[dict[str, Any]] = []
	_overview.ToolTip = tooltips
	_overview.ArchivePatcher = lambda *_a, **_k: opened.append({"window": "ArchivePatcher"})
	real_tree_window = _overview.TreeWindow

	def tree_window(*args: Any, **kwargs: Any) -> None:
		window = real_tree_window(*args, **kwargs)
		tree = window.tree_items
		opened.append({
			"window": "TreeWindow",
			"width": window.width,
			"height": window.height,
			"title": str(window.wm_title()),
			"text": window.win_text,
			"headers": [str(tree.heading(f"#{c}", "text")) for c in range(len(window.headers))],
			"rows": [[str(tree.item(i, "text")), *(str(v) for v in tree.item(i, "values"))] for i in tree.get_children()],
		})
		window.destroy()

	_overview.TreeWindow = tree_window

	root = session.root
	assert root is not None
	errors: list[BaseException] = []
	# A tab load raises inside Tk's <<NotebookTabChanged>> callback; record it instead of printing it.
	root.report_callback_exception = lambda _type, value, _tb: errors.append(value)  # type: ignore[method-assign]
	checker = CMChecker(root, AppSettings())
	tab = checker.tabs[Tab.Overview]
	capture = capture_logs()
	root.update()

	def click(widget: Any) -> dict[str, Any] | None:
		before = len(opened)
		widget.event_generate("<Button-1>")
		return opened[before] if len(opened) > before else None

	def label(widget: Any) -> dict[str, Any]:
		return {"text": str(widget.cget("text")), "color": str(widget.cget("foreground")), "tooltip": tooltips.of(widget)}

	def counts(frame: Any, rows: list[int]) -> list[dict[str, Any]]:
		return [{"row": row, **label(_grid_at(frame, row, 1))} for row in rows]

	def button(frame: Any) -> dict[str, Any]:
		widget = _grid_at(frame, frame.grid_size()[1] - 1, 0)
		before = len(opened)
		widget.invoke()
		return {"text": str(widget.cget("text")), "opens": opened[before] if len(opened) > before else None}

	def read_archives() -> dict[str, Any]:
		frame = tab.frame_info_archives
		return {
			"title": str(frame.cget("text")),
			"formats": label(_grid_at(frame, 0, 0)),
			"unreadable": label(_grid_at(frame, 3, 0)),
			"versions": label(_grid_at(frame, 5, 0)),
			"counts": counts(frame, [0, 1, 2, 3, 5, 6]),
			"limits": label(_grid_at(frame, 0, 2)),
			"button": button(frame),
		}

	def read_modules() -> dict[str, Any]:
		frame = tab.frame_info_modules
		icon = _grid_at(frame, 7, 2)
		return {
			"title": str(frame.cget("text")),
			"types": label(_grid_at(frame, 0, 0)),
			"unreadable": label(_grid_at(frame, 3, 0)),
			"hedr100": label(_grid_at(frame, 5, 0)),
			"hedr95": label(_grid_at(frame, 6, 0)),
			"hedrUnknown": label(_grid_at(frame, 7, 0)),
			"counts": counts(frame, [0, 1, 2, 3, 5, 6, 7]),
			"limits": label(_grid_at(frame, 0, 2)),
			"hedrIcon": None if icon is None else {
				"image": _image_path(checker, icon),
				"tooltip": tooltips.of(icon),
				"opens": click(icon),
			},
		}

	def paths(values: Any) -> list[str | None]:
		return [_path_text(golden, p) for p in values]

	def state(first_record: int) -> dict[str, Any]:
		game = checker.game
		problems = [_overview_problem(golden, p) for p in checker.overview_problems]
		log = log_lines_of(golden, capture.records[first_record:], _OVERVIEW_LOGGERS)
		return {
			"installType": str(game.install_type),
			"modules": {
				"enabled": paths(game.modules_enabled),
				"unreadable": golden.unordered(sorted(paths(game.modules_unreadable))),
				"hedr95": golden.unordered(sorted(paths(game.modules_hedr_95))),
				"hedrUnknown": [{"module": _path_text(golden, k), "version": v} for k, v in game.modules_hedr_unknown.items()],
				"full": game.module_count_full,
				"light": game.module_count_light,
				"hedr100": game.module_count_v1,
			},
			"archives": {
				"enabled": golden.unordered(sorted(paths(game.archives_enabled))),
				"unreadable": golden.unordered(sorted(paths(game.archives_unreadable))),
				"oldGen": golden.unordered(sorted(paths(game.archives_og))),
				"nextGen": golden.unordered(sorted(paths(game.archives_ng))),
				"general": game.ba2_count_gnrl,
				"texture": game.ba2_count_dx10,
			},
			"problems": unordered_runs(golden, problems, _is_archive_problem),
			"display": {"log": unordered_runs(golden, log, _is_archive_check)},
		}

	if not tab.is_loaded:
		(error,) = errors
		failed = state(0)
		failed["loaded"] = False
		failed["error"] = _error(golden, error)  # type: ignore[arg-type]
		failed["display"]["loadingLabel"] = {"text": tab.sv_loading_text.get(), "color": str(tab.label_loading.cget("foreground"))}
		return {"overview": failed}

	def read(first_record: int) -> dict[str, Any]:
		result = state(first_record)
		result["loaded"] = True
		result["display"]["archives"] = read_archives()
		result["display"]["modules"] = read_modules()
		return result

	loaded = read(0)
	before_refresh = len(capture.records)
	_grid_at(tab.pack_slaves()[0], 0, 3).invoke()
	return {"overview": loaded, "refresh": read(before_refresh)}


_F4SE_LOGGERS = {"helpers", "tabs._f4se"}

# The F4SE tree's cell glyphs (tabs._f4se EMOJI_*), by the semantic verdict each shows. The OG/NG/AE cells show
# "unsupported" as an empty cell, Your Game as a cross mark.
_F4SE_VERDICTS = {
	"\N{HEAVY CHECK MARK}": "supported",
	"\N{HEAVY ASTERISK}": "independent",
	"\N{WARNING SIGN}": "note",
	"\N{BLACK QUESTION MARK ORNAMENT}": "unknown",
	"\N{CROSS MARK}": "unsupported",
	"": "unsupported",
}


@operation("f4se-tab")
def f4se_tab(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""F4SE-1..F4SE-7, T-4, T-5, B-18: the real ``CMChecker`` with its Overview loaded as at startup (which sets the
	Install Type the "Your Game" column reads), then the F4SE tab selected, as the user selects it.

	The Overview's Modules and Archives gathering is skipped, as in ``overview-tab``: neither changes the Install Type,
	and their message boxes aren't this tab's. The plugin DLLs are real files in the tree, loaded by the reference's own
	``parse_dll``; the only patch there records each ``WinDLL`` so the DLLs can be unmapped afterwards, which the
	reference never does, and the temp root couldn't otherwise be deleted.

	``dlls`` is ``dll_info`` (the inspection, F4SE-2/F4SE-3) and ``verdicts`` the semantic value of each row's cells
	(F4SE-5/F4SE-6), read off the Treeview. An exception out of the tab's load is recorded by kind only (B-18): the
	Error Window's wording isn't part of Behaviour Parity. ``display`` is the tree, the heading and the text as built
	(F4SE-4/F4SE-7), or the loading label left up when the load failed (F4SE-1), plus the log lines of the selection.
	"""
	import ctypes  # noqa: PLC0415

	import utils  # noqa: PLC0415
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview  # noqa: PLC0415, PLC2701 - the module the patched names are used in

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	_overview.OverviewTab.get_info_modules = lambda _self, *, refresh=False: None  # type: ignore[method-assign]  # noqa: ARG005
	_overview.OverviewTab.get_info_archives = lambda _self: None  # type: ignore[method-assign]

	mapped: list[Any] = []

	def win_dll(*args: Any, **kwargs: Any) -> Any:
		dll = ctypes.WinDLL(*args, **kwargs)
		mapped.append(dll)
		return dll

	utils.ctypes = ModuleProxy(ctypes, WinDLL=win_dll)

	root = session.root
	assert root is not None
	errors: list[BaseException] = []
	# What Tk does with an exception out of a callback (an event binding here): the reference's default prints it.
	root.report_callback_exception = lambda _type, value, _traceback: errors.append(value)  # type: ignore[method-assign]

	checker = CMChecker(root, AppSettings())
	root.update()
	if not checker.tabs[Tab.Overview].is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	tab = checker.tabs[Tab.F4SE]
	capture = capture_logs()
	first_record = len(capture.records)
	tab.master.select(tab)
	root.update()

	try:
		failure = None
		if errors:
			if not isinstance(errors[0], OSError):
				raise errors[0]
			failure = {"kind": "os"}

		result: dict[str, Any] = {
			"installType": str(checker.game.install_type),
			"loaded": tab.is_loaded,
			"loadingError": tab.loading_error,
			"failure": failure,
			"dlls": None,
			"verdicts": None,
		}
		display: dict[str, Any] = {}
		if tab.is_loaded:
			result["dlls"] = [
				{
					"name": name,
					"isF4se": info["IsF4SE"],
					"supportsOg": info["SupportsOG"],
					"supportsNgAe": info["SupportsNGAE"],
					"supportsNg": info["SupportsNG"],
					"supportsAe": info["SupportsAE"],
					"addressIndependent": info["AddrIndependent"],
					"structureIndependent": info["StructIndependent"],
				}
				for name, info in tab.dll_info.items()
			]
			display = _f4se_display(tab)
			result["verdicts"] = [
				{
					"name": row["text"],
					"og": _F4SE_VERDICTS[row["values"][0]],
					"ng": _F4SE_VERDICTS[row["values"][1]],
					"ae": _F4SE_VERDICTS[row["values"][2]],
					"yourGame": _F4SE_VERDICTS[row["values"][3]] if len(row["values"]) > 3 else None,
				}
				for row in display["tree"]["rows"]
			]
		else:
			label = tab.label_loading
			display["loading"] = {
				"text": tab.sv_loading_text.get(),
				"color": _tk_colour(label, str(label.cget("foreground"))),
			}

		display["log"] = log_lines_of(golden, capture.records[first_record:], _F4SE_LOGGERS)
		result["display"] = display
		return {"f4se": result}
	finally:
		for dll in mapped:
			ctypes.windll.kernel32.FreeLibrary(ctypes.c_void_p(dll._handle))  # noqa: SLF001


def _f4se_display(tab: Any) -> dict[str, Any]:
	"""The built F4SE tab as shown: the Treeview's columns, rows and tag colours, its scrollbar, the heading and the
	read-only text with its coloured runs (as Tk ``line.char`` index ranges).
	"""
	from tkinter import Text, ttk  # noqa: PLC0415

	children = _grid_children(tab)
	tree = next(c for c in children if isinstance(c, ttk.Treeview))
	scrollbar = next(c for c in children if isinstance(c, ttk.Scrollbar))
	heading = next(c for c in children if isinstance(c, ttk.Label))
	text = next(c for c in children if isinstance(c, Text))

	columns = []
	for column in ("#0", *tree.cget("columns")):
		options = tree.column(column)
		columns.append({
			"id": column,
			"heading": str(tree.heading(column, "text")),
			"width": int(options["width"]),
			"stretch": bool(options["stretch"]),
			"anchor": str(options["anchor"]),
		})
	rows = []
	for item in tree.get_children():
		options = tree.item(item)
		rows.append({
			"text": str(options["text"]),
			"values": [str(v) for v in options["values"]],
			"tags": [str(t) for t in options["tags"]],
		})
	tags = sorted({tag for row in rows for tag in row["tags"]} | {"neutral", "good", "bad", "note", "independent"})

	return {
		"tree": {
			"columns": columns,
			"rows": rows,
			"tagColors": {tag: _tk_colour(tree, str(tree.tag_configure(tag, "foreground"))) for tag in tags},
		},
		"scrollbar": {"orient": str(scrollbar.cget("orient")), "column": int(scrollbar.grid_info()["column"])},
		"heading": {"text": str(heading.cget("text")), "font": _tk_font(heading)},
		"about": {
			"text": text.get("1.0", "end-1c"),
			"font": _tk_font(text),
			"wrap": str(text.cget("wrap")),
			"state": str(text.cget("state")),
			"runs": [
				{
					"tag": tag,
					"color": _tk_colour(text, str(text.tag_cget(tag, "foreground"))),
					"ranges": [str(index) for index in text.tag_ranges(tag)],
				}
				for tag in ("neutral", "good", "independent", "note")
			],
		},
	}


# The loggers a scan writes: the Overview refresh (cm_checker's Refresh Tab line, then the Overview's own), the settings
# save, and the Scanner tab's lifecycle (helpers).
_SCANNER_LOGGERS = {"cm_checker", "helpers", "tabs._overview", "game_info", "app_settings", "tabs._scanner"}


@operation("scanner-tab")
def scanner_tab(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""SCN-1..SCN-5, SCN-S1..SCN-S3, SCN-R1, SCN-R2 (no index), SCN-R3, SET-7, SET-8, B-1, B-4: the real ``CMChecker``
	with its Overview tab loaded (nothing stubbed), then its Scanner tab selected and driven like a user would.

	The script: read the side pane as created (``side``) and the empty results (``initial``); untick every box but
	Overview Issues, then Overview Issues too, then tick it again, reading Scan Game's state after each (``toggles``);
	press Scan Game and read the tab at once (``during``: the Overview refresh has run on the UI thread, the worker is
	started but not polled), then pump Tk until the results are populated (``results``), with ``settings.json`` as the
	scan left it and the log lines written. Then select a group row and an item row (``selection``: whether the details
	pane exists after each), leave the tab and come back (``returned``), untick the same boxes, delete ``settings.json``,
	select an item and scan again (``rescan``): the details pane is destroyed and, since no box differs from the stored
	settings, the file isn't written back.

	The panes are withdrawn as they are created (their placement is the App's OwnedPaneDocker's, proven by its own
	tests); the worker thread runs for real, and the result group order is Python set order, so ``groups`` is unordered
	(B-4). If the tab fails to load (SCN-1), ``loaded`` is false and ``switchAway`` is what leaving it does: selecting
	the Overview, then the never-loaded Tools tab.
	"""
	import time  # noqa: PLC0415
	from itertools import groupby  # noqa: PLC0415

	from app_settings import SETTINGS_PATH, AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview, _scanner  # noqa: PLC0415, PLC2701 - the modules the patched names are used in

	tooltips = _Tooltips()
	_scanner.ToolTip = tooltips
	_overview.ToolTip = _Tooltips()

	real_side, real_details = _scanner.SidePane, _scanner.ResultDetailsPane

	class HiddenSidePane(real_side):  # type: ignore[misc, valid-type]
		def __init__(self, *args: Any, **kwargs: Any) -> None:
			super().__init__(*args, **kwargs)
			self.wm_withdraw()

	class HiddenDetailsPane(real_details):  # type: ignore[misc, valid-type]
		def __init__(self, *args: Any, **kwargs: Any) -> None:
			super().__init__(*args, **kwargs)
			self.wm_withdraw()

	_scanner.SidePane = HiddenSidePane
	_scanner.ResultDetailsPane = HiddenDetailsPane

	root = session.root
	assert root is not None
	errors: list[BaseException] = []
	# Tab loads and switches run inside Tk callbacks; record what they raise instead of printing it.
	root.report_callback_exception = lambda _type, value, _tb: errors.append(value)  # type: ignore[method-assign]
	checker = CMChecker(root, AppSettings())
	root.update()
	if not checker.tabs[Tab.Overview].is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	capture = capture_logs()
	scanner = checker.tabs[Tab.Scanner]
	notebook = scanner.master

	def select(tab: Any) -> None:
		notebook.select(tab)
		root.update()

	select(scanner)
	if not scanner.is_loaded:
		label = scanner.label_loading
		loading = {"text": scanner.sv_loading_text.get(), "color": str(label.cget("foreground"))}
		before = len(errors)
		select(checker.tabs[Tab.Overview])
		to_overview = {"errors": len(errors) - before, "currentTab": type(checker.current_tab).__name__}
		before = len(errors)
		select(checker.tabs[Tab.Tools])
		to_tools = {
			"errors": len(errors) - before,
			"currentTab": type(checker.current_tab).__name__,
			"loaded": checker.tabs[Tab.Tools].is_loaded,
		}
		return {
			"scanner": {
				"loaded": False,
				"display": {
					"loadingLabel": loading,
					"switchAway": {"overview": to_overview, "tools": to_tools},
					"log": log_lines_of(golden, capture.records, _SCANNER_LOGGERS),
				},
			},
		}

	def checks() -> list[Any]:
		side = scanner.side_pane
		frame = next(c for c in side.winfo_children() if c.winfo_class() == "TLabelframe")
		return frame.pack_slaves()

	def read_side() -> dict[str, Any]:
		side = scanner.side_pane
		frame = next(c for c in side.winfo_children() if c.winfo_class() == "TLabelframe")
		button = side.button_scan
		return {
			"title": str(frame.cget("text")),
			"checks": [
				{
					"text": str(check.cget("text")),
					"checked": bool(side.getvar(str(check.cget("variable")))),
					"enabled": not check.instate(["disabled"]),
					"tooltip": tooltips.of(check),
				}
				for check in frame.pack_slaves()
			],
			"scan": {"text": str(button.cget("text")), "enabled": not button.instate(["disabled"]), "style": str(button.cget("style"))},
		}

	def read_tab() -> dict[str, Any]:
		tree = scanner.tree_results
		groups = []
		for group in tree.get_children():
			items = []
			for item in tree.get_children(group):
				values = tree.item(item, "values")
				items.append({
					"text": str(tree.item(item, "text")),
					"values": [str(v) for v in values] if values else [],
					"problem": _overview_problem(golden, scanner.tree_results_data[item]),
				})
			text = str(tree.item(group, "text"))
			if text == "Invalid Archive":
				# The Overview adds these in enabled-archive set order (T-15), and the stable sort by type + mod keeps
				# that order among items with the same mod: each such run is unordered.
				items = [golden.unordered(sorted(run, key=str)) for _, run in groupby(items, key=lambda i: i["problem"]["mod"])]
			groups.append({"text": text, "open": bool(tree.item(group, "open")), "items": items})
		side = scanner.side_pane
		return {
			"resultsInfo": scanner.sv_results_info.get(),
			"scanningLabel": None if scanner.label_scanning_text is None else scanner.sv_scanning_text.get(),
			"progress": float(scanner.dv_progress.get()),
			"selectable": str(tree.cget("selectmode")) != "none",
			"groups": golden.unordered(sorted(groups, key=operator.itemgetter("text"))),
			"scan": None if side is None else {"text": str(side.button_scan.cget("text")), "enabled": not side.button_scan.instate(["disabled"])},
			"detailsPane": scanner.details_pane is not None,
			"sidePane": side is not None,
		}

	def untick_data_boxes() -> list[dict[str, Any]]:
		toggles = []
		for check in checks()[1:]:
			check.invoke()
			toggles.append({"check": str(check.cget("text")), "scanEnabled": not scanner.side_pane.button_scan.instate(["disabled"])})
		return toggles

	def scan() -> tuple[dict[str, Any], dict[str, Any], list[dict[str, Any]]]:
		first = len(capture.records)
		scanner.side_pane.button_scan.invoke()
		during = read_tab()
		deadline = time.monotonic() + 30
		while str(scanner.side_pane.button_scan.cget("text")) != "Scan Game":
			if time.monotonic() > deadline:
				msg = "the scan didn't finish"
				raise RuntimeError(msg)
			root.update()
			time.sleep(0.01)
		log = log_lines_of(golden, capture.records[first:], _SCANNER_LOGGERS)
		return during, read_tab(), unordered_runs(golden, log, _is_archive_check)

	def first_group() -> Any:
		tree = scanner.tree_results
		return min(tree.get_children(), key=lambda g: str(tree.item(g, "text")))

	def select_row(row: Any) -> bool:
		scanner.tree_results.selection_set(row)
		root.update()
		return scanner.details_pane is not None

	side = read_side()
	initial = read_tab()
	toggles = untick_data_boxes()
	overview_check = checks()[0]
	overview_check.invoke()
	toggles.append({"check": str(overview_check.cget("text")), "scanEnabled": not scanner.side_pane.button_scan.instate(["disabled"])})
	overview_check.invoke()
	toggles.append({"check": str(overview_check.cget("text")), "scanEnabled": not scanner.side_pane.button_scan.instate(["disabled"])})

	settings_path = Path(SETTINGS_PATH).resolve()
	settings_before = file_snapshot(settings_path)
	during, results, log = scan()
	settings_after = file_snapshot(settings_path)

	selection = None
	if scanner.tree_results.get_children():
		group = first_group()
		selection = {
			"group": select_row(group),
			"item": select_row(scanner.tree_results.get_children(group)[0]),
		}

	select(checker.tabs[Tab.Overview])
	left = {"sidePane": scanner.side_pane is not None, "detailsPane": scanner.details_pane is not None}
	select(scanner)
	returned = read_tab()
	returned["side"] = read_side()
	returned["selection"] = list(scanner.tree_results.selection())

	untick_data_boxes()
	settings_path.unlink()
	if scanner.tree_results.get_children():
		select_row(scanner.tree_results.get_children(first_group())[0])
	rescan_during, rescan_results, _ = scan()

	return {
		"scanner": {
			"loaded": True,
			"usingStage": scanner.using_stage,
			"columns": list(scanner.tree_results.cget("columns")),
			"display": {
				"side": side,
				"initial": initial,
				"toggles": toggles,
				"during": during,
				"results": results,
				"log": log,
				"selection": selection,
				"left": left,
				"returned": returned,
				"rescanDuring": rescan_during,
				"rescan": rescan_results,
			},
			"settingsBefore": settings_before,
			"settingsAfter": settings_after,
			"settingsAfterRescan": file_snapshot(settings_path),
			"errors": len(errors),
		},
	}


# The name filter the archive-patcher operation types: a capital sharp s, which str.casefold() turns into "ss" (and
# str.lower() into "ß"), so it matches names spelled with "ß" or "ss" in any case (AP-2).
ARCHIVE_PATCHER_FILTER = "ẞ"

# The per-archive outcomes Patch All logs, in set order (T-15).
_PATCH_OUTCOMES = ("Patched to v", "Unrecognized format: ", "Skipping already-patched archive: ", "Unrecognized version [", "Failed patching (")


def apply_changes(golden: GoldenContext, root: Path) -> list[str]:
	"""Applies the tree's ``Changes/`` overlay to the machine: each file there is copied over the same root-relative path,
	and each ``<name>.delete`` deletes ``<name>``. Used by operations whose scenarios change files after the app has read
	them (an archive edited behind the Overview's back). Returns the paths changed, in a fixed order.
	"""
	changes = root / "Changes"
	if not changes.is_dir():
		return []
	applied = []
	for source in sorted(p for p in changes.rglob("*") if p.is_file()):
		relative = source.relative_to(changes)
		if relative.suffix == ".delete":
			target = root / relative.with_suffix("")
			target.unlink()
		else:
			target = root / relative
			target.write_bytes(source.read_bytes())
		applied.append(golden.path(target))
	return applied


def data_snapshot(golden: GoldenContext, data: Path) -> list[dict[str, Any]]:
	"""Every file under ``data`` after a mutating run: its path, size and SHA-256, sorted by path."""
	import hashlib  # noqa: PLC0415

	return [
		{"path": golden.path(p), "size": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
		for p in sorted(data.rglob("*"), key=lambda p: str(p).casefold())
		if p.is_file()
	]


def _log_pane_lines(text: Any) -> list[dict[str, Any]]:
	"""A ``Logger`` pane's messages, one per line (§9.5): the emoji and the space after it, the colour of the tag at the
	start of the line, and the text after the emoji, which must be untagged.

	Read per line rather than through ``_text_runs``, which drifted against Python string indices after the first line
	holding a non-BMP emoji when this was written (Tk 8.6 counts ``💭`` as two characters; ``_text_runs`` now maps the
	offsets), and so the goldens keep one entry per message. Because Tk counts ``💭`` as two, its tag on ``💭 `` ends
	before the space (``log_message`` tags ``len("💭 ")`` = 2 Tk characters, the
	emoji's surrogate pair); a space's colour can't be seen, so the emoji and its space are recorded as one.
	"""
	lines: list[dict[str, Any]] = []
	content = text.get("1.0", "end-1c")
	for number, line in enumerate(content.split("\n")[:-1] if content.endswith("\n") else content.split("\n"), start=1):
		tags = [t for t in text.tag_names(f"{number}.0") if t != "sel"]
		if text.compare(f"{number}.end", ">", f"{number}.0") and [t for t in text.tag_names(f"{number}.end - 1 chars") if t != "sel"]:
			msg = f"the pane's line {number} has tagged text after its emoji: {line!r}"
			raise RuntimeError(msg)
		lines.append({
			"emoji": line[:2],
			"color": str(text.tag_cget(tags[0], "foreground")) if tags else None,
			"text": line[2:],
		})
	return lines


def _is_patch_outcome(item: Any) -> bool:
	"""A pane line or log line Patch All writes once per archive, in set order."""
	if not isinstance(item, dict):
		return False
	text = item.get("text", item.get("message", ""))
	return text.startswith(_PATCH_OUTCOMES) or text.startswith("Archives: Checking ")


@operation("archive-patcher")
def archive_patcher(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""AP-1, AP-2, AP-3, LOG-1: the real ``ArchivePatcher`` opened over the real ``CMChecker`` once its Overview has
	loaded (nothing stubbed: the patcher's candidates are the Overview's archive sets), then driven through a fixed
	script, with its state read back after each step:

	1. ``opened``: as opened (target v1, no filter), including what About opens.
	2. ``patched``: Patch All.
	3. ``v8``: the ``v8 (NG)`` radio selected.
	4. ``filtered``: ``ARCHIVE_PATCHER_FILTER`` typed into the Name Filter (set, then its ``<KeyRelease>`` handler).
	5. ``patchedFiltered``: Patch All with the filter applied.
	6. ``unfiltered``: the filter emptied (another ``<KeyRelease>``).

	Between the Overview's load and the patcher opening, the tree's ``Changes/`` overlay is applied
	(``apply_changes``), so archives can differ from what the Overview read, as when a file changes while the app is
	open. ``changes`` lists what it touched.

	Each step records ``desiredVersion``, ``nameFilter`` and ``files`` (the set Patch All would patch, unordered); its
	``display`` has the filter caption, the Name Filter entry, the tree's rows in order, the log pane as lines (emoji,
	colour, text; the per-archive lines of a run as unordered, T-15) and the log lines written during the step (the
	root logger, the pane's ``logger`` and the Overview's refresh). After a Patch All, ``data`` is the Data folder's
	snapshot. The window's fixed parts (title, size, the frames and buttons) are in ``display`` of ``opened``.
	"""
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from patcher import _archives, _base  # noqa: PLC0415, PLC2701 - the modules the patched names are used in
	from tabs import _overview  # noqa: PLC0415, PLC2701

	opened: list[dict[str, Any]] = []
	_base.AboutWindow = lambda _parent, _cmc, width, height, title, text: opened.append({
		"window": "AboutWindow",
		"width": width,
		"height": height,
		"title": title,
		"text": golden.text(text),
	})
	_overview.ToolTip = _Tooltips()

	from tkinter import ttk  # noqa: PLC0415
	from types import SimpleNamespace  # noqa: PLC0415

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	key_handlers: dict[str, Callable[[Any], Any]] = {}

	class RecordingEntry(ttk.Entry):
		"""The Name Filter entry, which also keeps the handlers bound to it, so the script can call them."""

		def bind(self, sequence: Any = None, func: Any = None, add: Any = None) -> Any:
			if sequence is not None and func is not None:
				key_handlers[sequence] = func
			return super().bind(sequence, func, add)

	_archives.ttk = ModuleProxy(ttk, Entry=RecordingEntry)

	root = session.root
	assert root is not None
	machine_root = session.machine.root
	checker = CMChecker(root, AppSettings())
	root.update()
	if not checker.tabs[Tab.Overview].is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	changes = apply_changes(golden, machine_root)
	capture = capture_logs()
	window = _archives.ArchivePatcher(root, checker)
	root.update_idletasks()

	button_about, button_patch, frame_radio, label_filter = window.frame_top.pack_slaves()
	radio_og, radio_ng = sorted(_grid_children(frame_radio), key=lambda r: int(r.grid_info()["column"]))
	label_name = _grid_at(window.frame_middle, 0, 0)
	loggers = {"root", "logger", *_OVERVIEW_LOGGERS, "cm_checker"}
	data = Path(str(checker.game.data_path))
	read_from = 0

	def label(widget: Any) -> dict[str, Any]:
		return {"text": str(widget.cget("text")), "color": str(widget.cget("foreground"))}

	def step(name: str, *, patched: bool = False) -> dict[str, Any]:
		nonlocal read_from
		records = capture.records[read_from:]
		read_from = len(capture.records)
		tree = window._tree_files  # noqa: SLF001 - the patcher's file list
		result: dict[str, Any] = {
			"step": name,
			"desiredVersion": int(window.desired_version.get()),
			"nameFilter": window.name_filter,
			"files": golden.unordered(sorted(golden.path(p) for p in window.files_to_patch)),
			"display": {
				"caption": label(label_filter),
				"filterEntry": str(window.text_filter.get()),
				"tree": [str(tree.item(i, "text")) for i in tree.get_children()],
				"log": unordered_runs(golden, _log_pane_lines(window.logger._text), _is_patch_outcome),  # noqa: SLF001
				"logLines": unordered_runs(golden, log_lines_of(golden, records, loggers), _is_patch_outcome),
				"processingData": window.processing_data,
			},
		}
		if patched:
			result["data"] = data_snapshot(golden, data)
		return result

	def type_filter(text: str) -> None:
		window.text_filter.delete(0, "end")
		window.text_filter.insert(0, text)
		# The bound handler is called directly, with the one event field it reads: the window is never mapped in
		# record mode and never has focus, so Tk drops a generated key event before it reaches the binding.
		key_handlers["<KeyRelease>"](SimpleNamespace(widget=window.text_filter))

	first = step("opened")
	before = len(opened)
	button_about.invoke()
	# The size the modal asks for in its wm_geometry: in record mode it is never mapped, so Tk would report 1x1.
	first["display"].update({
		"title": window.wm_title(),
		"width": window.width,
		"height": window.height,
		"desiredVersion": {
			"title": str(frame_radio.cget("text")),
			"radios": [
				{
					"text": str(radio.cget("text")),
					"selected": str(window.getvar(str(radio.cget("variable")))) == str(radio.cget("value")),
				}
				for radio in (radio_og, radio_ng)
			],
		},
		"patch": {"text": str(button_patch.cget("text"))},
		"about": {"text": str(button_about.cget("text")), "opens": opened[before] if len(opened) > before else None},
		"nameLabel": label(label_name),
	})
	steps = [first]

	button_patch.invoke()
	steps.append(step("patched", patched=True))
	radio_ng.invoke()
	steps.append(step("v8"))
	type_filter(ARCHIVE_PATCHER_FILTER)
	steps.append(step("filtered"))
	button_patch.invoke()
	steps.append(step("patchedFiltered", patched=True))
	type_filter("")
	steps.append(step("unfiltered"))

	return {"archivePatcher": {"changes": changes, "steps": steps}}


class _LiveTooltips:
	"""Stands in for ``tktooltip.ToolTip`` where a widget's tooltip is kept, retexted and destroyed (the Scanner's
	details pane): each call returns a tooltip object whose ``msg`` the caller may change and which it may ``destroy``,
	so ``of`` reads the tooltip a widget has now, not the first one it was given.
	"""

	class _Tip:
		def __init__(self, live: dict[str, Any], widget: str, msg: str) -> None:
			self._live = live
			self._widget = widget
			self.msg = msg

		def destroy(self) -> None:
			if self._live.get(self._widget) is self:
				del self._live[self._widget]

	def __init__(self) -> None:
		self.live: dict[str, Any] = {}

	def __call__(self, widget: Any, msg: str, *_args: Any, **_kwargs: Any) -> Any:
		tip = self._Tip(self.live, str(widget), msg)
		self.live[str(widget)] = tip
		return tip

	def of(self, widget: Any) -> str | None:
		"""The live tooltip's text on ``widget``, or ``None``."""
		tip = self.live.get(str(widget))
		return None if tip is None else str(tip.msg)


@operation("scanner-details")
def scanner_details(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""SCN-R4..SCN-R7, THR-3: the Scanner's details pane, on the real ``CMChecker`` with its Overview loaded.

	The script: select the Scanner, untick every box but Overview Issues and Race Subgraphs, scan, then select every
	result in turn, sorted by (type, item text, relative path, mod) so the order doesn't depend on the set-ordered groups
	(B-4). For each: read the pane's rows (texts, fonts, value colour, wraplength), click the path (``os.startfile`` is
	recorded), left- and right-click the solution (``webbrowser.open`` and the clipboard are recorded), read the buttons,
	then let the previous Copy Details' ``after`` run and click Copy Details (its clipboard text, ``Copied!`` state and
	``after`` delay), and click File List when there is one (the real ``TreeWindow``, read back and destroyed). The copy
	button outlives a selection, so each item's ``buttons`` show the previous click's ``Copied!``, until it reverts.
	Finally a rescan: the details pane is destroyed when it starts (``afterRescan``).

	The panes are withdrawn as they are created; the clipboard and the copy button's ``after`` are patched on the
	instances that use them, so the system clipboard is never touched and the 3 s revert runs on demand.
	"""
	import os  # noqa: PLC0415
	import time  # noqa: PLC0415

	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview, _scanner  # noqa: PLC0415, PLC2701 - the modules the patched names are used in

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	tooltips = _LiveTooltips()
	_scanner.ToolTip = tooltips
	_overview.ToolTip = _Tooltips()
	started: list[str | None] = []
	opened: list[str] = []
	clipboard: list[str] = []
	scheduled: list[tuple[int, Any]] = []
	windows: list[dict[str, Any]] = []
	os.startfile = lambda path, *_a, **_k: started.append(_path_text(golden, path))  # type: ignore[attr-defined]
	_scanner.webbrowser = ModuleProxy(_scanner.webbrowser, open=lambda url, *_a, **_k: opened.append(url))

	real_side, real_details, real_tree_window = _scanner.SidePane, _scanner.ResultDetailsPane, _scanner.TreeWindow

	def patch_clipboard(widget: Any) -> None:
		widget.clipboard_clear = lambda **_k: clipboard.clear()
		widget.clipboard_append = lambda text, **_k: clipboard.append(text)

	class HiddenSidePane(real_side):  # type: ignore[misc, valid-type]
		def __init__(self, *args: Any, **kwargs: Any) -> None:
			super().__init__(*args, **kwargs)
			self.wm_withdraw()

	class HiddenDetailsPane(real_details):  # type: ignore[misc, valid-type]
		def __init__(self, *args: Any, **kwargs: Any) -> None:
			super().__init__(*args, **kwargs)
			self.wm_withdraw()
			# copy_text_button works on the button's master: its clipboard and its after().
			patch_clipboard(self.frame_buttons)
			self.frame_buttons.after = lambda ms, func=None, *args: scheduled.append((ms, lambda: func(*args)))

	def tree_window(*args: Any, **kwargs: Any) -> None:
		window = real_tree_window(*args, **kwargs)
		tree = window.tree_items
		windows.append({
			"window": "TreeWindow",
			"width": window.width,
			"height": window.height,
			"title": str(window.wm_title()),
			"text": window.win_text,
			"headers": [str(tree.heading(f"#{c}", "text")) for c in range(len(window.headers))],
			"rows": [[str(tree.item(i, "text")), *(str(v) for v in tree.item(i, "values"))] for i in tree.get_children()],
		})
		window.destroy()

	_scanner.SidePane = HiddenSidePane
	_scanner.ResultDetailsPane = HiddenDetailsPane
	_scanner.TreeWindow = tree_window

	root = session.root
	assert root is not None
	errors: list[BaseException] = []
	root.report_callback_exception = lambda _type, value, _tb: errors.append(value)  # type: ignore[method-assign]
	checker = CMChecker(root, AppSettings())
	root.update()
	scanner = checker.tabs[Tab.Scanner]
	scanner.master.select(scanner)
	root.update()
	if not scanner.is_loaded:
		msg = "the Scanner tab didn't load"
		raise RuntimeError(msg)
	# copy_text (the solution's right-click) works on the tab's clipboard.
	patch_clipboard(scanner)

	side = scanner.side_pane
	frame = next(c for c in side.winfo_children() if c.winfo_class() == "TLabelframe")
	for check in frame.pack_slaves():
		if str(check.cget("text")) not in {"Overview Issues", "Race Subgraphs"}:
			check.invoke()

	def scan() -> None:
		scanner.side_pane.button_scan.invoke()
		deadline = time.monotonic() + 30
		while str(scanner.side_pane.button_scan.cget("text")) != "Scan Game":
			if time.monotonic() > deadline:
				msg = "the scan didn't finish"
				raise RuntimeError(msg)
			root.update()
			time.sleep(0.01)

	scan()

	def click(widget: Any, sequence: str, record: list[Any]) -> Any:
		"""Fires ``sequence`` on ``widget`` and returns what it recorded, or ``None`` if nothing is bound."""
		before = len(record)
		widget.event_generate(sequence)
		root.update()
		return record[before] if len(record) > before else None

	def right_click(widget: Any) -> str | None:
		clipboard.clear()
		clipboard.append("<untouched>")
		widget.event_generate("<Button-3>")
		root.update()
		text = "".join(clipboard)
		return None if text == "<untouched>" else text

	def state(button: Any) -> dict[str, Any]:
		return {"text": str(button.cget("text")), "enabled": not button.instate(["disabled"])}

	def read_rows(pane: Any) -> list[dict[str, Any]]:
		rows = []
		children = _grid_children(pane)
		for label in (c for c in children if int(c.grid_info()["column"]) == 0):
			row = int(label.grid_info()["row"])
			value = _grid_at(pane, row, 1)
			wraplength = str(value.cget("wraplength"))
			rows.append({
				"label": str(label.cget("text")),
				"labelFont": _tk_font(label),
				"value": _label_text(value),
				"valueFont": _tk_font(value),
				"valueColor": _tk_colour(value, str(value.cget("foreground"))),
				"wraplength": int(wraplength) if wraplength not in {"", "0"} else None,
			})
		return rows

	tree = scanner.tree_results
	items = [i for g in tree.get_children() for i in tree.get_children(g)]

	def key(item: Any) -> tuple[str, ...]:
		p = _overview_problem(golden, scanner.tree_results_data[item])
		return (p["type"], p["itemText"], p["relativePath"], p["mod"])

	details = []
	for item in sorted(items, key=key):
		tree.selection_set(item)
		root.update()
		pane = scanner.details_pane
		path_label, solution_label = pane.label_file_path, pane.label_solution
		buttons = pane.frame_buttons.pack_slaves()
		entry: dict[str, Any] = {
			"type": key(item)[0],
			"itemText": key(item)[1],
			"display": {
				"rows": read_rows(pane),
				"path": {"cursor": str(path_label.cget("cursor")), "tooltip": tooltips.of(path_label)},
				"solution": {"tooltip": tooltips.of(solution_label)},
				"buttons": [state(b) for b in buttons],
			},
			"pathOpens": click(path_label, "<Button-1>", started),
			"solutionOpens": click(solution_label, "<Button-1>", opened),
			"solutionCopies": right_click(solution_label),
		}
		while scheduled:
			_delay, revert = scheduled.pop(0)
			revert()
		copy_button = next(b for b in buttons if str(b.cget("text")) == "Copy Details")
		clipboard.clear()
		copy_button.invoke()
		entry["copy"] = {
			"copies": "".join(clipboard),
			"clicked": state(copy_button),
			"revertAfterMs": [ms for ms, _ in scheduled],
		}
		files = next((b for b in buttons if str(b.cget("text")) == "File List"), None)
		if files is not None:
			files.invoke()
			root.update()
			entry["fileList"] = windows.pop() if windows else None
		details.append(entry)

	scan()

	return {
		"scanner": {
			"usingStage": scanner.using_stage,
			"details": details,
			"afterRescan": {"detailsPane": scanner.details_pane is not None},
			"errors": len(errors),
		},
	}


# The boxes each scanner-scan run ticks, by ScanSetting member name: every box first, then each box alone in pane order.
_SCAN_RUNS: list[set[str]] = [
	{"OverviewIssues", "Errors", "WrongFormat", "LoosePrevis", "JunkFiles", "ProblemOverrides", "RaceSubgraphs"},
	{"OverviewIssues"},
	{"Errors"},
	{"WrongFormat"},
	{"LoosePrevis"},
	{"JunkFiles"},
	{"ProblemOverrides"},
	{"RaceSubgraphs"},
]


def _scan_problem(golden: GoldenContext, problem: Any) -> dict[str, Any]:
	"""``_overview_problem`` plus the file list a ``SimpleProblemInfo`` can carry (the Race Subgraphs problem's
	``(count, module)`` pairs, SCN-S5), or ``None``.
	"""
	projected = _overview_problem(golden, problem)
	file_list = getattr(problem, "file_list", None)
	projected["fileList"] = None if file_list is None else [[count, _path_text(golden, path)] for count, path in file_list]
	return projected


def _mod_file_index(golden: GoldenContext, mod_files: Any) -> dict[str, Any] | None:
	"""``scan_settings.mod_files`` (SCN-S6) in dict order: each entry's key (``str`` of the mod-relative path, or the
	file name), mod and staged path. ``None`` when no index was built.
	"""
	if mod_files is None:
		return None

	def entries(table: dict[Any, tuple[str, Any]]) -> list[dict[str, Any]]:
		return [{"key": str(key), "mod": mod, "path": _path_text(golden, path)} for key, (mod, path) in table.items()]

	return {
		"folders": entries(mod_files.folders),
		"files": entries(mod_files.files),
		"modules": entries(mod_files.modules),
		"archives": entries(mod_files.archives),
	}


@operation("scanner-scan")
def scanner_scan(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""SCN-S4..S6, SCN-C1..C9, SCN-R2 (with an index), B-3, B-5: the real ``CMChecker`` with its Overview loaded and its
	Scanner selected, scanned once per ``_SCAN_RUNS`` entry by ticking those boxes and pressing Scan Game.

	The worker runs synchronously on the UI thread (issue #8's harness design): ``_scanner.threading.Thread`` is
	replaced by one whose ``start`` runs the target inline, so the walk's directory order is the only order there is,
	and the B-2 completion-flag race (exempt, ADR-0003) can't happen. Each progress update the worker queues (the
	top-level folder tuple, or a folder name) is drained at once by the tab's own ``check_scan_progress``, with its
	``after`` re-arm suppressed, and the scanning label and progress bar are read after it (``updates``): the finest
	timing the 100 ms polling could show. The problems list is left to the real polling, which then populates the
	results (``results``). A worker that raises is caught where its thread would have died: the error is recorded
	(``workerError``, with Python's object address masked) and the tab is read after its polling has spun for a while,
	stuck (B-3); the script stops there, since Scan Game stays disabled.

	``index`` is the mod file index the run built, or ``None``; ``workerLog`` the lines the worker logged.
	"""
	import re  # noqa: PLC0415
	import time  # noqa: PLC0415
	from itertools import groupby  # noqa: PLC0415
	from queue import Queue  # noqa: PLC0415
	from types import SimpleNamespace  # noqa: PLC0415

	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from scan_settings import ScanSetting  # noqa: PLC0415
	from tabs import _overview, _scanner  # noqa: PLC0415, PLC2701 - the modules the patched names are used in

	_scanner.ToolTip = _Tooltips()
	_overview.ToolTip = _Tooltips()

	real_side, real_details = _scanner.SidePane, _scanner.ResultDetailsPane

	class HiddenSidePane(real_side):  # type: ignore[misc, valid-type]
		def __init__(self, *args: Any, **kwargs: Any) -> None:
			super().__init__(*args, **kwargs)
			self.wm_withdraw()

	class HiddenDetailsPane(real_details):  # type: ignore[misc, valid-type]
		def __init__(self, *args: Any, **kwargs: Any) -> None:
			super().__init__(*args, **kwargs)
			self.wm_withdraw()

	_scanner.SidePane = HiddenSidePane
	_scanner.ResultDetailsPane = HiddenDetailsPane

	root = session.root
	assert root is not None
	errors: list[BaseException] = []
	root.report_callback_exception = lambda _type, value, _tb: errors.append(value)  # type: ignore[method-assign]
	capture = capture_logs()

	worker: dict[str, Any] = {}

	class InlineThread:
		"""``threading.Thread`` for the scan worker: ``start`` runs it to completion on the calling (UI) thread."""

		def __init__(self, target: Callable[..., None], args: tuple[Any, ...] = ()) -> None:
			self._target, self._args = target, args

		def start(self) -> None:
			worker["settings"] = self._args[0]
			first = len(capture.records)
			try:
				self._target(*self._args)
			except Exception as error:  # noqa: BLE001 - the thread died here; the tab is left polling (B-3)
				worker["error"] = error
			worker["log"] = capture.records[first:]

	_scanner.threading = SimpleNamespace(Thread=InlineThread)  # type: ignore[attr-defined]

	checker = CMChecker(root, AppSettings())
	root.update()
	if not checker.tabs[Tab.Overview].is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	scanner = checker.tabs[Tab.Scanner]
	scanner.master.select(scanner)
	root.update()
	if not scanner.is_loaded:
		msg = "the Scanner tab didn't load"
		raise RuntimeError(msg)

	updates: list[dict[str, Any]] = []

	class ProbedQueue(Queue):  # type: ignore[type-arg]
		"""The progress queue: each folder tuple or name is drained by the tab as soon as it is put."""

		def put(self, item: Any, block: bool = True, timeout: float | None = None) -> None:  # noqa: FBT001, FBT002
			super().put(item, block, timeout)
			if isinstance(item, list):
				return
			update = {"folders": list(item)} if isinstance(item, tuple) else {"folder": item}
			# check_scan_progress re-arms itself with after() while the worker runs; the real poll does that instead.
			root.after = lambda *_args, **_kwargs: ""  # type: ignore[method-assign]
			try:
				scanner.check_scan_progress(worker.get("settings"))
			finally:
				del root.after
			updates.append({"update": update, "scanningText": scanner.sv_scanning_text.get(), "progress": float(scanner.dv_progress.get())})

	scanner.queue_progress = ProbedQueue()

	def read_tab() -> dict[str, Any]:
		tree = scanner.tree_results
		groups = []
		for group in tree.get_children():
			items = []
			for item in tree.get_children(group):
				values = tree.item(item, "values")
				items.append({
					"text": str(tree.item(item, "text")),
					"values": [str(v) for v in values] if values else [],
					"problem": _scan_problem(golden, scanner.tree_results_data[item]),
				})
			text = str(tree.item(group, "text"))
			if text == "Invalid Archive":
				# The Overview adds these in enabled-archive set order (T-15); each same-mod run is unordered.
				items = [golden.unordered(sorted(run, key=str)) for _, run in groupby(items, key=lambda i: i["problem"]["mod"])]
			groups.append({"text": text, "open": bool(tree.item(group, "open")), "items": items})
		side = scanner.side_pane
		return {
			"resultsInfo": scanner.sv_results_info.get(),
			"scanningLabel": None if scanner.label_scanning_text is None else scanner.sv_scanning_text.get(),
			"progress": float(scanner.dv_progress.get()),
			"groups": golden.unordered(sorted(groups, key=operator.itemgetter("text"))),
			"scan": {"text": str(side.button_scan.cget("text")), "enabled": not side.button_scan.instate(["disabled"])},
		}

	runs = []
	for ticked in _SCAN_RUNS:
		side = scanner.side_pane
		frame = next(c for c in side.winfo_children() if c.winfo_class() == "TLabelframe")
		for check, setting in zip(frame.pack_slaves(), ScanSetting, strict=True):
			if bool(side.bool_vars[setting].get()) != (setting.name in ticked):
				check.invoke()
		updates.clear()
		worker.clear()
		side.button_scan.invoke()
		if "error" in worker:
			# The reference's polling spins forever on a dead worker; let it run a few times before reading the tab.
			deadline = time.monotonic() + 0.5
			while time.monotonic() < deadline:
				root.update()
				time.sleep(0.01)
		else:
			deadline = time.monotonic() + 30
			while str(side.button_scan.cget("text")) != "Scan Game":
				if time.monotonic() > deadline:
					msg = "the scan didn't finish"
					raise RuntimeError(msg)
				root.update()
				time.sleep(0.01)
		error = worker.get("error")
		runs.append({
			"boxes": [setting.value[0] for setting in ScanSetting if setting.name in ticked],
			"updates": list(updates),
			"workerError": None if error is None else re.sub(r"0x[0-9A-Fa-f]+", "0x<ADDRESS>", golden.text(str(error))),
			"workerLog": log_lines_of(golden, worker.get("log", []), {"helpers", "tabs._scanner"}),
			"index": _mod_file_index(golden, worker["settings"].mod_files),
			"results": read_tab(),
		})
		if error is not None:
			break

	return {"scanner": {"usingStage": scanner.using_stage, "runs": runs, "errors": len(errors)}}


# The keys a downgrader-run scenario's Script/downgrader-run.json may set, each optional.
_DOWNGRADER_SCRIPT_KEYS = {"desiredVersion", "keepBackups", "deletePatches"}

# The loggers a Patch All run writes through: the log pane (logger), cmc.refresh_tab (cm_checker), the settings save
# (app_settings) and the Overview refresh.
_DOWNGRADER_RUN_LOGGERS = {"logger", "cm_checker", "app_settings", *_OVERVIEW_LOGGERS}


def _downgrader_frame(frame: Any) -> dict[str, Any]:
	"""A Downgrader Current frame's version labels, in row order: each one's text and colour (DG-3)."""
	values = sorted(
		(c for c in _grid_children(frame) if int(c.grid_info()["column"]) == 1),
		key=lambda c: int(c.grid_info()["row"]),
	)
	return {"values": [{"text": str(v.cget("text")), "color": str(v.cget("foreground"))} for v in values]}


def machine_snapshot(golden: GoldenContext, root: Path, folders: tuple[str, ...]) -> list[dict[str, Any]]:
	"""Every file under the root's ``folders`` after a mutating run: path, size, SHA-256 and whether it is read-only,
	sorted by the case-folded path.
	"""
	import hashlib  # noqa: PLC0415
	import stat  # noqa: PLC0415

	files = [p for folder in folders if (root / folder).is_dir() for p in (root / folder).rglob("*") if p.is_file()]
	return [
		{
			"path": golden.path(p),
			"size": p.stat().st_size,
			"sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
			"readonly": bool(p.stat().st_file_attributes & stat.FILE_ATTRIBUTE_READONLY),
		}
		for p in sorted(files, key=lambda p: str(p).casefold())
	]


@operation("downgrader-run")
def downgrader_run(session: Session, golden: GoldenContext) -> dict[str, Any]:
	"""DG-4, DG-5, DG-6, NET-3, THR-2, SET-7: the real ``Downgrader`` over the real ``CMChecker`` (its Overview loaded
	in full, as at startup), set up by the scenario's ``Script/downgrader-run.json`` (the Desired Version radio to click,
	the Keep Backups / Delete Patches values to click the boxes to), then Patch All clicked and the Tk loop pumped until
	the patch queue is done or stuck.

	``afterClick`` is the window as the click's callback leaves it, before any download completes: the driver holds every
	download until the callback has returned, as a real download takes longer than the loop (otherwise a fake download
	could finish first and be applied inside the callback, which a real one never is). ``final`` is the window once the
	queue has emptied, or once its download thread has died (B-10: the poll then waits forever, so the driver pumps a
	little longer to show nothing else happens). Each holds the button, the close guard, the progress bar, the versions
	the window holds, and in ``display`` the version labels, the log pane's lines and the log lines written since the
	previous one (the Overview refresh's archive checks unordered, T-15).

	``errors`` counts what died: ``callback`` for Tk callbacks (``patch_files``, a poll), ``thread`` for download threads.
	``files`` is the Game and Cwd folders after the run (sizes, hashes, read-only), and ``settings`` is ``settings.json``
	byte for byte (SET-7). Every HTTP request is in the golden's ``httpRequests``.
	"""
	import json  # noqa: PLC0415
	import threading  # noqa: PLC0415
	import time  # noqa: PLC0415

	import downgrader  # noqa: PLC0415
	from app_settings import AppSettings  # noqa: PLC0415
	from cm_checker import CMChecker  # noqa: PLC0415
	from enums import Tab  # noqa: PLC0415
	from tabs import _overview  # noqa: PLC0415, PLC2701 - the module the patched names are used in

	from harness.fakes import ModuleProxy  # noqa: PLC0415

	machine_root = session.machine.root
	script_path = machine_root / "Script" / "downgrader-run.json"
	script: dict[str, Any] = json.loads(script_path.read_text("utf-8")) if script_path.is_file() else {}
	if set(script) - _DOWNGRADER_SCRIPT_KEYS:
		msg = f"{script_path}: unknown keys {sorted(set(script) - _DOWNGRADER_SCRIPT_KEYS)}"
		raise RuntimeError(msg)

	downgrader.ToolTip = _Tooltips()
	_overview.ToolTip = _Tooltips()
	# The run's print() progress notes went to the frozen exe's absent console, so they aren't part of the outcome; here
	# they would land in the golden, which the child writes to stdout.
	downgrader.print = lambda *_args, **_kwargs: None  # type: ignore[attr-defined]

	released = threading.Event()
	fake_get = session.http.get  # type: ignore[attr-defined]

	def held_get(url: str, **kwargs: Any) -> Any:
		if not released.wait(30):
			msg = "the download was never released"
			raise RuntimeError(msg)
		return fake_get(url, **kwargs)

	downgrader.requests = ModuleProxy(session.http, get=held_get)

	thread_errors: list[str] = []
	callback_errors: list[str] = []
	threading.excepthook = lambda args: thread_errors.append(args.exc_type.__name__)

	root = session.root
	assert root is not None
	root.report_callback_exception = lambda kind, _value, _tb: callback_errors.append(kind.__name__)  # type: ignore[method-assign]
	checker = CMChecker(root, AppSettings())
	root.update()
	if not checker.tabs[Tab.Overview].is_loaded:
		msg = "the Overview tab didn't load"
		raise RuntimeError(msg)

	window = downgrader.Downgrader(root, checker)
	root.update_idletasks()

	radios = _grid_children(_grid_at(window, 0, 1))
	checks = {str(c.cget("text")): c for c in _grid_children(_grid_at(window, 2, 1))}
	if "desiredVersion" in script:
		(radio,) = [r for r in radios if str(r.cget("text")) == script["desiredVersion"]]
		radio.invoke()
	for key, text in (("keepBackups", "Keep Backups"), ("deletePatches", "Delete Patches")):
		if key in script and bool(window.getvar(str(checks[text].cget("variable")))) != script[key]:
			checks[text].invoke()

	capture = capture_logs()
	read_from = 0
	pane = window.logger._text  # noqa: SLF001 - the log pane's only widget

	def state() -> dict[str, Any]:
		nonlocal read_from
		records = capture.records[read_from:]
		read_from = len(capture.records)
		return {
			"patchEnabled": not window.button_patch.instate(["disabled"]),
			"processingData": window.processing_data,
			"progress": float(window.progress_var.get()),
			"versions": [{"file": name, "installType": str(t)} for name, t in window.current_versions.items()],
			"display": {
				"game": _downgrader_frame(window.frame_game),
				"creationKit": _downgrader_frame(window.frame_ck),
				# An empty pane has no lines (_log_pane_lines would read its empty text as one empty line).
				"log": _log_pane_lines(pane) if pane.get("1.0", "end-1c") else [],
				"logLines": unordered_runs(golden, log_lines_of(golden, records, _DOWNGRADER_RUN_LOGGERS), _is_archive_check),
			},
		}

	window.button_patch.invoke()
	after_click = state()
	released.set()

	deadline = time.monotonic() + 30
	while window.download_or_patch_in_progress:
		thread = window.download_thread
		if thread is not None and not thread.is_alive():
			# B-10: the thread died; the poll would wait for it forever. Pump a little longer to show nothing changes.
			for _ in range(30):
				root.update()
				time.sleep(0.01)
			break
		if time.monotonic() > deadline:
			msg = "the patch queue neither finished nor got stuck"
			raise RuntimeError(msg)
		root.update()
		time.sleep(0.01)
	root.update()

	return {
		"downgraderRun": {
			"script": script,
			"afterClick": after_click,
			"final": state(),
			"errors": {"callback": len(callback_errors), "thread": len(thread_errors)},
			"files": machine_snapshot(golden, machine_root, ("Game", "Cwd")),
			"settings": file_snapshot(machine_root / "Cwd" / "settings.json"),
		},
	}
