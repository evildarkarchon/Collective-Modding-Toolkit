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

from PySide6.QtGui import QColor, QFont, QTextCharFormat, QTextCursor
from PySide6.QtWidgets import QTextEdit, QVBoxLayout, QWidget

from enums import LogType
from globals import COLOR_BAD, COLOR_GOOD, COLOR_INFO

logger = logging.getLogger(__name__)


class QtLogger(QWidget):
	"""Qt version of the Logger widget for displaying colored log messages."""

	def __init__(self, parent: QWidget | None = None) -> None:
		super().__init__(parent)

		self._emoji = {
			LogType.Bad: "❌ ",
			LogType.Good: "✅ ",
			LogType.Info: "💭 ",
		}

		self._colors = {
			LogType.Good: QColor(COLOR_GOOD),
			LogType.Bad: QColor(COLOR_BAD),
			LogType.Info: QColor(COLOR_INFO),
		}

		# Create layout
		layout = QVBoxLayout(self)
		layout.setContentsMargins(0, 0, 0, 0)

		# Create text widget
		self._text = QTextEdit()
		self._text.setReadOnly(True)
		self._text.setFont(QFont("Segoe UI", 8))
		self._text.setMinimumHeight(120)
		layout.addWidget(self._text)

	def log_message(self, log_type: LogType, message: str, *, skip_logging: bool = False) -> None:
		"""Log a message with the specified type."""
		if not skip_logging:
			if log_type == LogType.Bad:
				logger.error(message)
			else:
				logger.info(message)

		# Move cursor to end
		cursor = self._text.textCursor()
		cursor.movePosition(QTextCursor.MoveOperation.End)

		# Set format for emoji
		emoji_format = QTextCharFormat()
		emoji_format.setForeground(self._colors[log_type])

		# Insert emoji
		cursor.insertText(self._emoji[log_type], emoji_format)

		# Insert message with normal color
		normal_format = QTextCharFormat()
		cursor.insertText(f"{message}\n", normal_format)

		# Scroll to bottom
		self._text.ensureCursorVisible()

	def clear(self) -> None:
		"""Clear all log messages."""
		self._text.clear()
