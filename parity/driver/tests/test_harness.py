"""Unit tests for the parity driver's own pieces: goldens, scenarios, and the host fakes.

Run from the repo root (local only, like the rest of the driver):

    uv run python -m unittest discover -s parity/driver/tests -t parity/driver
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import winreg
from pathlib import Path
from unittest import mock

import psutil
import requests

from harness import REPO_ROOT
from harness.fakes import (
	PARENT_PID,
	DialogScript,
	FakeRequests,
	FakeWinreg,
	HostError,
	logical_resource,
	process_factory,
)
from harness.golden import GoldenContext, dumps
from harness.scenario import TEMP_OVERRIDE_VARIABLE, ScenarioError, discover, load, materialize


def identity(value: str) -> str:
	return value


class GoldenTests(unittest.TestCase):
	def test_paths_become_root_tokens_case_insensitively(self) -> None:
		golden = GoldenContext(Path(r"C:\Temp\cmt-parity-x"))

		self.assertEqual(golden.path(r"C:\Temp\cmt-parity-x"), "<ROOT>")
		self.assertEqual(golden.path(r"c:\temp\CMT-PARITY-X\Game\Data\a.esp"), "<ROOT>/Game/Data/a.esp")
		with self.assertRaises(ValueError):
			golden.path(r"C:\Temp\elsewhere")

	def test_text_replaces_both_spellings_of_the_root(self) -> None:
		golden = GoldenContext(Path(r"C:\Temp\r"))

		self.assertEqual(golden.text(r"Path: C:\Temp\r\Game and C:/Temp/r/Game/Fallout4.exe"), r"Path: <ROOT>\Game and <ROOT>/Game/Fallout4.exe")

	def test_dumps_is_canonical(self) -> None:
		text = dumps({"b": GoldenContext.unordered(["y", "x"]), "a": "\ufeff\x1c"})

		self.assertEqual(text, '{\n  "a": "\\ufeff\\u001c",\n  "b": {\n    "$unordered": [\n      "y",\n      "x"\n    ]\n  }\n}\n')


class ScenarioTests(unittest.TestCase):
	def setUp(self) -> None:
		self.root = Path(tempfile.mkdtemp(prefix="cmt-driver-tests-"))

	def tearDown(self) -> None:
		shutil.rmtree(self.root, ignore_errors=True)

	def author(self, scenario_id: str, manifest: dict, files: dict[str, bytes] | None = None) -> None:
		directory = self.root / scenario_id
		directory.mkdir(parents=True)
		(directory / "scenario.json").write_text(json.dumps(manifest), "utf-8")
		for rel, data in (files or {}).items():
			path = directory / "tree" / rel
			path.parent.mkdir(parents=True, exist_ok=True)
			path.write_bytes(data)

	def test_unknown_keys_are_rejected_like_the_csharp_side(self) -> None:
		self.author("m/a", {"operation": "op", "parity": [], "host": {"appdir": "x"}})
		self.author("m/b", {"operation": "op", "host": {}})

		with self.assertRaises(ScenarioError):
			load("m/a", self.root)
		with self.assertRaises(ScenarioError):
			load("m/b", self.root)

	def test_discover_filters_by_glob_and_sorts_by_id(self) -> None:
		for scenario_id in ("s/b", "s/a", "o/a"):
			self.author(scenario_id, {"operation": "op", "parity": [], "host": {}})

		self.assertEqual([s.id for s in discover(None, self.root)], ["o/a", "s/a", "s/b"])
		self.assertEqual([s.id for s in discover(["s/*"], self.root)], ["s/a", "s/b"])

	def test_materialize_copies_bytes_applies_extras_and_cleans_up(self) -> None:
		self.author(
			"m/s",
			{
				"operation": "op",
				"parity": [],
				"host": {"appDir": "<ROOT>/App"},
				"emptyDirs": ["Game/Data"],
				"attributes": [{"path": "App/f.txt", "set": ["readonly", "hidden"]}],
			},
			{"App/f.txt": b"\xef\xbb\xbfx\r\n"},
		)

		machine = materialize(load("m/s", self.root))
		try:
			self.assertEqual((machine.root / "App/f.txt").read_bytes(), b"\xef\xbb\xbfx\r\n")
			self.assertTrue((machine.root / "Game/Data").is_dir())
			attributes = (machine.root / "App/f.txt").stat().st_file_attributes
			self.assertTrue(attributes & 0x1)
			self.assertTrue(attributes & 0x2)
			self.assertEqual(machine.expand("<ROOT>/App"), str(machine.root) + "/App")
		finally:
			machine.cleanup()
		self.assertFalse(machine.root.exists())

	def test_unknown_attributes_are_rejected(self) -> None:
		self.author("m/s", {"operation": "op", "parity": [], "host": {}, "attributes": [{"path": "f", "set": ["archive"]}]}, {"f": b""})

		with self.assertRaises(ScenarioError):
			materialize(load("m/s", self.root))

	def test_manifest_paths_that_escape_the_root_are_rejected(self) -> None:
		# Machine roots go in ``base``, so every escape targets ``base`` itself: a regression shows up as a leftover there
		# rather than as a change somewhere else on the machine.
		base = self.root / "base"
		base.mkdir()
		cases = {
			"m/up": {"emptyDirs": ["../escaped"]},
			"m/abs": {"emptyDirs": [f"{base.as_posix()}/escaped"]},
			"m/self": {"emptyDirs": [""]},
			"m/attr": {"attributes": [{"path": "..", "set": ["hidden"]}]},
		}
		for scenario_id, extra in cases.items():
			self.author(scenario_id, {"operation": "op", "parity": [], "host": {}, **extra})

		with mock.patch.dict(os.environ, {TEMP_OVERRIDE_VARIABLE: str(base)}):
			for scenario_id in cases:
				with self.subTest(scenario_id), self.assertRaisesRegex(ScenarioError, "beneath the tree root"):
					materialize(load(scenario_id, self.root))
		self.assertEqual(list(base.iterdir()), [])
		self.assertFalse(base.stat().st_file_attributes & 0x2)

	def test_a_name_that_merely_starts_with_two_dots_stays_inside_the_root(self) -> None:
		self.author("m/s", {"operation": "op", "parity": [], "host": {}, "emptyDirs": ["..data"]})

		machine = materialize(load("m/s", self.root))
		try:
			self.assertTrue((machine.root / "..data").is_dir())
		finally:
			machine.cleanup()


class RegistryTests(unittest.TestCase):
	def setUp(self) -> None:
		self.registry = FakeWinreg(
			{
				"HKLM\\SOFTWARE\\Vendor\\App": {
					"Path": {"type": "REG_SZ", "value": "<ROOT>\\Game"},
					"Size": {"type": "REG_QWORD", "value": 12},
				},
			},
			lambda v: v.replace("<ROOT>", "R:"),
		)

	def test_values_are_typed_expanded_and_case_insensitive(self) -> None:
		with self.registry.OpenKey(winreg.HKEY_LOCAL_MACHINE, R"software\vendor\APP") as key:
			self.assertEqual(self.registry.QueryValueEx(key, "path"), ("R:\\Game", winreg.REG_SZ))
			self.assertEqual(self.registry.QueryValueEx(key, "Size"), (12, winreg.REG_QWORD))

	def test_parent_keys_exist_and_missing_keys_and_values_raise_file_not_found(self) -> None:
		self.registry.OpenKey(winreg.HKEY_LOCAL_MACHINE, R"SOFTWARE\Vendor")
		with self.assertRaises(FileNotFoundError):
			self.registry.OpenKey(winreg.HKEY_CURRENT_USER, R"SOFTWARE\Vendor\App")
		with self.assertRaises(FileNotFoundError):
			self.registry.QueryValueEx(self.registry.OpenKey(winreg.HKEY_LOCAL_MACHINE, R"SOFTWARE\Vendor\App"), "Nope")

	def test_constants_fall_through_to_the_real_module(self) -> None:
		self.assertEqual(self.registry.REG_SZ, winreg.REG_SZ)
		self.assertEqual(self.registry.HKEY_LOCAL_MACHINE, winreg.HKEY_LOCAL_MACHINE)


class ProcessTests(unittest.TestCase):
	def test_the_chain_walks_up_from_the_parent(self) -> None:
		process = process_factory(
			[{"name": "a.exe", "exe": "<ROOT>/a.exe"}, {"name": "ModOrganizer.exe"}],
			lambda v: v.replace("<ROOT>", "R:"),
		)

		parent = process(PARENT_PID)
		self.assertEqual((parent.name(), parent.exe()), ("a.exe", "R:/a.exe"))
		grandparent = parent.parent()
		assert grandparent is not None
		self.assertEqual(grandparent.name(), "ModOrganizer.exe")
		with self.assertRaises(psutil.AccessDenied):
			grandparent.exe()
		self.assertIsNone(grandparent.parent())
		with self.assertRaises(HostError):
			process(1234)


class DialogTests(unittest.TestCase):
	def test_answers_are_consumed_in_order_and_every_call_is_recorded(self) -> None:
		script = DialogScript([{"function": "askyesno", "answer": True}, {"function": "askopenfilename", "answer": "<ROOT>/x"}], lambda v: v.replace("<ROOT>", "R:"))
		from tkinter import filedialog, messagebox  # noqa: PLC0415

		box = script.module(messagebox, "messagebox")
		files = script.module(filedialog, "filedialog")

		self.assertTrue(box.askyesno("T", "M"))
		self.assertEqual(box.showwarning("W", "careful"), "ok")
		self.assertEqual(files.askopenfilename(title="Pick", filetypes=(("Fallout 4", "Fallout4.exe"),)), "R:/x")
		self.assertEqual([c["function"] for c in script.calls], ["askyesno", "showwarning", "askopenfilename"])
		self.assertEqual(script.calls[2]["kwargs"]["filetypes"], [["Fallout 4", "Fallout4.exe"]])
		self.assertEqual(script.unused, [])

	def test_unscripted_and_out_of_order_dialogs_fail(self) -> None:
		with self.assertRaises(HostError):
			DialogScript([], identity).call("messagebox", "askyesno", (), {})
		with self.assertRaises(HostError):
			DialogScript([{"function": "askyesno", "answer": True}], identity).call("filedialog", "askopenfilename", (), {})


class HttpTests(unittest.TestCase):
	def setUp(self) -> None:
		self.http = Path(tempfile.mkdtemp(prefix="cmt-driver-http-"))

	def tearDown(self) -> None:
		shutil.rmtree(self.http, ignore_errors=True)

	def can(self, resource: str, meta: dict, body: bytes | None = None) -> None:
		base = self.http / resource
		base.parent.mkdir(parents=True, exist_ok=True)
		base.with_name(base.name + ".response.json").write_text(json.dumps(meta), "utf-8")
		if body is not None:
			base.with_name(base.name + ".body").write_bytes(body)

	def test_logical_resources_match_the_csharp_mapping(self) -> None:
		self.assertEqual(logical_resource("https://www.nexusmods.com/fallout4/mods/87907"), "nexus-page")
		self.assertEqual(logical_resource("https://api.github.com/repos/wxMichael/Collective-Modding-Toolkit/releases/latest"), "github-latest-release")
		self.assertEqual(
			logical_resource("https://github.com/wxMichael/Collective-Modding-Toolkit/releases/download/delta-patches/NG-to-OG-Fallout4.exe.xdelta"),
			"delta/NG-to-OG-Fallout4.exe.xdelta",
		)
		self.assertIsNone(logical_resource("https://www.nexusmods.com/fallout4/mods/1"))

	def test_responses_behave_like_real_requests_responses(self) -> None:
		self.can("nexus-page", {"status": 200, "headers": {"Content-Type": "text/html; charset=utf-8"}}, b"a\n<meta x>\nv\n")
		fake = FakeRequests(self.http)

		response = fake.get("https://www.nexusmods.com/fallout4/mods/87907", timeout=5, stream=True)

		self.assertEqual(response.status_code, 200)
		self.assertEqual(list(response.iter_lines(decode_unicode=True)), ["a", "<meta x>", "v"])
		self.assertEqual(fake.calls, [{"resource": "nexus-page", "kwargs": {"timeout": 5, "stream": True}}])

	def test_content_length_is_only_present_when_declared(self) -> None:
		self.can("delta/a.xdelta", {"status": 200}, b"123")
		response = FakeRequests(self.http).get("https://x.invalid/a.xdelta", timeout=10, stream=True)

		self.assertEqual(int(response.headers.get("content-length", 0)), 0)
		self.assertEqual(b"".join(response.iter_content(chunk_size=1024)), b"123")

	def test_failures_raise_the_real_requests_exceptions(self) -> None:
		self.can("nexus-page", {"failure": "timeout"})
		self.can("github-latest-release", {"failure": "connection"})
		fake = FakeRequests(self.http)

		with self.assertRaises(requests.RequestException):
			fake.get("https://www.nexusmods.com/fallout4/mods/87907")
		with self.assertRaises(requests.exceptions.ConnectionError):
			fake.get("https://api.github.com/repos/a/b/releases/latest")
		with self.assertRaises(HostError):
			fake.get("https://api.github.com/repos/a/b/tags")
		self.assertIs(fake.RequestException, requests.RequestException)


class SessionTests(unittest.TestCase):
	def test_the_reference_sees_the_fake_machine(self) -> None:
		"""Runs real PCInfo and GameInfo against the shell scenario in a child process (a session can't be undone)."""
		script = (
			"from harness.scenario import load, materialize\n"
			"from harness.reference import Session\n"
			"m = materialize(load('shell/main-window'))\n"
			"s = Session(m); s.start()\n"
			"from helpers import PCInfo\n"
			"from game_info import GameInfo\n"
			"from tkinter import StringVar\n"
			"pc = PCInfo(); g = GameInfo(StringVar(), StringVar())\n"
			"print(repr((pc.os, pc.ram, pc.cpu, pc.gpu, pc.vram, g.manager, str(g.game_path) == str(m.root / 'Game'), str(g.language))))\n"
			"s.root.destroy()\n"
			"import os; os.chdir(m.root.parent); m.cleanup()\n"
		)
		result = subprocess.run(
			[sys.executable, "-c", script],
			cwd=REPO_ROOT / "parity" / "driver",
			capture_output=True,
			text=True,
			env={**os.environ, "PYTHONHASHSEED": "0"},
			check=False,
		)

		self.assertEqual(result.returncode, 0, result.stderr)
		self.assertEqual(
			result.stdout.strip(),
			repr(("Windows 11 24H2", 64, "AMD Ryzen 7 7800X3D", "NVIDIA GeForce RTX 4070", 12, None, True, "en")),
		)


class ScreenshotShutdownTests(unittest.TestCase):
	def test_kill_tree_takes_the_descendants_down_too(self) -> None:
		"""A parent that starts a grandchild-style child (like a venv redirector) leaves no orphan behind."""
		from screenshots import kill_tree  # noqa: PLC0415 - imports PIL and user32, so only this test pays for it

		script = (
			"import subprocess, sys, time\n"
			"child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])\n"
			"print(child.pid, flush=True)\n"
			"time.sleep(60)\n"
		)
		parent = subprocess.Popen([sys.executable, "-c", script], stdout=subprocess.PIPE, text=True)
		try:
			child_pid = int(parent.stdout.readline())

			kill_tree(parent.pid)

			self.assertIsNotNone(parent.poll())
			self.assertFalse(psutil.pid_exists(child_pid))
		finally:
			parent.kill()
			parent.stdout.close()
			parent.wait()


if __name__ == "__main__":
	unittest.main()
