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
import shutil
from collections.abc import Callable
from pathlib import Path
from typing import TYPE_CHECKING

from PySide6.QtCore import Signal
from PySide6.QtWidgets import QMessageBox, QStyle, QWidget

from autofix_types import AutoFixResult
from enums import ProblemType, SolutionType
from helpers import ProblemInfo, SimpleProblemInfo
from qt_threading import BaseWorker
from utils import read_text_encoded

if TYPE_CHECKING:
	from tabs._scanner_qt import ResultDetailsPane

logger = logging.getLogger(__name__)

# Type alias for problem items
ProblemItem = ProblemInfo | SimpleProblemInfo
FixableProblem = tuple[ProblemItem, str]  # (problem, problem_id)


class AutofixHandlers:
	"""Registry of autofix handlers for different problem types."""

	@staticmethod
	def create_backup(file_path: Path) -> Path | None:
		"""Create a backup of the file before modifying it."""
		try:
			backup_path = file_path.with_suffix(file_path.suffix + ".bak")
			counter = 1
			while backup_path.exists():
				backup_path = file_path.with_suffix(f"{file_path.suffix}.bak{counter}")
				counter += 1
			shutil.copy2(file_path, backup_path)
			logger.info("Created backup: %s", backup_path)
		except (OSError, PermissionError, FileNotFoundError):
			logger.exception("Failed to create backup for %s", file_path)
			return None
		else:
			return backup_path

	@staticmethod
	def autofix_complex_sorter(problem_info: ProblemInfo | SimpleProblemInfo) -> AutoFixResult:
		"""Fix Complex Sorter INI file issues."""
		if isinstance(problem_info, SimpleProblemInfo):
			return AutoFixResult(
				success=False,
				details="Unsupported Problem Type",
			)

		try:
			ini_text, ini_encoding = read_text_encoded(problem_info.path)
			ini_text = ini_text.replace("\r\n", "\n")
			while "\n\n\n" in str(ini_text):
				ini_text = ini_text.replace("\n\n", "\n")
			ini_lines = ini_text.splitlines()
		except FileNotFoundError:
			logger.exception("Auto-Fix : %s : Failed", problem_info.path)
			return AutoFixResult(
				success=False,
				details=f"File Not Found: {problem_info.path}",
			)
		except PermissionError:
			logger.exception("Auto-Fix : %s : Failed", problem_info.path)
			return AutoFixResult(
				success=False,
				details=f"File Access Denied: {problem_info.path}",
			)
		except OSError:
			logger.exception("Auto-Fix : %s : Failed", problem_info.path)
			return AutoFixResult(
				success=False,
				details=f"OSError: {problem_info.path}",
			)

		lines_fixed = 0
		for i, ini_line in enumerate(ini_lines):
			if ini_line.startswith(";"):
				continue

			if 'FindNode OBTS(FindNode "Addon Index"' in ini_line or "FindNode OBTS(FindNode 'Addon Index'" in ini_line:
				ini_lines[i] = ini_line.replace(
					'FindNode OBTS(FindNode "Addon Index"',
					'FindNode OBTS(FindNode "Parent Combination Index"',
				).replace(
					"FindNode OBTS(FindNode 'Addon Index'",
					"FindNode OBTS(FindNode 'Parent Combination Index'",
				)
				lines_fixed += 1
				logger.info(
					'Auto-Fix : %s : Line %s : Updated "Addon Index" to "Parent Combination Index"',
					problem_info.path.name,
					i + 1,
				)

		if lines_fixed:
			# Create backup first
			AutofixHandlers.create_backup(problem_info.path)

			try:
				problem_info.path.write_text("\n".join(ini_lines) + "\n", ini_encoding)
			except PermissionError:
				logger.exception("Auto-Fix : %s : Failed", problem_info.path.name)
				result = AutoFixResult(
					success=False,
					details=f"File Access Denied: {problem_info.path}",
				)
			except OSError:
				logger.exception("Auto-Fix : %s : Failed", problem_info.path.name)
				result = AutoFixResult(
					success=False,
					details=f"OSError: {problem_info.path}",
				)
			else:
				logger.info("Auto-Fix : %s : %s Lines Fixed", problem_info.path.name, lines_fixed)
				result = AutoFixResult(
					success=True,
					details=f'All references to "Addon Index" updated to "Parent Combination Index".\nINI Lines Fixed: {lines_fixed}',
				)
			return result

		logger.error("Auto-Fix : %s : No fixes were needed.", problem_info.path.name)
		return AutoFixResult(
			success=True,
			details="No fixes were needed.",
		)

	@staticmethod
	def autofix_delete_junk_files(problem_info: ProblemInfo | SimpleProblemInfo) -> AutoFixResult:
		"""Delete junk files."""
		if not isinstance(problem_info.path, Path):
			return AutoFixResult(
				success=False,
				details="Invalid path type",
			)

		try:
			# Create backup by moving to recycle bin if possible
			if problem_info.path.is_file():
				# For Windows, we could use send2trash if available
				# For now, just delete
				problem_info.path.unlink()
				logger.info("Deleted junk file: %s", problem_info.path)
				return AutoFixResult(
					success=True,
					details=f"Deleted junk file: {problem_info.path.name}",
				)
			return AutoFixResult(
				success=False,
				details=f"Path is not a file: {problem_info.path}",
			)
		except (OSError, PermissionError, FileNotFoundError) as e:
			logger.exception("Failed to delete junk file %s", problem_info.path)
			return AutoFixResult(
				success=False,
				details=f"Failed to delete: {e!s}",
			)

	@staticmethod
	def autofix_rename_archive(problem_info: ProblemInfo | SimpleProblemInfo) -> AutoFixResult:
		"""Rename invalid archive extensions."""
		if not isinstance(problem_info.path, Path):
			return AutoFixResult(
				success=False,
				details="Invalid path type",
			)

		try:
			# Determine correct extension
			correct_ext = ".ba2"  # Default for Fallout 4
			new_path = problem_info.path.with_suffix(correct_ext)

			# Check if target already exists
			if new_path.exists():
				return AutoFixResult(
					success=False,
					details=f"Cannot rename: {new_path.name} already exists",
				)

			# Rename the file
			problem_info.path.rename(new_path)
			logger.info("Renamed archive: %s -> %s", problem_info.path, new_path)

			return AutoFixResult(
				success=True,
				details=f"Renamed to: {new_path.name}",
			)

		except (OSError, PermissionError, FileNotFoundError) as e:
			logger.exception("Failed to rename archive %s", problem_info.path)
			return AutoFixResult(
				success=False,
				details=f"Failed to rename: {e!s}",
			)

	@staticmethod
	def autofix_delete_loose_previs(problem_info: ProblemInfo | SimpleProblemInfo) -> AutoFixResult:
		"""Delete loose previs files."""
		if not problem_info.file_list:
			return AutoFixResult(
				success=False,
				details="No file list provided",
			)

		deleted_files: list[Path] = []
		failed_files: list[tuple[Path, str]] = []

		for file_info in problem_info.file_list:
			if len(file_info) >= 2:
				file_path = Path(file_info[1])
				try:
					if file_path.exists() and file_path.is_file():
						file_path.unlink()
						deleted_files.append(file_path)
						logger.info("Deleted loose previs: %s", file_path)
				except (OSError, PermissionError, FileNotFoundError) as e:
					logger.exception("Failed to delete %s", file_path)
					failed_files.append((file_path, str(e)))

		if failed_files:
			details = f"Deleted {len(deleted_files)} files, failed to delete {len(failed_files)} files"
			success = False
		else:
			details = f"Successfully deleted {len(deleted_files)} loose previs files"
			success = True

		return AutoFixResult(
			success=success,
			details=details,
		)

	@staticmethod
	def autofix_animation_text_data(problem_info: ProblemInfo | SimpleProblemInfo) -> AutoFixResult:
		"""Fix AnimTextData folder naming."""
		if not isinstance(problem_info.path, Path):
			return AutoFixResult(
				success=False,
				details="Invalid path type",
			)

		try:
			# Check if it's the wrong folder name
			if problem_info.path.name.lower() == "animationtextdata":
				correct_path = problem_info.path.parent / "AnimTextData"

				if correct_path.exists():
					return AutoFixResult(
						success=False,
						details="Cannot rename: AnimTextData already exists",
					)

				# Rename the folder
				problem_info.path.rename(correct_path)
				logger.info("Renamed folder: %s -> %s", problem_info.path, correct_path)

				return AutoFixResult(
					success=True,
					details="Renamed folder to: AnimTextData",
				)
			return AutoFixResult(
				success=False,
				details="Folder name doesn't match expected pattern",
			)

		except (OSError, PermissionError, FileNotFoundError) as e:
			logger.exception("Failed to rename folder %s", problem_info.path)
			return AutoFixResult(
				success=False,
				details=f"Failed to rename: {e!s}",
			)


