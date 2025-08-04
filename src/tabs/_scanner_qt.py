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


import contextlib
import fnmatch
import os
import sys
import webbrowser
from pathlib import Path

from PySide6.QtCore import QEvent, QObject, QPoint, Qt, QTimer, Slot
from PySide6.QtGui import QCloseEvent, QColor, QFont, QGuiApplication, QMouseEvent, QShowEvent
from PySide6.QtWidgets import (
	QAbstractItemView,
	QApplication,
	QCheckBox,
	QDialog,
	QDialogButtonBox,
	QGridLayout,
	QGroupBox,
	QHBoxLayout,
	QHeaderView,
	QLabel,
	QListWidget,
	QListWidgetItem,
	QMenu,
	QMessageBox,
	QProgressBar,
	QPushButton,
	QTabWidget,
	QTreeWidget,
	QTreeWidgetItem,
	QVBoxLayout,
	QWidget,
)

import cmt_globals
from autofix_handlers import AUTOFIX_REGISTRY, do_autofix_qt
from autofix_types import AutoFixResult
from enums import ProblemType, Tab
from helpers import ProblemInfo, SimpleProblemInfo
from qt_helpers import CMCheckerInterface, CMCTabWidget
from qt_modal_dialogs import TreeDialog
from qt_threading import ThreadManager
from qt_widgets import QtBoolVar, QtDoubleVar, QtStringVar
from qt_workers import ScannerWorker
from scan_settings import (
	IGNORE_FOLDERS,
	ModFiles,
	ScanSetting,
	ScanSettings,
)
from utils import (
	exists,
	is_dir,
)

# Type alias for problem info
ProblemInfoType = ProblemInfo | SimpleProblemInfo


class QtScanSettings(ScanSettings):
	"""Qt-compatible version of ScanSettings that works with the Qt SidePane."""

	def __init__(self, side_pane: "SidePane") -> None:
		# Initialize the parent dict without calling ScanSettings.__init__
		super(ScanSettings, self).__init__()

		self.skip_data_scan = True
		self.mod_files: ModFiles | None = None

		non_data = {
			ScanSetting.OverviewIssues,
			ScanSetting.RaceSubgraphs,
		}

		settings = side_pane.scanner_tab.cmc.settings
		resave = False
		for setting in ScanSetting:
			self[setting] = side_pane.bool_vars[setting].get()
			if self[setting] and setting not in non_data:
				self.skip_data_scan = False

			name = str(f"scanner_{setting.name}")
			if settings.dict[name] != self[setting]:
				settings.dict[name] = self[setting]
				resave = True
		if resave:
			settings.save()

		self.manager = side_pane.scanner_tab.cmc.game.manager
		self.using_stage = side_pane.scanner_tab.using_stage
		if self.manager and self.manager.name == "Mod Organizer":
			self.skip_file_suffixes = (*self.manager.skip_file_suffixes, ".vortex_backup")
			self.skip_directories = IGNORE_FOLDERS.union(self.manager.skip_directories)
		else:
			self.skip_file_suffixes = (".vortex_backup",)
			self.skip_directories = IGNORE_FOLDERS


