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


"""Qt versions of all tabs for the Collective Modding Toolkit."""

from ._about_qt import AboutTab
from ._settings_qt import SettingsTab
from ._overview_qt import OverviewTab
from ._f4se_qt import F4SETab
from ._tools_qt import ToolsTab
from ._scanner_qt import ScannerTab

__all__ = [
    "AboutTab",
    "SettingsTab", 
    "OverviewTab",
    "F4SETab",
    "ToolsTab",
    "ScannerTab",
]