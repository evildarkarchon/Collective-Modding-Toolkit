"""Running the unmodified Reference Implementation headless against a materialized scenario.

A ``Session`` puts ``src/`` on ``sys.path``, imports the Reference Implementation's modules, and swaps the names they
use for the OS (see ``harness.fakes``) for fakes driven by the scenario's host block. Nothing in ``src/`` is edited:
headless behaviour comes only from monkeypatching, a withdrawn Tk root and a duck-typed ``cmc`` stub (ADR-0004).

One session per process: the Reference Implementation keeps module-level state (``app_settings.download_source``,
``utils.win11_24h2``), so ``record.py`` runs each scenario in a fresh child process.
"""

import os
import sys
from dataclasses import dataclass
from tkinter import PhotoImage, StringVar, Tk
from typing import TYPE_CHECKING, Any, Literal

from harness import SRC
from harness.fakes import (
	PARENT_PID,
	DialogScript,
	FakeRequests,
	FakeShell32,
	FakeWinreg,
	HostError,
	ModuleProxy,
	fake_ntdll,
	file_version_wrapper,
	process_factory,
)

if TYPE_CHECKING:
	from harness.scenario import Machine

Mode = Literal["record", "show"]


def require_tk_86() -> None:
	"""Fails unless this interpreter's Tk is 8.6, the Tk the Reference Implementation ships and was written against.

	The bundled sv_ttk theme refuses Tk 9 (``version conflict for package "Tk"``), and widget-level results read back
	off Tk could differ under it. python.org's CPython 3.14 installers bundle Tk 8.6; uv's managed builds bundle Tk 9,
	so create the venv with ``uv venv --python <path to a python.org python.exe>``.
	"""
	from tkinter import TkVersion  # noqa: PLC0415

	# Compared as text: TkVersion is a float, and 8.6 has no exact binary representation.
	if str(TkVersion) != "8.6":
		msg = f"The parity driver needs Tk 8.6, but {sys.executable} has Tk {TkVersion}. See parity/README.md."
		raise RuntimeError(msg)


class CmcStub:
	"""A duck-typed stand-in for ``helpers.CMCheckerInterface`` (the ``cmc`` every tab and window receives).

	It carries the state the real ``CMChecker`` would (``game``, ``pc``, ``overview_problems``, ``settings`` and the
	bound ``StringVar``s), and routes ``refresh_tab`` back to the driver by recording it in ``refreshed``.
	"""

	def __init__(self, root: Tk, *, game: Any = None, pc: Any = None, settings: Any = None) -> None:
		self.root = root
		self.install_type_sv = StringVar(root)
		self.game_path_sv = StringVar(root)
		self.specs_sv_1 = StringVar(root)
		self.specs_sv_2 = StringVar(root)
		self.game = game
		self.pc = pc
		self.settings = settings
		self.overview_problems: list[Any] = []
		self.refreshed: list[Any] = []
		self._images: dict[str, PhotoImage] = {}

	def refresh_tab(self, tab: Any) -> None:
		"""Records the request; operations that need the refresh to happen run it themselves."""
		self.refreshed.append(tab)

	def get_image(self, relative_path: str) -> PhotoImage:
		"""Loads an image from the assets folder, cached as ``CMChecker.get_image`` caches it."""
		import utils  # noqa: PLC0415 - imported once the session has put src/ on sys.path

		if relative_path not in self._images:
			self._images[relative_path] = PhotoImage(master=self.root, file=utils.get_asset_path(relative_path))
		return self._images[relative_path]


