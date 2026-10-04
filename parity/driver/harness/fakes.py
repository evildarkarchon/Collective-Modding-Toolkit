"""Fakes for everything the Reference Implementation reads from the OS, driven by a scenario's ``host`` block.

``harness.reference`` installs them by replacing each name **where it is used** (``helpers.winreg``,
``game_info.messagebox``, ...), never in the module that defines it, so the rest of the process is untouched and the
Reference Implementation's own code runs unmodified.
"""

import io
import json
import os
import winreg
from collections.abc import Callable, Generator
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from typing import Any, Self
from urllib.parse import unquote, urlparse

import psutil
import requests
from requests.structures import CaseInsensitiveDict
from requests.utils import get_encoding_from_headers

Expand = Callable[[str], str]

# The pid the fake os.getppid() returns, so the fake psutil.Process can tell the parent apart.
PARENT_PID = 0x7FFF0001


class HostError(Exception):
	"""The Reference Implementation asked for something the scenario's host block doesn't describe."""


class ModuleProxy:
	"""Stands in for a module: listed names are overridden and everything else falls through to the real module."""

	def __init__(self, real: Any, **overrides: Any) -> None:
		object.__setattr__(self, "_real", real)
		object.__setattr__(self, "_overrides", overrides)

	def __getattr__(self, name: str) -> Any:
		"""Called only for names not set on the proxy itself: an override if there is one, else the real module's."""
		overrides = self._overrides
		if name in overrides:
			return overrides[name]
		return getattr(self._real, name)


# --- Registry ------------------------------------------------------------------------------------------------------

_HIVES = {
	"HKLM": winreg.HKEY_LOCAL_MACHINE,
	"HKEY_LOCAL_MACHINE": winreg.HKEY_LOCAL_MACHINE,
	"HKCU": winreg.HKEY_CURRENT_USER,
	"HKEY_CURRENT_USER": winreg.HKEY_CURRENT_USER,
}
_VALUE_TYPES = {
	"REG_SZ": winreg.REG_SZ,
	"REG_EXPAND_SZ": winreg.REG_EXPAND_SZ,
	"REG_DWORD": winreg.REG_DWORD,
	"REG_QWORD": winreg.REG_QWORD,
}


class FakeKey:
	"""An open fake registry key; usable as a context manager, like ``winreg.HKEYType``."""

	def __init__(self, hive: int, path: str) -> None:
		self.hive = hive
		self.path = path

	def Close(self) -> None:  # noqa: N802 - winreg's name
		"""Nothing to release."""

	def __enter__(self) -> Self:
		return self

	def __exit__(self, *_: object) -> None:
		self.Close()


class FakeWinreg(ModuleProxy):
	"""``winreg`` over the host block's ``registry``. Key paths and value names match case-insensitively, as in the
	real registry. A key exists if it, or any key below it, is listed. Missing keys and values raise
	``FileNotFoundError``, as ``winreg`` does.
	"""

	def __init__(self, registry: dict[str, dict[str, dict[str, Any]]], expand: Expand) -> None:
		super().__init__(winreg, OpenKey=self._open_key, OpenKeyEx=self._open_key, QueryValueEx=self._query_value_ex, CloseKey=self._close_key)
		keys: dict[tuple[int, str], dict[str, tuple[Any, int]]] = {}
		for full_path, values in registry.items():
			hive_name, _, path = full_path.partition("\\")
			if hive_name.upper() not in _HIVES:
				msg = f"Unknown registry hive in {full_path!r}; use HKLM or HKCU"
				raise HostError(msg)
			parsed = {}
			for name, value in values.items():
				value_type = _VALUE_TYPES[value["type"]]
				data = value["value"]
				parsed[name.lower()] = (expand(data) if isinstance(data, str) else data, value_type)
			keys[_HIVES[hive_name.upper()], path.lower()] = parsed
		object.__setattr__(self, "_keys", keys)

	def _open_key(self, key: int | FakeKey, sub_key: str, *_: Any, **__: Any) -> FakeKey:
		"""``winreg.OpenKey``: opens ``sub_key`` under a hive or an open key, or raises ``FileNotFoundError``."""
		hive, base = (key.hive, key.path) if isinstance(key, FakeKey) else (key, "")
		path = "\\".join(p for p in (base, sub_key) if p).lower()
		keys: dict[tuple[int, str], Any] = self._keys
		if not any(h == hive and (k == path or k.startswith(path + "\\")) for h, k in keys):
			raise FileNotFoundError(2, "The system cannot find the file specified")
		return FakeKey(hive, path)

	def _query_value_ex(self, key: FakeKey, value_name: str) -> tuple[Any, int]:
		"""``winreg.QueryValueEx``: ``(value, type)``, or ``FileNotFoundError`` if the key has no such value."""
		values = self._keys.get((key.hive, key.path), {})
		if (value_name or "").lower() not in values:
			raise FileNotFoundError(2, "The system cannot find the file specified")
		return values[value_name.lower()]

	@staticmethod
	def _close_key(key: FakeKey) -> None:
		key.Close()


