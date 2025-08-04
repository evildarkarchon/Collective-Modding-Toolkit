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
from abc import abstractmethod
from pathlib import Path
from typing import TYPE_CHECKING

from PySide6.QtWidgets import (
    QHBoxLayout,
    QPushButton,
    QTreeWidget,
    QTreeWidgetItem,
    QVBoxLayout,
    QWidget,
)

from cmt_globals import WINDOW_HEIGHT_PATCHER, WINDOW_WIDTH_PATCHER
from enums import LogType, Tab
from qt_logger import QtLogger
from qt_modal_dialogs import AboutDialog, ModalDialogBase

if TYPE_CHECKING:
    from qt_helpers import CMCheckerInterface

logger = logging.getLogger()


class QtPatcherBase(ModalDialogBase):
    """Base class for Qt patcher dialogs."""
    
    def __init__(self, parent: QWidget, cmc: "CMCheckerInterface", window_title: str) -> None:
        self.name_filter: str | None = None
        super().__init__(parent, cmc, window_title, WINDOW_WIDTH_PATCHER, WINDOW_HEIGHT_PATCHER)
        self.populate_tree()
    
    @property
    @abstractmethod
    def about_title(self) -> str:
        """Return the about dialog title."""
    
    @property
    @abstractmethod
    def about_text(self) -> str:
        """Return the about dialog text."""
    
    @property
    @abstractmethod
    def filter_text(self) -> str:
        """Return the filter description text."""
    
    @property
    @abstractmethod
    def files_to_patch(self) -> set[Path]:
        """Return the set of files that need patching."""
    
    @abstractmethod
    def patch_files(self) -> None:
        """Patch the files. Must be implemented by subclasses."""
    
    @abstractmethod
    def build_gui_secondary(self) -> None:
        """Build subclass-specific GUI elements. Must be implemented by subclasses."""
    
    def build_gui(self) -> None:
        """Build the primary GUI structure."""
        layout = QVBoxLayout(self)
        
        # Top section
        self.top_widget = QWidget()
        self.top_layout = QHBoxLayout(self.top_widget)
        layout.addWidget(self.top_widget)
        
        # Tree widget
        self.tree_files = QTreeWidget()
        self.tree_files.setHeaderHidden(True)
        self.tree_files.setAlternatingRowColors(True)
        layout.addWidget(self.tree_files, 1)  # Stretch factor 1
        
        # Logger
        self.logger = QtLogger()
        layout.addWidget(self.logger)
        
        # Add secondary GUI elements (implemented by subclasses)
        self.build_gui_secondary()
        
        # Add common buttons to top layout
        button_patch_all = QPushButton("Patch All")
        button_patch_all.clicked.connect(self._patch_wrapper)
        
        button_about = QPushButton("About")
        button_about.clicked.connect(self.show_about)
        
        # Add spacer before buttons
        self.top_layout.addStretch()
        self.top_layout.addWidget(button_patch_all)
        self.top_layout.addWidget(button_about)
    
    def _patch_wrapper(self) -> None:
        """Wrapper for patch operation with logging."""
        assert self.cmc.game.data_path is not None
        self.processing_data = True
        
        logger.info("Patcher Running: %s", self.__class__.__name__)
        self.patch_files()
        logger.info("Patcher Finished")
        
        self.cmc.refresh_tab(Tab.Overview)
        self.populate_tree()
        self.processing_data = False
    
    def populate_tree(self) -> None:
        """Populate the file tree with files to patch."""
        assert self.cmc.game.data_path is not None
        
        self.tree_files.clear()
        
        files = sorted(self.files_to_patch)
        for file_path in files:
            item = QTreeWidgetItem(self.tree_files)
            item.setText(0, file_path.name)
        
        self.logger.log_message(
            LogType.Info, 
            f"Showing {len(files)} files to be patched.", 
            skip_logging=True,
        )
    
    def show_about(self) -> None:
        """Show the about dialog."""
        dialog = AboutDialog(self, self.cmc, 500, 435, self.about_title, self.about_text)
        dialog.exec()