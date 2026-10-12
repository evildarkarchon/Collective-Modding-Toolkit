"""Loading Parity Scenarios and materializing them as fake machines on NTFS.

This mirrors the C# side (``dotnet/tests/CMToolkit.Tests/Parity``): the same ``scenario.json`` schema, the same
``<ROOT>`` expansion, and the same tree copy plus ``emptyDirs`` and ``attributes``. Both must stay in step, since a
golden is only meaningful if both sides ran against the same tree.
"""

import ctypes
import fnmatch
import json
import os
import shutil
import tempfile
import uuid
from ctypes import wintypes
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from harness import SCENARIOS
from harness.golden import ROOT_TOKEN

TEMP_OVERRIDE_VARIABLE = "CMT_PARITY_TEMP"

# The closed vocabulary of attributes a scenario can set (FILE_ATTRIBUTE_*).
ATTRIBUTES = {"readonly": 0x1, "hidden": 0x2, "system": 0x4}
FILE_ATTRIBUTE_NORMAL = 0x80
INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF

MANIFEST_KEYS = {"operation", "parity", "host", "description", "emptyDirs", "attributes", "deviations"}
REQUIRED_KEYS = {"operation", "parity", "host"}
HOST_KEYS = {"appDir", "cwd", "argv0", "env", "knownFolders", "registry", "processes", "os", "pc", "dialogs"}
DEVIATION_KEYS = {"bug", "reason"}

_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_kernel32.GetFileAttributesW.argtypes = [wintypes.LPCWSTR]
_kernel32.GetFileAttributesW.restype = wintypes.DWORD
_kernel32.SetFileAttributesW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD]
_kernel32.SetFileAttributesW.restype = wintypes.BOOL
_kernel32.GetVolumeInformationW.argtypes = [
	wintypes.LPCWSTR,
	wintypes.LPWSTR,
	wintypes.DWORD,
	ctypes.POINTER(wintypes.DWORD),
	ctypes.POINTER(wintypes.DWORD),
	ctypes.POINTER(wintypes.DWORD),
	wintypes.LPWSTR,
	wintypes.DWORD,
]
_kernel32.GetVolumeInformationW.restype = wintypes.BOOL


class ScenarioError(Exception):
	"""A scenario that doesn't fit the schema, or a tree extra that can't be applied."""


@dataclass
class Scenario:
	"""One scenario folder: ``tree/``, ``scenario.json``, optional ``http/``, and the recorded ``golden.json``."""

	id: str
	directory: Path
	manifest: dict[str, Any] = field(repr=False)

	@property
	def module(self) -> str:
		return self.id.split("/", 1)[0]

	@property
	def operation(self) -> str:
		return self.manifest["operation"]

	@property
	def host(self) -> dict[str, Any]:
		return self.manifest["host"]

	@property
	def http_directory(self) -> Path:
		return self.directory / "http"

	@property
	def golden_path(self) -> Path:
		"""The reference-recorded golden: the only file ``record.py`` writes, and the only one ``--check`` compares."""
		return self.directory / "golden.json"

	@property
	def deviations(self) -> list[dict[str, Any]]:
		"""The reference bugs the port fixes on this scenario (ADR-0006), as ``{"bug", "reason"}`` dicts; empty if none."""
		return self.manifest.get("deviations", [])

	@property
	def port_golden_path(self) -> Path:
		"""What the C# port must produce when the scenario deviates. Written by the fixer, never by the driver."""
		return self.directory / "golden.port.json"


def load(scenario_id: str, scenarios_root: Path = SCENARIOS) -> Scenario:
	"""Loads and validates ``<scenarios_root>/<scenario_id>/scenario.json``. Unknown keys are errors, as in C#."""
	directory = scenarios_root / scenario_id
	try:
		manifest = json.loads((directory / "scenario.json").read_text("utf-8"), object_pairs_hook=_unique_keys)
	except _DuplicateKeyError as error:
		msg = f"{scenario_id}: scenario.json has a duplicate key {error.args[0]!r}"
		raise ScenarioError(msg) from None
	if not isinstance(manifest, dict):
		msg = f"{scenario_id}: scenario.json must hold an object"
		raise ScenarioError(msg)
	unknown = set(manifest) - MANIFEST_KEYS
	missing = REQUIRED_KEYS - set(manifest)
	unknown_host = set(manifest.get("host", {})) - HOST_KEYS
	if unknown or missing or unknown_host:
		msg = f"{scenario_id}: unknown keys {sorted(unknown | unknown_host)}, missing keys {sorted(missing)}"
		raise ScenarioError(msg)
	if "deviations" in manifest:
		_validate_deviations(scenario_id, manifest["deviations"])
	return Scenario(scenario_id, directory, manifest)


