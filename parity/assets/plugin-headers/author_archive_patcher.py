"""Authors the BA2 archives, Fallout4.ini and ``Changes/`` overlays of the Archive Patcher scenarios (AP-1..AP-3).

Each scenario is an Old-Gen Steam game whose Data folder holds archive headers (``author_headers.ba2``) named in
``Fallout4.ini``'s ``[Archive]`` lists. The ``archive-patcher`` operation applies the tree's ``Changes/`` overlay after
the Overview has read the archives, so an archive can differ from what the Overview saw when Patch All opens it:
``<name>`` there replaces the file, and ``<name>.delete`` deletes it. The output is committed into the scenario
trees, so this script is not on the test path; rerun it only to change a scenario:

    python parity/assets/plugin-headers/author_archive_patcher.py

It overwrites the files it declares and touches nothing else in the trees.
"""

from pathlib import Path
from typing import TYPE_CHECKING

from author_headers import ba2, module

if TYPE_CHECKING:
	from collections.abc import Callable

REPO_ROOT = Path(__file__).resolve().parents[3]
ARCHIVE_PATCHER = REPO_ROOT / "parity" / "scenarios" / "archive-patcher"

Files = dict[str, bytes]


def ini(archives: list[str]) -> bytes:
	"""A ``Fallout4.ini`` naming the Startup and Textures1 archives in their usual lists and ``archives`` in
	``sResourceArchiveList2``.
	"""
	lines = [
		"[General]",
		"sLanguage=en",
		"[Archive]",
		"sResourceIndexFileList=Fallout4 - Textures1.ba2",
		"sResourceStartUpArchiveList=Fallout4 - Startup.ba2",
		"sResourceArchiveList=Fallout4 - Startup.ba2",
		f"sResourceArchiveList2={', '.join(archives)}",
	]
	return "".join(f"{line}\n" for line in lines).encode("utf-8")


def round_trip() -> Files:
	r"""``archive-patcher/round-trip``: every Patch All outcome (AP-3), then back to v8 for the names the filter picks.

	Next-Gen archives that patch (v8 and v7, two of them spelled with ``ß``/``SS`` for the case-folded filter, AP-2),
	one read-only (Permissions/In-Use; the scenario sets the attribute), one enabled under two spellings of the same
	file (``Sub\..\``), so the second sees it already patched, and five the overlay changes behind the Overview's
	back: deleted (File Not Found), version 2 (Unrecognized version [02]), not BTDX (Unrecognized format), cut after
	the magic (Unrecognized version []) and already v1 (Skipping already-patched).
	"""
	next_gen = [
		"Straße - Main.ba2",
		"BOSS - Textures.ba2",
		"Plain - Main.ba2",
		"ReadOnly - Main.ba2",
		"Alias - Main.ba2",
		"Gone - Main.ba2",
		"Changed - Main.ba2",
		"Corrupt - Main.ba2",
		"Short - Main.ba2",
		"Flipped - Main.ba2",
	]
	files: Files = {
		"Documents/My Games/Fallout4/Fallout4.ini": ini([*next_gen, "Sub\\..\\Alias - Main.ba2"]),
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/Fallout4 - Textures1.ba2": ba2(1, b"DX10"),
	}
	for name in next_gen:
		files[f"Game/Data/{name}"] = ba2(7 if name.startswith("BOSS") else 8, b"DX10" if "Textures" in name else b"GNRL")
	files.update({
		"Changes/Game/Data/Gone - Main.ba2.delete": b"",
		"Changes/Game/Data/Changed - Main.ba2": ba2(2),
		"Changes/Game/Data/Corrupt - Main.ba2": ba2(8, magic=b"JUNK"),
		"Changes/Game/Data/Short - Main.ba2": b"BTDX",
		"Changes/Game/Data/Flipped - Main.ba2": ba2(1),
	})
	return files


def nothing_to_do() -> Files:
	"""``archive-patcher/nothing-to-do``: only v1 archives, so the v1 run has nothing to do, and the filter picks none
	of them for the v8 run either (AP-3's ``Nothing to do!``).
	"""
	return {
		"Documents/My Games/Fallout4/Fallout4.ini": ini(["Old - Main.ba2"]),
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/Fallout4 - Textures1.ba2": ba2(1, b"DX10"),
		"Game/Data/Old - Main.ba2": ba2(1),
	}


SCENARIOS: dict[str, Callable[[], Files]] = {
	"round-trip": round_trip,
	"nothing-to-do": nothing_to_do,
}


def main() -> None:
	"""Writes every declared file into its scenario's tree."""
	for scenario, build in SCENARIOS.items():
		tree = ARCHIVE_PATCHER / scenario / "tree"
		files = build()
		for relative, content in files.items():
			path = tree / relative
			path.parent.mkdir(parents=True, exist_ok=True)
			path.write_bytes(content)
		print(f"{scenario}: {len(files)} files")


if __name__ == "__main__":
	main()