class ScannerTab(CMCTabWidget):
	def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
		super().__init__(cmc, tab_widget, "Scanner")
		self.loading_text = "Checking game data..."

		self.using_stage = (
			self.cmc.game.manager is not None
			and self.cmc.game.manager.name == "Vortex"
			and bool(self.cmc.game.manager.stage_path)
		)

		self.tree_results_data: dict[str, ProblemInfo | SimpleProblemInfo] = {}
		self.tree_results_items: dict[str, QTreeWidgetItem] = {}  # Maps item_id to tree widget items
		self.side_pane: SidePane | None = None
		self.details_pane: ResultDetailsPane | None = None

		self.scan_results: list[ProblemInfo | SimpleProblemInfo] = []
		self.scan_worker: ScannerWorker | None = None
		self.thread_manager = ThreadManager()

		# Load ignore list from settings
		self.ignored_problems: set[str] = set(self.cmc.settings.dict.get("scanner_ignored_problems", []))
		self.ignore_patterns: list[str] = self.cmc.settings.dict.get("scanner_ignore_patterns", [])

		self.dv_progress = QtDoubleVar()
		self.sv_scanning_text = QtStringVar()
		self.label_scanning_text: QLabel | None = None
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

	def closeEvent(self, event: QCloseEvent) -> None:  # noqa: N802
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

		# Manage Ignore List button
		self.button_clear_ignore = QPushButton("Manage Ignore List")
		self.button_clear_ignore.clicked.connect(self.show_ignore_list_dialog)
		self.button_clear_ignore.setEnabled(False)  # Disabled until items are ignored
		controls_layout.addWidget(self.button_clear_ignore)

		controls_layout.addStretch()

		# Results info label
		label_results_info = QLabel()
		small_font = QFont()
		small_font.setPointSize(8)
		label_results_info.setFont(small_font)
		label_results_info.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
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
		self.tree_results.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
		self.tree_results.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
		header = self.tree_results.header()
		header.setSectionResizeMode(0, QHeaderView.ResizeMode.Stretch)
		if self.using_stage:
			header.setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)

		self.main_layout.addWidget(self.tree_results, 1, 0, 2, 1)

		# Progress bar
		self.progress_bar = QProgressBar()
		self.progress_bar.setMaximum(100)
		self.dv_progress.changed.connect(self._on_progress_changed)
		self.main_layout.addWidget(self.progress_bar, 3, 0, 1, 2)

	def _on_progress_changed(self, value: float) -> None:
		"""Handle progress value changes."""
		self.progress_bar.setValue(int(value))

	def start_threaded_scan(self) -> None:
		"""Start the scanning process in a separate thread."""
		if self.side_pane is None:
			raise ValueError

		# Prepare UI for scanning
		self.side_pane.button_scan.setEnabled(False)
		self.side_pane.button_scan.setText("Scanning...")
		self.tree_results.clear()
		self.tree_results_data.clear()
		self.tree_results_items.clear()
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
			self.label_scanning_text.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
			self.sv_scanning_text.changed.connect(self.label_scanning_text.setText)
			self.main_layout.addWidget(self.label_scanning_text, 2, 0, 1, 2)

		# Refresh overview tab first
		self.sv_scanning_text.set("Refreshing Overview...")
		self.cmc.refresh_tab(Tab.Overview)

		# Get scan settings - create a Qt-compatible version
		scan_settings = QtScanSettings(self.side_pane)

		# Get overview problems if enabled
		overview_problems = None
		if scan_settings[ScanSetting.OverviewIssues] and self.cmc.overview_problems:
			overview_problems = self.cmc.overview_problems

		self.dv_progress.set(1)

		# Create worker - TODO: Update to use proper Qt worker initialization
		# For now, using empty paths list as placeholder
		paths_to_scan: list[Path] = []
		self.scan_worker = ScannerWorker(scan_settings, paths_to_scan)

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
	def on_progress(self, value: int) -> None:
		"""Handle progress updates."""
		self.dv_progress.set(value)

	@Slot(tuple)
	def on_folder_update(self, folders: tuple[str, ...]) -> None:
		"""Handle folder list updates."""
		self.scan_folders = folders

	@Slot(str)
	def on_current_folder(self, folder: str) -> None:
		"""Handle current folder updates."""
		try:
			current_index = self.scan_folders.index(folder)
			total = max(1, len(self.scan_folders))
			self.sv_scanning_text.set(f"Scanning... {current_index}/{total}: {folder}")
		except ValueError:
			pass

	@Slot(list)
	def on_problems_found(self, problems: list[ProblemInfoType]) -> None:
		"""Handle batch of problems found."""
		self.scan_results.extend(problems)

	@Slot(object)
	def on_scan_complete(self, results: list[ProblemInfoType]) -> None:
		"""Handle scan completion."""
		self.scan_results = results
		self.populate_results()

	@Slot(str, str)
	def on_scan_error(self, error: str, traceback_str: str) -> None:
		"""Handle scan errors."""
		self.sv_scanning_text.set(f"Scan error: {error}")

		# Show error dialog with details
		msg = QMessageBox(self)
		msg.setWindowTitle("Scan Error")
		msg.setIcon(QMessageBox.Icon.Critical)
		msg.setText("An error occurred during the scan.")
		msg.setInformativeText(error)

		# Add detailed traceback if available
		if traceback_str:
			msg.setDetailedText(traceback_str)

		msg.setStandardButtons(QMessageBox.StandardButton.Ok)
		msg.exec()

	@Slot()
	def on_scan_finished(self) -> None:
		"""Handle scan thread finishing."""
		self.dv_progress.set(100)
		if self.side_pane:
			self.side_pane.button_scan.setEnabled(True)
			self.side_pane.button_scan.setText("Scan Game")

	def populate_results(self) -> None:
		"""Populate the tree with scan results using batched updates for better performance."""
		if self.side_pane is None:
			raise ValueError

		# Remove scanning label
		if self.label_scanning_text is not None:
			self.main_layout.removeWidget(self.label_scanning_text)
			self.label_scanning_text.deleteLater()
			self.label_scanning_text = None
			self.sv_scanning_text.set("")

		# Update results info
		non_ignored_count = sum(1 for p in self.scan_results if not self._is_problem_ignored(p))
		ignored_count = len(self.ignored_problems)
		if ignored_count > 0:
			self.sv_results_info.set(f"{non_ignored_count} Results ({ignored_count} ignored) ~ Select an item for details")
		else:
			self.sv_results_info.set(f"{non_ignored_count} Results ~ Select an item for details")

		# Enable batch fix button if there are fixable problems (excluding ignored)
		fixable_count = sum(1 for p in self.scan_results if p.solution in AUTOFIX_REGISTRY and not self._is_problem_ignored(p))
		self.button_batch_fix.setEnabled(fixable_count > 0)
		if fixable_count > 0:
			self.button_batch_fix.setText(f"Batch Auto-Fix ({fixable_count})")
		else:
			self.button_batch_fix.setText("Batch Auto-Fix")

		# Enable Clear Ignore List button if we have ignored items
		self.button_clear_ignore.setEnabled(len(self.ignored_problems) > 0 or len(self.ignore_patterns) > 0)

		# Clear previous selection handler to avoid duplicate connections
		with contextlib.suppress(TypeError):
			self.tree_results.itemSelectionChanged.disconnect()

		# Clear existing tree data
		self.tree_results.clear()
		self.tree_results_data.clear()
		self.tree_results_items.clear()

		# Define problem severity for sorting and coloring
		problem_severity = {
			ProblemType.FileNotFound: (1, QColor(cmt_globals.COLOR_BAD)),
			ProblemType.WrongVersion: (2, QColor(cmt_globals.COLOR_BAD)),
			ProblemType.InvalidModule: (3, QColor(cmt_globals.COLOR_BAD)),
			ProblemType.ComplexSorter: (4, QColor(cmt_globals.COLOR_BAD)),
			ProblemType.MisplacedDLL: (5, QColor(cmt_globals.COLOR_WARNING)),
			ProblemType.InvalidArchive: (6, QColor(cmt_globals.COLOR_WARNING)),
			ProblemType.InvalidArchiveName: (7, QColor(cmt_globals.COLOR_WARNING)),
			ProblemType.F4SEOverride: (8, QColor(cmt_globals.COLOR_WARNING)),
			ProblemType.LoosePrevis: (9, QColor(cmt_globals.COLOR_WARNING)),
			ProblemType.AnimTextDataFolder: (10, QColor(cmt_globals.COLOR_WARNING)),
			ProblemType.UnexpectedFormat: (11, QColor(cmt_globals.COLOR_INFO)),
			ProblemType.JunkFile: (12, QColor(cmt_globals.COLOR_NEUTRAL_2)),
		}

		# Start batched population
		self._populate_results_batched(problem_severity)

	def _populate_results_batched(self, problem_severity: dict[ProblemType, tuple[int, QColor]]) -> None:
		"""Populate tree with batched updates to avoid UI blocking."""
		# Disable updates during population for better performance
		self.tree_results.setUpdatesEnabled(False)

		# Group and sort results
		groups = {p.type for p in self.scan_results}
		sorted_groups = sorted(
			groups,
			key=lambda g: problem_severity.get(g, (99, QColor()))[0] if isinstance(g, ProblemType) else 99,
		)

		# Create group items first
		self._group_items: dict[str, QTreeWidgetItem] = {}
		for group in sorted_groups:
			group_item = QTreeWidgetItem(self.tree_results)
			group_item.setText(0, group)
			group_item.setExpanded(True)

			# Set group color based on severity
			if isinstance(group, ProblemType):
				severity_info = problem_severity.get(group)
				if severity_info:
					group_item.setForeground(0, severity_info[1])

			self._group_items[group] = group_item

		# Prepare sorted problems for batch processing
		self._sorted_problems: list[tuple[str, ProblemInfoType]] = []
		for group in sorted_groups:
			group_problems = [p for p in self.scan_results if p.type == group]

			# Sort by mod name first, then by path/name
			def sort_key(p: ProblemInfoType) -> tuple[str, str]:
				mod_name = p.mod if hasattr(p, "mod") else ""
				path_name = (p.path.name if hasattr(p.path, "name") else str(p.path)) if isinstance(p, ProblemInfo) else p.path
				return (mod_name.lower(), path_name.lower())

			sorted_problems = sorted(group_problems, key=sort_key)
			for problem in sorted_problems:
				self._sorted_problems.append((group, problem))

		# Start batch processing
		self._batch_index = 0
		self._batch_size = 50  # Process 50 items per batch
		self._populate_next_batch()

	def _populate_next_batch(self) -> None:
		"""Process the next batch of items."""
		end_index = min(self._batch_index + self._batch_size, len(self._sorted_problems))

		# Process current batch
		for i in range(self._batch_index, end_index):
			group, problem_info = self._sorted_problems[i]

			# Skip ignored problems
			if self._is_problem_ignored(problem_info):
				continue

			group_item = self._group_items[group]

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
			self.tree_results_items[item_id] = item  # Store tree item reference

		self._batch_index = end_index

		# Schedule next batch or finish
		if self._batch_index < len(self._sorted_problems):
			# Process next batch after a short delay to keep UI responsive
			QTimer.singleShot(5, self._populate_next_batch)
		else:
			# Finished - clean up and enable tree
			self._finish_population()

	def _finish_population(self) -> None:
		"""Finish tree population and clean up."""
		# Clean up temporary attributes
		if hasattr(self, "_group_items"):
			delattr(self, "_group_items")
		if hasattr(self, "_sorted_problems"):
			delattr(self, "_sorted_problems")
		if hasattr(self, "_batch_index"):
			delattr(self, "_batch_index")
		if hasattr(self, "_batch_size"):
			delattr(self, "_batch_size")

		# Re-enable updates and refresh tree
		self.tree_results.setUpdatesEnabled(True)
		self.tree_results.expandAll()

		# Connect selection handler
		self.tree_results.itemSelectionChanged.connect(self.on_row_select)

	def on_row_select(self) -> None:
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

	def show_context_menu(self, position: QPoint) -> None:
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
			action_autofix.triggered.connect(lambda: self._autofix_from_context_menu(item_id))

		# Ignore Problem
		action_ignore = menu.addAction("Ignore This Problem")
		action_ignore.triggered.connect(lambda: self.ignore_problem(problem_info))

		# Ignore by pattern
		problem_type = problem_info.type if isinstance(problem_info, ProblemInfo) else problem_info.problem
		action_ignore_type = menu.addAction(f"Ignore All '{problem_type}' Problems")
		action_ignore_type.triggered.connect(lambda: self.ignore_by_type(problem_type))

		# Ignore by path pattern (for files in specific folders)
		path_parts = str(problem_info.relative_path).split("/")
		if len(path_parts) > 1:
			folder_pattern = "/".join(path_parts[:-1]) + "/*"
			action_ignore_folder = menu.addAction(f"Ignore All in '{path_parts[-2]}' Folder")
			action_ignore_folder.triggered.connect(lambda: self.ignore_by_pattern(f"*:{folder_pattern}"))

		# Show menu at cursor position
		menu.exec_(self.tree_results.viewport().mapToGlobal(position))

	def _autofix_from_context_menu(self, item_id: str) -> None:
		"""Handle autofix from context menu by ensuring details pane exists."""
		# Ensure details pane exists and is showing the correct item
		if self.details_pane is None:
			self.details_pane = ResultDetailsPane(self)

		self.details_pane.set_info(item_id, using_stage=self.using_stage)
		self.details_pane.show()
		self.details_pane.update_position()

		# Now call autofix with the properly initialized details pane
		do_autofix_qt(self.details_pane, item_id)

	def browse_to_file(self, problem_info: ProblemInfoType) -> None:  # noqa: PLR6301
		"""Browse to the problem file location."""
		target = problem_info.path
		if isinstance(target, Path) and sys.platform == "win32":
			if exists(target):
				if is_dir(target):
					os.startfile(target)  # type: ignore[attr-defined]
				else:
					# Open parent directory and select file
					os.startfile(target.parent)  # type: ignore[attr-defined]
			elif exists(target.parent):
				os.startfile(target.parent)  # type: ignore[attr-defined]

	def copy_path_from_menu(self, problem_info: ProblemInfoType) -> None:  # noqa: PLR6301
		"""Copy file path to clipboard from context menu."""
		path_text = str(problem_info.path)
		clipboard = QApplication.clipboard()
		clipboard.setText(path_text)

	def copy_details_from_menu(self, problem_info: ProblemInfoType) -> None:
		"""Copy problem details to clipboard from context menu."""
		mod = f"Mod: {problem_info.mod}\n" if self.using_stage else ""

		# Handle different problem info types
		problem_type = problem_info.type if isinstance(problem_info, ProblemInfo) else problem_info.problem

		details = (
			f"{mod}Type: {problem_type}\n"
			f"Path: {problem_info.relative_path!s}\n"
			f"Summary: {problem_info.summary}\n"
			f"Solution: {problem_info.solution or 'Solution not found.'}\n"
		)

		if problem_info.extra_data:
			extra_text = "\n".join(problem_info.extra_data)
			details += f"Extra Data: {extra_text}\n"

		clipboard = QApplication.clipboard()
		clipboard.setText(details)

	def ignore_problem(self, problem_info: ProblemInfoType) -> None:
		"""Add problem to ignore list and refresh display."""
		# Create a unique signature for the problem
		signature = self._get_problem_signature(problem_info)

		# Add to ignore list
		self.ignored_problems.add(signature)

		# Save to settings
		self._save_ignore_lists()

		# Remove from current display
		selected_items = self.tree_results.selectedItems()
		if selected_items:
			item = selected_items[0]
			item_id = item.data(0, Qt.ItemDataRole.UserRole)

			parent = item.parent()
			if parent:
				parent.removeChild(item)
				# If parent has no more children, remove it too
				if parent.childCount() == 0:
					self.tree_results.invisibleRootItem().removeChild(parent)

			# Remove from data dict
			if item_id and item_id in self.tree_results_data:
				del self.tree_results_data[item_id]
				if item_id in self.tree_results_items:
					del self.tree_results_items[item_id]

		# Update results count
		self._update_results_info()

		# Enable Clear Ignore List button if we have ignored items
		self.button_clear_ignore.setEnabled(len(self.ignored_problems) > 0 or len(self.ignore_patterns) > 0)

		# Close details pane if it was showing this problem
		if self.details_pane and self.details_pane.problem_info == problem_info:
			self.details_pane.close()

	@staticmethod
	def _get_problem_signature(problem_info: ProblemInfoType) -> str:
		"""Generate a unique signature for a problem to use in ignore list."""
		# Create signature from problem type and path
		problem_type = problem_info.type if isinstance(problem_info, ProblemInfo) else problem_info.problem
		return f"{problem_type}:{problem_info.relative_path}"

	def _is_problem_ignored(self, problem_info: ProblemInfoType) -> bool:
		"""Check if a problem is in the ignore list or matches an ignore pattern."""
		signature = self._get_problem_signature(problem_info)

		# Check exact matches
		if signature in self.ignored_problems:
			return True

		# Check patterns (wildcards for problem type or path)
		return any(fnmatch.fnmatch(signature, pattern) for pattern in self.ignore_patterns)

	def clear_ignore_list(self) -> None:
		"""Clear all ignored problems and refresh display."""
		self.ignored_problems.clear()
		self.ignore_patterns.clear()

		# Save to settings
		self._save_ignore_lists()

		# Disable Clear Ignore List button
		self.button_clear_ignore.setEnabled(False)
		# Re-populate results to show previously ignored problems
		if self.scan_results:
			self.populate_results()

	def _save_ignore_lists(self) -> None:
		"""Save ignore lists to settings."""
		self.cmc.settings.dict["scanner_ignored_problems"] = list(self.ignored_problems)
		self.cmc.settings.dict["scanner_ignore_patterns"] = self.ignore_patterns
		self.cmc.settings.save()

	def ignore_by_type(self, problem_type: str) -> None:
		"""Add a pattern to ignore all problems of a specific type."""
		pattern = f"{problem_type}:*"
		if pattern not in self.ignore_patterns:
			self.ignore_patterns.append(pattern)
			self._save_ignore_lists()

			# Update UI
			self.button_clear_ignore.setEnabled(True)

			# Refresh display to apply new pattern
			if self.scan_results:
				self.populate_results()

			# Show confirmation
			QMessageBox.information(
				self,
				"Pattern Added",
				f"All '{problem_type}' problems will now be ignored.",
			)

	def ignore_by_pattern(self, pattern: str) -> None:
		"""Add a custom pattern to the ignore list."""
		if pattern not in self.ignore_patterns:
			self.ignore_patterns.append(pattern)
			self._save_ignore_lists()

			# Update UI
			self.button_clear_ignore.setEnabled(True)

			# Refresh display to apply new pattern
			if self.scan_results:
				self.populate_results()

			# Show confirmation
			QMessageBox.information(
				self,
				"Pattern Added",
				f"Pattern '{pattern}' added to ignore list.",
			)

	def _update_results_info(self) -> None:
		"""Update the results info label with count including ignored items."""
		visible_count = self.tree_results.topLevelItemCount()

		# Count total ignored (exact matches + patterns)
		total_ignored = len(self.ignored_problems) + len(self.ignore_patterns)

		if total_ignored > 0:
			self.sv_results_info.set(f"{visible_count} Results ({total_ignored} ignore rules) ~ Select an item for details")
		else:
			self.sv_results_info.set(f"{visible_count} Results ~ Select an item for details")

	def show_ignore_list_dialog(self) -> None:
		"""Show dialog to manage the ignore list."""
		dialog = QDialog(self)
		dialog.setWindowTitle("Manage Ignore List")
		dialog.setModal(True)
		dialog.resize(500, 400)

		layout = QVBoxLayout(dialog)

		# List widget to show all ignore rules
		list_widget = QListWidget()

		# Add exact matches
		for signature in sorted(self.ignored_problems):
			item = QListWidgetItem(f"📍 {signature}")
			item.setData(Qt.ItemDataRole.UserRole, ("exact", signature))
			list_widget.addItem(item)

		# Add patterns
		for pattern in sorted(self.ignore_patterns):
			item = QListWidgetItem(f"🔍 {pattern}")
			item.setData(Qt.ItemDataRole.UserRole, ("pattern", pattern))
			list_widget.addItem(item)

		if list_widget.count() == 0:
			item = QListWidgetItem("(No ignore rules)")
			item.setFlags(Qt.ItemFlag.NoItemFlags)
			list_widget.addItem(item)

		layout.addWidget(list_widget)

		# Buttons
		button_box = QDialogButtonBox()

		# Remove Selected button
		remove_button = button_box.addButton("Remove Selected", QDialogButtonBox.ButtonRole.ActionRole)
		remove_button.clicked.connect(lambda: self._remove_ignore_rule(list_widget))

		# Clear All button
		clear_button = button_box.addButton("Clear All", QDialogButtonBox.ButtonRole.DestructiveRole)
		clear_button.clicked.connect(lambda: (self.clear_ignore_list(), dialog.accept()))

		# Close button
		button_box.addButton(QDialogButtonBox.StandardButton.Close)
		button_box.rejected.connect(dialog.reject)

		layout.addWidget(button_box)

		dialog.exec()

	def _remove_ignore_rule(self, list_widget: QListWidget) -> None:
		"""Remove selected ignore rule from the list."""
		current_item = list_widget.currentItem()
		if not current_item:
			return

		data = current_item.data(Qt.ItemDataRole.UserRole)
		if not data:
			return

		rule_type, rule_value = data

		if rule_type == "exact":
			self.ignored_problems.discard(rule_value)
		elif rule_type == "pattern" and rule_value in self.ignore_patterns:
			self.ignore_patterns.remove(rule_value)

		# Save changes
		self._save_ignore_lists()

		# Remove from list widget
		list_widget.takeItem(list_widget.row(current_item))

		# Update button state
		if len(self.ignored_problems) == 0 and len(self.ignore_patterns) == 0:
			self.button_clear_ignore.setEnabled(False)

		# Refresh display
		if self.scan_results:
			self.populate_results()

	def show_batch_autofix(self) -> None:
		"""Show batch autofix dialog."""
		# Get all fixable problems
		fixable_problems = [(p, str(id(p))) for p in self.scan_results if p.solution in AUTOFIX_REGISTRY]

		if not fixable_problems:
			QMessageBox.information(self, "No Fixable Problems", "No problems found that can be auto-fixed.")
			return

		# Create dialog
		from autofix_handlers import BatchAutofixDialog  # noqa: PLC0415

		dialog = BatchAutofixDialog(self, fixable_problems)
		if dialog.exec() == 1:  # QDialog.Accepted
			selected_problems = dialog.get_selected_problems()
			if selected_problems:
				self.run_batch_autofix(selected_problems)

	def run_batch_autofix(self, problems_to_fix: list[tuple[ProblemInfoType, str]]) -> None:
		"""Run batch autofix on selected problems."""
		from PySide6.QtCore import Qt  # noqa: PLC0415
		from PySide6.QtWidgets import QProgressDialog  # noqa: PLC0415

		from autofix_handlers import BatchAutofixWorker  # noqa: PLC0415

		# Create progress dialog
		progress = QProgressDialog("Running batch auto-fix...", "Cancel", 0, len(problems_to_fix), self)
		progress.setWindowTitle("Batch Auto-Fix Progress")
		progress.setWindowModality(Qt.WindowModality.WindowModal)
		progress.setMinimumDuration(0)

		# Create worker
		worker = BatchAutofixWorker(problems_to_fix, self.tree_results_data)

		# Connect signals
		def on_progress(current: int, _total: int, message: str) -> None:
			progress.setValue(current)
			progress.setLabelText(message)
			if progress.wasCanceled():
				worker.cancel()

		def on_finished(results: list[AutoFixResult]) -> None:
			progress.close()
			# Show results summary
			success_count = sum(1 for r in results if r.success)
			fail_count = len(results) - success_count

			msg = QMessageBox(self)
			msg.setWindowTitle("Batch Auto-Fix Complete")
			msg.setIcon(QMessageBox.Icon.Information)
			msg.setText(f"Batch auto-fix completed.\n\nSuccessful: {success_count}\nFailed: {fail_count}")

			if fail_count > 0:
				details = "\n\nFailed fixes:\n"
				for problem, result in zip(problems_to_fix, results, strict=False):
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

	def __init__(self, scanner_tab: ScannerTab) -> None:
		super().__init__()
		self.scanner_tab = scanner_tab

	def eventFilter(self, _obj: QObject, event: QEvent) -> bool:  # noqa: N802
		"""Filter events from the main window."""
		if event.type() in {QEvent.Type.Move, QEvent.Type.Resize, QEvent.Type.WindowStateChange}:
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
		self.setWindowFlags(Qt.WindowType.Tool | Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)  # type: ignore[arg-type]
		self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)

		# Keep windows grouped with main window
		self.setModal(False)

		# Track whether window is being dragged
		self._is_dragging = False
		self._drag_start_pos = None

		# Create main widget with border
		main_widget = QWidget()
		main_widget.setObjectName("SidePaneWidget")
		main_widget.setStyleSheet("#SidePaneWidget { border: 1px solid #424242; background-color: #2b2b2b; }")

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

		self.bool_vars: dict[ScanSetting, QtBoolVar] = {}
		for setting in ScanSetting:
			var = QtBoolVar(True)  # noqa: FBT003
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
		any_checked: bool = any(var.get() for var in self.bool_vars.values())
		self.button_scan.setEnabled(any_checked)

	def update_position(self) -> None:
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

	def showEvent(self, event: QShowEvent) -> None:  # noqa: N802
		"""Handle show event."""
		super().showEvent(event)
		self.update_position()

	def mousePressEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		"""Handle mouse press for window dragging."""
		if event.button() == Qt.MouseButton.LeftButton and event.y() <= 40:  # Title bar height
			self._is_dragging = True
			self._drag_start_pos = event.globalPos() - self.frameGeometry().topLeft()
			event.accept()

	def mouseMoveEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		"""Handle mouse move for window dragging."""
		if self._is_dragging and event.buttons() == Qt.MouseButton.LeftButton and self._drag_start_pos is not None:
			self.move(event.globalPos() - self._drag_start_pos)
			event.accept()

	def mouseReleaseEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		"""Handle mouse release."""
		if event.button() == Qt.MouseButton.LeftButton:
			self._is_dragging = False
			event.accept()