class _DuplicateKeyError(ValueError):
	"""A JSON object in scenario.json repeats a key; its argument is the key."""


def _unique_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
	"""``json.loads`` object hook that rejects a repeated key, as the C# side's ``AllowDuplicateProperties = false``
	does. By default the last one wins, which would hide a deviation or a parity ID from every check.

	Raises ``_DuplicateKeyError`` naming the key.
	"""
	result: dict[str, Any] = {}
	for key, value in pairs:
		if key in result:
			raise _DuplicateKeyError(key)
		result[key] = value
	return result


def _validate_deviations(scenario_id: str, deviations: Any) -> None:
	"""Applies the C# ``ScenarioManifest.Parse`` rules: a non-empty list of ``{bug, reason}`` with a positive integer bug,
	a non-blank reason and no bug twice. The driver ignores deviations otherwise; checking here keeps both sides' schema
	in step, so a scenario the driver accepts is one the C# side accepts too.

	Raises ``ScenarioError`` naming the first problem.
	"""
	if not isinstance(deviations, list) or not deviations:
		msg = f"{scenario_id}: deviations must be a non-empty list of {{bug, reason}} objects"
		raise ScenarioError(msg)
	seen: set[int] = set()
	for entry in deviations:
		if not isinstance(entry, dict) or set(entry) != DEVIATION_KEYS:
			msg = f"{scenario_id}: each deviation needs exactly the keys bug and reason: {entry!r}"
			raise ScenarioError(msg)
		bug, reason = entry["bug"], entry["reason"]
		# bool is an int subclass, so True would otherwise pass as bug 1.
		if type(bug) is not int or bug <= 0:
			msg = f"{scenario_id}: deviation bug number {bug!r} isn't a positive issue number"
			raise ScenarioError(msg)
		if not isinstance(reason, str) or not reason.strip():
			msg = f"{scenario_id}: the deviation for bug #{bug} needs a reason"
			raise ScenarioError(msg)
		if bug in seen:
			msg = f"{scenario_id}: the deviation for bug #{bug} is listed twice"
			raise ScenarioError(msg)
		seen.add(bug)


def discover(patterns: list[str] | None = None, scenarios_root: Path = SCENARIOS) -> list[Scenario]:
	"""Every scenario whose ``<module>/<scenario>`` ID matches one of the glob ``patterns`` (all when empty), by ID."""
	ids = sorted(
		f"{module.name}/{scenario.name}"
		for module in scenarios_root.iterdir()
		if module.is_dir()
		for scenario in module.iterdir()
		if (scenario / "scenario.json").is_file()
	)
	if patterns:
		ids = [i for i in ids if any(fnmatch.fnmatchcase(i, p) for p in patterns)]
	return [load(i, scenarios_root) for i in ids]


@dataclass
class Machine:
	"""A materialized scenario: its temp root and the host block that describes the fake OS around it."""

	scenario: Scenario
	root: Path

	@property
	def host(self) -> dict[str, Any]:
		return self.scenario.host

	def expand(self, value: str) -> str:
		"""Expands a leading ``<ROOT>`` to the temp root and keeps the rest as written."""
		return str(self.root) + value[len(ROOT_TOKEN) :] if value.startswith(ROOT_TOKEN) else value

	def path_of(self, tree_relative: str) -> Path:
		"""The absolute path of a tree-relative path such as ``Game/Data``.

		Raises ``ScenarioError`` unless it resolves strictly beneath the root: an absolute path or a ``..`` in a manifest
		would otherwise change the real machine, and ``cleanup`` only removes the root. ``normpath`` collapses ``..``
		lexically, like ``Path.GetFullPath`` on the C# side; ``Path.resolve`` would also follow links, which C# doesn't.
		"""
		full = Path(os.path.normpath(self.root / tree_relative))
		if full == self.root or not full.is_relative_to(self.root):
			msg = f"{self.scenario.id}: {tree_relative!r} doesn't resolve beneath the tree root"
			raise ScenarioError(msg)
		return full

	def cleanup(self) -> None:
		"""Deletes the root, clearing read-only and other attributes first so ``rmtree`` can remove everything."""
		if not self.root.exists():
			return
		_reset_attributes(self.root, include_dirs=True)
		shutil.rmtree(self.root, ignore_errors=True)


