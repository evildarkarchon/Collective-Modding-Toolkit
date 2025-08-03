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


import stat
from pathlib import Path
from shutil import copy2
from types import MappingProxyType
from typing import TYPE_CHECKING

import requests
from PySide6.QtCore import Qt, QThread, Signal, Slot
from PySide6.QtWidgets import (
	QCheckBox,
	QGridLayout,
	QGroupBox,
	QHBoxLayout,
	QLabel,
	QProgressBar,
	QPushButton,
	QRadioButton,
	QVBoxLayout,
	QWidget,
)

import pyxdelta
from enums import InstallType, LogType, Tab
from globals import *
from qt_logger import QtLogger
from qt_modal_dialogs import AboutDialog, ModalDialogBase
from qt_threading import BaseWorker
from utils import get_crc32, is_file

if TYPE_CHECKING:
	from qt_helpers import CMCheckerInterface


COLOR_OG = "#1E90FF"  # DodgerBlue
COLOR_NG = "#836FFF"  # SlateBlue1
PATCH_URL_BASE = "https://github.com/wxMichael/Collective-Modding-Toolkit/releases/download/delta-patches/"


class DownloadWorker(BaseWorker):
	"""Worker thread for downloading patches."""

	progress = Signal(float)  # Progress percentage
	download_complete = Signal(str, Path, Path)  # url, infile, outfile
	error = Signal(str)
	finished = Signal()  # Add the missing finished signal

	def __init__(self, url: str, infile: Path, outfile: Path) -> None:
		super().__init__()
		self.url = url
		self.infile = infile
		self.outfile = outfile
		self.file_path = Path(Path(url).name)

	def run(self) -> None:
		"""Download the file."""
		try:
			response = requests.get(self.url, timeout=10, stream=True)
			response.raise_for_status()  # Raise HTTPError for bad responses
			total_size = int(response.headers.get("content-length", 0))
			downloaded_size = 0

			with self.file_path.open("wb") as f:
				for data in response.iter_content(chunk_size=1024):
					downloaded_size += len(data)
					f.write(data)
					if total_size > 0:
						self.progress.emit(downloaded_size / total_size * 100)

			self.progress.emit(100)
			self.download_complete.emit(self.url, self.infile, self.outfile)

		except (requests.RequestException, OSError, ValueError) as e:
			self.error.emit(str(e))
		finally:
			self.finished.emit()  # Ensure finished signal is always emitted