# Registry of autofix handlers
AUTOFIX_REGISTRY: dict[SolutionType, Callable[[ProblemInfo | SimpleProblemInfo], AutoFixResult]] = {
	SolutionType.ComplexSorterFix: AutofixHandlers.autofix_complex_sorter,
	SolutionType.DeleteFile: AutofixHandlers.autofix_delete_junk_files,
	SolutionType.RenameArchive: AutofixHandlers.autofix_rename_archive,
	SolutionType.ArchiveOrDeleteFile: AutofixHandlers.autofix_delete_loose_previs,
	SolutionType.ArchiveOrDeleteFolder: AutofixHandlers.autofix_animation_text_data,
}


def show_autofix_confirmation(
	parent: QWidget | None,
	problem_info: ProblemInfo | SimpleProblemInfo,
	solution_type: SolutionType,
) -> bool:
	"""Show confirmation dialog before applying autofix."""
	msg = QMessageBox(parent)
	msg.setWindowTitle("Confirm Auto-Fix")
	msg.setIcon(QMessageBox.Icon.Question)

	# Build confirmation message
	if solution_type == SolutionType.DeleteFile:
		msg.setText(f"Delete this file?\n\n{problem_info.path}")
		msg.setInformativeText("The file will be permanently deleted.")
	elif solution_type == SolutionType.ArchiveOrDeleteFile:
		file_count = len(problem_info.file_list) if problem_info.file_list else 0
		msg.setText(f"Delete {file_count} loose previs files?")
		msg.setInformativeText("These files will be permanently deleted.")
	elif solution_type == SolutionType.RenameArchive:
		msg.setText(f"Rename this archive?\n\n{problem_info.path}")
		msg.setInformativeText("The file extension will be changed to .ba2")
	elif solution_type == SolutionType.ArchiveOrDeleteFolder:
		msg.setText(f"Rename this folder?\n\n{problem_info.path}")
		msg.setInformativeText("The folder will be renamed to AnimTextData")
	elif solution_type == SolutionType.ComplexSorterFix:
		msg.setText(f"Fix Complex Sorter INI?\n\n{problem_info.path}")
		msg.setInformativeText("This will update Addon Index references to Parent Combination Index")
	else:
		msg.setText(f"Apply auto-fix to:\n\n{problem_info.path}")
		msg.setInformativeText("A backup will be created if possible.")

	msg.setStandardButtons(QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No)
	msg.setDefaultButton(QMessageBox.StandardButton.No)

	return msg.exec() == QMessageBox.StandardButton.Yes


