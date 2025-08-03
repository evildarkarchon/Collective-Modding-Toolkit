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
from typing import Optional

from qt_compat import *
from qt_theme import get_dark_stylesheet

from tabs import __init_qt__ as tabs_qt
from app_settings import AppSettings
from enums import Tab
from game_info import GameInfo
from globals import *
from helpers import (
    CMCheckerInterface,
    CMCTabFrame,
    PCInfo,
)
from qt_helpers import CMCTabWidget
from utils import (
    check_for_update_github,
    check_for_update_nexus,
    get_asset_path,
)

logger = logging.getLogger(__name__)


class QtStringVar:
    """Qt-compatible replacement for Tkinter StringVar"""
    def __init__(self, value=""):
        self._value = value
        self._callbacks = []
    
    def get(self):
        return self._value
    
    def set(self, value):
        self._value = value
        for callback in self._callbacks:
            callback()
    
    def trace_add(self, mode, callback):
        # Simplified trace - just store callback
        self._callbacks.append(callback)
    
    def trace_remove(self, mode, cbname):
        # Not implemented for simplicity
        pass


class CMCheckerQt(QMainWindow):  # TODO: Implement CMCheckerInterface methods
    # Qt signals for thread communication
    game_info_updated = Signal()
    processing_started = Signal()
    processing_finished = Signal()
    install_type_changed = Signal(str)
    game_path_changed = Signal(str)
    
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
        self.game = GameInfo(self.install_type_sv, self.game_path_sv)
        self.current_tab: Optional[CMCTabWidget] = None
        self.overview_problems = []
        self.processing_data = False
        
        # Connect signals for property changes
        self.install_type_changed.connect(lambda v: self.install_type_sv.set(v))
        self.game_path_changed.connect(lambda v: self.game_path_sv.set(v))
        
        self.setup_window()
        self.check_for_updates()
    
    def get_image(self, relative_path: str) -> QPixmap:
        if relative_path not in self._images:
            self._images[relative_path] = QPixmap(str(get_asset_path(relative_path)))
        return self._images[relative_path]
    
    def closeEvent(self, event: QCloseEvent) -> None:
        if self.processing_data:
            event.ignore()
            return
        sys.stderr = sys.__stderr__
        event.accept()
    
    def setup_window(self) -> None:
        # Window properties
        self.setWindowTitle(f"{APP_TITLE} v{APP_VERSION}")
        self.setFixedSize(WINDOW_WIDTH, WINDOW_HEIGHT)
        self.setWindowIcon(QIcon(str(get_asset_path("images/icon-32.png"))))
        
        # Center window on screen
        screen = self.app.primaryScreen()
        screen_geometry = screen.geometry()
        x = (screen_geometry.width() - WINDOW_WIDTH) // 2
        y = (screen_geometry.height() - WINDOW_HEIGHT) // 2
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
        
        # Create tabs - use Qt versions where available
        self.tabs: dict[Tab, CMCTabWidget] = {}
        
        # Create all Qt tabs
        self.tabs[Tab.Overview] = tabs_qt.OverviewTab(self, self.tab_widget)
        self.tabs[Tab.F4SE] = tabs_qt.F4SETab(self, self.tab_widget)
        self.tabs[Tab.Scanner] = tabs_qt.ScannerTab(self, self.tab_widget)
        self.tabs[Tab.Tools] = tabs_qt.ToolsTab(self, self.tab_widget)
        self.tabs[Tab.Settings] = tabs_qt.SettingsTab(self, self.tab_widget)
        self.tabs[Tab.About] = tabs_qt.AboutTab(self, self.tab_widget)
        
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
            nexus_link = QLabel(f"v{nexus_version} (NexusMods)")
            nexus_link.setProperty("class", "link")
            nexus_link.setCursor(Qt.PointingHandCursor)
            nexus_link.setToolTip("Open Nexus Mods")
            nexus_link.mousePressEvent = lambda e: webbrowser.open(NEXUS_LINK)
            update_layout.addWidget(nexus_link)
        
        if github_version and nexus_version:
            separator = QLabel(" / ")
            update_layout.addWidget(separator)
        
        if github_version:
            github_link = QLabel(f"v{github_version} (GitHub)")
            github_link.setProperty("class", "link")
            github_link.setCursor(Qt.PointingHandCursor)
            github_link.setToolTip("Open GitHub")
            github_link.mousePressEvent = lambda e: webbrowser.open(GITHUB_LINK)
            update_layout.addWidget(github_link)
        
        update_layout.addStretch()
    
    def changeEvent(self, event: QEvent) -> None:
        if event.type() == QEvent.WindowStateChange:
            if self.isMinimized():
                self.on_minimize()
            elif event.oldState() & Qt.WindowMinimized:
                self.on_restore()
        super().changeEvent(event)
    
    def on_minimize(self) -> None:
        # Handle scanner tab windows when minimizing
        scanner_tab = self.tabs.get(Tab.Scanner)
        if scanner_tab and scanner_tab.is_loaded and hasattr(scanner_tab, 'side_pane'):
            if scanner_tab.side_pane:
                scanner_tab.side_pane.hide()
            if scanner_tab.details_pane:
                scanner_tab.details_pane.hide()
    
    def on_restore(self) -> None:
        # Handle scanner tab windows when restoring
        scanner_tab = self.tabs.get(Tab.Scanner)
        if scanner_tab and scanner_tab.is_loaded and hasattr(scanner_tab, 'side_pane'):
            if scanner_tab.side_pane:
                scanner_tab.side_pane.show()
            if scanner_tab.details_pane:
                scanner_tab.details_pane.show()
    
    def on_tab_changed(self, index: int) -> None:
        if self.current_tab is not None and hasattr(self.current_tab, 'switch_from'):
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
    def root(self):
        # For compatibility with tabs that reference self.root
        return self
    
    def update(self):
        # Qt processes events automatically, but we can force it
        QApplication.processEvents()
    
    # Thread-safe UI update methods
    @Slot(str)
    def update_status(self, message: str) -> None:
        """Thread-safe method to update status messages"""
        # This would update a status bar if we had one
        logger.debug("Status update: %s", message)
    
    @Slot()
    def on_processing_started(self) -> None:
        """Called when heavy processing starts"""
        self.processing_data = True
        self.processing_started.emit()
    
    @Slot()
    def on_processing_finished(self) -> None:
        """Called when heavy processing finishes"""
        self.processing_data = False
        self.processing_finished.emit()
    
    # Tkinter compatibility methods
    def after(self, ms: int, func, *args):
        """Compatibility method for Tkinter's after() - uses QTimer"""
        timer = QTimer()
        timer.setSingleShot(True)
        timer.timeout.connect(lambda: func(*args))
        timer.start(ms)
        return timer
    
    def after_idle(self, func, *args):
        """Compatibility method for Tkinter's after_idle()"""
        QTimer.singleShot(0, lambda: func(*args))
    
    def wm_title(self, title: str) -> None:
        """Compatibility method for Tkinter's wm_title()"""
        self.setWindowTitle(title)
    
    def wm_geometry(self, geometry: str) -> None:
        """Compatibility method for Tkinter's wm_geometry()"""
        # Parse geometry string like "800x600+100+50"
        if '+' in geometry:
            size, pos = geometry.split('+', 1)
            width, height = map(int, size.split('x'))
            x, y = map(int, pos.split('+'))
            self.setGeometry(x, y, width, height)
        else:
            width, height = map(int, geometry.split('x'))
            self.resize(width, height)
    
    def destroy(self) -> None:
        """Compatibility method for Tkinter's destroy()"""
        self.close()
    
    # Additional helper methods
    def run_in_thread(self, func, *args, **kwargs):
        """Helper to run a function in a separate thread"""
        from threading import Thread
        thread = Thread(target=func, args=args, kwargs=kwargs)
        thread.daemon = True
        thread.start()
        return thread