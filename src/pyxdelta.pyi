# Type stubs for pyxdelta
"""Python interface for xdelta."""

import os

def decode(infile: str | os.PathLike[str], patchfile: str | os.PathLike[str], outfile: str | os.PathLike[str]) -> None:
	"""
	Decode xdelta patch.

	Apply an xdelta patch to a source file to create the target file.

	Args:
	    infile: Path to the source file to be patched
	    patchfile: Path to the xdelta patch file
	    outfile: Path where the patched output file will be created

	Raises:
	    OSError: If any of the files cannot be opened or if patching fails
	"""

def run(infile: str | os.PathLike[str], outfile: str | os.PathLike[str], patchfile: str | os.PathLike[str]) -> None:
	"""
	Create xdelta patch.

	Create an xdelta patch file that represents the difference between two files.

	Args:
	    infile: Path to the original/source file
	    outfile: Path to the target/modified file
	    patchfile: Path where the xdelta patch file will be created

	Raises:
	    OSError: If any of the files cannot be opened or if patch creation fails
	"""
