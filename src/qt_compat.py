"""Qt compatibility module for PySide6 migration.

This module provides a centralized import location for all PySide6 components,
making it easier to manage imports across the codebase during migration.
"""

# Core Qt modules
from PySide6.QtCore import (
	QEvent,
	QMutex,
	QMutexLocker,
	QObject,
	Qt,
	QThread,
	QTimer,
	Signal,
	Slot,
)
from PySide6.QtGui import (
	QCloseEvent,
	QFontDatabase,
	QIcon,
	QKeySequence,
	QMouseEvent,
	QPixmap,
	QShortcut,
	QWindowStateChangeEvent,
)

# Widget module imports
from PySide6.QtWidgets import (
	QApplication,
	QDialog,
	QDialogButtonBox,
	QFileDialog,
	QHBoxLayout,
	QHeaderView,
	QLabel,
	QLayout,
	QMainWindow,
	QMessageBox,
	QProgressBar,
	QProgressDialog,
	QTabWidget,
	QTreeWidget,
	QTreeWidgetItem,
	QVBoxLayout,
	QWidget,
)

# Type definitions for file dialog compatibility
FileTypes = tuple[tuple[str, str], ...] | None


# File dialog functionality
class FileDialog:
	"""Qt file dialog wrapper class compatible with tkinter filedialog interface."""

	@staticmethod
	def askopenfilename(
		parent: QWidget | None = None,
		title: str = "Open File",
		filetypes: FileTypes = None,
		initialdir: str = "",
		defaultextension: str = "",
	) -> str:
		"""Open file dialog - tkinter compatible interface."""
		caption = title
		directory = initialdir

		# Convert filetypes to Qt filter format
		filter_str = FileDialog._convert_filetypes(filetypes, defaultextension)

		path, _ = QFileDialog.getOpenFileName(parent, caption, directory, filter_str)
		return path

	@staticmethod
	def asksaveasfilename(
		parent: QWidget | None = None,
		title: str = "Save File",
		filetypes: FileTypes = None,
		initialdir: str = "",
		defaultextension: str = "",
	) -> str:
		"""Save file dialog - tkinter compatible interface."""
		caption = title
		directory = initialdir

		# Convert filetypes to Qt filter format
		filter_str = FileDialog._convert_filetypes(filetypes, defaultextension)

		path, _ = QFileDialog.getSaveFileName(parent, caption, directory, filter_str)
		return path

	@staticmethod
	def askdirectory(parent: QWidget | None = None, title: str = "Select Directory", initialdir: str = "") -> str:
		"""Directory selection dialog - tkinter compatible interface."""
		caption = title
		directory = initialdir

		return QFileDialog.getExistingDirectory(parent, caption, directory)

	@staticmethod
	def askopenfilenames(
		parent: QWidget | None = None,
		title: str = "Open Files",
		filetypes: FileTypes = None,
		initialdir: str = "",
		defaultextension: str = "",
	) -> list[str]:
		"""Multiple file selection dialog - tkinter compatible interface."""
		caption = title
		directory = initialdir

		# Convert filetypes to Qt filter format
		filter_str = FileDialog._convert_filetypes(filetypes, defaultextension)

		paths, _ = QFileDialog.getOpenFileNames(parent, caption, directory, filter_str)
		return paths

	@staticmethod
	def _convert_filetypes(filetypes: FileTypes, defaultextension: str = "") -> str:
		"""Convert tkinter filetypes to Qt filter format.

		Args:
		    filetypes: Tuple of (description, pattern) tuples like (("Text files", "*.txt"), ("All files", "*.*"))
		    defaultextension: Default file extension if none specified

		Returns:
		    Qt-compatible filter string
		"""
		if not filetypes:
			if defaultextension:
				return f"Files (*{defaultextension});;All files (*.*)"
			return "All files (*.*)"

		filters: list[str] = []
		for description, pattern in filetypes:
			# Convert pattern to string - it should always be a string in practice
			pattern_str = str(pattern)
			filters.append(f"{description} ({pattern_str})")

		# Add "All files" if not present
		if not any("*.*" in f for f in filters):
			filters.append("All files (*.*)")

		return ";;".join(filters)