def temp_base() -> Path:
	"""The folder roots are created in. It must be NTFS: goldens compare directory enumeration order exactly."""
	base = Path(os.environ.get(TEMP_OVERRIDE_VARIABLE) or tempfile.gettempdir()).resolve()
	fs_name = ctypes.create_unicode_buffer(64)
	if not _kernel32.GetVolumeInformationW(base.anchor, None, 0, None, None, None, fs_name, len(fs_name)):
		raise ctypes.WinError(ctypes.get_last_error())
	if fs_name.value.upper() != "NTFS":
		msg = f"Parity Scenario roots must be on NTFS, but {base} is on {fs_name.value}. Set {TEMP_OVERRIDE_VARIABLE}."
		raise ScenarioError(msg)
	return base


def materialize(scenario: Scenario) -> Machine:
	"""Copies the tree to a fresh NTFS temp root, then applies ``emptyDirs`` and ``attributes``.

	Files are copied without metadata, so attributes come only from the manifest, as on the C# side.
	"""
	root = temp_base() / f"cmt-parity-{uuid.uuid4().hex}"
	root.mkdir()
	machine = Machine(scenario, root)
	try:
		_build_tree(machine)
	except BaseException:
		machine.cleanup()
		raise
	return machine


def _build_tree(machine: Machine) -> None:
	"""Fills a fresh root: the tree copy, then the manifest's empty folders and attributes."""
	scenario = machine.scenario
	tree = scenario.directory / "tree"
	if tree.is_dir():
		shutil.copytree(tree, machine.root, dirs_exist_ok=True, copy_function=shutil.copyfile)
		# New files get the archive bit; clear it so both sides start from the same (empty) attribute set.
		_reset_attributes(machine.root, include_dirs=False)

	for entry in scenario.manifest.get("emptyDirs", []):
		path = machine.path_of(entry)
		if path.is_file():
			msg = f"{scenario.id}: emptyDirs entry {entry!r} is a file in the tree"
			raise ScenarioError(msg)
		path.mkdir(parents=True, exist_ok=True)

	for entry in scenario.manifest.get("attributes", []):
		_apply_attributes(scenario, machine.path_of(entry["path"]), entry)


def _reset_attributes(root: Path, *, include_dirs: bool) -> None:
	"""Sets ``FILE_ATTRIBUTE_NORMAL`` on every file under ``root``, and on every folder too when ``include_dirs``."""
	for dirpath, dirnames, filenames in os.walk(root):
		for name in (dirnames + filenames) if include_dirs else filenames:
			_kernel32.SetFileAttributesW(str(Path(dirpath) / name), FILE_ATTRIBUTE_NORMAL)


def _apply_attributes(scenario: Scenario, path: Path, entry: dict[str, Any]) -> None:
	"""Adds the entry's attributes (from the closed ``ATTRIBUTES`` vocabulary) to whatever ``path`` already has.

	Raises ``ScenarioError`` for a missing path or an unknown attribute, and ``OSError`` if Windows refuses the change.
	"""
	current = _kernel32.GetFileAttributesW(str(path))
	if current == INVALID_FILE_ATTRIBUTES:
		msg = f"{scenario.id}: attributes entry {entry['path']!r} doesn't exist in the tree"
		raise ScenarioError(msg)
	flags = current & ~FILE_ATTRIBUTE_NORMAL
	for name in entry["set"]:
		if name not in ATTRIBUTES:
			msg = f"{scenario.id}: unknown attribute {name!r}; the vocabulary is {', '.join(ATTRIBUTES)}"
			raise ScenarioError(msg)
		flags |= ATTRIBUTES[name]
	if not _kernel32.SetFileAttributesW(str(path), flags):
		raise ctypes.WinError(ctypes.get_last_error())
