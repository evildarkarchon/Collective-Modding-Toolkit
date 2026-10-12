"""The operations a scenario can name in ``scenario.json``: each runs one path of the Reference Implementation and
returns its golden. A C# test runs the matching Core or App call and compares against that golden.

To add one, write a function taking the started ``Session`` and a ``GoldenContext`` and returning a JSON-able dict,
and register it with ``@operation("<name>")``. Read results that only exist after the GUI is built off the widgets,
not by re-deriving them (ADR-0004).
"""

import base64
import logging
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