# Common dialog shortcuts (simplified interface)
def get_open_file_name(parent: QWidget | None = None, caption: str = "", directory: str = "", filter_str: str = "") -> str:
	"""Wrapper for QFileDialog.getOpenFileName with simpler return."""
	path, _ = QFileDialog.getOpenFileName(parent, caption, directory, filter_str)
	return path


def get_save_file_name(parent: QWidget | None = None, caption: str = "", directory: str = "", filter_str: str = "") -> str:
	"""Wrapper for QFileDialog.getSaveFileName with simpler return."""
	path, _ = QFileDialog.getSaveFileName(parent, caption, directory, filter_str)
	return path


def get_existing_directory(parent: QWidget | None = None, caption: str = "", directory: str = "") -> str:
	"""Wrapper for QFileDialog.getExistingDirectory."""
	return QFileDialog.getExistingDirectory(parent, caption, directory)


def get_open_file_names(parent: QWidget | None = None, caption: str = "", directory: str = "", filter_str: str = "") -> list[str]:
	"""Wrapper for QFileDialog.getOpenFileNames with simpler return."""
	paths, _ = QFileDialog.getOpenFileNames(parent, caption, directory, filter_str)
	return paths


# Tkinter compatibility - create module-like object that can be imported as 'filedialog'
class TkinterCompatibleFileDialog:
	"""Provides tkinter.filedialog compatible interface."""

	askopenfilename = FileDialog.askopenfilename
	asksaveasfilename = FileDialog.asksaveasfilename
	askdirectory = FileDialog.askdirectory
	askopenfilenames = FileDialog.askopenfilenames


# Create instance for import compatibility
filedialog = TkinterCompatibleFileDialog()


class TkinterCompatibleMessageBox:
	"""Provides tkinter.messagebox compatible interface."""

	@staticmethod
	def showinfo(title: str, message: str, parent: QWidget | None = None) -> None:
		"""Show information message box."""
		QMessageBox.information(parent, title, message)

	@staticmethod
	def showwarning(title: str, message: str, parent: QWidget | None = None) -> None:
		"""Show warning message box."""
		QMessageBox.warning(parent, title, message)

	@staticmethod
	def showerror(title: str, message: str, parent: QWidget | None = None) -> None:
		"""Show error message box."""
		QMessageBox.critical(parent, title, message)

	@staticmethod
	def askyesno(title: str, message: str, parent: QWidget | None = None) -> bool:
		"""Show yes/no question dialog."""
		reply = QMessageBox.question(
			parent,
			title,
			message,
			QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
			QMessageBox.StandardButton.No,
		)
		return reply == QMessageBox.StandardButton.Yes


# Create instance for import compatibility
messagebox = TkinterCompatibleMessageBox()


# Message box shortcuts
def show_info(parent: QWidget | None, title: str, message: str) -> None:
	"""Show information message box."""
	QMessageBox.information(parent, title, message)


def show_warning(parent: QWidget | None, title: str, message: str) -> None:
	"""Show warning message box."""
	QMessageBox.warning(parent, title, message)


def show_error(parent: QWidget | None, title: str, message: str) -> None:
	"""Show error message box."""
	QMessageBox.critical(parent, title, message)


def ask_yes_no(parent: QWidget | None, title: str, message: str) -> bool:
	"""Show yes/no question dialog."""
	reply = QMessageBox.question(
		parent,
		title,
		message,
		QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
		QMessageBox.StandardButton.No,
	)
	return reply == QMessageBox.StandardButton.Yes


# Common style constants
BUTTON_STYLE = """
    QPushButton {
        padding: 5px 15px;
        border-radius: 3px;
    }
    QPushButton:hover {
        background-color: palette(midlight);
    }
"""


# Layout helper functions
def clear_layout(layout: QLayout) -> None:
	"""Clear all widgets from a layout."""
	while layout.count():
		child = layout.takeAt(0)
		if child.widget():
			child.widget().deleteLater()


def set_margins(layout: QLayout, margin: int) -> None:
	"""Set all margins of a layout to the same value."""
	layout.setContentsMargins(margin, margin, margin, margin)
	child = layout.takeAt(0)
	if child.widget():
		child.widget().deleteLater()
	layout.setContentsMargins(margin, margin, margin, margin)
