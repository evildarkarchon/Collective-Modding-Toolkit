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
import webbrowser
from pathlib import Path
from typing import Optional

from PySide6.QtCore import Qt, Signal, Slot, QEvent, QObject
from PySide6.QtWidgets import (
    QTreeWidget, QTreeWidgetItem, QLabel, QPushButton,
    QVBoxLayout, QHBoxLayout, QGridLayout, QTabWidget,
    QProgressBar, QGroupBox, QCheckBox, QDialog,
    QWidget, QHeaderView, QMenu, QApplication, QMessageBox
)
from PySide6.QtGui import QFont, QColor, QGuiApplication

from autofix_handlers import AUTOFIX_REGISTRY, do_autofix_qt
from enums import ProblemType, SolutionType, Tab, Tool
from globals import *
from qt_helpers import CMCheckerInterface, CMCTabWidget
from qt_widgets import QtStringVar, QtDoubleVar, QtBoolVar
from qt_threading import ThreadManager
from scanner_threading import ScanWorker, ScannerWorkerSignals
from helpers import ProblemInfo, SimpleProblemInfo
from qt_modal_dialogs import TreeDialog
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
        self.scan_worker: Optional[ScanWorker] = None
        self.thread_manager = ThreadManager()
        
        self.dv_progress = QtDoubleVar()
        self.sv_scanning_text = QtStringVar()
        self.label_scanning_text: Optional[QLabel] = None
        self.scan_folders: tuple[str, ...] = ("",)
        self.sv_results_info = QtStringVar()
        
        # Track main window events
        self._main_window_filter = None
    
    def _load(self) -> bool:
        if self.cmc.game.data_path is None:
            self.loading_error = "Data folder not found"
            return False
        return True
    
    def _switch_to(self) -> None:
        """Called when switching to this tab."""
        if self.side_pane is None:
            self.side_pane = SidePane(self)
        
        # Install event filter on main window to track moves/resizes
        if self._main_window_filter is None:
            self._main_window_filter = MainWindowEventFilter(self)
            self.window().installEventFilter(self._main_window_filter)
        
        # Show and position side pane
        self.side_pane.show()
        self.side_pane.update_position()
    
    def switch_from(self) -> None:
        """Called when switching away from this tab."""
        if self.side_pane:
            self.side_pane.hide()
        if self.details_pane:
            self.details_pane.hide()
        # Stop any running scans
        self.thread_manager.stop_all()
    
    def closeEvent(self, event):
        """Handle tab close event."""
        # Clean up windows
        if self.side_pane:
            self.side_pane.close()
            self.side_pane = None
        if self.details_pane:
            self.details_pane.close()
            self.details_pane = None
        # Remove event filter
        if self._main_window_filter:
            self.window().removeEventFilter(self._main_window_filter)
            self._main_window_filter = None
        super().closeEvent(event)
    
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
        
        # Batch Auto-Fix button
        self.button_batch_fix = QPushButton("Batch Auto-Fix")
        self.button_batch_fix.clicked.connect(self.show_batch_autofix)
        self.button_batch_fix.setEnabled(False)  # Disabled until results loaded
        controls_layout.addWidget(self.button_batch_fix)
        
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
        self.tree_results.setSelectionMode(QTreeWidget.SingleSelection)
        self.tree_results.setContextMenuPolicy(Qt.CustomContextMenu)
        self.tree_results.customContextMenuRequested.connect(self.show_context_menu)
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
            self.details_pane.hide()
        
        # Create scanning label if needed
        if self.label_scanning_text is None:
            self.label_scanning_text = QLabel()
            font = QFont()
            font.setPointSize(10)
            self.label_scanning_text.setFont(font)
            self.label_scanning_text.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
            self.sv_scanning_text.changed.connect(self.label_scanning_text.setText)
            self.main_layout.addWidget(self.label_scanning_text, 2, 0, 1, 2)
        
        # Refresh overview tab first
        self.sv_scanning_text.set("Refreshing Overview...")
        self.cmc.refresh_tab(Tab.Overview)
        
        # Get scan settings
        scan_settings = ScanSettings()
        scan_settings.using_stage = self.using_stage
        scan_settings.manager = self.cmc.game.manager
        
        # Apply checkbox settings
        for setting, var in self.side_pane.bool_vars.items():
            scan_settings[setting] = var.get()
        
        # Get overview problems if enabled
        overview_problems = None
        if scan_settings[ScanSetting.OverviewIssues] and self.cmc.overview_problems:
            overview_problems = self.cmc.overview_problems
        
        self.dv_progress.set(1)
        
        # Create worker
        self.scan_worker = ScanWorker(self.cmc, scan_settings, overview_problems)
        
        # Connect signals
        self.scan_worker.signals.progress.connect(self.on_progress)
        self.scan_worker.signals.progress_text.connect(self.sv_scanning_text.set)
        self.scan_worker.signals.progress_value.connect(self.dv_progress.set)
        self.scan_worker.signals.scan_stage.connect(self.sv_scanning_text.set)
        self.scan_worker.signals.folder_update.connect(self.on_folder_update)
        self.scan_worker.signals.current_folder.connect(self.on_current_folder)
        self.scan_worker.signals.problems_found.connect(self.on_problems_found)
        self.scan_worker.signals.result_ready.connect(self.on_scan_complete)
        self.scan_worker.signals.error_occurred.connect(self.on_scan_error)
        self.scan_worker.signals.finished.connect(self.on_scan_finished)
        
        # Start scan
        self.thread_manager.start_worker(self.scan_worker, "scanner_thread")
    
    @Slot(int)
    def on_progress(self, value: int):
        """Handle progress updates."""
        self.dv_progress.set(value)
    
    @Slot(tuple)
    def on_folder_update(self, folders: tuple):
        """Handle folder list updates."""
        self.scan_folders = folders
    
    @Slot(str)
    def on_current_folder(self, folder: str):
        """Handle current folder updates."""
        try:
            current_index = self.scan_folders.index(folder)
            total = max(1, len(self.scan_folders))
            self.sv_scanning_text.set(f"Scanning... {current_index}/{total}: {folder}")
        except ValueError:
            pass
    
    @Slot(list)
    def on_problems_found(self, problems: list):
        """Handle batch of problems found."""
        self.scan_results.extend(problems)
    
    @Slot(object)
    def on_scan_complete(self, results: list):
        """Handle scan completion."""
        self.scan_results = results
        self.populate_results()
    
    @Slot(str, str)
    def on_scan_error(self, error: str, traceback: str):
        """Handle scan errors."""
        self.sv_scanning_text.set(f"Scan error: {error}")
        # TODO: Show error dialog
    
    @Slot()
    def on_scan_finished(self):
        """Handle scan thread finishing."""
        self.dv_progress.set(100)
        if self.side_pane:
            self.side_pane.button_scan.setEnabled(True)
            self.side_pane.button_scan.setText("Scan Game")
    
    def populate_results(self) -> None:
        """Populate the tree with scan results."""
        if self.side_pane is None:
            raise ValueError
        
        # Remove scanning label
        if self.label_scanning_text is not None:
            self.main_layout.removeWidget(self.label_scanning_text)
            self.label_scanning_text.deleteLater()
            self.label_scanning_text = None
            self.sv_scanning_text.set("")
        
        # Update results info
        self.sv_results_info.set(f"{len(self.scan_results)} Results ~ Select an item for details")
        
        # Enable batch fix button if there are fixable problems
        fixable_count = sum(1 for p in self.scan_results if p.solution in AUTOFIX_REGISTRY)
        self.button_batch_fix.setEnabled(fixable_count > 0)
        if fixable_count > 0:
            self.button_batch_fix.setText(f"Batch Auto-Fix ({fixable_count})")
        else:
            self.button_batch_fix.setText("Batch Auto-Fix")
        
        # Clear previous selection handler to avoid duplicate connections
        try:
            self.tree_results.itemSelectionChanged.disconnect()
        except TypeError:
            pass  # No connections
        
        # Group results by problem type
        groups = {p.type for p in self.scan_results}
        
        # Define problem severity for sorting and coloring
        problem_severity = {
            ProblemType.FileNotFound: (1, QColor(COLOR_BAD)),
            ProblemType.WrongVersion: (2, QColor(COLOR_BAD)),
            ProblemType.InvalidModule: (3, QColor(COLOR_BAD)),
            ProblemType.ComplexSorter: (4, QColor(COLOR_BAD)),
            ProblemType.MisplacedDLL: (5, QColor(COLOR_WARNING)),
            ProblemType.InvalidArchive: (6, QColor(COLOR_WARNING)),
            ProblemType.InvalidArchiveName: (7, QColor(COLOR_WARNING)),
            ProblemType.F4SEOverride: (8, QColor(COLOR_WARNING)),
            ProblemType.LoosePrevis: (9, QColor(COLOR_WARNING)),
            ProblemType.AnimTextDataFolder: (10, QColor(COLOR_WARNING)),
            ProblemType.UnexpectedFormat: (11, QColor(COLOR_INFO)),
            ProblemType.JunkFile: (12, QColor(COLOR_NEUTRAL_2)),
        }
        
        # Sort groups by severity
        sorted_groups = sorted(groups, key=lambda g: problem_severity.get(g, (99, None))[0])
        
        for group in sorted_groups:
            group_item = QTreeWidgetItem(self.tree_results)
            group_item.setText(0, group)
            group_item.setExpanded(True)
            
            # Set group color based on severity
            severity_info = problem_severity.get(group)
            if severity_info and severity_info[1]:
                group_item.setForeground(0, severity_info[1])
            
            # Get problems for this group and sort them
            group_problems = [p for p in self.scan_results if p.type == group]
            
            # Sort by mod name first, then by path/name
            def sort_key(p):
                mod_name = p.mod if hasattr(p, 'mod') else ""
                if isinstance(p, ProblemInfo):
                    path_name = p.path.name if hasattr(p.path, 'name') else str(p.path)
                else:
                    path_name = p.path
                return (mod_name.lower(), path_name.lower())
            
            sorted_problems = sorted(group_problems, key=sort_key)
            
            # Add problems to group
            for problem_info in sorted_problems:
                # Create item for problem
                item = QTreeWidgetItem(group_item)
                
                if isinstance(problem_info, ProblemInfo):
                    item.setText(0, problem_info.path.name)
                    if self.using_stage:
                        item.setText(1, problem_info.mod)
                else:
                    # SimpleProblemInfo
                    item.setText(0, problem_info.path)
                    if self.using_stage:
                        item.setText(1, problem_info.mod)
                
                # Store data reference using string ID
                item_id = str(id(item))
                self.tree_results_data[item_id] = problem_info
        
        # Connect selection handler
        self.tree_results.itemSelectionChanged.connect(self.on_row_select)
    
    def on_row_select(self):
        """Handle tree selection changes."""
        selected_items = self.tree_results.selectedItems()
        if not selected_items:
            return
            
        item = selected_items[0]
        item_id = str(id(item))
        
        if item_id in self.tree_results_data:
            if self.details_pane is None:
                self.details_pane = ResultDetailsPane(self)
            self.details_pane.set_info(item_id, using_stage=self.using_stage)
            self.details_pane.show()
            self.details_pane.update_position()
    
    def show_context_menu(self, position):
        """Show context menu for tree item."""
        item = self.tree_results.itemAt(position)
        if not item:
            return
            
        # Check if it's a problem item (not a group header)
        item_id = str(id(item))
        if item_id not in self.tree_results_data:
            return
            
        problem_info = self.tree_results_data[item_id]
        
        # Create context menu
        menu = QMenu(self)
        
        # Browse to File
        action_browse = menu.addAction("Browse to File")
        action_browse.triggered.connect(lambda: self.browse_to_file(problem_info))
        
        # Copy Path
        action_copy_path = menu.addAction("Copy Path")
        action_copy_path.triggered.connect(lambda: self.copy_path_from_menu(problem_info))
        
        # Copy Details
        action_copy_details = menu.addAction("Copy Details")
        action_copy_details.triggered.connect(lambda: self.copy_details_from_menu(problem_info))
        
        menu.addSeparator()
        
        # Auto-Fix (if available)
        if problem_info.solution in AUTOFIX_REGISTRY:
            action_autofix = menu.addAction("Auto-Fix")
            action_autofix.triggered.connect(lambda: do_autofix_qt(self.details_pane, item_id))
        
        # Ignore Problem
        action_ignore = menu.addAction("Ignore Problem")
        action_ignore.triggered.connect(lambda: self.ignore_problem(problem_info))
        
        # Show menu at cursor position
        menu.exec_(self.tree_results.viewport().mapToGlobal(position))
    
    def browse_to_file(self, problem_info):
        """Browse to the problem file location."""
        target = problem_info.path
        if isinstance(target, Path):
            if exists(target):
                if is_dir(target):
                    os.startfile(target)
                else:
                    # Open parent directory and select file
                    os.startfile(target.parent)
            elif exists(target.parent):
                os.startfile(target.parent)
    
    def copy_path_from_menu(self, problem_info):
        """Copy file path to clipboard from context menu."""
        path_text = str(problem_info.path)
        clipboard = QApplication.clipboard()
        clipboard.setText(path_text)
    
    def copy_details_from_menu(self, problem_info):
        """Copy problem details to clipboard from context menu."""
        if self.using_stage:
            mod = f"Mod: {problem_info.mod}\n"
        else:
            mod = ""
        
        # Handle different problem info types
        if isinstance(problem_info, ProblemInfo):
            problem_type = problem_info.type
        else:
            problem_type = problem_info.problem
        
        details = (
            f"{mod}Type: {problem_type}\n"
            f"Path: {str(problem_info.relative_path)}\n"
            f"Summary: {problem_info.summary}\n"
            f"Solution: {problem_info.solution or 'Solution not found.'}\n"
        )
        
        if problem_info.extra_data:
            extra_text = "\n".join(problem_info.extra_data)
            details += f"Extra Data: {extra_text}\n"
        
        clipboard = QApplication.clipboard()
        clipboard.setText(details)
    
    def ignore_problem(self, problem_info):
        """Add problem to ignore list and refresh display."""
        # TODO: Implement ignore list functionality
        # For now, just remove from display
        selected_items = self.tree_results.selectedItems()
        if selected_items:
            item = selected_items[0]
            parent = item.parent()
            if parent:
                parent.removeChild(item)
                # If parent has no more children, remove it too
                if parent.childCount() == 0:
                    self.tree_results.invisibleRootItem().removeChild(parent)
            
            # Update results count
            if problem_info in self.scan_results:
                self.scan_results.remove(problem_info)
                self.sv_results_info.set(f"{len(self.scan_results)} Results ~ Select an item for details")
    
    def show_batch_autofix(self):
        """Show batch autofix dialog."""
        # Get all fixable problems
        fixable_problems = [(p, str(id(p))) for p in self.scan_results if p.solution in AUTOFIX_REGISTRY]
        
        if not fixable_problems:
            QMessageBox.information(self, "No Fixable Problems", "No problems found that can be auto-fixed.")
            return
        
        # Create dialog
        from autofix_handlers import BatchAutofixDialog
        dialog = BatchAutofixDialog(self, fixable_problems)
        if dialog.exec() == dialog.Accepted:
            selected_problems = dialog.get_selected_problems()
            if selected_problems:
                self.run_batch_autofix(selected_problems)
    
    def run_batch_autofix(self, problems_to_fix):
        """Run batch autofix on selected problems."""
        from autofix_handlers import BatchAutofixWorker
        from PySide6.QtWidgets import QProgressDialog
        from PySide6.QtCore import Qt
        
        # Create progress dialog
        progress = QProgressDialog("Running batch auto-fix...", "Cancel", 0, len(problems_to_fix), self)
        progress.setWindowTitle("Batch Auto-Fix Progress")
        progress.setWindowModality(Qt.WindowModal)
        progress.setMinimumDuration(0)
        
        # Create worker
        worker = BatchAutofixWorker(problems_to_fix, self.tree_results_data)
        
        # Connect signals
        def on_progress(current, total, message):
            progress.setValue(current)
            progress.setLabelText(message)
            if progress.wasCanceled():
                worker.cancel()
        
        def on_finished(results):
            progress.close()
            # Show results summary
            success_count = sum(1 for r in results if r.success)
            fail_count = len(results) - success_count
            
            msg = QMessageBox(self)
            msg.setWindowTitle("Batch Auto-Fix Complete")
            msg.setIcon(QMessageBox.Information)
            msg.setText(f"Batch auto-fix completed.\n\nSuccessful: {success_count}\nFailed: {fail_count}")
            
            if fail_count > 0:
                details = "\n\nFailed fixes:\n"
                for problem, result in zip(problems_to_fix, results):
                    if not result.success:
                        details += f"\n{problem[0].path}: {result.details}"
                msg.setDetailedText(details)
            
            msg.exec()
            
            # Refresh the tree to update any fixed items
            self.populate_results()
        
        worker.progress.connect(on_progress)
        worker.finished.connect(on_finished)
        
        # Start the worker
        self.thread_manager.start_worker(worker, "batch_autofix")


