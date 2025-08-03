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
from enum import StrEnum

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QWidget, QGroupBox, QRadioButton, QVBoxLayout, 
    QGridLayout, QTabWidget, QButtonGroup
)

from globals import *
from qt_helpers import CMCheckerInterface, CMCTabWidget
from qt_widgets import QtStringVar

logger = logging.getLogger(__name__)


class UpdateMode(StrEnum):
    DontCheck = "none"
    NexusModsOnly = "nexus"
    GitHubOnly = "github"
    GitHubAndNexusMods = "both"


class LogLevel(StrEnum):
    DEBUG = "DEBUG"
    INFO = "INFO"
    ERROR = "ERROR"


class SettingsTab(CMCTabWidget):
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget) -> None:
        super().__init__(cmc, tab_widget, "Settings")
        
        self.sv_setting_update_source = QtStringVar(cmc.settings.dict["update_source"])
        self.sv_setting_log_level = QtStringVar(cmc.settings.dict["log_level"])
    
    def _build_gui(self) -> None:
        # Clear main layout and recreate
        while self.main_layout.count():
            item = self.main_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        
        # Configure grid layout - no stretch so items stay compact
        self.main_layout.setAlignment(Qt.AlignTop | Qt.AlignLeft)
        self.main_layout.setContentsMargins(5, 5, 5, 5)
        self.main_layout.setSpacing(5)
        
        # Radio button groups configuration
        options_radios = {
            "Update Channel": (
                TOOLTIP_UPDATE_SOURCE,
                1,  # column
                0,  # row
                self.sv_setting_update_source,
                "update_source",
                (
                    ("All: GitHub & Nexus Mods", UpdateMode.GitHubAndNexusMods),
                    ("Early: GitHub", UpdateMode.GitHubOnly),
                    ("Stable: Nexus Mods", UpdateMode.NexusModsOnly),
                    ("Never: Don't Check", UpdateMode.DontCheck),
                ),
            ),
            "Log Level": (
                TOOLTIP_LOG_LEVEL,
                2,  # column
                0,  # row
                self.sv_setting_log_level,
                "log_level",
                (
                    ("Debug", LogLevel.DEBUG),
                    ("Info", LogLevel.INFO),
                    ("Error", LogLevel.ERROR),
                ),
            ),
        }
        
        # Create radio button groups
        for name, (tooltip, column, row, var, action, options) in options_radios.items():
            # Create group box (equivalent to LabelFrame)
            group_box = QGroupBox(name)
            group_layout = QVBoxLayout()
            group_layout.setContentsMargins(5, 5, 5, 5)
            group_box.setLayout(group_layout)
            
            # Create button group to manage radio buttons
            button_group = QButtonGroup()
            
            # Create radio buttons
            for text, value in options:
                radio = QRadioButton(text)
                radio.setToolTip(tooltip)
                
                # Set checked state based on current value
                if var.get() == value:
                    radio.setChecked(True)
                
                # Connect change signal
                radio.toggled.connect(
                    lambda checked, s=action, v=value: 
                    self.on_radio_change(s, v) if checked else None
                )
                
                button_group.addButton(radio)
                group_layout.addWidget(radio)
            
            # Add to main layout
            self.main_layout.addWidget(group_box, row, column, Qt.AlignTop)
        
        # Add stretch to push everything to top-left
        self.main_layout.setRowStretch(1, 1)
        self.main_layout.setColumnStretch(0, 1)
    
    def on_radio_change(self, setting: str, value: str) -> None:
        """Handle radio button change."""
        # Update the variable
        if setting == "update_source":
            self.sv_setting_update_source.set(value)
        elif setting == "log_level":
            self.sv_setting_log_level.set(value)
        
        # Save to settings
        self.cmc.settings.dict[setting] = value
        self.cmc.settings.save()