class ResultDetailsPane(QDialog):
	"""Details pane for showing problem information."""

	def __init__(self, scanner_tab: ScannerTab) -> None:
		super().__init__(scanner_tab.window())
		self.scanner_tab = scanner_tab
		self.setWindowTitle("Problem Details")
		# Set window flags to create a frameless tool window (type: ignore[arg-type] is needed for Qt.FramelessWindowHint)
		self.setWindowFlags(Qt.WindowType.Tool | Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)  # type: ignore[arg-type]
		self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)
		self.setAttribute(Qt.WidgetAttribute.WA_DeleteOnClose)

		# Keep windows grouped with main window
		self.setModal(False)

		# Track whether window is being dragged
		self._is_dragging = False
		self._drag_start_pos = None

		self.problem_info: ProblemInfo | SimpleProblemInfo | None = None
		self.sv_mod_name = QtStringVar()
		self.sv_file_path = QtStringVar()
		self.sv_problem = QtStringVar()
		self.sv_solution = QtStringVar()
		self.sv_problem_type = QtStringVar()
		self.sv_extra_data = QtStringVar()

		# Create main widget with border
		main_widget = QWidget()
		main_widget.setObjectName("DetailsPaneWidget")
		main_widget.setStyleSheet("#DetailsPaneWidget { border: 1px solid #424242; background-color: #2b2b2b; }")

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
		close_button = QPushButton("x")
		close_button.setFixedSize(20, 20)
		close_button.setStyleSheet(
			"QPushButton { border: none; font-size: 16px; }QPushButton:hover { background-color: #ff4444; }",
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
			layout.addWidget(QLabel("Mod:"), 0, 0, Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
			mod_label = QLabel()
			self.sv_mod_name.changed.connect(mod_label.setText)
			mod_label.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
			layout.addWidget(mod_label, 0, 1)

		# Problem type row
		layout.addWidget(QLabel("Type:"), start_row, 0, Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
		type_label = QLabel()
		type_label.setStyleSheet(f"color: {cmt_globals.COLOR_INFO};")
		self.sv_problem_type.changed.connect(type_label.setText)
		layout.addWidget(type_label, start_row, 1)

		# File path row
		layout.addWidget(QLabel("Path:"), start_row + 1, 0, Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
		self.label_file_path = QLabel()
		self.label_file_path.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2}; text-decoration: underline;")
		self.label_file_path.setCursor(Qt.CursorShape.PointingHandCursor)
		self.label_file_path.setWordWrap(True)
		self.sv_file_path.changed.connect(self.label_file_path.setText)
		layout.addWidget(self.label_file_path, start_row + 1, 1)

		# Summary row
		layout.addWidget(QLabel("Summary:"), start_row + 2, 0, Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
		summary_label = QLabel()
		summary_label.setWordWrap(True)
		summary_label.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
		self.sv_problem.changed.connect(summary_label.setText)
		layout.addWidget(summary_label, start_row + 2, 1)

		# Solution row
		layout.addWidget(QLabel("Solution:"), start_row + 3, 0, Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
		self.label_solution = QLabel()
		self.label_solution.setWordWrap(True)
		self.label_solution.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
		self.label_solution.setOpenExternalLinks(False)  # We'll handle clicks manually
		self.sv_solution.changed.connect(self.label_solution.setText)
		layout.addWidget(self.label_solution, start_row + 3, 1)

		# Extra data row (if applicable)
		self.extra_data_row = start_row + 4
		self.label_extra_title = QLabel("Extra Data:")
		self.label_extra_data = QLabel()
		self.label_extra_data.setWordWrap(True)
		self.label_extra_data.setStyleSheet(f"color: {cmt_globals.COLOR_WARNING};")
		self.sv_extra_data.changed.connect(self.label_extra_data.setText)
		# Initially hidden
		self.label_extra_title.hide()
		self.label_extra_data.hide()
		layout.addWidget(self.label_extra_title, self.extra_data_row, 0, Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
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

	def browse_to_file(self) -> None:
		"""Browse to the problem file location."""
		if not self.problem_info:
			return

		target = self.problem_info.path
		if isinstance(target, Path) and sys.platform == "win32":
			if exists(target):
				if is_dir(target):
					os.startfile(target)  # type: ignore[attr-defined]
				else:
					# Open parent directory and select file
					os.startfile(target.parent)  # type: ignore[attr-defined]
			elif exists(target.parent):
				os.startfile(target.parent)  # type: ignore[attr-defined]

	def copy_path(self) -> None:
		"""Copy file path to clipboard."""
		if self.problem_info:
			path_text = str(self.problem_info.path)
			clipboard = QApplication.clipboard()
			clipboard.setText(path_text)

			# Provide visual feedback
			original_text = self.button_copy_path.text()
			self.button_copy_path.setText("Copied!")
			self.button_copy_path.setEnabled(False)
			QTimer.singleShot(
				3000,
				lambda: (self.button_copy_path.setText(original_text), self.button_copy_path.setEnabled(True)),
			)

	def copy_details(self) -> None:
		"""Copy problem details to clipboard."""
		mod = f"Mod: {self.sv_mod_name.get()}\n" if self.scanner_tab.using_stage else ""

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
		clipboard = QApplication.clipboard()
		clipboard.setText(details)

		# Provide visual feedback
		original_text = self.button_copy.text()
		self.button_copy.setText("Copied!")
		self.button_copy.setEnabled(False)
		QTimer.singleShot(3000, lambda: (self.button_copy.setText(original_text), self.button_copy.setEnabled(True)))

	def set_info(self, selection: str, *, using_stage: bool) -> None:
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
			if sys.platform == "win32":
				self.label_file_path.mousePressEvent = lambda _: os.startfile(target)  # type: ignore[attr-defined]
			else:
				self.label_file_path.mousePressEvent = lambda _: None
			self.label_file_path.setToolTip("Click to open location")
			self.label_file_path.setCursor(Qt.CursorShape.PointingHandCursor)
		else:
			self.label_file_path.mousePressEvent = lambda _: None
			self.label_file_path.setCursor(Qt.CursorShape.ForbiddenCursor)
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
				self.label_solution.mousePressEvent = lambda _: (webbrowser.open(url), None)[1]
				self.label_solution.setCursor(Qt.CursorShape.PointingHandCursor)
				self.label_solution.setToolTip("Click to open URL")
				# Add URL indicator to solution text
				self.sv_solution.set(f"{solution_text}\n[Click to open: {url}]")
			else:
				self.label_solution.mousePressEvent = lambda _: None
				self.label_solution.setCursor(Qt.CursorShape.ArrowCursor)
				self.label_solution.setToolTip("")
		else:
			# Hide extra data
			self.label_extra_title.hide()
			self.label_extra_data.hide()
			self.sv_extra_data.set("")
			self.label_solution.mousePressEvent = lambda _: None
			self.label_solution.setCursor(Qt.CursorShape.ArrowCursor)
			self.label_solution.setToolTip("")

		# Update button states
		self.button_browse.setEnabled(
			isinstance(self.problem_info.path, Path)
			and (exists(self.problem_info.path) or exists(self.problem_info.path.parent)),
		)

		# Handle file list button
		if self.button_files:
			parent_widget = self.button_files.parent()
			if isinstance(parent_widget, QWidget):
				layout = parent_widget.layout()
				if layout is not None:
					layout.removeWidget(self.button_files)
			self.button_files.deleteLater()
			self.button_files = None

		if self.problem_info.file_list:
			self.button_files = QPushButton("Show File List")
			self.button_files.clicked.connect(self.show_file_list)
			parent_widget = self.button_copy.parent()
			if isinstance(parent_widget, QWidget):
				button_layout = parent_widget.layout()
				if isinstance(button_layout, QVBoxLayout):
					button_layout.insertWidget(3, self.button_files)

		# Handle auto-fix button
		if self.button_autofix:
			parent_widget = self.button_autofix.parent()
			if isinstance(parent_widget, QWidget):
				layout = parent_widget.layout()
				if layout is not None:
					layout.removeWidget(self.button_autofix)
			self.button_autofix.deleteLater()
			self.button_autofix = None

		if self.problem_info.solution in AUTOFIX_REGISTRY:
			self.button_autofix = QPushButton("Auto-Fix")
			self.button_autofix.clicked.connect(lambda: do_autofix_qt(self, selection))
			parent_widget = self.button_copy.parent()
			if isinstance(parent_widget, QWidget):
				button_layout = parent_widget.layout()
				if isinstance(button_layout, QVBoxLayout):
					position = 4 if self.button_files else 3
					button_layout.insertWidget(position, self.button_autofix)

	def show_file_list(self) -> None:
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
			items=self.problem_info.file_list,  # type: ignore[arg-type]
		)
		tree_dialog.exec()

	def mousePressEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		"""Handle mouse press for window dragging."""
		if event.button() == Qt.MouseButton.LeftButton and event.y() <= 40:  # Title bar height
			self._is_dragging = True
			self._drag_start_pos = event.globalPos() - self.frameGeometry().topLeft()
			event.accept()

	def mouseMoveEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		"""Handle mouse move for window dragging."""
		if self._is_dragging and event.buttons() == Qt.MouseButton.LeftButton and self._drag_start_pos is not None:
			self.move(event.globalPos() - self._drag_start_pos)
			event.accept()

	def mouseReleaseEvent(self, event: QMouseEvent) -> None:  # noqa: N802
		"""Handle mouse release."""
		if event.button() == Qt.MouseButton.LeftButton:
			self._is_dragging = False
			event.accept()

	def update_position(self) -> None:
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

	def showEvent(self, event: QShowEvent) -> None:  # noqa: N802
		"""Handle show event."""
		super().showEvent(event)
		self.update_position()
