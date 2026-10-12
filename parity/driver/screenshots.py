"""Captures a screenshot pair: the Tk reference and the C# app, run against the same Parity Scenario at 100 % DPI.

Usage, from the repo root, after publishing the C# app (``dotnet publish dotnet/src/CMToolkit.App -c Release -r win-x64
-o dotnet/artifacts/publish``):

    uv run python parity/driver/screenshots.py shell/main-window --slice shell
    uv run python parity/driver/screenshots.py shell/main-window --slice shell --crop tab-strip=0,0,-1,80
    uv run python parity/driver/screenshots.py shell/main-window --slice tools --tab Tools
    uv run python parity/driver/screenshots.py downgrader/og-steam --slice downgrader/og-steam --modal Downgrader
    uv run python parity/driver/screenshots.py scanner/overview-issues --slice scanner/results --tab Scanner --scan --panes

It writes ``parity/screenshots/<slice>/tk.png`` and ``avalonia.png`` (each window's outer rectangle, title bar included),
``compare.png`` (the two side by side plus a 50/50 blend), and one ``compare-<name>.png`` per ``--crop`` (the three
crops stacked, at 2x). A crop is ``x,y,width,height`` in window pixels; a width or height of -1 runs to the edge.
A human approves the pair in the slice PR; nothing compares pixels automatically, and CI never runs this.

The reference runs as ``src/main.py`` itself, unmodified, in a child process with the scenario's host fakes installed
(``Session`` in show mode). The C# exe starts in the scenario's ``cwd`` with ``CMT_PARITY_SCENARIO`` and
``CMT_PARITY_ROOT`` set, so it runs on the same host block (its ``ScenarioHostEnvironment``). Each app gets its own copy
of the tree. Scripted ``dialogs`` answer only the reference's; the exe shows real ones, so pick scenarios that ask none.
"""

import argparse
import contextlib
import ctypes
import os
import runpy
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path
from typing import Any

import psutil
from PIL import Image, ImageDraw

from harness import PARITY, REPO_ROOT, SRC
from harness.scenario import Machine, load, materialize

TITLE = "Collective Modding Toolkit v0.6.2-dev"
DEFAULT_EXE = REPO_ROOT / "dotnet" / "artifacts" / "publish" / "cm-toolkit.exe"
WM_CLOSE = 0x0010
WM_LBUTTONDOWN = 0x0201
WM_LBUTTONUP = 0x0202
MK_LBUTTON = 0x0001
CWP_SKIPINVISIBLE = 0x0001
CWP_SKIPTRANSPARENT = 0x0004
# Where the C# app's tab headers sit, in client pixels (the strip is fixed: the window can't be resized). Tk can't be
# driven by posted clicks (it reads the real button state), so the reference selects its tab in-process instead.
TAB_CENTRES = {
	"Overview": (53, 25),
	"F4SE": (139, 25),
	"Scanner": (220, 25),
	"Tools": (306, 25),
	"Settings": (397, 25),
	"About": (487, 25),
}
# The modals ``--modal`` can open: where the C# app's opening button sits on the Overview tab, in client pixels (posted
# clicks again), and the reference's opener, called in-process on the CMChecker once the Overview has loaded.
MODAL_BUTTONS = {
	"Downgrader": (142, 420),  # Overview, Binaries frame: "Downgrade Manager..."
}
# Tells the reference child which modal to open, so its positional arguments stay as they were.
MODAL_VARIABLE = "CMT_SCREENSHOT_MODAL"
# Tells the reference child to run an Overview Issues scan once the Scanner tab is up (``--scan``).
SCAN_VARIABLE = "CMT_SCREENSHOT_SCAN"
# The C# Scanner side pane's title (PaneWindow), and where ``--scan`` clicks in its client pixels: the six data boxes
# (Errors .. Race Subgraphs) to untick them, then Scan Game. Measured from parity/screenshots/scanner/side-pane.
SIDE_PANE_TITLE = "Collective Modding Toolkit pane (Side)"
SCAN_CLICKS = [(26, 70), (26, 98), (26, 126), (26, 154), (26, 182), (26, 210), (100, 375)]
PW_RENDERFULLCONTENT = 2
DPI_100 = 96

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = [EnumWindowsProc, wintypes.LPARAM]
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetDpiForWindow.argtypes = [wintypes.HWND]
user32.GetDpiForWindow.restype = wintypes.UINT
user32.GetDC.argtypes = [wintypes.HWND]
user32.GetDC.restype = wintypes.HDC
user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
user32.ScreenToClient.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
user32.ChildWindowFromPointEx.argtypes = [wintypes.HWND, wintypes.POINT, wintypes.UINT]
user32.ChildWindowFromPointEx.restype = wintypes.HWND
gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
gdi32.CreateCompatibleDC.restype = wintypes.HDC
gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
gdi32.CreateCompatibleBitmap.restype = wintypes.HBITMAP
gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
gdi32.SelectObject.restype = wintypes.HGDIOBJ
gdi32.GetDIBits.argtypes = [wintypes.HDC, wintypes.HBITMAP, wintypes.UINT, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT]
gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
gdi32.DeleteDC.argtypes = [wintypes.HDC]


