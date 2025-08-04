#
# Collective Modding Toolkit
# Copyright (C) 2024, 2025  wxMichael
#
# This program is free software; you can redistribute it and/or
# modify it under the terms of the GNU General Public License
# as published by the Free Software Foundation; either version 2
# of the License, or (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program; if not, see <https://www.gnu.org/licenses/>.
#


import logging
import sys
import webbrowser
from collections.abc import Callable
from pathlib import Path  # noqa: F401
from typing import TYPE_CHECKING

import cmt_globals
from app_settings import AppSettings
from enums import Tab
from game_info_qt import GameInfo
from helpers import PCInfo
from qt_compat import (
    QApplication,
    QCloseEvent,
    QEvent,
    QFontDatabase,
    QHBoxLayout,
    QIcon,
    QKeySequence,
    QLabel,
    QMainWindow,
    QMouseEvent,
    QMutex,
    QMutexLocker,
    QObject,
    QPixmap,
    QProgressBar,
    QShortcut,
    Qt,
    QTabWidget,
    QThread,
    QTimer,
    QVBoxLayout,
    QWidget,
    QWindowStateChangeEvent,
    Signal,
    Slot,
)
from qt_theme import get_dark_stylesheet
from qt_threading import BaseWorker, ThreadManager
from qt_widgets import QtStringVar

# Import Qt tabs from the tabs package
from tabs._about_qt import AboutTab  # noqa: PLC2701
from tabs._f4se_qt import F4SETab  # noqa: PLC2701
from tabs._overview_qt import OverviewTab  # noqa: PLC2701
from tabs._scanner_qt import ScannerTab  # noqa: PLC2701
from tabs._settings_qt import SettingsTab  # noqa: PLC2701
from tabs._tools_qt import ToolsTab  # noqa: PLC2701
from utils import (
	check_for_update_github,
	check_for_update_nexus,
	get_asset_path,
)

if TYPE_CHECKING:
	from helpers import ProblemInfo, SimpleProblemInfo
	from qt_helpers import CMCTabWidget

logger = logging.getLogger(__name__)


class ImageLoaderWorker(QObject):
	"""Worker for loading images in background"""

	finished = Signal()
	image_loaded = Signal(str, QPixmap)  # Signal emitted when each image is loaded

	def __init__(self, image_paths: list[str], image_cache: dict[str, QPixmap], mutex: QMutex) -> None:
		super().__init__()
		self.image_paths = image_paths
		self.image_cache = image_cache
		self.mutex = mutex

	def run(self) -> None:
		"""Load images in background thread"""
		for path in self.image_paths:
			with QMutexLocker(self.mutex):
				if path not in self.image_cache:
					pixmap = QPixmap(str(get_asset_path(path)))
					if not pixmap.isNull():
						self.image_cache[path] = pixmap
						self.image_loaded.emit(path, pixmap)
		self.finished.emit()


class ClickableLabel(QLabel):
	"""Custom QLabel that can handle click events"""

	def __init__(self, text: str = "", url: str = "") -> None:
		super().__init__(text)
		self.url = url
		self.setProperty("class", "link")
		self.setCursor(Qt.CursorShape.PointingHandCursor)

	def mousePressEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		if event.button() == Qt.MouseButton.LeftButton and self.url:
			webbrowser.open(self.url)
		super().mousePressEvent(event)


