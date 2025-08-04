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


import webbrowser

from PySide6.QtCore import Qt
from PySide6.QtGui import QFont
from PySide6.QtWidgets import QApplication, QFrame, QGridLayout, QLabel, QPushButton, QSizePolicy, QTabWidget, QVBoxLayout

from cmt_globals import *
from qt_helpers import CMCheckerInterface, CMCTabWidget


class AboutTab(CMCTabWidget):
	def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
		super().__init__(cmc, tab_widget, "About")

	def _build_gui(self) -> None:
		# Clear main layout and recreate
		while self.main_layout.count():
			item = self.main_layout.takeAt(0)
			if item.widget():
				item.widget().deleteLater()

		# Configure grid layout
		self.main_layout.setColumnStretch(0, 1)
		self.main_layout.setRowStretch(1, 1)

		# Title label
		title_label = QLabel("\n".join(APP_TITLE.rsplit(maxsplit=1)))
		title_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
		title_font = QFont()
		title_font.setPointSize(14)  # FONT_LARGE equivalent
		title_label.setFont(title_font)
		self.main_layout.addWidget(title_label, 0, 0, Qt.AlignmentFlag.AlignCenter)
		self.main_layout.setContentsMargins(10, 10, 10, 10)

		# Icon label
		icon_label = QLabel()
		icon_pixmap = self.cmc.get_image("images/icon-256.png")
		icon_label.setPixmap(icon_pixmap)
		icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
		icon_label.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Expanding)
		self.main_layout.addWidget(icon_label, 1, 0, Qt.AlignmentFlag.AlignCenter)
		self.main_layout.setVerticalSpacing(20)  # Bottom padding

		# About text frame
		frame_about_text = QFrame()
		frame_layout = QVBoxLayout(frame_about_text)
		frame_layout.setContentsMargins(0, 0, 20, 0)  # Right padding
		self.main_layout.addWidget(frame_about_text, 0, 1, 2, 1)

		# Version and creator info
		info_label = QLabel(
			f"v{APP_VERSION}\n\nCreated by wxMichael for the\nCollective Modding Community\n#cm-toolkit on Discord",
		)
		info_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
		info_font = QFont()
		info_font.setPointSize(10)  # FONT equivalent
		info_label.setFont(info_font)
		frame_layout.addWidget(info_label)
		frame_layout.addSpacing(10)

		# Nexus frame
		frame_nexus = self._create_link_frame("images/logo-nexusmods.png", "Open Link", "Copy Link", NEXUS_LINK)
		frame_layout.addWidget(frame_nexus, alignment=Qt.AlignmentFlag.AlignRight)
		frame_layout.addSpacing(10)

		# Discord frame
		frame_discord = self._create_link_frame("images/logo-discord.png", "Open Invite", "Copy Invite", DISCORD_INVITE)
		frame_layout.addWidget(frame_discord, alignment=Qt.AlignmentFlag.AlignRight)
		frame_layout.addSpacing(10)

		# GitHub frame
		frame_github = self._create_link_frame("images/logo-github.png", "Open Link", "Copy Link", GITHUB_LINK)
		frame_layout.addWidget(frame_github, alignment=Qt.AlignmentFlag.AlignRight)

		# Add stretch to push everything up
		frame_layout.addStretch()

	def _create_link_frame(self, logo_path: str, open_text: str, copy_text: str, url: str) -> QFrame:
		"""Create a frame with logo and link buttons."""
		frame = QFrame()
		layout = QGridLayout(frame)
		layout.setContentsMargins(0, 0, 0, 0)
		layout.setSpacing(5)

		# Logo
		logo_label = QLabel()
		logo_pixmap = self.cmc.get_image(logo_path)
		logo_label.setPixmap(logo_pixmap)
		layout.addWidget(logo_label, 0, 0, 2, 1)

		# Separator (vertical line)
		separator = QFrame()
		separator.setFrameShape(QFrame.Shape.VLine)
		separator.setFrameShadow(QFrame.Shadow.Sunken)
		layout.addWidget(separator, 0, 1, 2, 1)

		# Open button
		open_button = QPushButton(open_text)
		open_button.setFixedWidth(100)  # Approximate width=12
		open_button.clicked.connect(lambda: webbrowser.open(url))
		layout.addWidget(open_button, 0, 2)

		# Copy button
		copy_button = QPushButton(copy_text)
		copy_button.setFixedWidth(100)
		copy_button.clicked.connect(lambda: self._copy_to_clipboard(copy_button, url))
		layout.addWidget(copy_button, 1, 2)

		return frame

	def _copy_to_clipboard(self, button: QPushButton, text: str) -> None:
		"""Copy text to clipboard and update button text temporarily."""
		clipboard = QApplication.clipboard()
		clipboard.setText(text)

		# Update button text temporarily
		original_text = button.text()
		button.setText("Copied!")
		button.setEnabled(False)

		# Reset after 2 seconds
		from PySide6.QtCore import QTimer  # noqa: PLC0415

		QTimer.singleShot(2000, lambda: self._reset_button(button, original_text))

	def _reset_button(self, button: QPushButton, original_text: str) -> None:  # noqa: PLR6301
		"""Reset button to original state."""
		button.setText(original_text)
		button.setEnabled(True)
