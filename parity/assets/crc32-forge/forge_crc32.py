"""Appends four bytes to a file so that its CRC32 becomes a chosen value.

The Reference Implementation recognises some files only by their CRC32 (``zlib.crc32``, printed as 8 uppercase hex
digits): ``BASE_FILES``' CRC-keyed binaries (OVW-B2) and the Down-Grade ``Fallout4 - Startup.ba2``, whose CRC skips
its first 12 bytes (OVW-B6). Parity scenarios can't commit the real game files, so they commit small stand-ins forged
to those CRCs instead.

Usage (stdlib only, any Python 3):

    python forge_crc32.py <file> <CRC32 hex> [--skip N]

The file is changed in place: four bytes are appended so that the CRC32 of everything from offset ``N`` (default 0)
to the end equals the target. Appending keeps whatever the file already is valid: a PE's version resource and
headers don't cover trailing overlay bytes.
"""

import argparse
import sys
import zlib
from pathlib import Path


def forge(data: bytes, target: int) -> bytes:
	"""The four bytes that, appended to ``data``, make ``zlib.crc32`` of the result equal ``target``.

	CRC32 is affine over GF(2) in its input bits, so the effect of the 32 appended bits on the final CRC is a 32x32
	linear map plus a constant. Each column is measured with ``zlib.crc32`` itself (one bit set), and the system is
	solved by Gaussian elimination; the map is invertible for a 4-byte suffix, so a solution always exists.
	"""
	base = zlib.crc32(data + bytes(4))
	columns = [zlib.crc32(data + (1 << bit).to_bytes(4, "little")) ^ base for bit in range(32)]

	# pivots[b] = (vector whose highest set bit is b, mask of the suffix bits that produce it)
	pivots: dict[int, tuple[int, int]] = {}
	for bit, column in enumerate(columns):
		vector, mask = column, 1 << bit
		while vector:
			high = vector.bit_length() - 1
			if high not in pivots:
				pivots[high] = (vector, mask)
				break
			pivot_vector, pivot_mask = pivots[high]
			vector ^= pivot_vector
			mask ^= pivot_mask

	want, suffix = target ^ base, 0
	while want:
		high = want.bit_length() - 1
		if high not in pivots:
			msg = "the CRC32 suffix map is singular; this can't happen for a 4-byte suffix"
			raise ArithmeticError(msg)
		pivot_vector, pivot_mask = pivots[high]
		want ^= pivot_vector
		suffix ^= pivot_mask

	result = suffix.to_bytes(4, "little")
	assert zlib.crc32(data + result) == target
	return result


def main() -> int:
	"""Parses the command line, forges the file in place and prints the CRC it now has."""
	parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	parser.add_argument("file", type=Path)
	parser.add_argument("crc", help="the target CRC32, 8 hex digits")
	parser.add_argument("--skip", type=int, default=0, help="bytes at the start of the file the CRC doesn't cover")
	args = parser.parse_args()

	target = int(args.crc, 16)
	data = args.file.read_bytes()
	suffix = forge(data[args.skip :], target)
	args.file.write_bytes(data + suffix)
	sys.stdout.write(f"{args.file}: CRC32 from offset {args.skip} is now {target:08X}\n")
	return 0


if __name__ == "__main__":
	sys.exit(main())
