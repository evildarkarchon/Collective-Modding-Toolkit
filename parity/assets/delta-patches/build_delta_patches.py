"""Builds the self-made Delta Patches the Downgrader run's scenarios serve from their ``http/`` folders (DG-6, NET-3).

The real Delta Patches (the ``delta-patches`` release, issue #16) diff Bethesda's binaries, which the parity scenarios
can't commit. These patch between the Downgrader's CRC32-forged stand-ins instead (``../crc32-forge/out/downgrader/``):
``OG-to-NG-<file>.xdelta`` turns the Old-Gen stand-in into the Next-Gen one, ``NG-to-OG-<file>.xdelta`` the reverse,
for each of the six files. Decoding one yields a file with the other version's table CRC, so the run re-detects it.

They're built with ``xdelta3 -e -S none``: the C# port decodes with the VCDiff package, which can't read xdelta3's
DJW/FGK secondary compression (issue #3), and the real patches use none either. Each patch is decoded back with
``xdelta3 -d`` and its output's CRC32 checked against the stand-in it should rebuild.

Also writes ``out/big/``: a larger Old-Gen / Next-Gen ``Fallout4Launcher.exe`` pair (64 KiB of seeded random bytes,
forged to the launcher's CRCs) and the patch between them, built with 16 KiB windows so it has several. A failed decode
of a truncated copy shows how much output the decoder leaves behind (B-9's scenario), and its download spans many
1 KiB chunks (NET-3's progress).

Usage, from anywhere, with xdelta3 3.x on hand (stdlib only otherwise):

    python build_delta_patches.py <path to xdelta3.exe>

The output is deterministic for a given xdelta3 build. The goldens were recorded with patches from xdelta 3.1.0.
"""

import random
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "crc32-forge"))

from forge_crc32 import forge  # noqa: E402 - found through the path set just above

STAND_INS = HERE.parent / "crc32-forge" / "out" / "downgrader"
OUT = HERE / "out"
FILES = ["Fallout4.exe", "Fallout4Launcher.exe", "steam_api64.dll", "CreationKit.exe", "Archive2.exe", "Archive2Interop.dll"]

# The launcher's Old-Gen and Next-Gen CRCs (Downgrader.CRCs_game).
LAUNCHER_OG = 0x02445570
LAUNCHER_NG = 0xF6A06FF5


def encode(xdelta3: str, source: Path, target: Path, patch: Path, *, window: int | None = None) -> None:
	"""``xdelta3 -e -S none`` (no secondary compression), then decodes the patch back and checks the output.

	The application header, which xdelta3 otherwise fills with the paths it was given, names only the files, so the
	patch doesn't depend on where the repository is checked out.
	"""
	args = [xdelta3, "-e", "-f", "-S", "none", f"-A={target.name}//{source.name}/"]
	if window is not None:
		args += ["-W", str(window)]
	patch.parent.mkdir(parents=True, exist_ok=True)
	subprocess.run([*args, "-s", str(source), str(target), str(patch)], check=True)
	with tempfile.TemporaryDirectory() as temp:
		rebuilt = Path(temp) / "rebuilt"
		subprocess.run([xdelta3, "-d", "-f", "-s", str(source), str(patch), str(rebuilt)], check=True)
		if zlib.crc32(rebuilt.read_bytes()) != zlib.crc32(target.read_bytes()):
			msg = f"{patch.name} doesn't rebuild {target}"
			raise RuntimeError(msg)


def big_launcher(seed: int, crc: int) -> bytes:
	"""64 KiB of seeded random bytes, every 31st of which differs between the two seeds' builds, forged to ``crc``."""
	rng = random.Random(1)
	data = bytearray(rng.randbytes(65536))
	flip = random.Random(seed)
	for i in range(0, len(data), 31):
		data[i] ^= flip.randrange(1, 256)
	return bytes(data) + forge(bytes(data), crc)


def main() -> int:
	"""Writes every patch and prints its size. Returns the exit code."""
	if len(sys.argv) != 2:
		sys.stderr.write(__doc__ or "")
		return 2
	xdelta3 = sys.argv[1]
	for name in FILES:
		og, ng = STAND_INS / "og" / name, STAND_INS / "ng" / name
		for direction, source, target in (("OG-to-NG-", og, ng), ("NG-to-OG-", ng, og)):
			patch = OUT / f"{direction}{name}.xdelta"
			encode(xdelta3, source, target, patch)
			sys.stdout.write(f"{patch.relative_to(HERE)}: {patch.stat().st_size} bytes\n")

	big = OUT / "big"
	big.mkdir(parents=True, exist_ok=True)
	og, ng = big / "og" / "Fallout4Launcher.exe", big / "ng" / "Fallout4Launcher.exe"
	og.parent.mkdir(exist_ok=True)
	ng.parent.mkdir(exist_ok=True)
	og.write_bytes(big_launcher(2, LAUNCHER_OG))
	ng.write_bytes(big_launcher(3, LAUNCHER_NG))
	patch = big / "OG-to-NG-Fallout4Launcher.exe.xdelta"
	encode(xdelta3, og, ng, patch, window=16384)
	truncated = big / "OG-to-NG-Fallout4Launcher.exe.truncated.xdelta"
	data = patch.read_bytes()
	truncated.write_bytes(data[: len(data) // 2])
	for path in (og, ng, patch, truncated):
		sys.stdout.write(f"{path.relative_to(HERE)}: {path.stat().st_size} bytes, CRC {zlib.crc32(path.read_bytes()):08X}\n")
	return 0


if __name__ == "__main__":
	sys.exit(main())
