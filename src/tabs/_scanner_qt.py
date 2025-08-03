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


import os
import queue
import webbrowser
from pathlib import Path
from typing import Optional

from PySide6.QtCore import Qt, QThread, Signal, Slot, QTimer
from PySide6.QtWidgets import (
    QTreeWidget, QTreeWidgetItem, QLabel, QPushButton,
    QVBoxLayout, QHBoxLayout, QGridLayout, QTabWidget,
    QProgressBar, QGroupBox, QCheckBox, QDialog,
    QWidget, QHeaderView
)
from PySide6.QtGui import QFont, QColor

from autofixes import AUTO_FIXES, do_autofix
from enums import ProblemType, SolutionType, Tab, Tool
from globals import *
from qt_helpers import CMCheckerInterface, CMCTabWidget
from qt_widgets import QtStringVar, QtDoubleVar, QtBoolVar
from helpers import ProblemInfo, SimpleProblemInfo
from modal_window import TreeWindow
from scan_settings import (
    DATA_WHITELIST,
    JUNK_FILE_SUFFIXES,
    JUNK_FILES,
    PROPER_FORMATS,
    ModFiles,
    ScanSetting,
    ScanSettings,
)
from utils import (
    copy_text,
    copy_text_button,
    exists,
    is_dir,
    is_file,
    read_text_encoded,
)


class ScanThread(QThread):
    """Thread for running the scan operation."""
    progress_update = Signal(object)  # str, tuple, or list
    finished = Signal()
    
    def __init__(self, scanner_tab, scan_settings):
        super().__init__()
        self.scanner_tab = scanner_tab
        self.scan_settings = scan_settings
        self.problems = []
    
    def run(self):
        """Run the scan in the thread."""
        # Simplified implementation - would need full port of scan_data_files
        self.progress_update.emit("Starting scan...")
        
        # TODO: Implement full scanning logic
        # This is a placeholder showing the structure
        
        self.progress_update.emit(self.problems)
        self.finished.emit()