# --- Processes and file versions -----------------------------------------------------------------------------------


class FakeProcess:
	"""One ``psutil.Process`` in the host block's parent-process chain (MM-1)."""

	def __init__(self, chain: list[dict[str, Any]], index: int, expand: Expand) -> None:
		self._chain = chain
		self._index = index
		self._expand = expand

	def name(self) -> str:
		"""The image name, such as ``ModOrganizer.exe``; empty for the nameless parent of an empty chain."""
		return self._chain[self._index]["name"] if self._index < len(self._chain) else ""

	def exe(self) -> str:
		"""The exe path. A process with none listed raises ``AccessDenied``, as an elevated parent does (T-5)."""
		exe = self._chain[self._index].get("exe") if self._index < len(self._chain) else None
		if exe is None:
			raise psutil.AccessDenied(PARENT_PID + self._index)
		return self._expand(exe)

	def parent(self) -> FakeProcess | None:
		"""The next process up the chain, or ``None`` past its end (as for a process whose parent has exited)."""
		return FakeProcess(self._chain, self._index + 1, self._expand) if self._index + 1 < len(self._chain) else None

	@contextmanager
	def oneshot(self) -> Generator[None]:  # noqa: PLR6301 - psutil's method, called on an instance
		yield


def process_factory(processes: list[dict[str, Any]], expand: Expand) -> Callable[[int], FakeProcess]:
	"""A ``psutil.Process`` replacement that only knows the fake parent (``PARENT_PID``).

	An empty chain still has a parent, with no name and no parent of its own, since a real process always has one.
	"""

	def process(pid: int) -> FakeProcess:
		if pid != PARENT_PID:
			msg = f"Unexpected psutil.Process({pid}); only the fake parent pid {PARENT_PID} is known"
			raise HostError(msg)
		return FakeProcess(processes, 0, expand)

	return process


def file_version_wrapper(
	real: Callable[[Path], tuple[int, int, int, int] | None],
	processes: list[dict[str, Any]],
	expand: Expand,
) -> Callable[[Path], tuple[int, int, int, int] | None]:
	"""``utils.get_file_version``, answering from the host block for process exes that list a ``fileVersion`` and
	from the real version resource for everything else (later slices commit version-resourced stubs).
	"""
	known = {
		os.path.normcase(Path(expand(p["exe"])).absolute()): tuple(p["fileVersion"])
		for p in processes
		if p.get("exe") and p.get("fileVersion")
	}

	def get_file_version(path: Path) -> tuple[int, int, int, int] | None:
		key = os.path.normcase(Path(path).absolute())
		return known[key] if key in known else real(path)  # type: ignore[return-value]

	return get_file_version


# --- Win32 ---------------------------------------------------------------------------------------------------------

# CSIDL values (enums.CSIDL) and the knownFolders names that stand for them.
CSIDL_NAMES = {0: "Desktop", 5: "Documents", 26: "AppData", 28: "LocalAppData"}


class FakeShell32(ModuleProxy):
	"""``windll.shell32`` with ``SHGetFolderPathW`` answering from the host block's ``knownFolders``."""

	def __init__(self, real: Any, known_folders: dict[str, str], expand: Expand) -> None:
		super().__init__(real, SHGetFolderPathW=self._sh_get_folder_path)
		object.__setattr__(self, "_folders", {k: expand(v) for k, v in known_folders.items()})

	def _sh_get_folder_path(self, _hwnd: Any, csidl: int, _token: Any, _flags: int, buffer: Any) -> int:
		name = CSIDL_NAMES.get(int(csidl), str(csidl))
		folders = self._folders
		if name not in folders:
			msg = f"The host block has no knownFolders.{name} (CSIDL {csidl})"
			raise HostError(msg)
		buffer.value = folders[name]
		return 0


def fake_ntdll(*, wine: bool) -> SimpleNamespace:
	"""An ``ntdll`` that exports ``wine_get_version`` only on a Wine host (PC-1)."""
	return SimpleNamespace(wine_get_version=lambda: "fake") if wine else SimpleNamespace()


# --- Dialogs -------------------------------------------------------------------------------------------------------

# Dialog functions that return the user's choice, and so take a scripted answer. The show* functions only inform.
ASKING = {
	"askokcancel",
	"askquestion",
	"askretrycancel",
	"askyesno",
	"askyesnocancel",
	"askopenfilename",
	"askopenfilenames",
	"asksaveasfilename",
	"askdirectory",
}
INFORMING = {"showinfo", "showwarning", "showerror"}


def _jsonable(value: Any) -> Any:
	if isinstance(value, (list, tuple)):
		return [_jsonable(v) for v in value]
	if isinstance(value, dict):
		return {str(k): _jsonable(v) for k, v in value.items()}
	if value is None or isinstance(value, (bool, int, float, str)):
		return value
	return str(value)


