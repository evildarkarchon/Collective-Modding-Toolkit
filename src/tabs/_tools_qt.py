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
from collections.abc import Callable

from PySide6.QtCore import Qt
from PySide6.QtGui import QFont
from PySide6.QtWidgets import QGroupBox, QHBoxLayout, QLabel, QPushButton, QTabWidget, QVBoxLayout

from patcher._qt_archives import QtArchivePatcher  # noqa: PLC2701
from qt_downgrader import QtDowngrader
from qt_helpers import CMCheckerInterface, CMCTabWidget
from qt_modal_dialogs import ModalDialogBase


class ToolsTab(CMCTabWidget):
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
        super().__init__(cmc, tab_widget, "Tools")
    
    def add_tool_button(
        self,
        layout: QVBoxLayout,
        text: str,
        action: str | Callable[[], ModalDialogBase] | None = None,
        tooltip: str | None = None,
    ) -> None:
        """Add a tool button to the given layout."""
        # Create horizontal layout for button and info icon
        button_layout = QHBoxLayout()
        button_layout.setContentsMargins(10, 5, 0, 5)
        
        # Create button
        button = QPushButton(text)
        button.setSizePolicy(button.sizePolicy().horizontalPolicy(), button.sizePolicy().verticalPolicy())
        
        if action is None:
            button.setEnabled(False)
        elif isinstance(action, str):
            # URL action
            button.clicked.connect(lambda: webbrowser.open(action))
            if "nexusmods" in action:
                button.setToolTip("View on Nexus Mods")
            elif "github" in action:
                button.setToolTip("View on GitHub")
            else:
                button.setToolTip("Open website")
        else:
            # Function action
            button.clicked.connect(action)
        
        button_layout.addWidget(button, 1)  # Stretch factor 1
        
        # Add info icon if tooltip provided
        if tooltip is not None:
            info_label = QLabel()
            info_pixmap = self.cmc.get_image("images/info-16.png")
            info_label.setPixmap(info_pixmap)
            info_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            info_label.setToolTip(tooltip)
            button_layout.addWidget(info_label)
            button_layout.addSpacing(5)
        
        layout.addLayout(button_layout)
    
    def _build_gui(self) -> None:
        # Clear main layout
        while self.main_layout.count():
            item = self.main_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        
        # Create horizontal layout for columns
        columns_layout = QHBoxLayout()
        columns_layout.setContentsMargins(5, 5, 5, 5)
        columns_layout.setSpacing(5)
        self.main_layout.addLayout(columns_layout, 0, 0)
        
        tool_buttons = {
            "Toolkit Utilities": (
                (
                    "Downgrade Manager",
                    lambda: QtDowngrader(self.window(), self.cmc).exec(),
                ),
                (
                    "Archive Patcher",
                    lambda: QtArchivePatcher(self.window(), self.cmc).exec(),
                ),
            ),
            "Other CM Authors' Tools": (
                (
                    "Bethini Pie",
                    "https://www.nexusmods.com/site/mods/631",
                    "Bethini Pie (Performance INI Editor) makes editing INI config files simple.\nDiscord channel: #bethini-doubleyou-etc",
                ),
                (
                    "CLASSIC Crash Log Scanner",
                    "https://www.nexusmods.com/fallout4/mods/56255",
                    "Scans Buffout crash logs for key indicators of crashes.\nYou can also post crash logs to the CM Discord for assistance.\nDiscord channel: #fo4-crash-logs",
                ),
                (
                    "  Vault-Tec Enhanced\nFaceGen System (VEFS)",
                    "https://www.nexusmods.com/fallout4/mods/86374",
                    "Automates the process of generating FaceGen models and textures with xEdit/CK.\nDiscord channel: #bethini-doubleyou-etc",
                ),
                (
                    "PJM's Precombine/Previs\n    Patching Scripts",
                    "https://www.nexusmods.com/fallout4/mods/69978",
                    "Scripts to find precombine/previs (flickering/occlusion) errors in your mod list, and optionally generate a patch to fix those problems.",
                ),
                (
                    "DDS Texture Scanner",
                    "https://www.nexusmods.com/fallout4/mods/71588",
                    "Sniff out textures that might CTD your game. With BA2 support.\nDiscord channel: #nistonmakemod",
                ),
            ),
            "Other Useful Tools": (
                (
                    "xEdit / FO4Edit",
                    "https://github.com/TES5Edit/TES5Edit#xedit",
                    "Module editor and conflict detector for Bethesda games.\nFO4Edit/SSEEdit are xEdit, renamed to auto-set a game mode.",
                ),
                (
                    "Creation Kit Platform\n   Extended (CKPE)",
                    "https://www.nexusmods.com/fallout4/mods/51165",
                    "Various patches and bug fixes for the Creation Kit to make life easier.",
                ),
                (
                    "Cathedral Assets\nOptimizer (CAO)",
                    "https://www.nexusmods.com/skyrimspecialedition/mods/23316",
                    "An automation tool used to optimize BSAs, meshes, textures and animations.",
                ),
                (
                    "Unpackrr",
                    "https://www.nexusmods.com/fallout4/mods/82082",
                    "Batch unpacks small BA2 files to stay below the limit.",
                ),
                (
                    "BA2 Merging Automation\n     Tool (BMAT)",
                    "https://www.nexusmods.com/fallout4/mods/89306",
                    "Automated BA2 files repackaging and merging.",
                ),
                (
                    "IceStorm's Texture Tools",
                    "https://storage.icestormng-mods.de/s/QG43aExydefeGXy",
                    "Converts textures from various formats into a Fallout 4 compatible format.",
                ),
                (
                    "CapFrameX",
                    "https://www.capframex.com/",
                    "Benchmarking tool - Record FPS, frametime, and sensors; analyse and plot the results.",
                ),
            ),
        }
        
        # Store reference to first column for planned tools
        first_group_box: QGroupBox | None = None
        
        # Create columns
        for i, (column_title, buttons) in enumerate(tool_buttons.items()):
            # Create group box for column
            group_box = QGroupBox(column_title)
            group_layout = QVBoxLayout()
            group_layout.setContentsMargins(5, 5, 5, 5)
            group_box.setLayout(group_layout)
            
            # Add buttons
            for button_info in buttons:
                self.add_tool_button(group_layout, *button_info)  # type: ignore[reportArgumentType]
            
            # Add stretch to push buttons to top
            group_layout.addStretch()
            
            # Add to columns layout with equal stretch
            columns_layout.addWidget(group_box, 1)
            
            # Store reference to first column
            if i == 0:
                first_group_box = group_box
        
        # Add planned tools to first column
        if first_group_box:
            layout = first_group_box.layout()
            # Remove stretch temporarily
            if layout and layout.count() > 0:
                layout.takeAt(layout.count() - 1)
            
            if layout:
                # Add planned tools label
                planned_label = QLabel("Planned Tools:")
                small_font = QFont()
                small_font.setPointSize(8)
                planned_label.setFont(small_font)
                planned_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
                layout.addWidget(planned_label)
                
                # Add planned tool buttons (disabled) - cast to QVBoxLayout
                if isinstance(layout, QVBoxLayout):
                    self.add_tool_button(layout, "File Inspector")
                    self.add_tool_button(layout, "Move CC to\nMod Manager")
                    self.add_tool_button(layout, "Papyrus Script\n   Compiler")
                
                # Re-add stretch if it's a QVBoxLayout
                if isinstance(layout, QVBoxLayout):
                    layout.addStretch()