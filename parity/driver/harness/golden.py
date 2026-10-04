"""Writing goldens as a neutral JSON projection (ADR-0004).

A golden records semantic values. Where the Reference Implementation renders a value into user-visible text, the
object holding it also carries a ``display`` sub-object with the rendered text, which C# Core tests skip and App
presenter tests compare. Paths are ``<ROOT>``-relative tokens, and hash-ordered or otherwise unordered arrays are
wrapped as ``{"$unordered": [...]}`` so the C# comparer treats them as multisets.
"""

import json
from pathlib import Path, PureWindowsPath
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
	from collections.abc import Iterable

ROOT_TOKEN = "<ROOT>"
UNORDERED_KEY = "$unordered"


class GoldenContext:
	"""Projects values from one materialized scenario into golden form."""

	def __init__(self, root: Path) -> None:
		"""``root`` is the scenario's temp root, which ``<ROOT>`` stands for."""
		self.root = PureWindowsPath(root)

	def path(self, value: str | Path) -> str:
		"""Returns ``<ROOT>`` plus the root-relative part with forward slashes, e.g. ``<ROOT>/Game/Data``.

		Raises ``ValueError`` for a path outside the root, which would leak a machine-specific path into a golden.
		Comparison is case-insensitive, like Windows paths.
		"""
		path = PureWindowsPath(value)
		try:
			relative = path.relative_to(self.root)
		except ValueError:
			msg = f"{value!r} is outside the scenario root {str(self.root)!r}"
			raise ValueError(msg) from None
		return ROOT_TOKEN if relative == PureWindowsPath() else f"{ROOT_TOKEN}/{relative.as_posix()}"

	def text(self, value: str) -> str:
		"""Replaces the root inside rendered text with ``<ROOT>``, keeping the separators that follow as written.

		Both the backslash and the forward-slash spelling of the root are replaced, since Tk dialogs return
		forward slashes.
		"""
		for spelling in (str(self.root), self.root.as_posix()):
			value = value.replace(spelling, ROOT_TOKEN)
		return value

	@staticmethod
	def unordered(items: Iterable[Any]) -> dict[str, list[Any]]:
		"""Marks ``items`` as an unordered array (T-15, B-4): the C# comparer compares it as a multiset."""
		return {UNORDERED_KEY: list(items)}


def dumps(golden: Any) -> str:
	r"""Canonical golden text: sorted keys, two-space indent, ASCII-only (so BOMs and control characters stay visible as
	``\uXXXX`` escapes), and a trailing newline.
	"""
	return json.dumps(golden, indent=2, sort_keys=True, ensure_ascii=True) + "\n"
