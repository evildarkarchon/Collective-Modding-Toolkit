"""Builds the Downgrader's CRC32-forged stand-ins: one small file per CRC its tables recognise (DG-1).

The Downgrader knows its six files only by full-file CRC32 (``downgrader.py`` ``CRCs_game`` / ``CRCs_ck``). Parity
scenarios can't commit the real game files, so they commit these stand-ins instead, copied into their ``tree/``.

Where the Overview also reads the same file by its version resource (``Fallout4.exe``, ``steam_api64.dll``,
``CreationKit.exe``; OVW-B2), the stand-in is the matching version stub from ``../version-stubs/out/`` with four bytes
appended (``forge_crc32.forge``), so the Overview and the Downgrader agree on the install. Every other stand-in is a
short text label forged the same way. A stub that already has the wanted CRC is copied as is.

Usage (stdlib only, any Python 3), from anywhere:

    python build_downgrader.py

Writes ``out/downgrader/<set>/<file name>`` (``Archive2.exe`` and ``Archive2Interop.dll`` too, without their
``Tools\\Archive2`` folder; scenarios place them), plus ``out/downgrader/all/<file name>/<CRC>``, a text stand-in for
every CRC in the tables. The output is deterministic, so rebuilding changes nothing unless a
version stub was rebuilt; then re-record the Downgrader goldens only if ``record.py --check`` reports a change.
"""

import sys
import zlib
from pathlib import Path

from forge_crc32 import forge

HERE = Path(__file__).resolve().parent
STUBS = HERE.parent / "version-stubs" / "out"
OUT = HERE / "out" / "downgrader"

# (set, file name, version stub set or None for a text label, CRC32 or None to keep the input's own CRC). The sets
# mirror the installs the downgrader scenarios model; the comments name the Downgrader's reading of each.
STAND_INS: list[tuple[str, str, str | None, str | None]] = [
	# Old-Gen 1.10.163 and its Creation Kit: every file Old-Gen.
	("og", "Fallout4.exe", "og", "C6053902"),
	("og", "Fallout4Launcher.exe", "og", "02445570"),
	("og", "steam_api64.dll", "og", "BBD912FC"),
	("og", "CreationKit.exe", "og", "0F5C065B"),
	("og", "Archive2.exe", "og", "4CDFC7B5"),
	("og", "Archive2Interop.dll", None, "850D36A9"),
	# Next-Gen 1.10.984: steam_api64 is the shared Next-Gen & Anniversary one.
	("ng", "Fallout4.exe", "ng", "C5965A2E"),
	("ng", "Fallout4Launcher.exe", "ng", "F6A06FF5"),
	("ng", "steam_api64.dll", "ng", "E36E7B4D"),
	("ng", "CreationKit.exe", "ng", "481CCE95"),
	("ng", "Archive2.exe", "ng", "71A5240B"),
	("ng", "Archive2Interop.dll", None, "EFBE3622"),
	# Anniversary (the Downgrader's AE CRCs; the Overview reads Fallout4.exe and CreationKit.exe as 1.11.240).
	("ae", "Fallout4.exe", "ae", "CF47788D"),
	("ae", "Fallout4Launcher.exe", None, "720BB9C3"),
	("ae", "CreationKit.exe", "ae", "49E45284"),
	("ae", "Archive2.exe", None, "C867674F"),
	("ae", "Archive2Interop.dll", None, "7B893B0D"),
	# Obsolete builds: the 1.10.980 game and the 1.11.137 launcher.
	("obsolete", "Fallout4.exe", "obsolete", "B61675B1"),
	("obsolete", "Fallout4Launcher.exe", None, "0E696744"),
	# CRCs no table lists: Unknown. The game keeps the unknown-version stub's own CRC.
	("unknown", "Fallout4.exe", "unknown", None),
	("unknown", "Fallout4Launcher.exe", None, None),
	("unknown", "Archive2.exe", None, None),
]


def label(set_name: str, file_name: str) -> bytes:
	"""The text a stand-in without a version stub starts from, which says what it is."""
	return f"Collective Modding Toolkit parity stub (not a game file): Downgrader {set_name} {file_name}\n".encode("ascii")


# Every CRC in the Downgrader's tables (``Downgrader.CRCs_game`` + ``CRCs_ck``, file name -> CRCs), so the ``all`` set
# has a text stand-in for each: ``out/downgrader/all/<file name>/<CRC>``. The sets above are what scenarios copy; this
# one is the full library (the Patch All run needs both directions' CRCs, and backups' CRCs, DG-5).
TABLE_CRCS: dict[str, list[str]] = {
	"Fallout4.exe": [
		"97DA3E03", "2ED2A242", "A0100017", "9ABC94F0", "C6053902", "B61675B1",
		"C5965A2E", "0AEB19A7", "1E90BE57", "0481725D", "0E176ABC", "CF47788D",
	],
	"Fallout4Launcher.exe": ["02445570", "F6A06FF5", "0E696744", "D15C6A49", "8C52BE93", "591009C9", "720BB9C3"],
	"steam_api64.dll": ["BBD912FC", "E36E7B4D"],
	"CreationKit.exe": ["0F5C065B", "481CCE95", "49E45284"],
	"Archive2.exe": ["4CDFC7B5", "71A5240B", "C867674F"],
	"Archive2Interop.dll": ["850D36A9", "EFBE3622", "7B893B0D"],
}


def main() -> int:
	"""Writes every stand-in and prints its CRC. Returns the exit code."""
	for file_name, crcs in TABLE_CRCS.items():
		for crc in crcs:
			STAND_INS.append(("all", f"{file_name}/{crc}", None, crc))
	for set_name, file_name, stub_set, crc in STAND_INS:
		data = label(set_name, file_name) if stub_set is None else (STUBS / stub_set / file_name).read_bytes()
		if crc is not None and zlib.crc32(data) != int(crc, 16):
			data += forge(data, int(crc, 16))
		target = OUT / set_name / file_name
		target.parent.mkdir(parents=True, exist_ok=True)
		target.write_bytes(data)
		sys.stdout.write(f"{target.relative_to(HERE)}: {zlib.crc32(data):08X}\n")
	return 0


if __name__ == "__main__":
	sys.exit(main())