class BitmapInfoHeader(ctypes.Structure):
	"""Win32 ``BITMAPINFOHEADER``: describes the 32-bit top-down DIB that ``GetDIBits`` copies the capture into."""

	_fields_ = [
		("biSize", wintypes.DWORD),
		("biWidth", wintypes.LONG),
		("biHeight", wintypes.LONG),
		("biPlanes", wintypes.WORD),
		("biBitCount", wintypes.WORD),
		("biCompression", wintypes.DWORD),
		("biSizeImage", wintypes.DWORD),
		("biXPelsPerMeter", wintypes.LONG),
		("biYPelsPerMeter", wintypes.LONG),
		("biClrUsed", wintypes.DWORD),
		("biClrImportant", wintypes.DWORD),
	]


def process_tree(pid: int) -> set[int]:
	r"""``pid`` and its descendants. A venv's ``Scripts\python.exe`` is a redirector that starts the real interpreter as
	a child, so the Tk window belongs to a grandchild of the process this script starts.
	"""
	try:
		return {pid, *(child.pid for child in psutil.Process(pid).children(recursive=True))}
	except psutil.NoSuchProcess:
		return {pid}


def find_window(pid: int, title: str) -> int | None:
	"""A visible top-level window with exactly ``title``, owned by ``pid`` or one of its descendants."""
	found: list[int] = []
	pids = process_tree(pid)

	def visit(hwnd: int, _: int) -> bool:
		owner = wintypes.DWORD()
		user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
		if owner.value in pids and user32.IsWindowVisible(hwnd):
			text = ctypes.create_unicode_buffer(512)
			user32.GetWindowTextW(hwnd, text, len(text))
			if text.value == title:
				found.append(hwnd)
		return True

	user32.EnumWindows(EnumWindowsProc(visit), 0)
	return found[0] if found else None


def capture(hwnd: int) -> Image.Image:
	"""The window's outer rectangle via ``PrintWindow(PW_RENDERFULLCONTENT)``, which also captures GPU-composed
	windows such as Avalonia's, without bringing them to the front (the same method as ``dotnet/eng/Test-ShellStarts.ps1``).
	"""
	rect = wintypes.RECT()
	user32.GetWindowRect(hwnd, ctypes.byref(rect))
	width, height = rect.right - rect.left, rect.bottom - rect.top
	screen_dc = user32.GetDC(None)
	memory_dc = gdi32.CreateCompatibleDC(screen_dc)
	bitmap = gdi32.CreateCompatibleBitmap(screen_dc, width, height)
	previous = gdi32.SelectObject(memory_dc, bitmap)
	try:
		if not user32.PrintWindow(hwnd, memory_dc, PW_RENDERFULLCONTENT):
			raise ctypes.WinError(ctypes.get_last_error())
		header = BitmapInfoHeader(ctypes.sizeof(BitmapInfoHeader), width, -height, 1, 32, 0, 0, 0, 0, 0, 0)
		pixels = ctypes.create_string_buffer(width * height * 4)
		gdi32.GetDIBits(memory_dc, bitmap, 0, height, pixels, ctypes.byref(header), 0)
		return Image.frombuffer("RGB", (width, height), pixels, "raw", "BGRX", 0, 1).copy()
	finally:
		gdi32.SelectObject(memory_dc, previous)
		gdi32.DeleteObject(bitmap)
		gdi32.DeleteDC(memory_dc)
		user32.ReleaseDC(None, screen_dc)


