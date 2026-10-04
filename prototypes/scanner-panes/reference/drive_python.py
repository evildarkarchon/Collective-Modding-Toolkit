"""PROTOTYPE helper (throwaway): drives the Python reference implementation into the docked-panes state.

Boots the app exactly like ``src/main.py``, switches to the Scanner tab, runs a real scan, selects the first
result row so the details pane opens, then prints every window's geometry (logical Tk pixels, which equal
physical pixels because the shipped build is DPI-unaware) and stays up for ``shot.ps1`` to capture.

Run from a scratch directory so ``settings.json`` / ``cm-toolkit.log`` don't land in the worktree::

    cd $env:TEMP\\cmt-ref; uv run --project <worktree> python <worktree>\\prototypes\\scanner-panes\\reference\\drive_python.py

Optional argv[1] = "move" also moves the root by +137/+61 after the details pane opens and re-prints geometry,
to show what the <Configure> handler does with the panes.
"""

import sys
from pathlib import Path

SRC = Path(__file__).resolve().parents[3] / "src"
sys.path.insert(0, str(SRC))

import logging  # noqa: E402
from tkinter import Tk  # noqa: E402

from app_settings import AppSettings  # noqa: E402
from cm_checker import CMChecker  # noqa: E402
from enums import Tab  # noqa: E402
from helpers import StdErr  # noqa: E402
from utils import get_asset_path, load_font, set_theme  # noqa: E402

logging.basicConfig(filename="cm-toolkit.log", level=logging.INFO)

settings = AppSettings()
load_font(str(get_asset_path("fonts/CascadiaMono.ttf")))
root = Tk()
root.wm_withdraw()
root.update_idletasks()
sys.stderr = StdErr(root)
cmc = CMChecker(root, settings)
set_theme(root)
root.update_idletasks()
root.wm_deiconify()
root.update_idletasks()

MODE = sys.argv[1] if len(sys.argv) > 1 else ""
# argv[2]: pick the first result row whose group (problem) title contains this text, e.g. "Unexpected".
PICK = sys.argv[2] if len(sys.argv) > 2 else ""


def geom(label: str, w) -> None:
	"""Prints a window's client origin and size as Tk reports them."""
	print(f"{label:8} client=({w.winfo_rootx()},{w.winfo_rooty()}) size={w.winfo_width()}x{w.winfo_height()}", flush=True)


def dump(tag: str) -> None:
	scanner = cmc.tabs[Tab.Scanner]
	print(f"--- {tag}", flush=True)
	geom("root", root)
	if scanner.side_pane:
		geom("side", scanner.side_pane)
	if scanner.details_pane:
		geom("details", scanner.details_pane)


def open_scanner() -> None:
	notebook = next(w for w in root.winfo_children() if w.winfo_class() == "TNotebook")
	notebook.select(2)
	root.after(1500, start_scan)


def start_scan() -> None:
	scanner = cmc.tabs[Tab.Scanner]
	dump("scanner tab, side pane only")
	scanner.start_threaded_scan()
	root.after(500, wait_scan)


def wait_scan() -> None:
	scanner = cmc.tabs[Tab.Scanner]
	if scanner.thread_scan is not None or not scanner.tree_results_data:
		root.after(500, wait_scan)
		return
	# Select the first leaf that has ProblemInfo, which is what a user click does.
	tree = scanner.tree_results
	first = next(
		(i for i in scanner.tree_results_data if PICK and PICK in str(tree.item(tree.parent(i), "text"))),
		next(iter(scanner.tree_results_data)),
	)
	parent = scanner.tree_results.parent(first)
	scanner.tree_results.item(parent, open=True)
	scanner.tree_results.selection_set(first)
	scanner.tree_results.see(first)
	root.after(800, after_select)


def after_select() -> None:
	dump("details pane open")
	print("READY", flush=True)
	if MODE == "move":
		x, y = root.winfo_x(), root.winfo_y()
		root.wm_geometry(f"+{x + 137}+{y + 61}")
		root.after(800, lambda: dump("after move +137/+61"))


root.after(1500, open_scanner)
root.mainloop()