class CMCheckerQt(QMainWindow):
	# Qt signals for thread communication
	game_info_updated = Signal()
	processing_started = Signal()
	processing_finished = Signal()
	install_type_changed = Signal(str)  # Specify that this signal emits str
	game_path_changed = Signal(str)  # Specify that this signal emits str

	def __init__(self, app: QApplication, settings: AppSettings) -> None:
		super().__init__()
		self.app = app
		self.settings = settings
		self.pc = PCInfo()

		# Create Qt-compatible StringVar objects
		self.install_type_sv = QtStringVar()
		self.game_path_sv = QtStringVar()

		# Create StringVar objects for specs display
		self.specs_sv_1 = QtStringVar(f"{self.pc.os}\n{self.pc.ram}GB RAM")
		self.specs_sv_2 = QtStringVar(f"{self.pc.cpu}\n{self.pc.gpu} {self.pc.vram}GB")

		self._images: dict[str, QPixmap] = {}
		self._image_loading_lock = QMutex()
		self._image_thread: QThread | None = None

		# QtStringVar is already the correct type for Qt version
		self.game = GameInfo(self.install_type_sv, self.game_path_sv)
		self.current_tab: CMCTabWidget | None = None
		self.overview_problems: list[ProblemInfo | SimpleProblemInfo] = []
		self.processing_data = False

		# Initialize thread manager
		self.thread_manager = ThreadManager()

		# Connect signals for property changes
		self.install_type_changed.connect(self.install_type_sv.set)
		self.game_path_changed.connect(self.game_path_sv.set)

		self.setup_window()
		self.preload_critical_images()
		self.check_for_updates()

	def preload_critical_images(self) -> None:
		"""Preload commonly used images to avoid UI blocking"""
		critical_images = [
			"images/icon-32.png",
			"images/update-24.png",
			"images/check-20.png",
			"images/warning-16.png",
			"images/info-16.png",
		]

		# Create worker and thread
		self._image_thread = QThread()
		worker = ImageLoaderWorker(critical_images, self._images, self._image_loading_lock)
		worker.moveToThread(self._image_thread)

		# Connect signals
		self._image_thread.started.connect(worker.run)
		worker.finished.connect(self._image_thread.quit)
		worker.finished.connect(worker.deleteLater)
		self._image_thread.finished.connect(self._image_thread.deleteLater)

		# Start loading
		self._image_thread.start()

	def get_image(self, relative_path: str) -> QPixmap:
		with QMutexLocker(self._image_loading_lock):
			if relative_path not in self._images:
				self._images[relative_path] = QPixmap(str(get_asset_path(relative_path)))
			return self._images[relative_path]

	def closeEvent(self, event: QCloseEvent) -> None:  # noqa: N802
		if self.processing_data:
			event.ignore()
			return

		# Stop image loading thread if running
		if self._image_thread and self._image_thread.isRunning():
			self._image_thread.quit()
			self._image_thread.wait(1000)  # Wait up to 1 second

		# Stop all threads before closing
		active_threads = self.thread_manager.get_active_threads()
		if active_threads:
			logger.debug("Stopping %d active threads before closing", len(active_threads))
			self.thread_manager.stop_all(wait=True, timeout=3000)

		sys.stderr = sys.__stderr__
		event.accept()

	def setup_window(self) -> None:
		# Window properties
		self.setWindowTitle(f"{cmt_globals.APP_TITLE} {cmt_globals.APP_VERSION}")
		self.setFixedSize(cmt_globals.WINDOW_WIDTH, cmt_globals.WINDOW_HEIGHT)
		self.setWindowIcon(QIcon(str(get_asset_path("images/icon-32.png"))))

		# Center window on screen
		screen = self.app.primaryScreen()
		screen_geometry = screen.geometry()
		x = (screen_geometry.width() - cmt_globals.WINDOW_WIDTH) // 2
		y = (screen_geometry.height() - cmt_globals.WINDOW_HEIGHT) // 2
		self.move(x, y)

		# Create central widget and layout
		central_widget = QWidget()
		self.setCentralWidget(central_widget)
		layout = QVBoxLayout(central_widget)
		layout.setContentsMargins(0, 0, 0, 0)

		# Create update banner container (initially empty)
		self.update_container = QWidget()
		self.update_container.setVisible(False)
		layout.addWidget(self.update_container)

		# Create tab widget
		self.tab_widget = QTabWidget()
		layout.addWidget(self.tab_widget)

		# Create status bar with progress bar
		self.status_bar = self.statusBar()
		self.progress_bar = QProgressBar()
		self.progress_bar.setMaximumWidth(200)
		self.progress_bar.setVisible(False)
		self.status_bar.addPermanentWidget(self.progress_bar)

		# Create tabs - use Qt versions where available
		self.tabs: dict[Tab, CMCTabWidget] = {}

		# Create all Qt tabs
		self.tabs[Tab.Overview] = OverviewTab(self, self.tab_widget)
		self.tabs[Tab.F4SE] = F4SETab(self, self.tab_widget)
		self.tabs[Tab.Scanner] = ScannerTab(self, self.tab_widget)
		self.tabs[Tab.Tools] = ToolsTab(self, self.tab_widget)
		self.tabs[Tab.Settings] = SettingsTab(self, self.tab_widget)
		self.tabs[Tab.About] = AboutTab(self, self.tab_widget)

		# Connect tab change signal
		self.tab_widget.currentChanged.connect(self.on_tab_changed)

		# Keyboard shortcuts
		QShortcut(QKeySequence("Escape"), self, self.close)

		# Load custom font
		font_path = get_asset_path("fonts/CascadiaMono.ttf")
		if font_path.exists():
			QFontDatabase.addApplicationFont(str(font_path))

		# Apply custom dark theme
		self.setStyleSheet(get_dark_stylesheet())

	def check_for_updates(self) -> None:
		update_source = self.settings.dict["update_source"]
		if update_source == "none":
			return

		nexus_version = check_for_update_nexus() if update_source in {"nexus", "both"} else None
		github_version = check_for_update_github() if update_source in {"github", "both"} else None
		if not (nexus_version or github_version):
			return

		# Create update banner
		self.update_container.setVisible(True)
		update_layout = QHBoxLayout(self.update_container)
		update_layout.setContentsMargins(5, 5, 5, 5)

		# Set update container class for styling
		self.update_container.setObjectName("updateBanner")

		# Update icon and text
		icon_label = QLabel()
		icon_label.setPixmap(self.get_image("images/update-24.png"))
		update_layout.addWidget(icon_label)

		text_label = QLabel("An update is available:")
		update_layout.addWidget(text_label)

		if nexus_version:
			nexus_link = ClickableLabel(f"v{nexus_version} (NexusMods)", cmt_globals.NEXUS_LINK)
			nexus_link.setToolTip("Open Nexus Mods")
			update_layout.addWidget(nexus_link)

		if github_version and nexus_version:
			separator = QLabel(" / ")
			update_layout.addWidget(separator)

		if github_version:
			github_link = ClickableLabel(f"v{github_version} (GitHub)", cmt_globals.GITHUB_LINK)
			github_link.setToolTip("Open GitHub")
			update_layout.addWidget(github_link)

		update_layout.addStretch()

	def changeEvent(self, event: QEvent) -> None:  # noqa: N802
		if event.type() == QEvent.Type.WindowStateChange:
			if self.isMinimized():
				self.on_minimize()
			elif isinstance(event, QWindowStateChangeEvent) and event.oldState() & Qt.WindowState.WindowMinimized:
				self.on_restore()
		super().changeEvent(event)

	def on_minimize(self) -> None:
		# Handle scanner tab windows when minimizing
		scanner_tab = self.tabs.get(Tab.Scanner)
		if scanner_tab and scanner_tab.is_loaded and isinstance(scanner_tab, ScannerTab):
			if hasattr(scanner_tab, "side_pane") and scanner_tab.side_pane:
				scanner_tab.side_pane.hide()
			if hasattr(scanner_tab, "details_pane") and scanner_tab.details_pane:
				scanner_tab.details_pane.hide()

	def on_restore(self) -> None:
		# Handle scanner tab windows when restoring
		scanner_tab = self.tabs.get(Tab.Scanner)
		if scanner_tab and scanner_tab.is_loaded and isinstance(scanner_tab, ScannerTab):
			if hasattr(scanner_tab, "side_pane") and scanner_tab.side_pane:
				scanner_tab.side_pane.show()
			if hasattr(scanner_tab, "details_pane") and scanner_tab.details_pane:
				scanner_tab.details_pane.show()

	def on_tab_changed(self, index: int) -> None:
		if self.current_tab is not None and hasattr(self.current_tab, "switch_from"):
			self.current_tab.switch_from()

		# Get tab name from widget
		tab_text = self.tab_widget.tabText(index)

		# Map tab text to enum
		tab_map = {
			"Overview": Tab.Overview,
			"F4SE": Tab.F4SE,
			"Scanner": Tab.Scanner,
			"Tools": Tab.Tools,
			"Settings": Tab.Settings,
			"About": Tab.About,
		}

		tab_enum = tab_map.get(tab_text)
		if tab_enum and tab_enum in self.tabs:
			self.current_tab = self.tabs[tab_enum]
			self.current_tab.load()

	def refresh_tab(self, tab: Tab) -> None:
		logger.debug("Refresh Tab : %s", tab)
		if tab in self.tabs:
			self.tabs[tab].refresh()

	# Compatibility method for Tkinter code
	@property
	def root(self) -> "CMCheckerQt":
		# For compatibility with tabs that reference self.root
		return self

	def update_ui(self) -> None:  # noqa: PLR6301
		"""Process pending Qt events to update the UI"""
		QApplication.processEvents()

	# Thread-safe UI update methods
	@Slot(str)
	def update_status(self, message: str) -> None:
		"""Thread-safe method to update status messages"""
		self.status_bar.showMessage(message, 5000)  # Show for 5 seconds
		logger.debug("Status update: %s", message)

	@Slot(int)
	def update_progress(self, value: int) -> None:
		"""Thread-safe method to update progress bar"""
		if value > 0:
			self.progress_bar.setVisible(True)
			self.progress_bar.setValue(value)
		else:
			self.progress_bar.setVisible(False)

	@Slot()
	def on_processing_started(self) -> None:
		"""Called when heavy processing starts"""
		self.processing_data = True
		self.progress_bar.setVisible(True)
		self.progress_bar.setValue(0)
		self.processing_started.emit()

	@Slot()
	def on_processing_finished(self) -> None:
		"""Called when heavy processing finishes"""
		self.processing_data = False
		self.progress_bar.setVisible(False)
		self.processing_finished.emit()

	# Tkinter compatibility methods
	def after(self, ms: int, func: Callable[..., None], *args: object) -> QTimer:  # noqa: PLR6301
		"""Compatibility method for Tkinter's after() - uses QTimer"""
		timer = QTimer()
		timer.setSingleShot(True)
		timer.timeout.connect(lambda: func(*args))
		timer.start(ms)
		return timer

	def after_idle(self, func: Callable[..., None], *args: object) -> None:  # noqa: PLR6301
		"""Compatibility method for Tkinter's after_idle()"""
		QTimer.singleShot(0, lambda: func(*args))

	def wm_title(self, title: str) -> None:
		"""Compatibility method for Tkinter's wm_title()"""
		self.setWindowTitle(title)

	def wm_geometry(self, geometry: str) -> None:
		"""Compatibility method for Tkinter's wm_geometry()"""
		# Parse geometry string like "800x600+100+50"
		if "+" in geometry:
			size, pos = geometry.split("+", 1)
			width, height = map(int, size.split("x"))
			x, y = map(int, pos.split("+"))
			self.setGeometry(x, y, width, height)
		else:
			width, height = map(int, geometry.split("x"))
			self.resize(width, height)

	def tk_destroy(self) -> None:
		"""Compatibility method for Tkinter's destroy()"""
		self.close()

	# Additional helper methods
	def start_worker(self, worker: BaseWorker, thread_name: str | None = None) -> BaseWorker:
		"""Start a worker in a managed thread

		Args:
		    worker: The worker instance to run
		    thread_name: Optional thread name (defaults to worker class name)

		Returns:
		    The worker instance for signal connections
		"""
		if thread_name is None:
			thread_name = worker.__class__.__name__

		# Connect standard signals
		worker.signals.progress.connect(self.update_progress)
		worker.signals.status.connect(self.update_status)
		worker.signals.started.connect(self.on_processing_started)
		worker.signals.finished.connect(self.on_processing_finished)

		# Start the worker
		self.thread_manager.start_worker(worker, thread_name)
		return worker
