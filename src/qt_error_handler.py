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
import sys
from typing import TYPE_CHECKING

from PySide6.QtCore import QObject, Qt, QTimer, Signal
from PySide6.QtGui import QFont, QTextCursor
from PySide6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QPlainTextEdit,
    QVBoxLayout,
    QWidget,
)

if TYPE_CHECKING:
    from PySide6.QtWidgets import QMainWindow

logger = logging.getLogger(__name__)


class QtStdErrHandler(QObject):
    """Qt-based stderr handler that displays errors in a popup window."""
    
    error_occurred = Signal(str)
    
    def __init__(self, parent: "QMainWindow") -> None:
        super().__init__(parent)
        self.parent_window = parent
        self.error_dialog: ErrorDialog | None = None
        self.buffer: list[str] = []
        self.flush_timer = QTimer(self)
        self.flush_timer.timeout.connect(self._flush_buffer)
        self.flush_timer.setInterval(100)  # Flush every 100ms
        
        # Connect signal to slot
        self.error_occurred.connect(self._show_error)
        
        # Store the original stderr
        self.original_stderr = sys.stderr
        
    def write(self, text: str) -> int:
        """Write method for stderr replacement."""
        self.buffer.append(text)
        
        # Start flush timer if not already running
        if not self.flush_timer.isActive():
            self.flush_timer.start()
            
        return len(text)
    
    def flush(self) -> None:
        """Flush method for stderr replacement."""
        self._flush_buffer()
        
    def _flush_buffer(self) -> None:
        """Flush the buffer and emit signal if there's content."""
        if self.buffer:
            content = "".join(self.buffer).strip()
            self.buffer.clear()
            
            if content:
                # Log the error
                logger.error("StdErr : %s", content)
                # Emit signal to show error (thread-safe)
                self.error_occurred.emit(content)
                
        # Stop the timer
        self.flush_timer.stop()
    
    def _show_error(self, error_text: str) -> None:
        """Show error in dialog (runs in main thread)."""
        if not self.error_dialog:
            self.error_dialog = ErrorDialog(self.parent_window)
            self.error_dialog.finished.connect(self._on_dialog_closed)
            
        self.error_dialog.append_error(error_text)
        
        if not self.error_dialog.isVisible():
            self.error_dialog.show()
            
    def _on_dialog_closed(self) -> None:
        """Handle dialog close."""
        self.error_dialog = None
        
    def install(self) -> None:
        """Install this handler as sys.stderr."""
        sys.stderr = self  # type: ignore[assignment]
        
    def uninstall(self) -> None:
        """Restore original stderr."""
        sys.stderr = self.original_stderr
        
        # Clean up dialog if open
        if self.error_dialog:
            self.error_dialog.close()
            self.error_dialog = None


class ErrorDialog(QDialog):
    """Dialog window for displaying stderr output."""
    
    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setWindowTitle("An Error Occurred")
        self.setModal(False)  # Non-modal so it doesn't block
        self.resize(800, 400)
        
        # Set window flags to stay on top
        self.setWindowFlags(
            Qt.WindowType.Window | 
            Qt.WindowType.WindowTitleHint | 
            Qt.WindowType.WindowCloseButtonHint |
            Qt.WindowType.WindowStaysOnTopHint
        )
        
        self._setup_ui()
        
    def _setup_ui(self) -> None:
        """Set up the dialog UI."""
        layout = QVBoxLayout(self)
        
        # Text display area
        self.text_display = QPlainTextEdit()
        self.text_display.setReadOnly(True)
        self.text_display.setFont(QFont("Consolas", 9))
        layout.addWidget(self.text_display)
        
        # Buttons
        button_box = QDialogButtonBox(QDialogButtonBox.StandardButton.Close)
        button_box.rejected.connect(self.close)
        
        # Add clear button
        self.clear_button = button_box.addButton(
            "Clear", 
            QDialogButtonBox.ButtonRole.ActionRole
        )
        self.clear_button.clicked.connect(self.clear_errors)
        
        layout.addWidget(button_box)
        
    def append_error(self, error_text: str) -> None:
        """Append error text to the display."""
        # Move cursor to end
        cursor = self.text_display.textCursor()
        cursor.movePosition(QTextCursor.MoveOperation.End)
        self.text_display.setTextCursor(cursor)
        
        # Insert the error text
        if self.text_display.toPlainText():
            self.text_display.insertPlainText("\n" + error_text)
        else:
            self.text_display.insertPlainText(error_text)
            
        # Scroll to bottom
        scrollbar = self.text_display.verticalScrollBar()
        if scrollbar:
            scrollbar.setValue(scrollbar.maximum())
            
    def clear_errors(self) -> None:
        """Clear all error text."""
        self.text_display.clear()


def install_qt_error_handler(parent: "QMainWindow") -> QtStdErrHandler:
    """
    Install a Qt-based stderr handler for the application.
    
    Args:
        parent: The main window to use as parent for the error dialog
        
    Returns:
        The installed error handler instance
    """
    handler = QtStdErrHandler(parent)
    handler.install()
    return handler