class Session:
	"""The Reference Implementation, imported and patched for one materialized scenario.

	``mode="record"`` also creates the withdrawn Tk root and makes modal windows stay hidden and ungrabbed.
	``mode="show"`` leaves windows real, for screenshots of the running app; ``main.py`` then creates its own root.
	"""

	def __init__(self, machine: Machine, mode: Mode = "record") -> None:
		self.machine = machine
		self.mode = mode
		host = machine.host
		self.dialogs = DialogScript(host.get("dialogs", []), machine.expand)
		self.http = FakeRequests(machine.scenario.http_directory)
		self.root: Tk | None = None

	def start(self) -> None:
		"""Imports the Reference Implementation and installs the fakes. Call once, before anything else touches it."""
		require_tk_86()
		host = self.machine.host
		expand = self.machine.expand

		sys.path.insert(0, str(SRC))
		# The folder holding the exe. utils.get_asset_path reads sys._MEIPASS, the frozen exe's equivalent. Without an
		# appDir the reference's own assets folder is used, which the GUI needs for its fonts and images.
		sys._MEIPASS = expand(host["appDir"]) if "appDir" in host else str(SRC)  # type: ignore[attr-defined]  # noqa: SLF001
		os.chdir(expand(host["cwd"]) if "cwd" in host else self.machine.root)
		if "argv0" in host:
			sys.argv = [host["argv0"]]

		# Importing the modules runs no Tk code and no OS lookups that matter: utils and mod_manager_info compute
		# win11_24h2 from the real build, which is overridden below. app_settings is NOT imported here, because its
		# import is when the Download Source is read (SET-2); operations import it once the patches are in.
		import downgrader  # noqa: PLC0415
		import game_info  # noqa: PLC0415
		import helpers  # noqa: PLC0415
		import mod_manager_info  # noqa: PLC0415
		import modal_window  # noqa: PLC0415
		import utils  # noqa: PLC0415
		from tabs import _overview  # noqa: PLC0415, PLC2701 - the module the patched names are used in

		# Plain existence semantics, which match the .NET exemption, whatever build records the golden (ADR-0004).
		utils.win11_24h2 = False
		mod_manager_info.win11_24h2 = False

		processes = host.get("processes", [])
		registry = FakeWinreg(host.get("registry", {}), expand)
		helpers.winreg = registry
		utils.winreg = registry
		game_info.winreg = registry

		helpers.windll = ModuleProxy(helpers.windll, ntdll=fake_ntdll(wine=host.get("os", {}).get("wine", False)))
		helpers.platform = ModuleProxy(helpers.platform, system=lambda: self._os("system"), release=lambda: self._os("release"))
		helpers.sys = ModuleProxy(sys, getwindowsversion=lambda: _WindowsVersion(self._os("build")))
		helpers.psutil = ModuleProxy(helpers.psutil, virtual_memory=self._virtual_memory)

		utils.windll = ModuleProxy(utils.windll, shell32=FakeShell32(utils.windll.shell32, host.get("knownFolders", {}), expand))
		utils.os = ModuleProxy(utils.os, getppid=lambda: PARENT_PID)
		utils.Process = process_factory(processes, expand)
		version = file_version_wrapper(utils.get_file_version, processes, expand)
		utils.get_file_version = version
		_overview.get_file_version = version

		env = {k.upper(): expand(v) for k, v in host.get("env", {}).items()}
		# Only the host block's variables exist, so the recording machine's real LOCALAPPDATA can't leak in.
		game_info.os = ModuleProxy(game_info.os, getenv=lambda name, default=None: env.get(name.upper(), default))

		game_info.messagebox = self.dialogs.module(game_info.messagebox, "messagebox")
		game_info.filedialog = self.dialogs.module(game_info.filedialog, "filedialog")
		_overview.messagebox = self.dialogs.module(_overview.messagebox, "messagebox")

		utils.requests = self.http
		downgrader.requests = self.http

		if self.mode == "record":
			modal_window.ModalWindow.wm_deiconify = lambda _self: None  # type: ignore[method-assign]
			modal_window.ModalWindow.grab_set = lambda _self: None  # type: ignore[method-assign]
			self.root = Tk()
			self.root.wm_withdraw()

	def make_cmc(self, **state: Any) -> CmcStub:
		"""A ``cmc`` stub on the session's withdrawn root, for operations that drive one tab or window."""
		if self.root is None:
			msg = "make_cmc needs a record-mode session"
			raise RuntimeError(msg)
		return CmcStub(self.root, **state)

	def _os(self, key: str) -> Any:
		if "os" not in self.machine.host:
			msg = f"The host block has no os, but the reference read os.{key}"
			raise HostError(msg)
		return self.machine.host["os"][key]

	def _virtual_memory(self) -> Any:
		if "pc" not in self.machine.host:
			msg = "The host block has no pc, but the reference read the total memory"
			raise HostError(msg)
		return _Memory(self.machine.host["pc"]["ramBytes"])


@dataclass(frozen=True)
class _WindowsVersion:
	"""The part of ``sys.getwindowsversion()`` the reference reads."""

	build: int


@dataclass(frozen=True)
class _Memory:
	"""The part of ``psutil.virtual_memory()`` the reference reads."""

	total: int

