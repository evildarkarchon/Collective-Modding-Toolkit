"""Example worker implementations for common CMT operations.

These workers demonstrate how to use the Qt threading infrastructure
for various long-running operations in the Collective Modding Toolkit.
"""

from __future__ import annotations

import logging
import urllib.error
import urllib.request
from typing import TYPE_CHECKING, Any, TypedDict

from qt_threading import BaseWorker
from utils import find_mod_manager

if TYPE_CHECKING:
	from pathlib import Path

	from game_info_qt import GameInfo
	from scan_settings import ScanSettings

logger = logging.getLogger(__name__)


class DownloadCancelledError(Exception):
	"""Exception raised when a download operation is cancelled by the user."""


class GameInfoResult(TypedDict):
	"""Type definition for game info worker results."""

	found: bool
	path: str | None
	install_type: str | None
	errors: list[str]


class GameInfoWorker(BaseWorker):
	"""Worker for loading game information in the background."""

	def __init__(self, game_info: GameInfo) -> None:
		super().__init__()
		self.game_info = game_info

	def execute(self) -> GameInfoResult:
		"""Load game information and return results."""
		self.update_status("Detecting Fallout 4 installation...")
		self.update_progress(10)

		# Find game installation
		result: GameInfoResult = {
			"found": False,
			"path": None,
			"install_type": None,
			"errors": [],
		}

		try:
			# Check for game - game_path property is available if game was found during initialization
			if hasattr(self.game_info, "game_path"):
				result["found"] = True
				result["path"] = str(self.game_info.game_path)
				result["install_type"] = self.game_info.install_type

				self.update_progress(40)
				self.update_status("Checking game information...")

				# Additional game info could be gathered here if needed

				self.update_progress(70)
				self.update_status("Checking game files...")

				# Additional checks could go here

				self.update_progress(100)
				self.update_status("Game information loaded successfully")
			else:
				result["errors"].append("Fallout 4 installation not found")
				self.update_status("Fallout 4 not found")

		except (OSError, AttributeError, ValueError) as e:
			logger.exception("Error loading game info")
			result["errors"].append(str(e))

		return result


class ModManagerWorker(BaseWorker):
	"""Worker for detecting mod managers."""

	def execute(self) -> dict[str, Any]:
		"""Detect installed mod managers."""
		self.update_status("Detecting mod managers...")
		self.update_progress(20)

		result: dict[str, Any] = {"manager": None}

		# Check for mod manager using the unified detection function
		self.update_status("Checking for mod managers...")
		manager = find_mod_manager()
		if manager:
			manager_info = {
				"name": manager.name,
				"path": str(manager.exe_path),
				"version": str(manager.version),
				"portable": getattr(manager, "portable", False),
			}

			# Add manager-specific information
			if manager.name == "Mod Organizer":
				if hasattr(manager, "selected_profile") and manager.selected_profile:
					manager_info["profile"] = manager.selected_profile
				if hasattr(manager, "profiles_path") and manager.profiles_path:
					# Get available profiles
					try:
						profiles = [p.name for p in manager.profiles_path.iterdir() if p.is_dir()]
						manager_info["profiles"] = profiles
					except (OSError, AttributeError):
						manager_info["profiles"] = []
				if hasattr(manager, "stage_path") and manager.stage_path:
					manager_info["mods_path"] = str(manager.stage_path)
			elif manager.name == "Vortex":
				if hasattr(manager, "stage_path") and manager.stage_path:
					manager_info["mods_path"] = str(manager.stage_path)

			result["manager"] = manager_info

		self.update_progress(100)
		self.update_status("Mod manager detection complete")

		return result


