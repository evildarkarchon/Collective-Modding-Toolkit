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
from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QTreeWidget, QTreeWidgetItem, QTextEdit, QLabel,
    QTabWidget, QHeaderView
)
from PySide6.QtGui import QFont, QColor, QTextCharFormat, QTextCursor

from cmt_globals import (
    ABOUT_F4SE_DLLS,
    COLOR_BAD,
    COLOR_GOOD,
    COLOR_NEUTRAL_1,
    COLOR_NEUTRAL_2,
)
from qt_helpers import CMCheckerInterface, CMCTabWidget
from helpers import DLLInfo
from utils import parse_dll

TAG_NEUTRAL = "neutral"
TAG_GOOD = "good"
TAG_BAD = "bad"
TAG_NOTE = "note"

EMOJI_DLL_UNKNOWN = "\N{BLACK QUESTION MARK ORNAMENT}"
EMOJI_DLL_GOOD = "\N{HEAVY CHECK MARK}"
EMOJI_DLL_BAD = ""
EMOJI_DLL_NOTE = "\N{WARNING SIGN}"

DLL_OGNG_WHITELIST = (
    "AchievementsModsEnablerLoader.dll",
    "BetterConsole.dll",
    "Buffout4.dll",
    "ClockWidget.dll",
    "FloatingDamage.dll",
    "GCBugFix.dll",
    "HUDPlusPlus.dll",
    "IndirectFire.dll",
    "MinimalMinimap.dll",
    "MoonRotationFix.dll",
    "mute_on_focus_loss.dll",
    "SprintStutteringFix.dll",
    "UnlimitedFastTravel.dll",
    "WeaponDebrisCrashFix.dll",
    "x-cell-fo4.dll",
)

logger = logging.getLogger(__name__)


