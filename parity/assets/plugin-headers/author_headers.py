"""Authors the ESP/ESM/ESL and BA2 headers the Overview module and archive scenarios read (OVW-M6..M8, OVW-A4).

The Overview reads only the first 34 bytes of a module and the first 12 of an archive, so every file written here is
a header and nothing more: a TES4 record whose HEDR subrecord carries the version float, or a BTDX header with its
version and format. Each scenario's files are declared in ``SCENARIOS`` below, together with the long generated lists
the limit scenarios need (hundreds of archives named in an INI, thousands of ``plugins.txt`` lines). The output is
committed into the scenario trees, so this script is not on the test path; rerun it only to change a scenario:

    python parity/assets/plugin-headers/author_headers.py

It overwrites the files it declares and touches nothing else in the trees.
"""

import struct
from collections.abc import Callable
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
OVERVIEW = REPO_ROOT / "parity" / "scenarios" / "overview"

# globals.MODULE_VERSION_1 / MODULE_VERSION_95: the two HEDR versions Fallout 4 accepts.
HEDR_100 = b"\x00\x00\x80\x3f"
HEDR_095 = b"\x33\x33\x73\x3f"
LIGHT = 0x200  # enums.ModuleFlag.Light


def hedr(version: float) -> bytes:
	"""A HEDR version as the 4 little-endian float32 bytes a plugin stores (what ``struct.unpack('<f')`` reads back)."""
	return struct.pack("<f", version)


def module(version: bytes = HEDR_100, *, flags: int = 0, magic: bytes = b"TES4", tag: bytes = b"HEDR") -> bytes:
	"""A module's TES4 record header plus its HEDR subrecord: 42 bytes, of which the Overview reads 34.

	Layout: record type (0-3), data size (4-7), flags (8-11), form ID (12-15), version control (16-19), form version
	and unknown (20-23), then the HEDR subrecord: its tag (24-27), size 12 (28-29), version float (30-33), record
	count (34-37) and next object ID (38-41).
	"""
	header = magic + struct.pack("<IIIIHH", 54, flags, 0, 0, 131, 0)
	return header + tag + struct.pack("<H", 12) + version + struct.pack("<II", 0, 0x800)


def ba2(version: int = 1, kind: bytes = b"GNRL", *, magic: bytes = b"BTDX") -> bytes:
	"""A BA2 header: magic (0-3), version (4-7), format (8-11), file count and name table offset; 24 bytes."""
	return magic + struct.pack("<I", version) + kind + struct.pack("<IQ", 0, 24)


Files = dict[str, bytes]


def mixed() -> Files:
	"""``overview/modules-archives-mixed``: every module and archive outcome in one Old-Gen Data folder."""
	return {
		# Game masters (OVW-M2): present, Fallout 4 VR's, and v0.95; DLCRobot.esm is a folder, made by the tree.
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/fallout4_vr.esm": module(HEDR_095),
		"Game/Data/DLCCoast.esm": module(HEDR_095),
		# Fallout4.ccc (OVW-M3) and plugins.txt (OVW-M4), ccDup in both (B-17).
		"Game/Data/ccBGSFO4001-PipBoy(Black).esl": module(),
		"Game/Data/ccDup.esm": module(HEDR_095),
		"Game/Data/Unofficial.esp": module(flags=LIGHT),
		"Game/Data/Disabled.esp": module(),
		"Game/Data/Short.esp": module()[:10],
		"Game/Data/BadMagic.esp": module(magic=b"TES3"),
		"Game/Data/NoHedr.esp": module(tag=b"CNAM"),
		# Unknown HEDR versions (OVW-M7, T-3, B-7): Skyrim's 0.94, Skyrim SE's 1.71, the unmatchable 1.70, Fallout
		# 76's 68.0, zero, and one large enough for exponent notation. Unknown.esl is light by its extension.
		"Game/Data/Skyrim.esp": module(hedr(0.94)),
		"Game/Data/SkyrimSE.esp": module(hedr(1.71)),
		"Game/Data/Legacy170.esp": module(hedr(1.70)),
		"Game/Data/Fo76.esm": module(hedr(68.0)),
		"Game/Data/Zero.esp": module(hedr(0.0)),
		"Game/Data/Huge.esp": module(hedr(1e20)),
		"Game/Data/Unknown.esl": module(hedr(0.94)),
		# Archives named in the INI lists (OVW-A3 (1)).
		"Game/Data/Fallout4 - Textures1.ba2": ba2(1, b"DX10"),
		"Game/Data/Fallout4 - Textures2.ba2": ba2(1, b"DX10"),
		"Game/Data/Fallout4 - Interface.ba2": ba2(8, b"GNRL"),
		# Archives beside enabled modules (OVW-A3 (2)), in any case (T-8), and every header outcome (OVW-A4).
		"Game/Data/Unofficial - Main.ba2": ba2(7, b"GNRL"),
		"Game/Data/unofficial - textures.ba2": ba2(8, b"DX10"),
		"Game/Data/ccDup - Main.ba2": ba2(1, b"GNRL"),
		"Game/Data/Skyrim - Voices_en.ba2": ba2(2, b"GNRL"),
		"Game/Data/Short - Main.ba2": ba2()[:5],
		"Game/Data/BadMagic - Main.ba2": ba2(magic=b"BSA\x00"),
		"Game/Data/Fo76 - Main.ba2": ba2(1, b"ABCD"),
		"Game/Data/NoHedr - Textures.ba2": ba2(8, b"DX10"),
		"Game/Data/Disabled - Main.ba2": ba2(),
	}