def do_autofix_qt(details_pane: "ResultDetailsPane", item_id: str) -> None:
	"""Execute autofix for Qt interface."""
	problem_info = details_pane.scanner_tab.tree_results_data.get(item_id)
	if not problem_info:
		return

	# Ensure solution is a valid SolutionType
	if not isinstance(problem_info.solution, SolutionType):
		QMessageBox.warning(details_pane, "Auto-Fix Not Available", f"Invalid solution type: {problem_info.solution}")
		return

	# Check if solution is in registry
	if problem_info.solution not in AUTOFIX_REGISTRY:
		QMessageBox.warning(
			details_pane,
			"Auto-Fix Not Available",
			f"No auto-fix available for solution type: {problem_info.solution}",
		)
		return

	# Show confirmation dialog
	if not show_autofix_confirmation(details_pane, problem_info, problem_info.solution):
		return

	# Disable button and update text
	if details_pane.button_autofix:
		details_pane.button_autofix.setEnabled(False)
		details_pane.button_autofix.setText("Fixing...")

	try:
		# Get the autofix handler
		autofix_handler = AUTOFIX_REGISTRY[problem_info.solution]

		# Execute the autofix
		logger.info("Auto-Fix : Running %s", autofix_handler.__name__)
		result = autofix_handler(problem_info)

		# Store result on problem info
		if hasattr(problem_info, "autofix_result"):
			problem_info.autofix_result = result

		# Update button based on result
		if details_pane.button_autofix:
			if result.success:
				details_pane.button_autofix.setText("Fixed!")
				details_pane.button_autofix.setStyleSheet("QPushButton { color: #00ff00; }")

				# Update tree item icon with check mark
				if hasattr(details_pane.scanner_tab, "tree_results_items"):
					tree_item = details_pane.scanner_tab.tree_results_items.get(item_id)
					if tree_item:
						# Use standard Qt check mark icon
						app = QWidget().style()
						check_icon = app.standardIcon(QStyle.StandardPixmap.SP_DialogYesButton)
						tree_item.setIcon(0, check_icon)
			else:
				details_pane.button_autofix.setText("Fix Failed")
				details_pane.button_autofix.setStyleSheet("QPushButton { color: #ff0000; }")

			details_pane.button_autofix.setEnabled(True)

		# Show results dialog
		msg = QMessageBox(details_pane)
		msg.setWindowTitle("Auto-Fix Results")

		if result.success:
			msg.setIcon(QMessageBox.Icon.Information)
			msg.setText("Auto-fix completed successfully!")
		else:
			msg.setIcon(QMessageBox.Icon.Warning)
			msg.setText("Auto-fix failed!")

		msg.setInformativeText(result.details)

		msg.exec()

	except Exception as e:
		logger.exception("Auto-fix failed with exception")
		if details_pane.button_autofix:
			details_pane.button_autofix.setText("Fix Failed")
			details_pane.button_autofix.setEnabled(True)

		QMessageBox.critical(details_pane, "Auto-Fix Error", f"An error occurred during auto-fix:\n\n{e!s}")


