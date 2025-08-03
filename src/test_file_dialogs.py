#!/usr/bin/env python3
"""Test script for Qt file dialog implementation.

This script demonstrates the file dialog functionality for the PySide6 migration.
Run with: python src/test_file_dialogs.py
"""

import sys
from pathlib import Path

# Add src to path to import our modules
sys.path.insert(0, str(Path(__file__).parent))

from PySide6.QtWidgets import QApplication, QMainWindow, QVBoxLayout, QWidget, QPushButton, QTextEdit, QLabel
from qt_compat import FileDialog, filedialog, get_open_file_name, get_save_file_name, get_existing_directory


class FileDialogTester(QMainWindow):
	"""Main window for testing file dialogs."""

	def __init__(self):
		super().__init__()
		self.setWindowTitle("File Dialog Test - PySide6 Migration")
		self.setGeometry(100, 100, 600, 400)

		# Central widget
		central_widget = QWidget()
		self.setCentralWidget(central_widget)
		layout = QVBoxLayout(central_widget)

		# Title
		title = QLabel("File Dialog Test for PySide6 Migration")
		title.setStyleSheet("font-size: 18px; font-weight: bold; margin: 10px;")
		layout.addWidget(title)

		# Output area
		self.output = QTextEdit()
		self.output.setReadOnly(True)
		layout.addWidget(self.output)

		# Test buttons
		self.create_test_buttons(layout)

	def create_test_buttons(self, layout):
		"""Create test buttons for different file dialog types."""

		# Tkinter-compatible interface tests
		btn_open_tkinter = QPushButton("Test: filedialog.askopenfilename (Tkinter compatible)")
		btn_open_tkinter.clicked.connect(self.test_tkinter_open_file)
		layout.addWidget(btn_open_tkinter)

		btn_save_tkinter = QPushButton("Test: filedialog.asksaveasfilename (Tkinter compatible)")
		btn_save_tkinter.clicked.connect(self.test_tkinter_save_file)
		layout.addWidget(btn_save_tkinter)

		btn_dir_tkinter = QPushButton("Test: filedialog.askdirectory (Tkinter compatible)")
		btn_dir_tkinter.clicked.connect(self.test_tkinter_directory)
		layout.addWidget(btn_dir_tkinter)

		btn_multiple_tkinter = QPushButton("Test: filedialog.askopenfilenames (Tkinter compatible)")
		btn_multiple_tkinter.clicked.connect(self.test_tkinter_multiple_files)
		layout.addWidget(btn_multiple_tkinter)

		# Qt native interface tests
		btn_open_qt = QPushButton("Test: get_open_file_name (Qt native)")
		btn_open_qt.clicked.connect(self.test_qt_open_file)
		layout.addWidget(btn_open_qt)

		btn_save_qt = QPushButton("Test: get_save_file_name (Qt native)")
		btn_save_qt.clicked.connect(self.test_qt_save_file)
		layout.addWidget(btn_save_qt)

		btn_dir_qt = QPushButton("Test: get_existing_directory (Qt native)")
		btn_dir_qt.clicked.connect(self.test_qt_directory)
		layout.addWidget(btn_dir_qt)

		# Game-specific test (like the actual usage in game_info.py)
		btn_game_specific = QPushButton("Test: Game-specific (Fallout4.exe selection)")
		btn_game_specific.clicked.connect(self.test_game_specific)
		layout.addWidget(btn_game_specific)

		# Clear output button
		btn_clear = QPushButton("Clear Output")
		btn_clear.clicked.connect(self.output.clear)
		layout.addWidget(btn_clear)

	def log_output(self, message: str):
		"""Add message to output area."""
		self.output.append(message)
		self.output.append("")  # Add empty line for readability

	def test_tkinter_open_file(self):
		"""Test tkinter-compatible open file dialog."""
		self.log_output("Testing filedialog.askopenfilename...")

		# Test with filetypes (like the real usage)
		filetypes = (("Python files", "*.py"), ("Text files", "*.txt"), ("All files", "*.*"))

		path = filedialog.askopenfilename(
			parent=self, title="Select a Python file", filetypes=filetypes, initialdir=str(Path(__file__).parent)
		)

		if path:
			self.log_output(f"Selected file: {path}")
		else:
			self.log_output("No file selected (dialog cancelled)")

	def test_tkinter_save_file(self):
		"""Test tkinter-compatible save file dialog."""
		self.log_output("Testing filedialog.asksaveasfilename...")

		filetypes = (("Text files", "*.txt"), ("Python files", "*.py"), ("All files", "*.*"))

		path = filedialog.asksaveasfilename(parent=self, title="Save file as...", filetypes=filetypes, defaultextension=".txt")

		if path:
			self.log_output(f"Save path: {path}")
		else:
			self.log_output("Save cancelled")

	def test_tkinter_directory(self):
		"""Test tkinter-compatible directory dialog."""
		self.log_output("Testing filedialog.askdirectory...")

		path = filedialog.askdirectory(parent=self, title="Select a directory", initialdir=str(Path(__file__).parent.parent))

		if path:
			self.log_output(f"Selected directory: {path}")
		else:
			self.log_output("No directory selected")

	def test_tkinter_multiple_files(self):
		"""Test tkinter-compatible multiple file selection."""
		self.log_output("Testing filedialog.askopenfilenames...")

		filetypes = (("Python files", "*.py"), ("All files", "*.*"))

		paths = filedialog.askopenfilenames(
			parent=self, title="Select multiple files", filetypes=filetypes, initialdir=str(Path(__file__).parent)
		)

		if paths:
			self.log_output(f"Selected {len(paths)} files:")
			for path in paths:
				self.log_output(f"  - {path}")
		else:
			self.log_output("No files selected")

	def test_qt_open_file(self):
		"""Test Qt native open file dialog."""
		self.log_output("Testing get_open_file_name...")

		path = get_open_file_name(
			parent=self,
			caption="Select a file (Qt native)",
			directory=str(Path(__file__).parent),
			filter="Python files (*.py);;Text files (*.txt);;All files (*.*)",
		)

		if path:
			self.log_output(f"Selected file: {path}")
		else:
			self.log_output("No file selected")

	def test_qt_save_file(self):
		"""Test Qt native save file dialog."""
		self.log_output("Testing get_save_file_name...")

		path = get_save_file_name(parent=self, caption="Save file (Qt native)", filter="Text files (*.txt);;All files (*.*)")

		if path:
			self.log_output(f"Save path: {path}")
		else:
			self.log_output("Save cancelled")

	def test_qt_directory(self):
		"""Test Qt native directory dialog."""
		self.log_output("Testing get_existing_directory...")

		path = get_existing_directory(
			parent=self, caption="Select directory (Qt native)", directory=str(Path(__file__).parent.parent)
		)

		if path:
			self.log_output(f"Selected directory: {path}")
		else:
			self.log_output("No directory selected")

	def test_game_specific(self):
		"""Test game-specific file selection (mimicking game_info.py usage)."""
		self.log_output("Testing game-specific file selection (Fallout4.exe)...")

		# This mimics the actual usage in game_info.py
		game_path = filedialog.askopenfilename(
			parent=self, title="Select Fallout4.exe", filetypes=(("Fallout 4", "Fallout4.exe"),)
		)

		if game_path:
			self.log_output(f"Game executable: {game_path}")
			# Check if it's actually Fallout4.exe
			if Path(game_path).name.lower() == "fallout4.exe":
				self.log_output("✓ Correct game executable selected!")
			else:
				self.log_output("⚠ Warning: Selected file is not Fallout4.exe")
		else:
			self.log_output("Game selection cancelled")


def main():
	"""Main function to run the test application."""
	app = QApplication(sys.argv)

	# Set application properties
	app.setApplicationName("File Dialog Tester")
	app.setApplicationVersion("1.0")

	# Create and show main window
	window = FileDialogTester()
	window.show()

	# Run the application
	sys.exit(app.exec())


if __name__ == "__main__":
	main()
