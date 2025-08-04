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
import os
import struct
import sys
from pathlib import Path

from packaging.version import Version
from PySide6.QtCore import Qt
from PySide6.QtGui import QCursor, QFont, QMouseEvent
from PySide6.QtWidgets import (
	QFrame,
	QGridLayout,
	QGroupBox,
	QHBoxLayout,
	QLabel,
	QMessageBox,
	QPushButton,
	QTabWidget,
	QVBoxLayout,
)

import cmt_globals
from enums import CSIDL, ArchiveVersion, InstallType, Magic, ModuleFlag, ProblemType, SolutionType
from helpers import ProblemInfo, SimpleProblemInfo
from patcher._qt_archives import QtArchivePatcher  # noqa: PLC2701
from qt_downgrader import QtDowngrader
from qt_helpers import CMCheckerInterface, CMCTabWidget
from qt_modal_dialogs import AboutDialog, TreeDialog
from utils import (
	exists,
	get_crc32,
	get_environment_path,
	get_file_version,
	is_file,
	ver_to_str,
)

logger = logging.getLogger(__name__)


class OverviewTab(CMCTabWidget):
	def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
		super().__init__(cmc, tab_widget, "Overview")

	def _load(self) -> bool:
		self.cmc.overview_problems.clear()
		self.get_info_binaries()
		self.get_info_modules()
		self.get_info_archives()
		return True

	def refresh(self) -> None:
		self.cmc.overview_problems.clear()
		self.get_info_binaries()
		self.get_info_modules(refresh=True)
		self.get_info_archives()

		# Destroy and rebuild frames
		if hasattr(self, "frame_info_binaries"):
			self.frame_info_binaries.deleteLater()
		if hasattr(self, "frame_info_archives"):
			self.frame_info_archives.deleteLater()
		if hasattr(self, "frame_info_modules"):
			self.frame_info_modules.deleteLater()

		self.build_gui_binaries()
		self.build_gui_archives()
		self.build_gui_modules()

	def _build_gui(self) -> None:
		# Clear main layout
		while self.main_layout.count():
			item = self.main_layout.takeAt(0)
			if item.widget():
				item.widget().deleteLater()

		# Create main vertical layout
		main_vbox = QVBoxLayout()
		main_vbox.setContentsMargins(0, 0, 0, 0)
		self.main_layout.addLayout(main_vbox, 0, 0)

		# Top frame with system info
		frame_top = QFrame()
		top_layout = QGridLayout(frame_top)
		top_layout.setContentsMargins(5, 5, 5, 5)
		top_layout.setColumnStretch(2, 1)
		main_vbox.addWidget(frame_top)

		# Info labels (left column)
		info_label = QLabel("Mod Manager:\nGame Path:\nVersion:\nPC Specs:\n")
		info_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
		info_font = QFont()
		info_font.setPointSize(10)
		info_label.setFont(info_font)
		info_label.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
		top_layout.addWidget(info_label, 0, 0, 4, 1)

		# Mod Manager info
		manager = self.cmc.game.manager
		if manager:
			if manager.name == "Mod Organizer":
				# Info icon for MO2
				label_mod_manager_icon = QLabel()
				icon_pixmap = self.cmc.get_image("images/info-16.png")
				label_mod_manager_icon.setPixmap(icon_pixmap)
				label_mod_manager_icon.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
				label_mod_manager_icon.setToolTip("Detection details")

				max_len = 0
				for key in manager.mo2_settings:
					max_len = max(max_len, len(key))

				def show_manager_details(_: QMouseEvent) -> None:
					dialog = AboutDialog(
						self.window(),
						self.cmc,
						750,
						350,
						"Detected Mod Manager Settings",
						(
							f"EXE: {manager.exe_path}\n"
							f"INI: {manager.ini_path}\n"
							f"Portable: {manager.portable}\n{'Portable.txt: ' + str(manager.portable_txt_path) + chr(10) if manager.portable_txt_path else ''}"
							f"{chr(10).join([f'{k.rjust(max_len)}: {v}' for k, v in manager.mo2_settings.items()])}"
						),
					)
					dialog.exec()

				label_mod_manager_icon.mousePressEvent = show_manager_details

				if self.cmc.pc.os == "Windows 11 24H2" and manager.version <= Version("2.5.2"):
					label_os_icon = QLabel()
					warning_pixmap = self.cmc.get_image("images/warning-16.png")
					label_os_icon.setPixmap(warning_pixmap)
					os_tooltip = (
						"Note: MO2 2.5.2 and earlier has issues on Windows 11 24H2.\n"
						"Python apps such as Wrye Bash and CLASSIC may give errors\n"
						"such as FileNotFound or fail to detect files that are only\n"
						"present in the VFS and not the Data folder."
					)
					label_os_icon.setToolTip(os_tooltip)
					top_layout.addWidget(label_os_icon, 3, 1, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

			else:  # Vortex
				label_mod_manager_icon = QLabel()
				warning_pixmap = self.cmc.get_image("images/warning-16.png")
				label_mod_manager_icon.setPixmap(warning_pixmap)
				tooltip = (
					"Note: Vortex is not yet fully supported.\n"
					"Overview should be accurate but Scanner "
					"will only look in Data and not your staging folders, "
					"so it cannot yet identify the source mod for each issue."
				)
				label_mod_manager_icon.setToolTip(tooltip)

			top_layout.addWidget(label_mod_manager_icon, 0, 1, Qt.AlignmentFlag.AlignLeft)

		# Mod manager text
		label_mod_manager = QLabel(
			f"{manager.name} v{manager.version} [Profile: {manager.selected_profile or 'Unknown'}]" if manager else "Not Found",
		)
		label_mod_manager.setFont(info_font)
		label_mod_manager.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2 if manager else cmt_globals.COLOR_BAD};")
		top_layout.addWidget(label_mod_manager, 0, 2, Qt.AlignmentFlag.AlignLeft)
		if not manager:
			label_mod_manager.setToolTip(cmt_globals.TOOLTIP_NO_MOD_MANAGER)

		# Game path
		label_path = QLabel()
		label_path.setFont(info_font)
		label_path.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
		label_path.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
		self.cmc.game_path_sv.changed.connect(label_path.setText)
		label_path.setText(self.cmc.game_path_sv.get())
		label_path.mousePressEvent = lambda _: os.startfile(self.cmc.game.game_path) # pyright: ignore[reportUnknownLambdaType, reportAttributeAccessIssue]
		label_path.setToolTip(cmt_globals.TOOLTIP_GAME_PATH)
		top_layout.addWidget(label_path, 1, 2, Qt.AlignmentFlag.AlignLeft)

		# Version
		label_version = QLabel()
		label_version.setFont(info_font)
		label_version.setStyleSheet(f"color: {cmt_globals.COLOR_GOOD};")
		self.cmc.install_type_sv.changed.connect(label_version.setText)
		label_version.setText(self.cmc.install_type_sv.get())
		top_layout.addWidget(label_version, 2, 2, Qt.AlignmentFlag.AlignLeft)

		# PC Specs frame
		frame_specs = QFrame()
		specs_layout = QHBoxLayout(frame_specs)
		specs_layout.setContentsMargins(0, 0, 0, 0)
		top_layout.addWidget(frame_specs, 3, 2)

		label_spec1 = QLabel()
		label_spec1.setFont(info_font)
		label_spec1.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
		self.cmc.specs_sv_1.changed.connect(label_spec1.setText)
		label_spec1.setText(self.cmc.specs_sv_1.get())
		specs_layout.addWidget(label_spec1)

		specs_layout.addSpacing(30)

		label_spec2 = QLabel()
		label_spec2.setFont(info_font)
		label_spec2.setStyleSheet(f"color: {cmt_globals.COLOR_NEUTRAL_2};")
		self.cmc.specs_sv_2.changed.connect(label_spec2.setText)
		label_spec2.setText(self.cmc.specs_sv_2.get())
		specs_layout.addWidget(label_spec2)

		specs_layout.addStretch()

		# Refresh button
		button_refresh = QPushButton()
		refresh_pixmap = self.cmc.get_image("images/refresh-32.png")
		button_refresh.setIcon(refresh_pixmap)
		button_refresh.setIconSize(refresh_pixmap.size())
		button_refresh.clicked.connect(self.refresh)
		button_refresh.setToolTip(cmt_globals.TOOLTIP_REFRESH)
		top_layout.addWidget(button_refresh, 0, 3, 2, 1, Qt.AlignmentFlag.AlignRight)

		# Info frames container
		info_container = QFrame()
		info_layout = QHBoxLayout(info_container)
		info_layout.setContentsMargins(0, 0, 0, 0)
		main_vbox.addWidget(info_container, 1)  # Stretch factor 1

		# Build the three info sections
		self.build_gui_binaries()
		self.build_gui_archives()
		self.build_gui_modules()

		info_layout.addWidget(self.frame_info_binaries)
		info_layout.addWidget(self.frame_info_archives)
		info_layout.addWidget(self.frame_info_modules)

	def build_gui_binaries(self) -> None:
		self.frame_info_binaries = QGroupBox("Binaries (EXE/DLL/BIN)")
		binaries_layout = QGridLayout(self.frame_info_binaries)

		# File names column
		file_names = "\n".join([f.rsplit(".", 1)[0] + ":" for f in self.cmc.game.file_info])
		rows = len(self.cmc.game.file_info)

		label_file_names = QLabel(file_names)
		label_file_names.setAlignment(Qt.AlignmentFlag.AlignRight)
		font = QFont()
		font.setPointSize(10)
		label_file_names.setFont(font)
		label_file_names.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
		binaries_layout.addWidget(label_file_names, 0, 0, rows, 1)

		# Address Library label
		label_al_title = QLabel("Address Library:")
		label_al_title.setAlignment(Qt.AlignmentFlag.AlignRight)
		label_al_title.setFont(font)
		label_al_title.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
		binaries_layout.addWidget(label_al_title, rows, 0)

		label_address_library = QLabel(
			"Not Found" if not self.cmc.game.address_library else "Next-Gen" if self.cmc.game.is_fong() else "Old-Gen",
		)
		label_address_library.setFont(font)
		color = cmt_globals.COLOR_GOOD if self.cmc.game.address_library else cmt_globals.COLOR_BAD
		label_address_library.setStyleSheet(f"color: {color};")
		binaries_layout.addWidget(label_address_library, rows, 1)
		if not self.cmc.game.address_library:
			label_address_library.setToolTip(cmt_globals.TOOLTIP_ADDRESS_LIBRARY_MISSING)

		# File version labels
		for i, file_name in enumerate(self.cmc.game.file_info.keys()):
			file_path = self.cmc.game.file_info[file_name]["File"] or Path(file_name)

			match self.cmc.game.file_info[file_name]["InstallType"]:
				case self.cmc.game.install_type:
					color = cmt_globals.COLOR_GOOD
				case InstallType.OG:
					if self.cmc.game.is_fodg():
						color = cmt_globals.COLOR_GOOD
					else:
						color = cmt_globals.COLOR_BAD
						self.cmc.overview_problems.append(
							ProblemInfo(
								ProblemType.WrongVersion,
								file_path,
								Path(file_path.name),
								None,
								"The version of this binary does not match your installed game version.",
								"Use the Downgrade Manager to downgrade your game files.",
							),
						)
				case InstallType.NG:
					if self.cmc.game.is_fong():
						color = cmt_globals.COLOR_GOOD
					else:
						color = cmt_globals.COLOR_BAD
						self.cmc.overview_problems.append(
							ProblemInfo(
								ProblemType.WrongVersion,
								file_path,
								Path(file_path.name),
								None,
								"The version of this binary does not match your installed game version.",
								SolutionType.DeleteFile,
							),
						)
				case _:
					color = cmt_globals.COLOR_NEUTRAL_2

			version_value = self.cmc.game.file_info[file_name]["Version"]
			label_version = QLabel(ver_to_str(version_value) if version_value is not None else "Not Found")
			label_version.setFont(font)
			label_version.setStyleSheet(f"color: {color};")
			binaries_layout.addWidget(label_version, i, 1)

			# Icon if needed
			if color == cmt_globals.COLOR_BAD:
				icon_label = QLabel()
				if self.cmc.game.file_info[file_name]["InstallType"] == InstallType.OG:
					icon_pixmap = self.cmc.get_image("images/downgrade-16.png")
					icon_label.setToolTip(cmt_globals.TOOLTIP_DOWNGRADE)
					icon_label.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
					icon_label.mousePressEvent = lambda _: (QtDowngrader(self.window(), self.cmc).exec(), None)[1]
				else:
					icon_pixmap = self.cmc.get_image("images/warning-16.png")
					icon_label.setToolTip(cmt_globals.TOOLTIP_DELETE_BINARIES)
				icon_label.setPixmap(icon_pixmap)
				binaries_layout.addWidget(icon_label, i, 2, Qt.AlignmentFlag.AlignLeft)

	def build_gui_archives(self) -> None:
		self.frame_info_archives = QGroupBox("Archives (BA2)")
		archives_layout = QGridLayout(self.frame_info_archives)
		archives_layout.setRowStretch(10, 1)  # Push button to bottom
		archives_layout.setColumnStretch(2, 1)

		# Archive info display
		lines_texture: list[str] = []
		lines_main: list[str] = []
		colors_texture: list[str] = []
		colors_main: list[str] = []

		for archive, version in self.cmc.game.archives.items():
			archive_type = archive[-11:-4]
			base_name = archive[:-12]

			if archive_type == "Texture":
				lines_texture.append(f"{base_name}:")
				if version == ArchiveVersion.OG:
					colors_texture.append(cmt_globals.COLOR_GOOD if self.cmc.game.is_fodg() else cmt_globals.COLOR_BAD)
				else:
					colors_texture.append(cmt_globals.COLOR_GOOD if self.cmc.game.is_fong() else cmt_globals.COLOR_BAD)
			else:
				lines_main.append(f"{base_name}:")
				if version == ArchiveVersion.OG:
					colors_main.append(cmt_globals.COLOR_GOOD if self.cmc.game.is_fodg() else cmt_globals.COLOR_BAD)
				else:
					colors_main.append(cmt_globals.COLOR_GOOD if self.cmc.game.is_fong() else cmt_globals.COLOR_BAD)

		# Create labels for archives
		if lines_texture:
			label_textures = QLabel("\n".join(lines_texture))
			label_textures.setAlignment(Qt.AlignmentFlag.AlignRight)
			font = QFont()
			font.setPointSize(10)
			label_textures.setFont(font)
			# Apply mixed colors if needed
			if all(c == colors_texture[0] for c in colors_texture):
				label_textures.setStyleSheet(f"color: {colors_texture[0]};")
			else:
				label_textures.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
			archives_layout.addWidget(label_textures, 0, 0, len(lines_texture), 1)

			# Version labels
			for i, (line, color) in enumerate(zip(lines_texture, colors_texture, strict=False)):
				version_label = QLabel(
					"NG" if self.cmc.game.archives[f"{line[:-1]} - Textures.ba2"] == ArchiveVersion.NG else "OG",
				)
				version_label.setFont(font)
				version_label.setStyleSheet(f"color: {color};")
				archives_layout.addWidget(version_label, i, 1)

		# Patch button
		if any(c == cmt_globals.COLOR_BAD for c in colors_texture + colors_main):
			button_patch = QPushButton("Patch Archives")
			button_patch.clicked.connect(lambda: QtArchivePatcher(self.window(), self.cmc).exec())
			archives_layout.addWidget(button_patch, 10, 0, 1, 3, Qt.AlignmentFlag.AlignBottom)

	def build_gui_modules(self) -> None:
		self.frame_info_modules = QGroupBox("Modules (ESM/ESL/ESP)")
		modules_layout = QGridLayout(self.frame_info_modules)

		font = QFont()
		font.setPointSize(10)

		# Module type labels
		label_module_types = QLabel("Full:\nLight:\nTotal:")
		label_module_types.setAlignment(Qt.AlignmentFlag.AlignRight)
		label_module_types.setFont(font)
		label_module_types.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
		label_module_types.setToolTip(cmt_globals.TOOLTIP_MODULE_TYPES)
		modules_layout.addWidget(label_module_types, 0, 0, 3, 1)

		# Module counts
		modules_layout.addWidget(self._create_label(str(self.cmc.game.module_count_full), font, cmt_globals.COLOR_NEUTRAL_2), 0, 1)
		modules_layout.addWidget(self._create_label(str(self.cmc.game.module_count_light), font, cmt_globals.COLOR_NEUTRAL_2), 1, 1)
		modules_layout.addWidget(
			self._create_label(
				str(self.cmc.game.module_count_full + self.cmc.game.module_count_light), font, cmt_globals.COLOR_NEUTRAL_2,
			),
			2,
			1,
		)

		# Unreadable
		color_unreadable = cmt_globals.COLOR_BAD if self.cmc.game.modules_unreadable else cmt_globals.COLOR_NEUTRAL_1
		label_unreadable = QLabel("Unreadable:")
		label_unreadable.setAlignment(Qt.AlignmentFlag.AlignRight)
		label_unreadable.setFont(font)
		label_unreadable.setStyleSheet(f"color: {color_unreadable};")
		label_unreadable.setToolTip(cmt_globals.TOOLTIP_UNREADABLE)
		modules_layout.addWidget(label_unreadable, 3, 0)
		modules_layout.addWidget(self._create_label(str(self.cmc.game.modules_unreadable), font, color_unreadable), 3, 1)

		# Separator
		separator = QFrame()
		separator.setFrameShape(QFrame.Shape.HLine)
		separator.setFrameShadow(QFrame.Shadow.Sunken)
		modules_layout.addWidget(separator, 4, 0, 1, 3)

		# HEDR versions
		label_hedr_100 = QLabel("HEDR v1.00:")
		label_hedr_100.setAlignment(Qt.AlignmentFlag.AlignRight)
		label_hedr_100.setFont(font)
		label_hedr_100.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
		label_hedr_100.setToolTip(cmt_globals.TOOLTIP_HEDR_100)
		modules_layout.addWidget(label_hedr_100, 5, 0)
		modules_layout.addWidget(self._create_label(str(self.cmc.game.module_count_v1), font, cmt_globals.COLOR_NEUTRAL_2), 5, 1)

		label_hedr_95 = QLabel("HEDR v0.95:")
		label_hedr_95.setAlignment(Qt.AlignmentFlag.AlignRight)
		label_hedr_95.setFont(font)
		label_hedr_95.setStyleSheet(f"color: {cmt_globals.COLOR_DEFAULT};")
		label_hedr_95.setToolTip(cmt_globals.TOOLTIP_HEDR_95)
		modules_layout.addWidget(label_hedr_95, 6, 0)
		modules_layout.addWidget(self._create_label(str(self.cmc.game.modules_hedr_95), font, cmt_globals.COLOR_NEUTRAL_2), 6, 1)

		# HEDR unknown
		color_hedr_unknown = cmt_globals.COLOR_BAD if self.cmc.game.modules_hedr_unknown else cmt_globals.COLOR_NEUTRAL_1
		label_hedr_unknown = QLabel("HEDR v????:")
		label_hedr_unknown.setAlignment(Qt.AlignmentFlag.AlignRight)
		label_hedr_unknown.setFont(font)
		label_hedr_unknown.setStyleSheet(f"color: {color_hedr_unknown};")
		label_hedr_unknown.setToolTip(cmt_globals.TOOLTIP_HEDR_UNKNOWN)
		modules_layout.addWidget(label_hedr_unknown, 7, 0)
		modules_layout.addWidget(self._create_label(str(len(self.cmc.game.modules_hedr_unknown)), font, color_hedr_unknown), 7, 1)

		# Add info icon if there are unknown HEDR modules
		if self.cmc.game.modules_hedr_unknown:
			label_hedr_unknown_icon = QLabel()
			info_pixmap = self.cmc.get_image("images/info-16.png")
			label_hedr_unknown_icon.setPixmap(info_pixmap)
			label_hedr_unknown_icon.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
			label_hedr_unknown_icon.setToolTip("Detection details")

			def show_hedr_details(_: QMouseEvent) -> None:
				dialog = TreeDialog(
					self.window(),
					self.cmc,
					400,
					500,
					"Detected Invalid Module Versions",
					"",
					("HEDR", " Module"),
					[(v, k) for k, v in self.cmc.game.modules_hedr_unknown.items()],
				)
				dialog.exec()

			label_hedr_unknown_icon.mousePressEvent = show_hedr_details
			modules_layout.addWidget(label_hedr_unknown_icon, 7, 2, Qt.AlignmentFlag.AlignLeft)

		# Add stretch
		modules_layout.setRowStretch(8, 1)
		modules_layout.setColumnStretch(2, 1)

	def _create_label(self, text: str, font: QFont, color: str) -> QLabel:  # noqa: PLR6301
		"""Helper to create a styled label."""
		label = QLabel(text)
		label.setFont(font)
		label.setStyleSheet(f"color: {color};")
		return label

	def get_info_binaries(self) -> None:
		"""Get binary file information."""
		logger.debug("Gathering Info: Binaries")
		self.cmc.game.reset_binaries()

		if not self.cmc.game.manager:
			self.cmc.overview_problems.append(
				SimpleProblemInfo(
					Path(sys.argv[0]).name,
					"No Mod Manager",
					"No Mod Manager Detected",
					cmt_globals.TOOLTIP_NO_MOD_MANAGER,
				),
			)

		for file_name in cmt_globals.BASE_FILES:
			file_path = self.cmc.game.game_path / file_name
			if not is_file(file_path):
				self.cmc.game.file_info[file_path.name] = {
					"File": None,
					"Version": None,
					"InstallType": None,
				}
				continue

			if cmt_globals.BASE_FILES[file_name].get("UseHash", False):
				version = get_crc32(file_path)
			else:
				ver = get_file_version(file_path)
				if ver is None and cmt_globals.BASE_FILES[file_name].get("UseHashFallback", False):
					version = get_crc32(file_path)
				else:
					version = ver_to_str(ver) if ver else "NO VERSION"

			self.cmc.game.file_info[file_path.name] = {
				"File": file_path,
				"Version": version,
				"InstallType": cmt_globals.BASE_FILES[file_name]["Versions"].get(version, InstallType.Unknown),
			}

			if file_path.name.lower() == "fallout4.exe":
				self.cmc.game.install_type = self.cmc.game.file_info[file_path.name]["InstallType"] or InstallType.Unknown

	def get_info_modules(self, *, refresh: bool = False) -> None:
		"""Get module file information."""
		logger.debug("Gathering Info: Modules")
		self.cmc.game.reset_modules()

		data_path = self.cmc.game.data_path
		if data_path is None:
			self.cmc.overview_problems.append(
				SimpleProblemInfo(
					"Data",
					ProblemType.FileNotFound,
					"The Data folder was not found in your game install path.",
					SolutionType.VerifyFiles,
				),
			)
			return

		self.cmc.game.modules_enabled = [
			master_path for master in cmt_globals.GAME_MASTERS if exists(master_path := data_path / master)
		]

		ccc_path = self.cmc.game.game_path / "Fallout4.ccc"
		if is_file(ccc_path):
			self.cmc.game.modules_enabled.extend([
				cc_path for cc in ccc_path.read_text("utf-8").splitlines() if is_file(cc_path := data_path / cc)
			])
		else:
			self.cmc.overview_problems.append(
				SimpleProblemInfo(
					"Fallout4.ccc",
					ProblemType.FileNotFound,
					"The CC list file was not found in your game install path.\nThis is used to detect which CC modules/archives may be enabled.",
					SolutionType.VerifyFiles,
				),
			)
			if not refresh:
				QMessageBox.warning(
					self,
					"Warning",
					f"{ccc_path.name} not found.\nCC files may not be detected. Verifying Steam files or reinstalling should fix this.",
				)

		plugins_path = get_environment_path(CSIDL.AppDataLocal) / "Fallout4\\plugins.txt"
		try:
			plugins_content = plugins_path.read_text("utf-8")
		except (PermissionError, FileNotFoundError):
			self.cmc.overview_problems.append(
				SimpleProblemInfo(
					plugins_path.name,
					ProblemType.FileNotFound,
					"plugins.txt was not found.\nThis is used to detect which modules/archives are enabled.",
					"N/A" if self.cmc.game.manager else "Launch this app with your mod manager.",
				),
			)
			if not refresh:
				QMessageBox.warning(
					self,
					"Warning",
					"plugins.txt not found.\nEnable state of plugins can't be detected.\nCounts will reflect all modules/archives in Data, which is likely higher than your actual counts.",
				)
			current_plugins = self.cmc.game.modules_enabled.copy()
			self.cmc.game.modules_enabled.extend([
				p for p in data_path.iterdir() if p.suffix.lower() in {".esp", ".esl", ".esm"} and p not in current_plugins
			])
		else:
			self.cmc.game.modules_enabled.extend([
				plugin_path
				for plugin in plugins_content.splitlines()
				if plugin.startswith("*") and is_file(plugin_path := data_path / plugin[1:])
			])

		for module_path in self.cmc.game.modules_enabled:
			try:
				with module_path.open("rb") as f:
					head = f.read(34)
			except (PermissionError, FileNotFoundError):
				self.cmc.game.modules_unreadable.add(module_path)
				self.cmc.overview_problems.append(
					ProblemInfo(
						ProblemType.InvalidModule,
						module_path,
						Path(module_path.name),
						"OVERVIEW",
						"Failed to read module due to permissions or the file is missing.",
						None,
					),
				)
				continue

			if len(head) < 34:
				self.cmc.game.modules_unreadable.add(module_path)
				self.cmc.overview_problems.append(
					ProblemInfo(
						ProblemType.InvalidModule,
						module_path,
						Path(module_path.name),
						"OVERVIEW",
						"Module too small. Expected at least 34 bytes.",
						None,
					),
				)
				continue

			tes4_header = struct.unpack("<4s I HHIH", head[:24])
			if tes4_header[0] != Magic.TES4:
				self.cmc.game.modules_unreadable.add(module_path)
				self.cmc.overview_problems.append(
					ProblemInfo(
						ProblemType.InvalidModule,
						module_path,
						Path(module_path.name),
						"OVERVIEW",
						f"TES4 header magic invalid. Expected {Magic.TES4.value}, got {tes4_header[0]}",
						None,
					),
				)
				continue

			flags = ModuleFlag(tes4_header[2])
			if flags & ModuleFlag.Light or module_path.suffix.lower() == ".esl":
				self.cmc.game.module_count_light += 1
			else:
				self.cmc.game.module_count_full += 1

			hedr_version = struct.unpack("<f", head[30:34])[0]
			if hedr_version == 1.0:
				self.cmc.game.module_count_v1 += 1
			elif hedr_version == 0.95:
				self.cmc.game.modules_hedr_95.add(module_path)
			else:
				self.cmc.game.modules_hedr_unknown[module_path] = hedr_version

	def get_info_archives(self) -> None:
		"""Get archive file information."""
		logger.debug("Gathering Info: Archives")
		self.cmc.game.reset_archives()

		if self.cmc.game.data_path is None:
			# Reported to Scanner in get_info_modules.
			return

		settings_archive_lists = (
			"sresourceindexfilelist",
			"sresourcestartuparchivelist",
			"sresourcearchivelist",
			"sresourcearchivelist2",
		)

		ini_archive = self.cmc.game.game_settings.get("archive")
		if ini_archive is None:
			msg = "Archive section missing from INIs"
			raise ValueError(msg)

		self.cmc.game.archives_enabled = {
			archive_path
			for archive_list in settings_archive_lists
			for n in ini_archive.get(archive_list, "").split(",")
			if is_file(archive_path := self.cmc.game.data_path / n.strip())
		}

		self.cmc.game.archives_enabled.update({
			ps
			for p in self.cmc.game.modules_enabled
			for s in self.cmc.game.ba2_suffixes
			if is_file(ps := p.with_name(f"{p.stem} - {s}.ba2"))
		})

		if self.cmc.game.game_prefs.get("nvflex", {}).get("bnvflexenable", "0") == "1":
			flex_ba2_path = self.cmc.game.data_path / "Fallout4 - Nvflex.ba2"
			if is_file(flex_ba2_path):
				self.cmc.game.archives_enabled.add(flex_ba2_path)
			else:
				self.cmc.overview_problems.append(
					SimpleProblemInfo(
						"Fallout4 - Nvflex.ba2",
						ProblemType.FileNotFound,
						"Nvidia Flex is enabled in your game INIs (bNVFlexEnable=1) but the Nvflex BA2 is missing.",
						SolutionType.VerifyFiles,
					),
				)

		for ba2_file in self.cmc.game.archives_enabled:
			try:
				with ba2_file.open("rb") as f:
					head = f.read(8)
			except (PermissionError, FileNotFoundError):
				continue

			if head == Magic.BTDX:
				self.cmc.game.archives[ba2_file.stem] = ArchiveVersion.OG
			elif head == Magic.DX10:
				self.cmc.game.archives[ba2_file.stem] = ArchiveVersion.NG
			else:
				self.cmc.overview_problems.append(
					SimpleProblemInfo(
						ba2_file.name,
						"Unknown",
						f"Invalid archive header. Expected {Magic.BTDX} or {Magic.DX10}, got {head}",
						"???",
					),
				)
