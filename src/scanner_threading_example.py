"""Example demonstrating the scanner threading system usage.

This example shows how to use the expanded threading infrastructure
for the scanner module.
"""
from PySide6.QtCore import Slot
from PySide6.QtWidgets import QApplication, QMainWindow, QVBoxLayout, QWidget, QPushButton, QTextEdit, QProgressBar
import sys

from scanner_threading import ScanWorker, ScannerWorkerSignals
from qt_threading import ThreadManager
from scan_settings import ScanSettings, ScanSetting

class ScannerExample(QMainWindow):
    """Example window demonstrating scanner threading."""
    
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Scanner Threading Example")
        self.thread_manager = ThreadManager()
        self.scan_worker = None
        
        # Create UI
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        
        # Start button
        self.start_button = QPushButton("Start Scan")
        self.start_button.clicked.connect(self.start_scan)
        layout.addWidget(self.start_button)
        
        # Stop button
        self.stop_button = QPushButton("Stop Scan")
        self.stop_button.clicked.connect(self.stop_scan)
        self.stop_button.setEnabled(False)
        layout.addWidget(self.stop_button)
        
        # Progress bar
        self.progress_bar = QProgressBar()
        layout.addWidget(self.progress_bar)
        
        # Output text
        self.output = QTextEdit()
        self.output.setReadOnly(True)
        layout.addWidget(self.output)
        
    def start_scan(self):
        """Start a scan operation."""
        self.output.append("Starting scan...")
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)
        
        # Create scan settings
        scan_settings = ScanSettings()
        scan_settings[ScanSetting.JunkFiles] = True
        scan_settings[ScanSetting.WrongFormat] = True
        scan_settings[ScanSetting.Errors] = True
        
        # Create worker (would normally pass real CMC instance)
        # self.scan_worker = ScanWorker(self.cmc, scan_settings)
        
        # For demo, we'll just show the signal connections:
        """
        # Connect all signals
        self.scan_worker.signals.started.connect(self.on_scan_started)
        self.scan_worker.signals.progress.connect(self.progress_bar.setValue)
        self.scan_worker.signals.progress_text.connect(self.on_progress_text)
        self.scan_worker.signals.scan_stage.connect(self.on_scan_stage)
        self.scan_worker.signals.folder_update.connect(self.on_folder_update)
        self.scan_worker.signals.current_folder.connect(self.on_current_folder)
        self.scan_worker.signals.problems_found.connect(self.on_problems_found)
        self.scan_worker.signals.scan_stats.connect(self.on_scan_stats)
        self.scan_worker.signals.result_ready.connect(self.on_scan_complete)
        self.scan_worker.signals.error_occurred.connect(self.on_scan_error)
        self.scan_worker.signals.finished.connect(self.on_scan_finished)
        
        # Start the worker
        self.thread_manager.start_worker(self.scan_worker, "scanner_thread")
        """
        
        self.output.append("Demo: Would start scan with connected signals")
        self.output.append("Signals available:")
        self.output.append("- started: Scan started")
        self.output.append("- progress: Progress percentage (0-100)")
        self.output.append("- progress_text: Status text updates")
        self.output.append("- scan_stage: Current scan stage")
        self.output.append("- folder_update: Folder list discovered")
        self.output.append("- current_folder: Currently scanning folder")
        self.output.append("- problems_found: Batch of problems found")
        self.output.append("- scan_stats: Statistics update")
        self.output.append("- result_ready: Final results")
        self.output.append("- error_occurred: Error with traceback")
        self.output.append("- finished: Scan completed")
        
    def stop_scan(self):
        """Stop the current scan."""
        self.output.append("Stopping scan...")
        self.thread_manager.stop_all()
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        
    @Slot()
    def on_scan_started(self):
        self.output.append("Scan started!")
        
    @Slot(str)
    def on_progress_text(self, text: str):
        self.output.append(f"Progress: {text}")
        
    @Slot(str)
    def on_scan_stage(self, stage: str):
        self.output.append(f"Stage: {stage}")
        
    @Slot(tuple)
    def on_folder_update(self, folders: tuple):
        self.output.append(f"Discovered {len(folders)} folders")
        
    @Slot(str)
    def on_current_folder(self, folder: str):
        self.output.append(f"Scanning: {folder}")
        
    @Slot(list)
    def on_problems_found(self, problems: list):
        self.output.append(f"Found {len(problems)} problems")
        
    @Slot(dict)
    def on_scan_stats(self, stats: dict):
        self.output.append(f"Stats: {stats}")
        
    @Slot(object)
    def on_scan_complete(self, results: list):
        self.output.append(f"Scan complete! Total problems: {len(results)}")
        
    @Slot(str, str)
    def on_scan_error(self, error: str, traceback: str):
        self.output.append(f"ERROR: {error}")
        self.output.append(f"Traceback: {traceback}")
        
    @Slot()
    def on_scan_finished(self):
        self.output.append("Scan finished!")
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        self.progress_bar.setValue(100)
        
    def closeEvent(self, event):
        """Ensure threads are stopped on close."""
        self.thread_manager.stop_all()
        event.accept()


if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = ScannerExample()
    window.show()
    sys.exit(app.exec())