class ScannerTab(CMCTabWidget):
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
        super().__init__(cmc, tab_widget, "Scanner")
        self.loading_text = "Checking game data..."
        
        self.using_stage = (
            self.cmc.game.manager is not None
            and self.cmc.game.manager.name == "Vortex"
            and self.cmc.game.manager.staging_folder
        )
        
        self.tree_results_data: dict[str, ProblemInfo | SimpleProblemInfo] = {}
        self.side_pane: Optional["SidePane"] = None
        self.details_pane: Optional[QDialog] = None
        
        self.scan_results: list[ProblemInfo | SimpleProblemInfo] = []
        self.scan_thread: Optional[ScanThread] = None
        self.dv_progress = QtDoubleVar()
        self.sv_scanning_text = QtStringVar()
        self.label_scanning_text: Optional[QLabel] = None
        self.scan_folders: tuple[str, ...] = ("",)
        self.sv_results_info = QtStringVar()
        
        # Timer for progress updates
        self.progress_timer = QTimer()
        self.progress_timer.timeout.connect(self.check_scan_progress)
    
    def _load(self) -> bool:
        if self.cmc.game.data_path is None:
            self.loading_error = "Data folder not found"
            return False
        return True
    
    def _switch_to(self) -> None:
        """Called when switching to this tab."""
        if self.side_pane is None:
            self.side_pane = SidePane(self)
            self.side_pane.show()
        
        # Position side pane next to main window
        main_geom = self.window().geometry()
        self.side_pane.move(main_geom.right() + 10, main_geom.top())
    
    def switch_from(self) -> None:
        """Called when switching away from this tab."""
        if self.side_pane:
            self.side_pane.hide()
        if self.details_pane:
            self.details_pane.hide()
    
    def set_expanded(self, *, expanded: bool) -> None:
        """Expand or collapse all tree items."""
        root = self.tree_results.invisibleRootItem()
        for i in range(root.childCount()):
            item = root.child(i)
            item.setExpanded(expanded)
    
    def _build_gui(self) -> None:
        # Clear main layout
        while self.main_layout.count():
            item = self.main_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        
        # Configure grid
        self.main_layout.setColumnStretch(0, 1)
        self.main_layout.setRowStretch(1, 1)
        
        # Tree controls frame
        controls_layout = QHBoxLayout()
        controls_layout.setContentsMargins(5, 5, 5, 5)
        
        button_collapse = QPushButton("Collapse All")
        button_collapse.clicked.connect(lambda: self.set_expanded(expanded=False))
        controls_layout.addWidget(button_collapse)
        
        button_expand = QPushButton("Expand All")
        button_expand.clicked.connect(lambda: self.set_expanded(expanded=True))
        controls_layout.addWidget(button_expand)
        
        controls_layout.addStretch()
        
        # Results info label
        label_results_info = QLabel()
        small_font = QFont()
        small_font.setPointSize(8)
        label_results_info.setFont(small_font)
        label_results_info.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
        self.sv_results_info.changed.connect(label_results_info.setText)
        controls_layout.addWidget(label_results_info)
        
        self.main_layout.addLayout(controls_layout, 0, 0, 1, 2)
        
        # Create tree widget
        if self.using_stage:
            self.tree_results = QTreeWidget()
            self.tree_results.setColumnCount(2)
            self.tree_results.setHeaderLabels(["Problem", "Mod"])
        else:
            self.tree_results = QTreeWidget()
            self.tree_results.setColumnCount(1)
            self.tree_results.setHeaderLabel("Problem")
        
        # Configure tree
        self.tree_results.setFont(small_font)
        header = self.tree_results.header()
        header.setSectionResizeMode(0, QHeaderView.Stretch)
        if self.using_stage:
            header.setSectionResizeMode(1, QHeaderView.Stretch)
        
        self.main_layout.addWidget(self.tree_results, 1, 0, 2, 1)
        
        # Progress bar
        self.progress_bar = QProgressBar()
        self.progress_bar.setMaximum(100)
        self.dv_progress.changed.connect(lambda v: self.progress_bar.setValue(int(v)))
        self.main_layout.addWidget(self.progress_bar, 3, 0, 1, 2)
    
    def start_threaded_scan(self) -> None:
        """Start the scanning process in a separate thread."""
        if self.side_pane is None:
            raise ValueError
        
        # Prepare UI for scanning
        self.side_pane.button_scan.setEnabled(False)
        self.side_pane.button_scan.setText("Scanning...")
        self.tree_results.clear()
        self.tree_results_data.clear()
        self.scan_results.clear()
        self.sv_results_info.set("")
        
        if self.details_pane is not None:
            self.details_pane.close()
            self.details_pane = None
        
        # Create scanning label if needed
        if self.label_scanning_text is None:
            self.label_scanning_text = QLabel()
            font = QFont()
            font.setPointSize(10)
            self.label_scanning_text.setFont(font)
            self.label_scanning_text.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
            self.sv_scanning_text.changed.connect(self.label_scanning_text.setText)
            self.main_layout.addWidget(self.label_scanning_text, 2, 0, 1, 2)
        
        # Get scan settings
        scan_settings = ScanSettings()
        for name, var in self.side_pane.bool_vars.items():
            scan_settings[name] = var.get()
        
        # Add overview problems if requested
        if scan_settings[ScanSetting.OverviewIssues] and self.cmc.overview_problems:
            self.scan_results.extend(self.cmc.overview_problems)
        
        self.dv_progress.set(1)
        
        # Create and start scan thread
        self.scan_thread = ScanThread(self, scan_settings)
        self.scan_thread.progress_update.connect(self.on_progress_update)
        self.scan_thread.finished.connect(lambda: self.populate_results(scan_settings))
        self.scan_thread.start()
        
        # Start progress timer
        self.progress_timer.start(100)
    
    @Slot(object)
    def on_progress_update(self, update):
        """Handle progress updates from scan thread."""
        if isinstance(update, tuple):
            self.scan_folders = update
        elif isinstance(update, str):
            self.sv_scanning_text.set(update)
            # Update progress based on folder
            try:
                current_index = self.scan_folders.index(update)
                self.dv_progress.set((current_index / len(self.scan_folders)) * 100)
            except (ValueError, ZeroDivisionError):
                pass
        elif isinstance(update, list):
            self.scan_results.extend(update)
    
    def check_scan_progress(self):
        """Check scan progress periodically."""
        if self.scan_thread is None or not self.scan_thread.isRunning():
            self.progress_timer.stop()
            self.dv_progress.set(100)
    
    def populate_results(self, scan_settings: ScanSettings) -> None:
        """Populate the tree with scan results."""
        if self.side_pane is None:
            raise ValueError
        
        # Remove scanning label
        if self.label_scanning_text is not None:
            self.main_layout.removeWidget(self.label_scanning_text)
            self.label_scanning_text.deleteLater()
            self.label_scanning_text = None
        
        # Reset UI
        self.side_pane.button_scan.setEnabled(True)
        self.side_pane.button_scan.setText("Scan Game")
        
        # TODO: Implement result population
        # This would populate self.tree_results with scan results
        
        # Update results info
        problem_count = len(self.scan_results)
        self.sv_results_info.set(f"{problem_count} problems found")


class SidePane(QDialog):
    """Side pane for scanner settings."""
    
    def __init__(self, scanner_tab: ScannerTab) -> None:
        super().__init__(scanner_tab.window())
        self.scanner_tab = scanner_tab
        self.setWindowTitle("Scanner Settings")
        self.setWindowFlags(Qt.Tool | Qt.WindowStaysOnTopHint)
        
        # Create layout
        layout = QVBoxLayout(self)
        
        # Scan settings
        settings_group = QGroupBox("Scan Settings")
        settings_layout = QVBoxLayout(settings_group)
        
        self.bool_vars = {}
        for setting in ScanSetting:
            var = QtBoolVar(True)
            checkbox = QCheckBox(setting.value)
            checkbox.setChecked(True)
            var.changed.connect(checkbox.setChecked)
            checkbox.toggled.connect(var.set)
            checkbox.toggled.connect(self.on_checkbox_toggle)
            settings_layout.addWidget(checkbox)
            self.bool_vars[setting] = var
        
        layout.addWidget(settings_group)
        layout.addStretch()
        
        # Scan button
        self.button_scan = QPushButton("Scan Game")
        self.button_scan.clicked.connect(scanner_tab.start_threaded_scan)
        layout.addWidget(self.button_scan)
    
    def on_checkbox_toggle(self) -> None:
        """Handle checkbox state changes."""
        any_checked = any(var.get() for var in self.bool_vars.values())
        self.button_scan.setEnabled(any_checked)