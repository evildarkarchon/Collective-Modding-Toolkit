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


def _text_runs(text: Any) -> list[dict[str, Any]]:
	"""A Tk ``Text`` widget's content as runs in reading order: each tagged range with its tag's foreground, and the
	untagged text between them with ``None``. The trailing newline Tk always keeps is left out.
	"""
	content = text.get("1.0", "end-1c")
	starts: list[tuple[int, int, str | None]] = []
	for tag in text.tag_names():
		if tag == "sel":
			continue
		ranges = text.tag_ranges(tag)
		for start, end in zip(ranges[::2], ranges[1::2], strict=True):
			first = int(text.count("1.0", start, "chars")[0] or 0) if text.compare(start, ">", "1.0") else 0
			length = int(text.count(start, end, "chars")[0] or 0)
			starts.append((first, first + length, str(text.tag_cget(tag, "foreground")) or None))
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
				"log": _text_runs(window.logger._text),  # noqa: SLF001 - the pane's only widget
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
			"groups": golden.unordered(sorted(groups, key=lambda g: g["text"])),
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
		return sorted(tree.get_children(), key=lambda g: str(tree.item(g, "text")))[0]

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