def visible_windows(pid: int) -> list[int]:
	"""Every visible top-level window owned by ``pid`` or one of its descendants, in Z order (topmost first)."""
	found: list[int] = []
	pids = process_tree(pid)

	def visit(hwnd: int, _: int) -> bool:
		owner = wintypes.DWORD()
		user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
		if owner.value in pids and user32.IsWindowVisible(hwnd):
			found.append(hwnd)
		return True

	user32.EnumWindows(EnumWindowsProc(visit), 0)
	return found


def capture_with_panes(pid: int, main: int) -> Image.Image:
	"""The main window with every other visible window of the app pasted where it sits on screen relative to it (the
	Scanner's docked panes), on black. The canvas starts at the main window's outer top-left, so pairs align.
	"""
	rects: dict[int, wintypes.RECT] = {}
	for hwnd in [main, *(h for h in reversed(visible_windows(pid)) if h != main)]:
		rect = wintypes.RECT()
		user32.GetWindowRect(hwnd, ctypes.byref(rect))
		rects[hwnd] = rect
	left, top = rects[main].left, rects[main].top
	right = max(r.right for r in rects.values())
	bottom = max(r.bottom for r in rects.values())
	canvas = Image.new("RGB", (right - left, bottom - top))
	for hwnd, rect in rects.items():
		canvas.paste(capture(hwnd), (rect.left - left, rect.top - top))
	return canvas


def click(hwnd: int, point: tuple[int, int]) -> None:
	"""Clicks at ``point`` in the window's client pixels without moving the real pointer: a posted left button down
	and up, sent to the deepest visible child window under the point.
	"""
	screen = wintypes.POINT(*point)
	user32.ClientToScreen(hwnd, ctypes.byref(screen))
	target = hwnd
	while True:
		local = wintypes.POINT(screen.x, screen.y)
		user32.ScreenToClient(target, ctypes.byref(local))
		child = user32.ChildWindowFromPointEx(target, local, CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT)
		if not child or child == target:
			break
		target = child
	local = wintypes.POINT(screen.x, screen.y)
	user32.ScreenToClient(target, ctypes.byref(local))
	position = (local.y << 16) | (local.x & 0xFFFF)
	user32.PostMessageW(target, WM_LBUTTONDOWN, MK_LBUTTON, position)
	user32.PostMessageW(target, WM_LBUTTONUP, 0, position)


def run_and_capture(
	command: list[str],
	cwd: Path,
	env: dict[str, str] | None,
	label: str,
	timeout: float,
	*,
	allow_dpi: bool,
	tab: str | None = None,
	click_tab: bool = False,
	modal: str | None = None,
	scan: bool = False,
	panes: bool = False,
	scan_all: bool = False,
) -> Image.Image:
	"""Starts an app, waits for its main window, lets it render, captures it, and closes it (WM_CLOSE, then kill).

	With ``tab``, the capture waits longer for that tab to be selected and built; ``click_tab`` makes this function
	select it by clicking its header (the C# app), rather than the app selecting it itself (the reference child).
	With ``modal``, the window captured is that modal instead of the main window; with ``click_tab`` it is opened by
	clicking its button (``MODAL_BUTTONS``), otherwise the app opens it itself. With ``scan`` (on the Scanner tab), an
	Overview Issues scan runs first: ``click_tab`` clicks the side pane's boxes and Scan Game (``SCAN_CLICKS``),
	otherwise the app runs it itself; with ``scan_all`` too, every box stays ticked and only Scan Game is clicked. With ``panes``, the app's other windows are pasted in place around the main one.
	"""
	process = subprocess.Popen(command, cwd=cwd, env=env)
	try:
		deadline = time.monotonic() + timeout
		hwnd = None
		while time.monotonic() < deadline and hwnd is None:
			if process.poll() is not None:
				msg = f"{label} exited with code {process.returncode} before showing its window"
				raise RuntimeError(msg)
			hwnd = find_window(process.pid, TITLE)
			if hwnd is None:
				time.sleep(0.25)
		if hwnd is None:
			msg = f"{label} showed no window titled {TITLE!r} within {timeout} s"
			raise RuntimeError(msg)

		# Let the first frames render (the reference loads the Overview tab after the window appears).
		time.sleep(2)
		if tab is not None:
			if click_tab:
				click(hwnd, TAB_CENTRES[tab])
			# Long enough for the reference's delayed select and for either app to build the tab.
			time.sleep(3)
		if scan:
			if click_tab:
				side = find_window(process.pid, SIDE_PANE_TITLE)
				if side is None:
					msg = f"{label} showed no Scanner side pane"
					raise RuntimeError(msg)
				for point in SCAN_CLICKS[-1:] if scan_all else SCAN_CLICKS:
					click(side, point)
					time.sleep(0.2)
			# Long enough for the reference's delayed scan and for either app to refresh the Overview and list results.
			time.sleep(4)
		if modal is not None:
			if click_tab:
				click(hwnd, MODAL_BUTTONS[modal])
			# Long enough for the reference's delayed open and for either app to detect and build the modal.
			time.sleep(3)
			hwnd = find_window(process.pid, modal)
			if hwnd is None:
				msg = f"{label} showed no window titled {modal!r}"
				raise RuntimeError(msg)
		dpi = user32.GetDpiForWindow(hwnd)
		if dpi != DPI_100 and not allow_dpi:
			msg = f"{label}'s window is at {dpi * 100 // DPI_100} % scaling; screenshot pairs are taken at 100 % (or pass --allow-dpi)"
			raise RuntimeError(msg)
		return capture_with_panes(process.pid, hwnd) if panes else capture(hwnd)
	finally:
		if process.poll() is None:
			hwnd = find_window(process.pid, TITLE)
			if hwnd:
				user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
			try:
				process.wait(5)
			except subprocess.TimeoutExpired:
				kill_tree(process.pid)