class ScannerWorker(BaseWorker):
	"""Worker for scanning mods for issues."""

	def __init__(self, scan_settings: ScanSettings, paths_to_scan: list[Path]) -> None:
		super().__init__()
		self.scan_settings = scan_settings
		self.paths_to_scan = paths_to_scan

	def execute(self) -> list[dict[str, Any]]:
		"""Perform mod scanning."""
		results: list[dict[str, Any]] = []
		total_paths = len(self.paths_to_scan)

		for idx, path in enumerate(self.paths_to_scan):
			if not self.is_running:  # Check if we should stop
				break

			# Update progress
			progress = int((idx / total_paths) * 100)
			self.update_progress(progress)
			self.update_status(f"Scanning {path.name}...")

			try:
				# Perform scan (simplified example)
				scan_result = self._scan_path(path)
				if scan_result:
					results.append(scan_result)

			except (OSError, PermissionError):
				logger.exception("Error scanning %s", path)

		self.update_progress(100)
		self.update_status(f"Scan complete. Found {len(results)} issues.")

		return results

	def _scan_path(self, path: Path) -> dict[str, Any] | None:  # noqa: PLR6301
		"""Scan a single path for issues."""
		# This is a simplified example - real implementation would use the scanner tab logic
		issues = []

		# Example checks
		if path.suffix.lower() == ".dll":
			# Check DLL compatibility
			issues.append({"type": "dll_check", "severity": "warning", "message": "DLL requires compatibility check"})

		if issues:
			return {"path": str(path), "name": path.name, "issues": issues}

		return None


class FileOperationWorker(BaseWorker):
	"""Worker for file operations like copying, moving, or deleting."""

	def __init__(self, operation: str, source_files: list[Path], destination: Path | None = None) -> None:
		super().__init__()
		self.operation = operation
		self.source_files = source_files
		self.destination = destination

	def execute(self) -> bool:
		"""Perform file operations."""
		total_files = len(self.source_files)

		for idx, file_path in enumerate(self.source_files):
			if not self.is_running:
				return False

			progress = int((idx / total_files) * 100)
			self.update_progress(progress)
			self.update_status(f"{self.operation} {file_path.name}...")

			try:
				if self.operation == "copy":
					self._copy_file(file_path)
				elif self.operation == "move":
					self._move_file(file_path)
				elif self.operation == "delete":
					self._delete_file(file_path)

			except (OSError, PermissionError, FileNotFoundError):
				logger.exception("Error during %s of %s", self.operation, file_path)
				return False

		self.update_progress(100)
		self.update_status(f"{self.operation} completed successfully")
		return True

	def _copy_file(self, source: Path) -> None:
		"""Copy a file to destination."""
		if self.destination:
			import shutil  # noqa: PLC0415

			dest_file = self.destination / source.name
			shutil.copy2(source, dest_file)

	def _move_file(self, source: Path) -> None:
		"""Move a file to destination."""
		if self.destination:
			import shutil  # noqa: PLC0415

			dest_file = self.destination / source.name
			shutil.move(str(source), str(dest_file))

	def _delete_file(self, file_path: Path) -> None:  # noqa: PLR6301
		"""Delete a file."""
		file_path.unlink()


class DownloadWorker(BaseWorker):
	"""Worker for downloading files with progress tracking."""

	def __init__(self, url: str, destination: Path) -> None:
		super().__init__()
		self.url = url
		self.destination = destination

	def execute(self) -> Path:
		"""Download file from URL."""
		self.update_status(f"Downloading from {self.url}...")

		def download_progress(block_num: int, block_size: int, total_size: int) -> None:
			if not self.is_running:
				msg = "Download cancelled"
				raise DownloadCancelledError(msg)

			downloaded = block_num * block_size
			if total_size > 0:
				progress = min(int((downloaded / total_size) * 100), 100)
				self.update_progress(progress)

		try:
			urllib.request.urlretrieve(self.url, self.destination, reporthook=download_progress)
		except (urllib.error.URLError, OSError, PermissionError):
			# Clean up partial download
			if self.destination.exists():
				self.destination.unlink()
			raise
		else:
			self.update_progress(100)
			self.update_status("Download complete")
			return self.destination