class MainWindowEventFilter(QObject):
    """Event filter to track main window movements and resizes."""
    
    def __init__(self, scanner_tab: ScannerTab):
        super().__init__()
        self.scanner_tab = scanner_tab
    
    def eventFilter(self, obj, event):
        """Filter events from the main window."""
        if event.type() in (QEvent.Move, QEvent.Resize, QEvent.WindowStateChange):
            # Update side pane position when main window moves, resizes, or changes screen
            if self.scanner_tab.side_pane and self.scanner_tab.side_pane.isVisible():
                self.scanner_tab.side_pane.update_position()
            if self.scanner_tab.details_pane and self.scanner_tab.details_pane.isVisible():
                self.scanner_tab.details_pane.update_position()
        return False


class SidePane(QDialog):
    """Side pane for scanner settings."""
    
    def __init__(self, scanner_tab: ScannerTab) -> None:
        super().__init__(scanner_tab.window())
        self.scanner_tab = scanner_tab
        self.setWindowTitle("Scanner Settings")
        # Set window flags to create a frameless tool window
        self.setWindowFlags(
            Qt.Tool | 
            Qt.FramelessWindowHint | 
            Qt.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WA_ShowWithoutActivating)\n        \n        # Keep windows grouped with main window\n        self.setModal(False)
        
        # Track whether window is being dragged
        self._is_dragging = False
        self._drag_start_pos = None
        
        # Create main widget with border
        main_widget = QWidget()
        main_widget.setObjectName("SidePaneWidget")
        main_widget.setStyleSheet(
            "#SidePaneWidget { border: 1px solid #424242; background-color: #2b2b2b; }"
        )
        
        # Create main layout
        outer_layout = QVBoxLayout(self)
        outer_layout.setContentsMargins(0, 0, 0, 0)
        outer_layout.addWidget(main_widget)
        
        # Create layout for content
        layout = QVBoxLayout(main_widget)
        layout.setContentsMargins(10, 10, 10, 10)
        
        # Add title bar
        title_bar = QWidget()
        title_bar.setFixedHeight(30)
        title_layout = QHBoxLayout(title_bar)
        title_layout.setContentsMargins(5, 0, 5, 0)
        
        title_label = QLabel("Scanner Settings")
        title_label.setStyleSheet("font-weight: bold;")
        title_layout.addWidget(title_label)
        title_layout.addStretch()
        
        layout.addWidget(title_bar)
        
        # Scan settings
        settings_group = QGroupBox("Scan Settings")
        settings_layout = QVBoxLayout(settings_group)
        
        self.bool_vars = {}
        for setting in ScanSetting:
            var = QtBoolVar(True)
            checkbox = QCheckBox(setting.value[0])  # Use display name from tuple
            checkbox.setChecked(True)
            checkbox.setToolTip(setting.value[1])  # Use tooltip from tuple
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
    
    def update_position(self):
        """Update side pane position relative to main window."""
        if not self.isVisible():
            return
            
        main_window = self.scanner_tab.window()
        if not main_window:
            return
            
        # Get main window geometry
        main_geom = main_window.geometry()
        
        # Calculate desired position (right of main window)
        x = main_geom.right() + 10
        y = main_geom.top()
        
        # Get the screen where the main window is located
        screen = main_window.screen()
        if screen:
            screen_geom = screen.availableGeometry()
        else:
            # Fallback to primary screen if window screen not available
            screen = QGuiApplication.primaryScreen()
            if screen:
                screen_geom = screen.availableGeometry()
            else:
                # No screen available, can't position properly
                return
            
            # Check if side pane would go off-screen
            if x + self.width() > screen_geom.right():
                # Position to left of main window instead
                x = main_geom.left() - self.width() - 10
            
            # Ensure y is within screen bounds
            if y < screen_geom.top():
                y = screen_geom.top()
            elif y + self.height() > screen_geom.bottom():
                y = screen_geom.bottom() - self.height()
        
        self.move(x, y)
    
    def showEvent(self, event):
        """Handle show event."""
        super().showEvent(event)
        self.update_position()
    
    def mousePressEvent(self, event):
        """Handle mouse press for window dragging."""
        if event.button() == Qt.LeftButton:
            self._is_dragging = True
            self._drag_start_pos = event.globalPos() - self.frameGeometry().topLeft()
            event.accept()
    
    def mouseMoveEvent(self, event):
        """Handle mouse move for window dragging."""
        if self._is_dragging and event.buttons() == Qt.LeftButton:
            self.move(event.globalPos() - self._drag_start_pos)
            event.accept()
    
    def mouseReleaseEvent(self, event):
        """Handle mouse release."""
        if event.button() == Qt.LeftButton:
            self._is_dragging = False
            event.accept()