def kill_tree(pid: int, timeout: float = 5) -> None:
	"""Kills ``pid`` and its descendants and waits for them to exit, so the caller can delete the tree they used.

	Killing only ``pid`` isn't enough: under a venv redirector the window belongs to a descendant, which would survive
	as an orphan. The descendants are listed before anything is killed, since an orphan no longer shows up as a child.
	Raises ``RuntimeError`` if any are still running after ``timeout`` seconds.
	"""
	try:
		root = psutil.Process(pid)
		processes = [*root.children(recursive=True), root]
	except psutil.NoSuchProcess:
		return
	for proc in processes:
		# A process that exited on its own between listing and killing is the outcome wanted anyway.
		with contextlib.suppress(psutil.NoSuchProcess):
			proc.kill()
	_, alive = psutil.wait_procs(processes, timeout=timeout)
	if alive:
		msg = f"processes {sorted(p.pid for p in alive)} survived being killed"
		raise RuntimeError(msg)


def label_panel(image: Image.Image, caption: str) -> Image.Image:
	"""The image with a small magenta caption strip above it, as in the walking skeleton's hand-made compare.png."""
	strip = 16
	panel = Image.new("RGB", (image.width, image.height + strip), "black")
	panel.paste(image, (0, strip))
	ImageDraw.Draw(panel).text((4, 2), caption, fill=(255, 0, 255))
	return panel


def _aligned(tk: Image.Image, avalonia: Image.Image) -> tuple[Image.Image, Image.Image, Image.Image]:
	"""Both captures padded to a common size (windows can differ by a frame pixel or two), plus their 50/50 blend."""
	width, height = max(tk.width, avalonia.width), max(tk.height, avalonia.height)
	tk_full, av_full = (_pad(i, width, height) for i in (tk, avalonia))
	return tk_full, av_full, Image.blend(tk_full, av_full, 0.5)


def compose_side_by_side(tk: Image.Image, avalonia: Image.Image, gap: int = 10) -> Image.Image:
	"""Tk, Avalonia and their 50/50 blend side by side, each captioned."""
	tk_full, av_full, blend = _aligned(tk, avalonia)
	width = tk_full.width
	panels = [
		label_panel(tk_full, "Tk reference (src/main.py)"),
		label_panel(av_full, "Avalonia (cm-toolkit.exe)"),
		label_panel(blend, "50/50 blend"),
	]
	out = Image.new("RGB", (sum(p.width for p in panels) + gap * 2, panels[0].height), "black")
	for index, panel in enumerate(panels):
		out.paste(panel, (index * (width + gap), 0))
	return out


def compose_crop(tk: Image.Image, avalonia: Image.Image, box: tuple[int, int, int, int], scale: int = 2, gap: int = 10) -> Image.Image:
	"""The same window region from Tk, Avalonia and the blend, stacked and scaled up for pixel-level reading."""
	x, y, w, h = box
	images = _aligned(tk, avalonia)
	w = images[0].width - x if w < 0 else w
	h = images[0].height - y if h < 0 else h
	crops = [i.crop((x, y, x + w, y + h)) for i in images]
	crops = [c.resize((c.width * scale, c.height * scale), Image.Resampling.NEAREST) for c in crops]
	out = Image.new("RGB", (crops[0].width, sum(c.height for c in crops) + gap * scale * 2), "black")
	for index, crop in enumerate(crops):
		out.paste(crop, (0, index * (crop.height + gap * scale)))
	return out