def fallback() -> Files:
	"""``overview/plugins-fallback``: no plugins.txt, so every module in Data is counted (OVW-M5)."""
	return {
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/DLCROBOT.ESM": module(HEDR_095),
		"Game/Data/Mod.ESP": module(),
		"Game/Data/Light.esl": module(),
		"Game/Data/Flagged.esm": module(flags=LIGHT),
		"Game/Data/Mod - Main.ba2": ba2(8, b"GNRL"),
	}


def vortex() -> Files:
	"""``overview/vortex-no-plugins``: Fallout4.ccc but no plugins.txt, under Vortex (OVW-P9's N/A)."""
	return {
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/ccBGSFO4044-HellfirePowerArmor.esl": module(),
		"Game/Data/ccBGSFO4044-HellfirePowerArmor - Main.ba2": ba2(),
	}


def section_missing() -> Files:
	"""``overview/archive-section-missing``: modules are read, then the archives fail (OVW-A2, B-15)."""
	return {
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/Mod.esp": module(hedr(0.94)),
	}


def anniversary() -> Files:
	"""``overview/ae-raised-limits``: Anniversary limits (T-2), German voices (INI-4) and NVFlex (OVW-A3)."""
	return {
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/Mod.esp": module(),
		"Game/Data/Mod - Main.ba2": ba2(8, b"GNRL"),
		"Game/Data/Mod - Voices_en.ba2": ba2(8, b"GNRL"),
		"Game/Data/Mod - Voices_de.ba2": ba2(8, b"GNRL"),
		"Game/Data/Mod - Voices_fr.ba2": ba2(8, b"GNRL"),
		"Game/Data/Fallout4 - Nvflex.ba2": ba2(1, b"GNRL"),
		"Game/Data/Fallout4 - Textures1.ba2": ba2(1, b"DX10"),
	}


def not_utf8() -> Files:
	"""``overview/archive-format-not-utf8``: one archive whose format bytes aren't UTF-8, which fails the load (T-1)."""
	return {
		"Game/Data/Fallout4.esm": module(),
		"Game/Data/Fallout4 - Startup.ba2": ba2(1, b"\xff\xfe\xfd\xfc"),
	}


def exceeded() -> Files:
	"""``overview/limits-exceeded``: every limit one past (OVW-C1, OVW-C2), so all four problems are raised.

	257 General and 256 Texture archives, named in the INI lists. The modules are two files listed many times in
	``plugins.txt``, which the Overview counts once per line (B-17): 255 Full and 4097 Light.
	"""
	files: Files = {
		"Game/Data/Full.esp": module(),
		"Game/Data/Light.esl": module(),
	}
	general = [f"General{i:03} - Main.ba2" for i in range(257)]
	texture = [f"Texture{i:03} - Textures.ba2" for i in range(256)]
	files.update({f"Game/Data/{name}": ba2(1, b"GNRL") for name in general})
	files.update({f"Game/Data/{name}": ba2(8, b"DX10") for name in texture})
	files["Documents/My Games/Fallout4/Fallout4.ini"] = ini(general, texture)
	files["LocalAppData/Fallout4/plugins.txt"] = plugins(["Full.esp"] * 255 + ["Light.esl"] * 4097)
	return files


def warning() -> Files:
	"""``overview/limits-warning``: module counts on the colour boundaries (OVW-C1): 254 Full is the limit itself
	(orange), 3890 Light is one below 95 % (green), and their 4144 total is past 95 % of 4350 (orange).
	"""
	return {
		"Game/Data/Full.esp": module(),
		"Game/Data/Light.esl": module(),
		"Game/Data/Fallout4 - Startup.ba2": ba2(),
		"LocalAppData/Fallout4/plugins.txt": plugins(["Full.esp"] * 254 + ["Light.esl"] * 3890),
	}


def ini(general: list[str], texture: list[str]) -> bytes:
	"""A ``Fallout4.ini`` naming the archives in two of the lists ``get_info_archives`` reads."""
	text = f"[General]\r\nsLanguage=en\r\n[Archive]\r\nsResourceArchiveList={', '.join(general)}\r\nsResourceArchiveList2={', '.join(texture)}\r\n"
	return text.encode("utf-8")


def plugins(names: list[str]) -> bytes:
	"""A ``plugins.txt`` with every name enabled (``*``), CRLF-terminated after the game's comment line."""
	lines = ["# This file is used by Fallout4 to keep track of your downloaded content.", *(f"*{name}" for name in names)]
	return "".join(f"{line}\r\n" for line in lines).encode("utf-8")


SCENARIOS: dict[str, Callable[[], Files]] = {
	"modules-archives-mixed": mixed,
	"plugins-fallback": fallback,
	"vortex-no-plugins": vortex,
	"archive-section-missing": section_missing,
	"ae-raised-limits": anniversary,
	"archive-format-not-utf8": not_utf8,
	"limits-exceeded": exceeded,
	"limits-warning": warning,
}


def main() -> None:
	"""Writes every declared file into its scenario's tree."""
	for scenario, build in SCENARIOS.items():
		tree = OVERVIEW / scenario / "tree"
		for relative, content in build().items():
			path = tree / relative
			path.parent.mkdir(parents=True, exist_ok=True)
			path.write_bytes(content)
		print(f"{scenario}: {len(build())} files")


if __name__ == "__main__":
	main()