class BatchAutofixDialog(QMessageBox):
	"""Dialog for selecting problems to batch fix."""

	def __init__(self, parent: QWidget | None, fixable_problems: list[FixableProblem]) -> None:
		super().__init__(parent)
		self.fixable_problems = fixable_problems
		self.selected_problems: list[FixableProblem] = []

		self.setWindowTitle("Batch Auto-Fix")
		self.setIcon(QMessageBox.Icon.Question)
		self.setText("Select which problems to auto-fix:")

		# Group problems by type
		problem_groups: dict[ProblemType | str, list[FixableProblem]] = {}
		for problem, problem_id in fixable_problems:
			problem_type: ProblemType | str = problem.type if isinstance(problem, ProblemInfo) else str(problem.problem)
			if problem_type not in problem_groups:
				problem_groups[problem_type] = []
			problem_groups[problem_type].append((problem, problem_id))

		# Build informative text
		info_text: list[str] = []
		for problem_type, _problems in problem_groups.items():
			info_text.append(f"\n{problem_type} ({len(_problems)} items)")

		self.setInformativeText("The following problem types will be fixed:" + "".join(info_text))

		# Add buttons
		self.addButton("Fix All", QMessageBox.ButtonRole.AcceptRole)
		self.addButton("Cancel", QMessageBox.ButtonRole.RejectRole)

	def get_selected_problems(self) -> list[FixableProblem]:
		"""Return all problems (for now, we fix all)."""
		return self.fixable_problems


class BatchAutofixWorker(BaseWorker):
	"""Worker for batch autofix operations."""

	progress = Signal(int, int, str)  # current, total, message
	finished = Signal(list)  # list of AutoFixResult

	def __init__(self, problems_to_fix: list[FixableProblem], tree_results_data: dict[str, ProblemItem]) -> None:
		super().__init__()
		self.problems_to_fix = problems_to_fix
		self.tree_results_data = tree_results_data
		self._cancelled = False

	def cancel(self) -> None:
		"""Cancel the batch operation."""
		self._cancelled = True

	def run(self) -> None:
		"""Run batch autofix."""
		results: list[AutoFixResult] = []
		total = len(self.problems_to_fix)

		for i, (problem, _problem_id) in enumerate(self.problems_to_fix):
			if self._cancelled:
				break

			# Emit progress with proper path handling
			path_display = problem.path.name if isinstance(problem.path, Path) else str(problem.path)
			self.progress.emit(i, total, f"Fixing: {path_display}")

			# Validate solution type before accessing registry
			if not isinstance(problem.solution, SolutionType):
				logger.warning("Invalid solution type for %s: %s", problem.path, problem.solution)
				results.append(AutoFixResult(success=False, details=f"Invalid solution type: {problem.solution}"))
				continue

			# Get autofix handler
			if problem.solution in AUTOFIX_REGISTRY:
				handler = AUTOFIX_REGISTRY[problem.solution]
				try:
					result = handler(problem)
					results.append(result)

					# Update problem info with result
					if hasattr(problem, "autofix_result"):
						problem.autofix_result = result

				except Exception as e:
					logger.exception("Batch autofix failed for %s", problem.path)
					results.append(AutoFixResult(success=False, details=f"Exception: {e!s}"))
			else:
				results.append(AutoFixResult(success=False, details="No autofix handler available"))

		self.finished.emit(results)