def _pad(image: Image.Image, width: int, height: int) -> Image.Image:
	"""``image`` on a black canvas of ``width`` x ``height``, anchored top-left."""
	if image.size == (width, height):
		return image
	padded = Image.new("RGB", (width, height), "black")
	padded.paste(image, (0, 0))
	return padded


def parse_crop(text: str) -> tuple[str, tuple[int, int, int, int]]:
	"""Parses a ``--crop`` value, ``name=x,y,width,height``, into the name and the box.

	Raises ``argparse.ArgumentTypeError`` for anything else, so argparse reports it as a usage error.
	"""
	name, _, numbers = text.partition("=")
	parts = [int(n) for n in numbers.split(",")]
	if not name or len(parts) != 4:
		msg = f"--crop must be name=x,y,width,height, got {text!r}"
		raise argparse.ArgumentTypeError(msg)
	return name, (parts[0], parts[1], parts[2], parts[3])


def run_reference_child(scenario_id: str, root: str, tab: str | None = None) -> None:
	"""Child mode: runs the unmodified ``src/main.py`` inside an already materialized root, with the fakes installed.

	With ``tab``, ``CMChecker`` is wrapped so that, two seconds after it is built (once the window is up and the
	Overview tab has loaded), the notebook selects that tab, just as a click on its header would.
	"""
	from harness.reference import Session  # noqa: PLC0415

	machine = Machine(load(scenario_id), Path(root))
	Session(machine, "show").start()
	if tab is not None:
		import cm_checker  # noqa: PLC0415
		from enums import Tab  # noqa: PLC0415

		original_init = cm_checker.CMChecker.__init__

		def init_then_select(self: Any, *args: Any, **kwargs: Any) -> None:
			original_init(self, *args, **kwargs)
			page = self.tabs[Tab[tab]]
			self.root.after(2000, lambda: page.master.select(page))

		cm_checker.CMChecker.__init__ = init_then_select  # type: ignore[method-assign]
	modal = os.environ.get(MODAL_VARIABLE)
	if modal is not None:
		import cm_checker  # noqa: PLC0415

		openers = {"Downgrader": _open_downgrader}
		original = cm_checker.CMChecker.__init__

		def init_then_open(self: Any, *args: Any, **kwargs: Any) -> None:
			original(self, *args, **kwargs)
			self.root.after(2000, lambda: openers[modal](self))

		cm_checker.CMChecker.__init__ = init_then_open  # type: ignore[method-assign]
	if os.environ.get(SCAN_VARIABLE):
		import cm_checker  # noqa: PLC0415

		before_scan = cm_checker.CMChecker.__init__

		def init_then_scan(self: Any, *args: Any, **kwargs: Any) -> None:
			before_scan(self, *args, **kwargs)
			# A second after the tab is selected: untick the data boxes, then press Scan Game, as --scan's clicks do
			# (--scan-all: just press Scan Game).
			scan = _scan_every_box if os.environ[SCAN_VARIABLE] == "all" else _scan_overview_issues
			self.root.after(3000, lambda: scan(self))

		cm_checker.CMChecker.__init__ = init_then_scan  # type: ignore[method-assign]
	runpy.run_path(str(SRC / "main.py"), run_name="__main__")


def _scan_overview_issues(checker: Any) -> None:
	"""Unticks every Scanner box but Overview Issues and presses Scan Game, on the open side pane."""
	from enums import Tab  # noqa: PLC0415

	side = checker.tabs[Tab.Scanner].side_pane
	frame = next(c for c in side.winfo_children() if c.winfo_class() == "TLabelframe")
	for check in frame.pack_slaves()[1:]:
		check.invoke()
	side.button_scan.invoke()


def _scan_every_box(checker: Any) -> None:
	"""Presses Scan Game on the open side pane, every box still ticked as it starts (B-1)."""
	from enums import Tab  # noqa: PLC0415

	checker.tabs[Tab.Scanner].side_pane.button_scan.invoke()


def _open_downgrader(checker: Any) -> None:
	"""What the Overview's ``Downgrade Manager...`` button runs."""
	from downgrader import Downgrader  # noqa: PLC0415

	Downgrader(checker.root, checker)