class ResultDetailsPane(QDialog):
    """Details pane for showing problem information."""
    
    def __init__(self, scanner_tab: ScannerTab) -> None:
        super().__init__(scanner_tab.window())
        self.scanner_tab = scanner_tab
        self.setWindowTitle("Problem Details")
        # Set window flags to create a frameless tool window
        self.setWindowFlags(
            Qt.Tool | 
            Qt.FramelessWindowHint | 
            Qt.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WA_ShowWithoutActivating)
        self.setAttribute(Qt.WA_DeleteOnClose)
        
        # Keep windows grouped with main window
        self.setModal(False)
        
        # Track whether window is being dragged
        self._is_dragging = False
        self._drag_start_pos = None
        
        self.problem_info: Optional[ProblemInfo | SimpleProblemInfo] = None
        self.sv_mod_name = QtStringVar()
        self.sv_file_path = QtStringVar()
        self.sv_problem = QtStringVar()
        self.sv_solution = QtStringVar()
        self.sv_problem_type = QtStringVar()
        self.sv_extra_data = QtStringVar()
        
        # Create main widget with border
        main_widget = QWidget()
        main_widget.setObjectName("DetailsPaneWidget")
        main_widget.setStyleSheet(
            "#DetailsPaneWidget { border: 1px solid #424242; background-color: #2b2b2b; }"
        )
        
        # Create main layout
        outer_layout = QVBoxLayout(self)
        outer_layout.setContentsMargins(0, 0, 0, 0)
        outer_layout.addWidget(main_widget)
        
        # Create layout for content
        content_layout = QVBoxLayout(main_widget)
        content_layout.setContentsMargins(10, 10, 10, 10)
        
        # Add title bar
        title_bar = QWidget()
        title_bar.setFixedHeight(30)
        title_layout = QHBoxLayout(title_bar)
        title_layout.setContentsMargins(5, 0, 5, 0)
        
        title_label = QLabel("Problem Details")
        title_label.setStyleSheet("font-weight: bold;")
        title_layout.addWidget(title_label)
        title_layout.addStretch()
        
        # Close button
        close_button = QPushButton("×")
        close_button.setFixedSize(20, 20)
        close_button.setStyleSheet(
            "QPushButton { border: none; font-size: 16px; }"
            "QPushButton:hover { background-color: #ff4444; }"
        )
        close_button.clicked.connect(self.hide)
        title_layout.addWidget(close_button)
        
        content_layout.addWidget(title_bar)
        
        # Create grid layout for details
        layout = QGridLayout()
        layout.setColumnStretch(1, 1)
        content_layout.addLayout(layout)
        
        start_row = 0
        if scanner_tab.using_stage:
            start_row = 1
            layout.addWidget(QLabel("Mod:"), 0, 0, Qt.AlignRight | Qt.AlignTop)
            mod_label = QLabel()
            self.sv_mod_name.changed.connect(mod_label.setText)
            mod_label.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
            layout.addWidget(mod_label, 0, 1)
        
        # Problem type row
        layout.addWidget(QLabel("Type:"), start_row, 0, Qt.AlignRight | Qt.AlignTop)
        type_label = QLabel()
        type_label.setStyleSheet(f"color: {COLOR_INFO};")
        self.sv_problem_type.changed.connect(type_label.setText)
        layout.addWidget(type_label, start_row, 1)
        
        # File path row
        layout.addWidget(QLabel("Path:"), start_row + 1, 0, Qt.AlignRight | Qt.AlignTop)
        self.label_file_path = QLabel()
        self.label_file_path.setStyleSheet(f"color: {COLOR_NEUTRAL_2}; text-decoration: underline;")
        self.label_file_path.setCursor(Qt.PointingHandCursor)
        self.label_file_path.setWordWrap(True)
        self.sv_file_path.changed.connect(self.label_file_path.setText)
        layout.addWidget(self.label_file_path, start_row + 1, 1)
        
        # Summary row
        layout.addWidget(QLabel("Summary:"), start_row + 2, 0, Qt.AlignRight | Qt.AlignTop)
        summary_label = QLabel()
        summary_label.setWordWrap(True)
        summary_label.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
        self.sv_problem.changed.connect(summary_label.setText)
        layout.addWidget(summary_label, start_row + 2, 1)
        
        # Solution row
        layout.addWidget(QLabel("Solution:"), start_row + 3, 0, Qt.AlignRight | Qt.AlignTop)
        self.label_solution = QLabel()
        self.label_solution.setWordWrap(True)
        self.label_solution.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
        self.label_solution.setOpenExternalLinks(False)  # We'll handle clicks manually
        self.sv_solution.changed.connect(self.label_solution.setText)
        layout.addWidget(self.label_solution, start_row + 3, 1)
        
        # Extra data row (if applicable)
        self.extra_data_row = start_row + 4
        self.label_extra_title = QLabel("Extra Data:")
        self.label_extra_data = QLabel()
        self.label_extra_data.setWordWrap(True)
        self.label_extra_data.setStyleSheet(f"color: {COLOR_WARNING};")
        self.sv_extra_data.changed.connect(self.label_extra_data.setText)
        # Initially hidden
        self.label_extra_title.hide()
        self.label_extra_data.hide()
        layout.addWidget(self.label_extra_title, self.extra_data_row, 0, Qt.AlignRight | Qt.AlignTop)
        layout.addWidget(self.label_extra_data, self.extra_data_row, 1)
        
        # Buttons
        button_layout = QVBoxLayout()
        
        self.button_browse = QPushButton("Browse to File")
        self.button_browse.clicked.connect(self.browse_to_file)
        button_layout.addWidget(self.button_browse)
        
        self.button_copy_path = QPushButton("Copy Path")
        self.button_copy_path.clicked.connect(self.copy_path)
        button_layout.addWidget(self.button_copy_path)
        
        self.button_copy = QPushButton("Copy Details")
        self.button_copy.clicked.connect(self.copy_details)
        button_layout.addWidget(self.button_copy)
        
        self.button_files = None
        self.button_autofix = None
        
        button_layout.addStretch()
        layout.addLayout(button_layout, 0, 2, start_row + 5, 1)
        
        # Set minimum size
        self.setMinimumWidth(700)
        self.setMaximumWidth(900)
    
    def browse_to_file(self):
        """Browse to the problem file location."""
        if not self.problem_info:
            return
            
        target = self.problem_info.path
        if isinstance(target, Path):
            if exists(target):
                if is_dir(target):
                    os.startfile(target)
                else:
                    # Open parent directory and select file
                    os.startfile(target.parent)
            elif exists(target.parent):
                os.startfile(target.parent)
    
    def copy_path(self):
        """Copy file path to clipboard."""
        if self.problem_info:
            path_text = str(self.problem_info.path)
            copy_text_button(self.button_copy_path, path_text)
    
    def copy_details(self):
        """Copy problem details to clipboard."""
        if self.scanner_tab.using_stage:
            mod = f"Mod: {self.sv_mod_name.get()}\n"
        else:
            mod = ""
        
        extra = ""
        if self.label_extra_data.isVisible():
            extra = f"Extra Data: {self.sv_extra_data.get()}\n"
        
        details = (
            f"{mod}Type: {self.sv_problem_type.get()}\n"
            f"Path: {self.sv_file_path.get()}\n"
            f"Summary: {self.sv_problem.get()}\n"
            f"Solution: {self.sv_solution.get()}\n"
            f"{extra}"
        )
        copy_text_button(self.button_copy, details)
    
    def set_info(self, selection: str, *, using_stage: bool):
        """Set problem information."""
        self.problem_info = self.scanner_tab.tree_results_data.get(selection)
        if not self.problem_info:
            return
            
        # Set mod name if using staging
        if using_stage:
            self.sv_mod_name.set(self.problem_info.mod or "N/A")
        
        # Set problem type
        if isinstance(self.problem_info, ProblemInfo):
            self.sv_problem_type.set(self.problem_info.type)
        else:
            self.sv_problem_type.set(self.problem_info.problem)
        
        # Set file path
        self.sv_file_path.set(str(self.problem_info.relative_path))
        
        # Make path clickable if it exists
        target = self.problem_info.path
        if isinstance(target, Path) and (exists(target) or exists(target.parent)):
            if not is_dir(target):
                target = target.parent
            self.label_file_path.mousePressEvent = lambda e: os.startfile(target)
            self.label_file_path.setToolTip("Click to open location")
            self.label_file_path.setCursor(Qt.PointingHandCursor)
        else:
            self.label_file_path.mousePressEvent = None
            self.label_file_path.setCursor(Qt.ForbiddenCursor)
            self.label_file_path.setToolTip("Location not found")
        
        # Set problem summary
        self.sv_problem.set(self.problem_info.summary)
        
        # Handle solution
        solution_text = str(self.problem_info.solution) if self.problem_info.solution else "Solution not found."
        self.sv_solution.set(solution_text)
        
        # Handle extra data separately
        if self.problem_info.extra_data:
            # Show extra data
            self.label_extra_title.show()
            self.label_extra_data.show()
            
            # Join extra data with newlines
            extra_text = "\n".join(self.problem_info.extra_data)
            self.sv_extra_data.set(extra_text)
            
            # Check if first extra is URL
            url = self.problem_info.extra_data[0]
            if url.startswith("http"):
                self.label_solution.mousePressEvent = lambda e: webbrowser.open(url)
                self.label_solution.setCursor(Qt.PointingHandCursor)
                self.label_solution.setToolTip("Click to open URL")
                # Add URL indicator to solution text
                self.sv_solution.set(f"{solution_text}\n[Click to open: {url}]")
            else:
                self.label_solution.mousePressEvent = None
                self.label_solution.setCursor(Qt.ArrowCursor)
                self.label_solution.setToolTip("")
        else:
            # Hide extra data
            self.label_extra_title.hide()
            self.label_extra_data.hide()
            self.sv_extra_data.set("")
            self.label_solution.mousePressEvent = None
            self.label_solution.setCursor(Qt.ArrowCursor)
            self.label_solution.setToolTip("")
        
        # Update button states
        self.button_browse.setEnabled(isinstance(self.problem_info.path, Path) and 
                                     (exists(self.problem_info.path) or exists(self.problem_info.path.parent)))
        
        # Handle file list button
        if self.button_files:
            layout = self.button_files.parent().layout()
            layout.removeWidget(self.button_files)
            self.button_files.deleteLater()
            self.button_files = None
        
        if self.problem_info.file_list:
            self.button_files = QPushButton("Show File List")
            self.button_files.clicked.connect(self.show_file_list)
            button_layout = self.button_copy.parent().layout()
            button_layout.insertWidget(3, self.button_files)
        
        # Handle auto-fix button
        if self.button_autofix:
            layout = self.button_autofix.parent().layout()
            layout.removeWidget(self.button_autofix)
            self.button_autofix.deleteLater()
            self.button_autofix = None
        
        if self.problem_info.solution in AUTOFIX_REGISTRY:
            self.button_autofix = QPushButton("Auto-Fix")
            self.button_autofix.clicked.connect(lambda: do_autofix_qt(self, selection))
            button_layout = self.button_copy.parent().layout()
            position = 4 if self.button_files else 3
            button_layout.insertWidget(position, self.button_autofix)
    
    def show_file_list(self):
        """Show the file list in a TreeWindow."""
        if not self.problem_info or not self.problem_info.file_list:
            return
            
        # Create TreeDialog to display file list
        headers = ("Count", "File") if isinstance(self.problem_info.file_list[0][0], (int, float)) else ("Info", "File")
        tree_dialog = TreeDialog(
            parent=self,
            cmc=self.scanner_tab.cmc,
            width=600,
            height=400,
            title="File List",
            text="",
            headers=headers,
            items=self.problem_info.file_list
        )
        tree_dialog.exec()
    
    def mousePressEvent(self, event):
        """Handle mouse press for window dragging."""
        if event.button() == Qt.LeftButton:
            # Only start dragging if clicking on title bar area
            if event.y() <= 40:  # Title bar height
                self._is_dragging = True
                self._drag_start_pos = event.globalPos() - self.frameGeometry().topLeft()
                event.accept()
    
    def mouseMoveEvent(self, event):
        """Handle mouse move for window dragging."""
        if self._is_dragging and event.buttons() == Qt.LeftButton:
            self.move(event.globalPos() - self._drag_start_pos)
            event.accept()
    
    def mouseReleaseEvent(self, event):
        """Handle mouse release."""
        if event.button() == Qt.LeftButton:
            self._is_dragging = False
            event.accept()
    
    def update_position(self):
        """Update details pane position relative to main window and side pane."""
        if not self.isVisible():
            return
            
        main_window = self.scanner_tab.window()
        if not main_window:
            return
            
        # Get main window geometry
        main_geom = main_window.geometry()
        
        # Calculate position below main window
        x = main_geom.left()
        y = main_geom.bottom() + 10
        
        # Get the screen where the main window is located
        screen = main_window.screen()
        if screen:
            screen_geom = screen.availableGeometry()
        else:
            # Fallback to primary screen if window screen not available
            screen = QGuiApplication.primaryScreen()
            if screen:
                screen_geom = screen.availableGeometry()
            else:
                # No screen available, can't position properly
                return
            
            # Check if details pane would go off-screen
            if y + self.height() > screen_geom.bottom():
                # Position above main window instead
                y = main_geom.top() - self.height() - 10
            
            # Ensure x is within screen bounds
            if x + self.width() > screen_geom.right():
                x = screen_geom.right() - self.width()
            elif x < screen_geom.left():
                x = screen_geom.left()
        
        self.move(x, y)
    
    def showEvent(self, event):
        """Handle show event."""
        super().showEvent(event)
        self.update_position()