class DialogScript:
	"""Scripted answers for ``messagebox``/``filedialog``, consumed in call order, plus a record of every call."""

	def __init__(self, answers: list[dict[str, Any]], expand: Expand) -> None:
		self._answers = list(answers)
		self._expand = expand
		self.calls: list[dict[str, Any]] = []

	def call(self, module: str, function: str, args: tuple[Any, ...], kwargs: dict[str, Any]) -> Any:
		"""Records the call, then returns the next scripted answer (asking dialogs) or ``"ok"`` (informing ones)."""
		self.calls.append({"module": module, "function": function, "args": _jsonable(args), "kwargs": _jsonable(kwargs)})
		if function in INFORMING:
			return "ok"
		if not self._answers:
			msg = f"Unscripted dialog: {module}.{function}{args!r} has no answer left in host.dialogs"
			raise HostError(msg)
		answer = self._answers.pop(0)
		if answer["function"] != function:
			msg = f"Dialog out of order: {module}.{function} was called, but the next answer is for {answer['function']}"
			raise HostError(msg)
		value = answer["answer"]
		return self._expand(value) if isinstance(value, str) else value

	@property
	def unused(self) -> list[dict[str, Any]]:
		"""Answers no dialog asked for, which usually means the scenario doesn't do what its author thought."""
		return self._answers

	def module(self, real: Any, name: str) -> ModuleProxy:
		"""A stand-in for ``tkinter.messagebox`` or ``tkinter.filedialog`` whose dialogs go through this script."""

		def make(function: str) -> Callable[..., Any]:
			return lambda *args, **kwargs: self.call(name, function, args, kwargs)

		return ModuleProxy(real, **{f: make(f) for f in (ASKING | INFORMING) if hasattr(real, f)})


# --- HTTP ----------------------------------------------------------------------------------------------------------


def logical_resource(url: str) -> str | None:
	"""The logical resource a URL stands for (the C# ``ParityHttpHandler.LogicalResource`` uses the same rules).

	``nexus-page`` (NET-1), ``github-latest-release`` (NET-2) and ``delta/<file>.xdelta`` (NET-3, any host).
	"""
	parsed = urlparse(url)
	path = parsed.path.rstrip("/")
	host = (parsed.hostname or "").lower()
	if host == "www.nexusmods.com" and path.lower() == "/fallout4/mods/87907":
		return "nexus-page"
	segments = [s for s in path.split("/") if s]
	if host == "api.github.com" and len(segments) == 5 and segments[0] == "repos" and segments[3:] == ["releases", "latest"]:
		return "github-latest-release"
	if segments and segments[-1].lower().endswith(".xdelta"):
		return "delta/" + unquote(segments[-1])
	return None


class FakeRequests(ModuleProxy):
	"""``requests`` whose ``get`` serves a scenario's ``http/`` folder by logical resource and records every call.

	Responses are real ``requests.Response`` objects over the canned bytes, so ``iter_lines``, ``iter_content``,
	``json`` and the encoding rules behave exactly as they do against a server. The exception classes fall through to
	the real module, so the Reference Implementation's ``except requests.RequestException`` clauses still match.
	"""

	def __init__(self, http_directory: Path) -> None:
		super().__init__(requests, get=self._get)
		object.__setattr__(self, "_dir", http_directory)
		object.__setattr__(self, "calls", [])

	def _get(self, url: str, **kwargs: Any) -> requests.Response:
		resource = logical_resource(url)
		self.calls.append({"resource": resource, "kwargs": _jsonable(kwargs)})  # type: ignore[attr-defined]
		if resource is None:
			msg = f"{url} isn't a logical resource the parity harness knows"
			raise HostError(msg)
		base: Path = self._dir / resource
		meta_path = base.with_name(base.name + ".response.json")
		if not meta_path.is_file():
			msg = f"Unscripted HTTP request: the scenario has no {resource}.response.json for {url}"
			raise HostError(msg)
		meta = json.loads(meta_path.read_text("utf-8"))
		match meta.get("failure"):
			case None:
				pass
			case "timeout":
				msg = f"Canned timeout for {resource}"
				raise requests.exceptions.ReadTimeout(msg)
			case "connection":
				msg = f"Canned connection failure for {resource}"
				raise requests.exceptions.ConnectionError(msg)
			case other:
				msg = f"{meta_path}: unknown failure {other!r}; use timeout or connection"
				raise HostError(msg)

		body_path = base.with_name(base.name + ".body")
		response = requests.Response()
		response.status_code = meta.get("status", 200)
		response.headers = CaseInsensitiveDict(meta.get("headers", {}))
		response.raw = io.BytesIO(body_path.read_bytes() if body_path.is_file() else b"")
		response.encoding = get_encoding_from_headers(response.headers)
		response.url = url
		return response