def main() -> int:
	"""Captures the pair and writes the images, or (``--reference-child``) runs the Tk reference. Returns the exit code."""
	# The child mode has its own arguments, so it is handled before the user-facing parser requires --slice.
	if len(sys.argv) in {4, 5} and sys.argv[1] == "--reference-child":
		run_reference_child(*sys.argv[2:])
		return 0

	parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	parser.add_argument("scenario", help="the <module>/<scenario> ID to run both apps against")
	parser.add_argument("--slice", required=True, help="the folder under parity/screenshots/ to write to")
	parser.add_argument("--exe", type=Path, default=DEFAULT_EXE, help=f"the published C# exe (default: {DEFAULT_EXE})")
	parser.add_argument("--crop", type=parse_crop, action="append", default=[], help="name=x,y,width,height (repeatable)")
	parser.add_argument("--tab", choices=list(TAB_CENTRES), help="the tab to select before capturing (default: Overview)")
	parser.add_argument("--modal", choices=list(MODAL_BUTTONS), help="capture this modal, opened from the Overview, instead")
	parser.add_argument("--scan", action="store_true", help="with --tab Scanner: run an Overview Issues scan first")
	parser.add_argument("--scan-all", action="store_true", help="with --tab Scanner: run a scan with every box ticked first")
	parser.add_argument("--panes", action="store_true", help="paste the app's other windows (the Scanner panes) in place")
	parser.add_argument("--timeout", type=float, default=60, help="seconds to wait for each window")
	parser.add_argument("--allow-dpi", action="store_true", help="capture even when the window isn't at 100 %% scaling")
	args = parser.parse_args()

	# Physical pixels, matching what PrintWindow captures, whatever this process's default awareness is.
	user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

	scenario = load(args.scenario)
	exe = args.exe.resolve()
	if not exe.is_file():
		sys.stderr.write(f"No published exe at {exe}. Publish it first (see --help).\n")
		return 1

	out_dir = PARITY / "screenshots" / args.slice
	out_dir.mkdir(parents=True, exist_ok=True)
	env = {**os.environ, "PYTHONHASHSEED": "0"}
	if args.modal is not None:
		env[MODAL_VARIABLE] = args.modal
	if args.scan or args.scan_all:
		env[SCAN_VARIABLE] = "all" if args.scan_all else "1"

	tk_machine = materialize(scenario)
	try:
		tk = run_and_capture(
			[
				sys.executable,
				str(Path(__file__).resolve()),
				"--reference-child",
				scenario.id,
				str(tk_machine.root),
				*([args.tab] if args.tab else []),
			],
			REPO_ROOT,
			env,
			"The Tk reference",
			args.timeout,
			allow_dpi=args.allow_dpi,
			tab=args.tab,
			modal=args.modal,
			scan=args.scan or args.scan_all,
			panes=args.panes,
			scan_all=args.scan_all,
		)
	finally:
		tk_machine.cleanup()

	av_machine = materialize(scenario)
	try:
		cwd = Path(av_machine.expand(scenario.host["cwd"])) if "cwd" in scenario.host else av_machine.root
		# The exe's ScenarioHostEnvironment reads the same host block, so both apps run on the same fake machine.
		av_env = {
			**os.environ,
			"CMT_PARITY_SCENARIO": str(scenario.directory / "scenario.json"),
			"CMT_PARITY_ROOT": str(av_machine.root),
		}
		avalonia = run_and_capture(
			[str(exe)],
			cwd,
			av_env,
			"cm-toolkit.exe",
			args.timeout,
			allow_dpi=args.allow_dpi,
			tab=args.tab,
			click_tab=True,
			modal=args.modal,
			scan=args.scan or args.scan_all,
			panes=args.panes,
			scan_all=args.scan_all,
		)
	finally:
		av_machine.cleanup()

	tk.save(out_dir / "tk.png")
	avalonia.save(out_dir / "avalonia.png")
	compose_side_by_side(tk, avalonia).save(out_dir / "compare.png")
	for name, box in args.crop:
		compose_crop(tk, avalonia, box).save(out_dir / f"compare-{name}.png")
	sys.stdout.write(f"Wrote {out_dir.relative_to(REPO_ROOT)}: tk.png {tk.size}, avalonia.png {avalonia.size}\n")
	return 0


if __name__ == "__main__":
	sys.exit(main())
