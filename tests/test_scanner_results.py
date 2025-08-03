#!/usr/bin/env python3
"""
Test script for Scanner tab result tree population.
Creates mock data to test the populate_results functionality.
"""

import sys
from pathlib import Path

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent / "src"))

from PySide6.QtWidgets import QApplication, QMainWindow, QVBoxLayout, QWidget, QPushButton
from PySide6.QtCore import Qt

from enums import ProblemType
from helpers import ProblemInfo, SimpleProblemInfo
from tabs._scanner_qt import ScannerTab


class MockCMChecker:
    """Mock CMChecker interface for testing."""
    def __init__(self):
        self.game = MockGame()
        self.overview_problems = []
    
    def refresh_tab(self, tab):
        pass
    
    def window(self):
        return QApplication.instance().activeWindow()


class MockGame:
    """Mock game info for testing."""
    def __init__(self):
        self.data_path = Path("C:/Games/Fallout4/Data")
        self.manager = MockManager()
        self.modules_enabled = []


class MockManager:
    """Mock mod manager for testing."""
    def __init__(self):
        self.name = "Vortex"
        self.staging_folder = Path("C:/Vortex/Fallout4/mods")
        self.stage_path = self.staging_folder
        self.profiles_path = Path("C:/Vortex/Fallout4/profiles")
        self.selected_profile = "Default"
        self.overwrite_path = Path("C:/Vortex/Fallout4/overwrite")
        self.executables = {}


def create_test_problems():
    """Create test problem data."""
    problems = []
    
    # Critical errors (red)
    problems.append(ProblemInfo(
        ProblemType.FileNotFound,
        Path("C:/Games/Fallout4/Data/Meshes/missing.nif"),
        Path("Meshes/missing.nif"),
        "TestMod1",
        "Required file is missing",
        "Download the missing file"
    ))
    
    problems.append(ProblemInfo(
        ProblemType.InvalidModule,
        Path("C:/Games/Fallout4/Data/BadPlugin.esp"),
        Path("BadPlugin.esp"),
        "BrokenMod",
        "Module has invalid header",
        "Recreate or remove the module"
    ))
    
    # Warnings (orange)
    problems.append(ProblemInfo(
        ProblemType.LoosePrevis,
        Path("C:/Games/Fallout4/Data/Vis"),
        Path("Vis"),
        "PrevissMod",
        "Loose previs files should be archived",
        "Archive these files"
    ))
    
    problems.append(ProblemInfo(
        ProblemType.F4SEOverride,
        Path("C:/Games/Fallout4/Data/Scripts/F4SE/Actor.pex"),
        Path("Scripts/F4SE/Actor.pex"),
        "OutdatedF4SE",
        "Outdated F4SE script detected",
        "Update F4SE or remove override"
    ))
    
    # Info (blue)
    problems.append(ProblemInfo(
        ProblemType.UnexpectedFormat,
        Path("C:/Games/Fallout4/Data/Sound/fx/weird.mp3"),
        Path("Sound/fx/weird.mp3"),
        "AudioMod",
        "MP3 files should be XWM format",
        "Convert to XWM format"
    ))
    
    # Low priority (neutral)
    problems.append(ProblemInfo(
        ProblemType.JunkFile,
        Path("C:/Games/Fallout4/Data/Thumbs.db"),
        Path("Thumbs.db"),
        "MessyMod",
        "Windows thumbnail cache file",
        "Delete this file"
    ))
    
    problems.append(ProblemInfo(
        ProblemType.JunkFile,
        Path("C:/Games/Fallout4/Data/desktop.ini"),
        Path("desktop.ini"),
        "MessyMod",
        "Windows folder settings file",
        "Delete this file"
    ))
    
    # Simple problem info
    problems.append(SimpleProblemInfo(
        "150 SADD Records from 12 modules",
        "Race Subgraph Record Count",
        "High count of race subgraph records detected",
        "Consider merging or removing some mods",
        file_list=[(25, Path("Mod1.esp")), (15, Path("Mod2.esp"))]
    ))
    
    return problems


class TestWindow(QMainWindow):
    """Test window for Scanner tab."""
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Scanner Tab Test")
        self.setGeometry(100, 100, 800, 600)
        
        # Create central widget
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        
        # Create mock CMChecker
        self.cmc = MockCMChecker()
        
        # Create scanner tab (without notebook)
        self.scanner = ScannerTab(self.cmc, None)
        self.scanner._loaded = True
        self.scanner._build_gui()
        layout.addWidget(self.scanner)
        
        # Add test button
        test_button = QPushButton("Populate Test Results")
        test_button.clicked.connect(self.populate_test_results)
        layout.addWidget(test_button)
    
    def populate_test_results(self):
        """Populate scanner with test results."""
        self.scanner.scan_results = create_test_problems()
        self.scanner.populate_results()
        print(f"Populated {len(self.scanner.scan_results)} test problems")
        print(f"Tree data storage has {len(self.scanner.tree_results_data)} items")


def main():
    """Run the test application."""
    app = QApplication(sys.argv)
    
    # Set dark theme if available
    try:
        from sv_ttk import set_theme
        set_theme("dark")
    except ImportError:
        pass
    
    window = TestWindow()
    window.show()
    
    sys.exit(app.exec())


if __name__ == "__main__":
    main()