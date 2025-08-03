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


from abc import ABC, abstractmethod
from pathlib import Path
from typing import Optional, TYPE_CHECKING

from PySide6.QtCore import Qt, QSize
from PySide6.QtWidgets import (
    QDialog, QLabel, QPushButton, QVBoxLayout, QHBoxLayout,
    QTreeWidget, QTreeWidgetItem, QTextEdit, QDialogButtonBox,
    QSizePolicy, QWidget
)
from PySide6.QtGui import QFont

if TYPE_CHECKING:
    from qt_helpers import CMCheckerInterface


class ModalDialogBase(QDialog, ABC):
    """Base class for modal dialogs."""
    
    def __init__(self, parent: QWidget, cmc: "CMCheckerInterface", 
                 window_title: str, width: int, height: int) -> None:
        super().__init__(parent)
        self.cmc = cmc
        self._window_title = window_title
        self.width = width
        self.height = height
        self.processing_data = False
        
        self.setup_window()
        self.build_gui()
    
    def setup_window(self) -> None:
        """Configure the dialog window."""
        self.setWindowTitle(self._window_title)
        self.setModal(True)
        self.setMinimumSize(self.width, self.height)
        self.resize(self.width, self.height)
        
        # Center on parent
        if self.parent():
            parent_rect = self.parent().geometry()
            x = parent_rect.center().x() - self.width // 2
            y = parent_rect.center().y() - self.height // 2
            self.move(x, y)
    
    @abstractmethod
    def build_gui(self) -> None:
        """Build the dialog GUI. Must be implemented by subclasses."""
        pass
    
    def closeEvent(self, event):
        """Handle close event."""
        if self.processing_data:
            event.ignore()
        else:
            event.accept()


class AboutDialog(ModalDialogBase):
    """Simple about/information dialog."""
    
    def __init__(self, parent: QWidget, cmc: "CMCheckerInterface",
                 width: int, height: int, title: str, text: str) -> None:
        self.about_text = text
        super().__init__(parent, cmc, title, width, height)
    
    def build_gui(self) -> None:
        """Build the about dialog GUI."""
        layout = QVBoxLayout(self)
        
        # Text display
        text_edit = QTextEdit()
        text_edit.setPlainText(self.about_text)
        text_edit.setReadOnly(True)
        text_edit.setFont(QFont("Segoe UI", 9))
        layout.addWidget(text_edit)
        
        # Close button
        button_box = QDialogButtonBox(QDialogButtonBox.Close)
        button_box.rejected.connect(self.close)
        layout.addWidget(button_box)
        
        # Allow closing with Escape
        self.setWindowFlag(Qt.WindowCloseButtonHint, True)


class TreeDialog(ModalDialogBase):
    """Dialog displaying items in a tree widget."""
    
    def __init__(self, parent: QWidget, cmc: "CMCheckerInterface",
                 width: int, height: int, title: str, text: str,
                 headers: tuple[str, str],
                 items: Optional[list[tuple]] = None) -> None:
        self.info_text = text
        self.headers = headers
        self.items = items or []
        super().__init__(parent, cmc, title, width, height)
    
    def build_gui(self) -> None:
        """Build the tree dialog GUI."""
        layout = QVBoxLayout(self)
        
        # Info label
        if self.info_text:
            info_label = QLabel(self.info_text)
            info_label.setWordWrap(True)
            info_label.setFont(QFont("Segoe UI", 9))
            layout.addWidget(info_label)
        
        # Tree widget
        self.tree = QTreeWidget()
        self.tree.setHeaderLabels(list(self.headers))
        self.tree.setAlternatingRowColors(True)
        layout.addWidget(self.tree)
        
        # Populate tree
        self.populate_tree()
        
        # Close button
        button_box = QDialogButtonBox(QDialogButtonBox.Close)
        button_box.rejected.connect(self.close)
        layout.addWidget(button_box)
    
    def populate_tree(self) -> None:
        """Populate the tree with items."""
        if not self.items:
            item = QTreeWidgetItem(self.tree)
            item.setText(0, "No items to display.")
            return
        
        # Sort items by first column (assuming numeric/string)
        sorted_items = sorted(self.items, key=lambda x: x[0], reverse=True)
        
        for item_data in sorted_items:
            item = QTreeWidgetItem(self.tree)
            item.setText(0, str(item_data[0]))
            
            # Handle Path objects specially
            if len(item_data) > 1:
                if isinstance(item_data[1], Path):
                    item.setText(1, item_data[1].name)
                else:
                    item.setText(1, str(item_data[1]))
        
        # Adjust column widths
        for i in range(self.tree.columnCount()):
            self.tree.resizeColumnToContents(i)


# Convenience function for showing simple message boxes
def show_info_dialog(parent: QWidget, title: str, message: str) -> None:
    """Show a simple information dialog."""
    from PySide6.QtWidgets import QMessageBox
    QMessageBox.information(parent, title, message)


def show_error_dialog(parent: QWidget, title: str, message: str) -> None:
    """Show an error dialog."""
    from PySide6.QtWidgets import QMessageBox
    QMessageBox.critical(parent, title, message)


def show_warning_dialog(parent: QWidget, title: str, message: str) -> None:
    """Show a warning dialog."""
    from PySide6.QtWidgets import QMessageBox
    QMessageBox.warning(parent, title, message)


def show_question_dialog(parent: QWidget, title: str, message: str) -> bool:
    """Show a yes/no question dialog. Returns True if Yes was clicked."""
    from PySide6.QtWidgets import QMessageBox
    reply = QMessageBox.question(parent, title, message)
    return reply == QMessageBox.Yes