class F4SETab(CMCTabWidget):
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
        super().__init__(cmc, tab_widget, "F4SE")
        self.loading_text = "Scanning DLLs..."
        
        self.dll_info: dict[str, DLLInfo | None] = {}
    
    def _load(self) -> bool:
        if self.cmc.game.data_path is None:
            self.loading_error = "Data folder not found"
            return False
        
        if self.cmc.game.f4se_path is None:
            self.loading_error = "Data/F4SE/Plugins folder not found"
            if not self.cmc.game.manager:
                self.loading_error += "\nTry launching via your mod manager."
            return False
        
        self.dll_info.clear()
        for dll_file in self.cmc.game.f4se_path.iterdir():
            if dll_file.suffix.lower() == ".dll" and not dll_file.name.startswith("msdia"):
                logger.debug("Scanning %s", dll_file.name)
                self.dll_info[dll_file.name] = parse_dll(dll_file)
        
        return True
    
    def _build_gui(self) -> None:
        # Clear main layout
        while self.main_layout.count():
            item = self.main_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        
        # Configure grid layout
        self.main_layout.setColumnStretch(2, 1)
        self.main_layout.setRowStretch(1, 1)
        
        # Create tree widget for DLLs
        self.tree_dlls = QTreeWidget()
        self.tree_dlls.setColumnCount(4)
        self.tree_dlls.setHeaderLabels(["DLL", "OG", "NG", "Your Game"])
        
        # Configure columns
        header = self.tree_dlls.header()
        header.setSectionResizeMode(0, QHeaderView.Interactive)
        header.setSectionResizeMode(1, QHeaderView.Fixed)
        header.setSectionResizeMode(2, QHeaderView.Fixed)
        header.setSectionResizeMode(3, QHeaderView.Fixed)
        
        # Set column widths
        self.tree_dlls.setColumnWidth(0, 300)
        self.tree_dlls.setColumnWidth(1, 60)
        self.tree_dlls.setColumnWidth(2, 60)
        self.tree_dlls.setColumnWidth(3, 80)
        
        # Set column alignments
        self.tree_dlls.headerItem().setTextAlignment(0, Qt.AlignRight)
        self.tree_dlls.headerItem().setTextAlignment(1, Qt.AlignCenter)
        self.tree_dlls.headerItem().setTextAlignment(2, Qt.AlignCenter)
        self.tree_dlls.headerItem().setTextAlignment(3, Qt.AlignCenter)
        
        # Add tree to layout
        self.main_layout.addWidget(self.tree_dlls, 0, 0, 2, 1)
        
        # Title label
        title_label = QLabel("F4SE DLLs")
        title_label.setAlignment(Qt.AlignCenter)
        font = QFont()
        font.setPointSize(10)
        title_label.setFont(font)
        self.main_layout.addWidget(title_label, 0, 2, Qt.AlignTop)
        
        # About text
        self.text_about_f4se = QTextEdit()
        self.text_about_f4se.setReadOnly(True)
        small_font = QFont()
        small_font.setPointSize(8)
        self.text_about_f4se.setFont(small_font)
        self.text_about_f4se.setPlainText(ABOUT_F4SE_DLLS)
        
        # Apply formatting to text
        self._format_about_text()
        
        self.main_layout.addWidget(self.text_about_f4se, 1, 2)
        
        # Populate tree with DLL info
        self._populate_tree()
    
    def _format_about_text(self) -> None:
        """Apply color formatting to the about text."""
        cursor = QTextCursor(self.text_about_f4se.document())
        
        # Define formats
        neutral_format = QTextCharFormat()
        neutral_format.setForeground(QColor(COLOR_NEUTRAL_2))
        
        good_format = QTextCharFormat()
        good_format.setForeground(QColor(COLOR_GOOD))
        
        bad_format = QTextCharFormat()
        bad_format.setForeground(QColor(COLOR_BAD))
        
        note_format = QTextCharFormat()
        note_format.setForeground(QColor("yellow"))
        
        # Apply formats to specific lines
        # Line 2, chars 0-18
        cursor.movePosition(QTextCursor.Start)
        cursor.movePosition(QTextCursor.Down, QTextCursor.MoveAnchor, 1)
        cursor.movePosition(QTextCursor.StartOfLine)
        cursor.movePosition(QTextCursor.Right, QTextCursor.KeepAnchor, 18)
        cursor.setCharFormat(neutral_format)
        
        # Line 6, char 0
        cursor.movePosition(QTextCursor.Start)
        cursor.movePosition(QTextCursor.Down, QTextCursor.MoveAnchor, 5)
        cursor.movePosition(QTextCursor.StartOfLine)
        cursor.movePosition(QTextCursor.Right, QTextCursor.KeepAnchor, 1)
        cursor.setCharFormat(good_format)
        
        # Line 8, char 0
        cursor.movePosition(QTextCursor.Start)
        cursor.movePosition(QTextCursor.Down, QTextCursor.MoveAnchor, 7)
        cursor.movePosition(QTextCursor.StartOfLine)
        cursor.movePosition(QTextCursor.Right, QTextCursor.KeepAnchor, 1)
        cursor.setCharFormat(bad_format)
        
        # Line 10, char 0
        cursor.movePosition(QTextCursor.Start)
        cursor.movePosition(QTextCursor.Down, QTextCursor.MoveAnchor, 9)
        cursor.movePosition(QTextCursor.StartOfLine)
        cursor.movePosition(QTextCursor.Right, QTextCursor.KeepAnchor, 1)
        cursor.setCharFormat(neutral_format)
        
        # Line 14, char 0
        cursor.movePosition(QTextCursor.Start)
        cursor.movePosition(QTextCursor.Down, QTextCursor.MoveAnchor, 13)
        cursor.movePosition(QTextCursor.StartOfLine)
        cursor.movePosition(QTextCursor.Right, QTextCursor.KeepAnchor, 1)
        cursor.setCharFormat(note_format)
    
    def _populate_tree(self) -> None:
        """Populate the tree widget with DLL information."""
        self.tree_dlls.clear()
        
        for dll, info in self.dll_info.items():
            item = QTreeWidgetItem()
            item.setText(0, dll)
            item.setTextAlignment(0, Qt.AlignRight)
            
            if info is None or not info["IsF4SE"]:
                # Unknown DLL
                item.setText(1, EMOJI_DLL_UNKNOWN)
                item.setText(2, EMOJI_DLL_UNKNOWN)
                item.setText(3, EMOJI_DLL_UNKNOWN)
                item.setForeground(0, QColor(COLOR_NEUTRAL_1))
                item.setForeground(1, QColor(COLOR_NEUTRAL_1))
                item.setForeground(2, QColor(COLOR_NEUTRAL_1))
                item.setForeground(3, QColor(COLOR_NEUTRAL_1))
            else:
                # F4SE DLL
                og = EMOJI_DLL_GOOD if info.get("SupportsOG") else EMOJI_DLL_BAD
                ng = EMOJI_DLL_GOOD if info.get("SupportsNG") else EMOJI_DLL_BAD
                
                # Determine status for current game
                cg = (
                    EMOJI_DLL_NOTE
                    if (info.get("SupportsOG") and info.get("SupportsNG"))
                    else EMOJI_DLL_GOOD
                    if (self.cmc.game.is_foog() and info.get("SupportsOG"))
                    or (self.cmc.game.is_fong() and info.get("SupportsNG"))
                    else "\N{CROSS MARK}"
                )
                
                # Check whitelist
                if cg == EMOJI_DLL_NOTE and dll in DLL_OGNG_WHITELIST:
                    cg = EMOJI_DLL_GOOD
                
                item.setText(1, og)
                item.setText(2, ng)
                item.setText(3, cg)
                
                # Center align status columns
                item.setTextAlignment(1, Qt.AlignCenter)
                item.setTextAlignment(2, Qt.AlignCenter)
                item.setTextAlignment(3, Qt.AlignCenter)
                
                # Set colors based on status
                if cg == EMOJI_DLL_NOTE:
                    color = QColor("yellow")
                elif cg == EMOJI_DLL_GOOD:
                    color = QColor(COLOR_GOOD)
                else:
                    color = QColor(COLOR_BAD)
                
                item.setForeground(0, color)
                item.setForeground(1, color)
                item.setForeground(2, color)
                item.setForeground(3, color)
            
            self.tree_dlls.addTopLevelItem(item)