class QtDowngrader(ModalDialogBase):
	"""Qt version of the Downgrader dialog."""

	CRCs_game = MappingProxyType({
		"Fallout4.exe": {
			"C6053902": InstallType.OG,
			"C5965A2E": InstallType.NG,
		},
		"Fallout4Launcher.exe": {
			"02445570": InstallType.OG,
			"F6A06FF5": InstallType.NG,
		},
		"steam_api64.dll": {
			"BBD912FC": InstallType.OG,
			"E36E7B4D": InstallType.NG,
		},
	})

	CRCs_ck = MappingProxyType({
		"CreationKit.exe": {
			"0F5C065B": InstallType.OG,
			"481CCE95": InstallType.NG,
		},
		"Tools\\Archive2\\Archive2.exe": {
			"4CDFC7B5": InstallType.OG,
			"71A5240B": InstallType.NG,
		},
		"Tools\\Archive2\\Archive2Interop.dll": {
			"850D36A9": InstallType.OG,
			"EFBE3622": InstallType.NG,
		},
	})

	CRCs_by_type: MappingProxyType[InstallType, list[str]] = MappingProxyType({
		InstallType.OG: [],
		InstallType.NG: [],
	})

	# Build CRCs_by_type
	for CRCs in list(CRCs_game.values()) + list(CRCs_ck.values()):
		for crc, install_type in CRCs.items():
			CRCs_by_type[install_type].append(crc)

	def __init__(self, parent: QWidget, cmc: "CMCheckerInterface") -> None:
		self.current_versions: dict[str, InstallType] = {}
		self.unknown_game = False
		self.unknown_ck = False
		self.version_labels: list[QLabel] = []

		self.download_queue: list[tuple[str, Path, Path]] = []
		self.download_worker: DownloadWorker | None = None
		self.download_thread: QThread | None = None
		self.download_in_progress = False

		super().__init__(parent, cmc, "Downgrader", 600, 400)

		# Get current versions
		self.get_info()

		# Set defaults based on current state
		self.wants_downgrade = self.current_versions.get("Fallout4.exe") == InstallType.OG
		self.keep_backups = cmc.settings.dict["downgrader_keep_backups"]
		self.delete_deltas = cmc.settings.dict["downgrader_delete_deltas"]

	def get_info(self) -> None:
		"""Get current version info for all files."""
		self.unknown_game = False
		self.unknown_ck = False

		for file_name, file_crcs in list(self.CRCs_game.items()) + list(self.CRCs_ck.items()):
			file_path = self.cmc.game.game_path / file_name
			if is_file(file_path):
				crc = get_crc32(file_path)
				self.current_versions[file_name] = file_crcs.get(crc, InstallType.Unknown)
			else:
				self.current_versions[file_name] = InstallType.NotFound

			if self.current_versions[file_name] in {InstallType.Unknown, InstallType.NotFound}:
				if file_name in self.CRCs_game:
					self.unknown_game = True
				else:
					self.unknown_ck = True

	def build_gui(self) -> None:
		"""Build the downgrader GUI."""
		layout = QVBoxLayout(self)

		# Top section with file versions
		top_layout = QHBoxLayout()
		layout.addLayout(top_layout)

		# Game files group
		game_group = QGroupBox("Current Game")
		game_layout = QGridLayout(game_group)
		top_layout.addWidget(game_group)

		# CK files group
		ck_group = QGroupBox("Current Creation Kit")
		ck_layout = QGridLayout(ck_group)
		top_layout.addWidget(ck_group)

		# Store layout references for later use
		self.game_layout = game_layout
		self.ck_layout = ck_layout

		# Options section
		options_layout = QVBoxLayout()
		top_layout.addLayout(options_layout)

		# Desired version
		version_group = QGroupBox("Desired Version")
		version_layout = QVBoxLayout(version_group)

		self.radio_og = QRadioButton("Old-Gen")
		self.radio_ng = QRadioButton("Next-Gen")
		self.radio_og.setChecked(self.wants_downgrade)
		self.radio_ng.setChecked(not self.wants_downgrade)

		version_layout.addWidget(self.radio_og)
		version_layout.addWidget(self.radio_ng)
		options_layout.addWidget(version_group)

		# Options
		options_group = QGroupBox("Options")
		opts_layout = QVBoxLayout(options_group)

		self.check_keep_backups = QCheckBox("Keep Backups")
		self.check_keep_backups.setChecked(self.keep_backups)
		self.check_keep_backups.setToolTip(TOOLTIP_DOWNGRADER_BACKUPS)

		self.check_delete_deltas = QCheckBox("Delete Patches")
		self.check_delete_deltas.setChecked(self.delete_deltas)
		self.check_delete_deltas.setToolTip(TOOLTIP_DOWNGRADER_DELTAS)

		opts_layout.addWidget(self.check_keep_backups)
		opts_layout.addWidget(self.check_delete_deltas)
		options_layout.addWidget(options_group)

		# Buttons
		button_layout = QVBoxLayout()

		self.button_patch = QPushButton("Patch\nAll")
		self.button_patch.setMinimumHeight(60)
		self.button_patch.clicked.connect(self.patch_files)
		button_layout.addWidget(self.button_patch)

		button_about = QPushButton("About")
		button_about.clicked.connect(self.show_about)
		button_layout.addWidget(button_about)

		button_layout.addStretch()
		top_layout.addLayout(button_layout)

		# Populate version labels
		self.populate_version_labels(game_layout, ck_layout)

		# Logger
		self.logger = QtLogger()
		layout.addWidget(self.logger)
		self.logger.log_message(LogType.Info, "Patches will be downloaded and applied as-needed.", skip_logging=True)

		# Progress bar
		self.progress_bar = QProgressBar()
		self.progress_bar.setMaximum(100)
		layout.addWidget(self.progress_bar)

	def populate_version_labels(self, game_layout: QGridLayout, ck_layout: QGridLayout) -> None:
		"""Populate the version labels."""
		# Clear existing labels
		for label in self.version_labels:
			label.deleteLater()
		self.version_labels.clear()

		# Add file names and versions
		game_row = 0
		ck_row = 0

		for file_name, install_type in self.current_versions.items():
			if file_name in self.CRCs_game:
				layout = game_layout
				row = game_row
				game_row += 1
			else:
				layout = ck_layout
				row = ck_row
				ck_row += 1

			# File name label
			name_label = QLabel(f"{Path(file_name).name}:")
			name_label.setAlignment(Qt.AlignmentFlag.AlignRight)
			layout.addWidget(name_label, row, 0)

			# Version label
			version_label = QLabel(install_type.value)

			# Set color
			if install_type == InstallType.NG:
				version_label.setStyleSheet(f"color: {COLOR_NG};")
			elif install_type == InstallType.NotFound:
				version_label.setStyleSheet(f"color: {COLOR_NEUTRAL_1};")
			elif install_type == InstallType.Unknown:
				version_label.setStyleSheet(f"color: {COLOR_BAD};")
			else:  # OG
				version_label.setStyleSheet(f"color: {COLOR_OG};")

			layout.addWidget(version_label, row, 1)
			self.version_labels.append(version_label)

	def show_about(self) -> None:
		"""Show the about dialog."""
		dialog = AboutDialog(self, self.cmc, 500, 300, ABOUT_DOWNGRADING_TITLE, ABOUT_DOWNGRADING)
		dialog.exec()

	def patch_files(self) -> None:
		"""Start the patching process."""
		self.button_patch.setEnabled(False)
		self.logger.clear()

		# Save settings if changed
		settings = self.cmc.settings
		resave = False

		if settings.dict["downgrader_keep_backups"] != self.check_keep_backups.isChecked():
			settings.dict["downgrader_keep_backups"] = self.check_keep_backups.isChecked()
			resave = True

		if settings.dict["downgrader_delete_deltas"] != self.check_delete_deltas.isChecked():
			settings.dict["downgrader_delete_deltas"] = self.check_delete_deltas.isChecked()
			resave = True

		if resave:
			settings.save()

		desired_version = InstallType.OG if self.radio_og.isChecked() else InstallType.NG

		# Clear download queue
		self.download_queue.clear()

		# Process each file
		patch_needed = False
		for file_name, install_type in self.current_versions.items():
			file_path = self.cmc.game.game_path / file_name

			if install_type == desired_version:
				self.logger.log_message(LogType.Info, f"Skipped {file_path.name}: Already {desired_version}.")
				continue
			if install_type == InstallType.NotFound:
				self.logger.log_message(LogType.Info, f"Skipped {file_path.name}: Not Found.")
				continue
			patch_needed = True
			self.patch_file(file_path, desired_version)

		if not patch_needed:
			self.button_patch.setEnabled(True)
		else:
			# Start processing download queue
			self.process_download_queue()

	def patch_file(self, file_path: Path, desired_version: InstallType) -> None:
		"""Queue a file for patching."""
		backup_name_og = f"{file_path.stem}_upgradeBackup{file_path.suffix}"
		backup_name_ng = f"{file_path.stem}_downgradeBackup{file_path.suffix}"

		if desired_version == InstallType.OG:
			patch_direction = "NG-to-OG-"
			backup_file_name_desired = backup_name_og
			backup_file_name_current = backup_name_ng
		else:
			patch_direction = "OG-to-NG-"
			backup_file_name_desired = backup_name_ng
			backup_file_name_current = backup_name_og

		backup_file_path_desired = file_path.with_name(backup_file_name_desired)
		backup_file_path_current = file_path.with_name(backup_file_name_current)

		try:
			# Remove read-only flag if needed
			if file_path.stat().st_file_attributes & stat.FILE_ATTRIBUTE_READONLY:
				file_path.chmod(stat.S_IWRITE)

			# Check existing backups
			if is_file(backup_file_path_current):
				if get_crc32(backup_file_path_current) == get_crc32(file_path):
					file_path.unlink()
				else:
					backup_file_path_current.unlink()

			# Backup current file
			if is_file(file_path):
				file_path.rename(backup_file_path_current)

			# Try to restore from backup
			if is_file(backup_file_path_desired):
				if get_crc32(backup_file_path_desired) in self.CRCs_by_type[desired_version]:
					if self.check_keep_backups.isChecked():
						copy2(backup_file_path_desired, file_path)
					else:
						backup_file_path_desired.replace(file_path)
					self.logger.log_message(LogType.Good, f"Patched {file_path.name}")
				else:
					backup_file_path_desired.unlink()

			# If file doesn't exist, need to download patch
			if not is_file(file_path):
				url = f"{PATCH_URL_BASE}{patch_direction}{file_path.name}.xdelta"
				self.download_queue.append((url, backup_file_path_current, file_path))
			elif not self.check_keep_backups.isChecked():
				backup_file_path_current.unlink()

		except OSError as e:
			self.logger.log_message(LogType.Bad, f"Failed patching {file_path.name}: {e!s}")

	def process_download_queue(self) -> None:
		"""Process the next item in the download queue."""
		if not self.download_queue:
			# Queue empty, refresh and re-enable button
			self.cmc.refresh_tab(Tab.Overview)
			self.get_info()
			# Use stored layout references instead of trying to navigate the layout tree
			if hasattr(self, "game_layout") and hasattr(self, "ck_layout"):
				self.populate_version_labels(self.game_layout, self.ck_layout)
			self.button_patch.setEnabled(True)
			self.processing_data = False
			return

		# Get next download
		url, infile, outfile = self.download_queue.pop(0)
		file_path = Path(Path(url).name)

		if is_file(file_path):
			# File already downloaded, just apply patch
			self.apply_patch(url, infile, outfile)
		else:
			# Start download
			self.processing_data = True
			self.progress_bar.setValue(0)

			# Create thread and worker
			self.download_thread = QThread()
			self.download_worker = DownloadWorker(url, infile, outfile)
			self.download_worker.moveToThread(self.download_thread)

			# Connect signals
			self.download_thread.started.connect(self.download_worker.run)
			self.download_worker.progress.connect(self.on_download_progress)
			self.download_worker.download_complete.connect(self.on_download_complete)
			self.download_worker.error.connect(self.on_download_error)
			self.download_worker.finished.connect(self.download_thread.quit)
			self.download_worker.finished.connect(self.download_worker.deleteLater)
			self.download_thread.finished.connect(self.download_thread.deleteLater)

			# Start the thread
			self.download_thread.start()

	@Slot(float)
	def on_download_progress(self, progress: float) -> None:
		"""Update progress bar."""
		self.progress_bar.setValue(int(progress))

	@Slot(str, Path, Path)
	def on_download_complete(self, url: str, infile: Path, outfile: Path) -> None:
		"""Handle download completion."""
		self.apply_patch(url, infile, outfile)

	@Slot(str)
	def on_download_error(self, error: str) -> None:
		"""Handle download error."""
		self.logger.log_message(LogType.Bad, f"Download failed: {error}")
		self.process_download_queue()

	def apply_patch(self, url: str, infile: Path, outfile: Path) -> None:
		"""Apply a downloaded patch."""
		patch_name = Path(url).name

		try:
			pyxdelta.decode(
				str(infile),
				str(Path(patch_name)),
				str(outfile),
			)
			self.logger.log_message(LogType.Good, f"Patched {outfile.name}")

			# Clean up files if requested
			if not self.check_keep_backups.isChecked():
				infile.unlink()

			if self.check_delete_deltas.isChecked():
				Path(patch_name).unlink()

		except OSError:
			self.logger.log_message(LogType.Bad, f"Failed patching {outfile.name}")

		# Process next item
		self.process_download_queue()
