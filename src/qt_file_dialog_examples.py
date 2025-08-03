"""Example of Qt file dialog implementation for CMT migration.

This file demonstrates how to replace tkinter file dialogs with Qt equivalents
in the Collective Modding Toolkit project.
"""

from pathlib import Path

from PySide6.QtWidgets import QWidget

from qt_compat import ask_yes_no, filedialog, show_error


class QtGameInfoExample:
	"""Example showing how to migrate game_info.py file dialog usage to Qt."""

	def __init__(self, parent: QWidget) -> None:
		self.parent = parent

	def select_game_executable_qt(self) -> Path | None:
		"""Qt version of game executable selection (migrated from game_info.py).

		This replaces the tkinter filedialog usage in the original game_info.py:

		Original Tkinter code:
		----------------------
		game_path = filedialog.askopenfilename(
		    title="Select Fallout4.exe",
		    filetypes=(("Fallout 4", "Fallout4.exe"),),
		)

		if not game_path:
		    messagebox.showerror(
		        "Game not found",
		        "A Fallout 4 installation could not be found.",
		    )
		    sys.exit()
		"""

		# First ask if user wants to manually specify location
		ask_location = ask_yes_no(
			self.parent,
			"Game Path Selection",
			"Could not automatically detect Fallout 4 installation.\n\n"
			"Manually specify a location?\n"
			"CM Toolkit will close otherwise.",
		)

		if not ask_location:
			return None

		# Use Qt file dialog with tkinter-compatible interface
		game_path = filedialog.askopenfilename(
			parent=self.parent, title="Select Fallout4.exe", filetypes=(("Fallout 4", "Fallout4.exe"),),
		)

		if not game_path:
			# Empty string if dialog cancelled
			show_error(self.parent, "Game not found", "A Fallout 4 installation could not be found.")
			return None

		return Path(game_path)

	def select_mod_archive(self) -> Path | None:
		"""Example of selecting mod archives (BA2 files)."""

		path = filedialog.askopenfilename(
			parent=self.parent,
			title="Select Mod Archive",
			filetypes=(("Bethesda Archives", "*.ba2"), ("All Archives", "*.ba2 *.bsa"), ("All files", "*.*")),
			initialdir=str(Path.home() / "Documents" / "My Games" / "Fallout4" / "Data"),
		)

		return Path(path) if path else None

	def select_multiple_plugins(self) -> list[Path]:
		"""Example of selecting multiple plugin files (ESP/ESM)."""

		paths = filedialog.askopenfilenames(
			parent=self.parent,
			title="Select Plugin Files",
			filetypes=(
				("Plugin files", "*.esp *.esm *.esl"),
				("ESP files", "*.esp"),
				("ESM files", "*.esm"),
				("ESL files", "*.esl"),
				("All files", "*.*"),
			),
		)

		return [Path(path) for path in paths]

	def select_mod_directory(self) -> Path | None:
		"""Example of selecting a mod directory."""

		path = filedialog.askdirectory(
			parent=self.parent,
			title="Select Mod Directory",
			initialdir=str(Path.home() / "Documents" / "My Games" / "Fallout4" / "Mods"),
		)

		return Path(path) if path else None

	def export_scan_results(self) -> Path | None:
		"""Example of save file dialog for exporting results."""

		path = filedialog.asksaveasfilename(
			parent=self.parent,
			title="Export Scan Results",
			filetypes=(("JSON files", "*.json"), ("Text files", "*.txt"), ("CSV files", "*.csv"), ("All files", "*.*")),
			defaultextension=".json",
		)

		return Path(path) if path else None


# Migration guide for common patterns
MIGRATION_EXAMPLES = {
	"basic_open_file": {
		"description": "Basic file selection",
		"tkinter": """
# Tkinter version
from tkinter import filedialog
path = filedialog.askopenfilename(title="Select File")
""",
		"qt": """
# Qt version
from qt_compat import filedialog
path = filedialog.askopenfilename(parent=self, title="Select File")
""",
	},
	"with_filetypes": {
		"description": "File selection with specific file types",
		"tkinter": """
# Tkinter version
path = filedialog.askopenfilename(
    title="Select Python File",
    filetypes=(("Python files", "*.py"), ("All files", "*.*"))
)
""",
		"qt": """
# Qt version  
path = filedialog.askopenfilename(
    parent=self,
    title="Select Python File", 
    filetypes=(("Python files", "*.py"), ("All files", "*.*"))
)
""",
	},
	"save_file": {
		"description": "Save file dialog",
		"tkinter": """
# Tkinter version
path = filedialog.asksaveasfilename(
    title="Save As",
    defaultextension=".txt",
    filetypes=(("Text files", "*.txt"), ("All files", "*.*"))
)
""",
		"qt": """
# Qt version
path = filedialog.asksaveasfilename(
    parent=self,
    title="Save As",
    defaultextension=".txt", 
    filetypes=(("Text files", "*.txt"), ("All files", "*.*"))
)
""",
	},
	"directory_selection": {
		"description": "Directory selection",
		"tkinter": """
# Tkinter version
path = filedialog.askdirectory(title="Select Folder")
""",
		"qt": """
# Qt version
path = filedialog.askdirectory(parent=self, title="Select Folder")
""",
	},
	"multiple_files": {
		"description": "Multiple file selection",
		"tkinter": """
# Tkinter version
paths = filedialog.askopenfilenames(
    title="Select Files",
    filetypes=(("Python files", "*.py"),)
)
""",
		"qt": """
# Qt version
paths = filedialog.askopenfilenames(
    parent=self,
    title="Select Files",
    filetypes=(("Python files", "*.py"),)
)
""",
	},
}


def print_migration_guide() -> None:
	"""Print migration examples to console."""
	print("File Dialog Migration Guide")
	print("=" * 50)
	print()

	for example in MIGRATION_EXAMPLES.values():
		print(f"## {example['description']}")
		print()
		print("### Before (Tkinter):")
		print("```python")
		print(example["tkinter"].strip())
		print("```")
		print()
		print("### After (Qt):")
		print("```python")
		print(example["qt"].strip())
		print("```")
		print()
		print("-" * 30)
		print()


if __name__ == "__main__":
	print_migration_guide()
