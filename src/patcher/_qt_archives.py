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
import stat
from pathlib import Path
from typing import TYPE_CHECKING, final

from PySide6.QtWidgets import (
    QGroupBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QRadioButton,
    QVBoxLayout,
    QWidget,
)

from cmt_globals import (
    ABOUT_ARCHIVES,
    ABOUT_ARCHIVES_TITLE,
    COLOR_NEUTRAL_2,
    PATCHER_FILTER_NG,
    PATCHER_FILTER_OG,
)
from enums import ArchiveVersion, LogType, Magic

from ._qt_base import QtPatcherBase

if TYPE_CHECKING:
    from qt_helpers import CMCheckerInterface

logger = logging.getLogger()


@final
class QtArchivePatcher(QtPatcherBase):
    """Qt version of the Archive Patcher dialog."""
    
    def __init__(self, parent: QWidget, cmc: "CMCheckerInterface") -> None:
        self.desired_version = ArchiveVersion.OG
        super().__init__(parent, cmc, "Archive Patcher")
    
    @property
    def about_title(self) -> str:
        return ABOUT_ARCHIVES_TITLE
    
    @property
    def about_text(self) -> str:
        return ABOUT_ARCHIVES
    
    @property
    def filter_text(self) -> str:
        if self.desired_version == ArchiveVersion.OG:
            return PATCHER_FILTER_NG
        return PATCHER_FILTER_OG
    
    @property
    def files_to_patch(self) -> set[Path]:
        files = self.cmc.game.archives_ng if self.desired_version == ArchiveVersion.OG else self.cmc.game.archives_og
        if not self.name_filter:
            return files
        return {file for file in files if self.name_filter in file.name.casefold()}
    
    def build_gui_secondary(self) -> None:
        """Build archive patcher specific GUI elements."""
        # Desired version radio group
        version_group = QGroupBox("Desired Version")
        version_layout = QHBoxLayout(version_group)
        
        self.radio_og = QRadioButton("v1 (OG)")
        self.radio_ng = QRadioButton("v8 (NG)")
        self.radio_og.setChecked(True)
        self.radio_og.toggled.connect(self.on_radio_change)
        
        version_layout.addWidget(self.radio_og)
        version_layout.addWidget(self.radio_ng)
        
        self.top_layout.insertWidget(0, version_group)
        
        # Filter label
        self.label_filter = QLabel(self.filter_text)
        self.label_filter.setStyleSheet(f"color: {COLOR_NEUTRAL_2};")
        self.top_layout.insertWidget(1, self.label_filter)
        
        # Name filter
        filter_widget = QWidget()
        filter_layout = QHBoxLayout(filter_widget)
        filter_layout.setContentsMargins(0, 0, 0, 0)
        
        filter_label = QLabel("Name Filter:")
        self.text_filter = QLineEdit()
        self.text_filter.textChanged.connect(self.on_filter_changed)
        
        filter_layout.addWidget(filter_label)
        filter_layout.addWidget(self.text_filter)
        filter_layout.addStretch()
        
        # Insert filter widget after top widget
        # Cast to QVBoxLayout since we know the layout type from the base class
        main_layout = self.layout()
        if isinstance(main_layout, QVBoxLayout):
            main_layout.insertWidget(1, filter_widget)
    
    def on_radio_change(self) -> None:
        """Handle radio button changes."""
        self.desired_version = ArchiveVersion.OG if self.radio_og.isChecked() else ArchiveVersion.NG
        self.logger.clear()
        self.label_filter.setText(self.filter_text)
        self.populate_tree()
    
    def on_filter_changed(self, text: str) -> None:
        """Handle filter text changes."""
        self.name_filter = text.casefold() if text else None
        self.logger.clear()
        self.populate_tree()
    
    def patch_files(self) -> None:
        """Patch the archive files."""
        patched = 0
        failed = 0
        
        if self.desired_version == ArchiveVersion.OG:
            old_bytes = [b"\x07", b"\x08"]
            new_bytes = b"\x01"
        else:
            old_bytes = [b"\x01"]
            new_bytes = b"\x08"
        
        files_to_patch = list(self.files_to_patch)
        logger.info("Files: %s | Version: %s | Filter: %s", 
                   len(files_to_patch), self.desired_version, self.name_filter)
        
        if not files_to_patch:
            self.logger.log_message(LogType.Info, "Nothing to do!", skip_logging=True)
            return
        
        for ba2_file in files_to_patch:
            try:
                # Remove read-only flag if needed
                if ba2_file.stat().st_file_attributes & stat.FILE_ATTRIBUTE_READONLY: # pyright: ignore[reportAttributeAccessIssue]
                    ba2_file.chmod(stat.S_IWRITE)
                    logger.info("Removed read-only flag: %s", ba2_file.name)
                
                with ba2_file.open("r+b") as f:
                    # Check file format
                    if f.read(4) != Magic.BTDX:
                        self.logger.log_message(LogType.Bad, f"Unrecognized format: {ba2_file.name}")
                        failed += 1
                        continue
                    
                    # Check current version
                    f.seek(4)
                    current_bytes = f.read(1)
                    
                    if current_bytes == new_bytes:
                        self.logger.log_message(LogType.Bad, f"Skipping already-patched archive: {ba2_file.name}")
                        failed += 1
                        continue
                    
                    if current_bytes not in old_bytes:
                        self.logger.log_message(
                            LogType.Bad,
                            f"Unrecognized version [{current_bytes.hex()}]: {ba2_file.name}",
                        )
                        failed += 1
                        continue
                    
                    # Patch the file
                    f.seek(4)
                    f.write(new_bytes)
                    
            except FileNotFoundError:
                self.logger.log_message(LogType.Bad, f"Failed patching (File Not Found): {ba2_file.name}")
                failed += 1
                
            except PermissionError:
                self.logger.log_message(LogType.Bad, f"Failed patching (Permissions/In-Use): {ba2_file.name}")
                failed += 1
                
            except OSError:
                self.logger.log_message(LogType.Bad, f"Failed patching (Unknown OS Error): {ba2_file.name}")
                failed += 1
                
            else:
                self.logger.log_message(LogType.Good, f"Patched to v{self.desired_version}: {ba2_file.name}")
                patched += 1
        
        self.logger.log_message(LogType.Info, f"Patching complete. {patched} Successful, {failed} Failed.")