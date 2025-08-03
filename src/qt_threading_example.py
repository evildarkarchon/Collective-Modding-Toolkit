"""Example usage of Qt threading infrastructure in CMT.

This file demonstrates how to use the threading system in various scenarios.
"""
from __future__ import annotations

import sys
from pathlib import Path

from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, 
    QPushButton, QTextEdit, QProgressBar, QLabel
)
from PySide6.QtCore import Slot

from qt_threading import ThreadManager, BaseWorker
from qt_workers import GameInfoWorker, ModManagerWorker, FileOperationWorker
from game_info import GameInfo


class ExampleWorker(BaseWorker[str]):
    """Simple example worker that counts to 10."""
    
    def execute(self) -> str:
        for i in range(1, 11):
            if not self.is_running:
                return "Cancelled"
                
            self.update_progress(i * 10)
            self.update_status(f"Counting: {i}/10")
            
            # Simulate work
            import time
            time.sleep(0.5)
            
        return "Counting complete!"


class ThreadingExampleWindow(QMainWindow):
    """Example window demonstrating threading usage."""
    
    def __init__(self):
        super().__init__()
        self.thread_manager = ThreadManager()
        self.setup_ui()
        
    def setup_ui(self):
        self.setWindowTitle("Qt Threading Example")
        self.setMinimumSize(600, 400)
        
        # Central widget
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        
        # Status label
        self.status_label = QLabel("Ready")
        layout.addWidget(self.status_label)
        
        # Progress bar
        self.progress_bar = QProgressBar()
        layout.addWidget(self.progress_bar)
        
        # Buttons
        self.simple_button = QPushButton("Run Simple Worker")
        self.simple_button.clicked.connect(self.run_simple_worker)
        layout.addWidget(self.simple_button)
        
        self.game_button = QPushButton("Load Game Info")
        self.game_button.clicked.connect(self.run_game_info_worker)
        layout.addWidget(self.game_button)
        
        self.cancel_button = QPushButton("Cancel All")
        self.cancel_button.clicked.connect(self.cancel_all)
        self.cancel_button.setEnabled(False)
        layout.addWidget(self.cancel_button)
        
        # Output text
        self.output_text = QTextEdit()
        self.output_text.setReadOnly(True)
        layout.addWidget(self.output_text)
        
    def closeEvent(self, event):
        """Clean up threads on close."""
        self.thread_manager.stop_all()
        event.accept()
        
    @Slot()
    def run_simple_worker(self):
        """Run the simple counting worker."""
        worker = ExampleWorker()
        
        # Connect signals
        worker.signals.started.connect(self.on_worker_started)
        worker.signals.progress.connect(self.on_progress)
        worker.signals.status.connect(self.on_status)
        worker.signals.result_ready.connect(self.on_simple_result)
        worker.signals.error_occurred.connect(self.on_error)
        worker.signals.finished.connect(self.on_worker_finished)
        
        # Start worker
        self.thread_manager.start_worker(worker, "simple_worker")
        
    @Slot()
    def run_game_info_worker(self):
        """Run the game info worker."""
        # Create mock game info
        from app_settings import QtStringVar
        install_type_sv = QtStringVar()
        game_path_sv = QtStringVar()
        game_info = GameInfo(install_type_sv, game_path_sv)
        
        worker = GameInfoWorker(game_info)
        
        # Connect signals
        worker.signals.started.connect(self.on_worker_started)
        worker.signals.progress.connect(self.on_progress)
        worker.signals.status.connect(self.on_status)
        worker.signals.result_ready.connect(self.on_game_info_result)
        worker.signals.error_occurred.connect(self.on_error)
        worker.signals.finished.connect(self.on_worker_finished)
        
        # Start worker
        self.thread_manager.start_worker(worker, "game_info_worker")
        
    @Slot()
    def cancel_all(self):
        """Cancel all running workers."""
        self.output_text.append("Cancelling all workers...")
        self.thread_manager.stop_all()
        
    @Slot()
    def on_worker_started(self):
        """Called when any worker starts."""
        self.simple_button.setEnabled(False)
        self.game_button.setEnabled(False)
        self.cancel_button.setEnabled(True)
        self.progress_bar.setValue(0)
        
    @Slot()
    def on_worker_finished(self):
        """Called when any worker finishes."""
        active = self.thread_manager.get_active_threads()
        if not active:
            self.simple_button.setEnabled(True)
            self.game_button.setEnabled(True)
            self.cancel_button.setEnabled(False)
            self.progress_bar.setValue(0)
            
    @Slot(int)
    def on_progress(self, value: int):
        """Update progress bar."""
        self.progress_bar.setValue(value)
        
    @Slot(str)
    def on_status(self, message: str):
        """Update status label."""
        self.status_label.setText(message)
        
    @Slot(object)
    def on_simple_result(self, result: str):
        """Handle simple worker result."""
        self.output_text.append(f"Simple worker result: {result}")
        
    @Slot(object)
    def on_game_info_result(self, result: dict):
        """Handle game info result."""
        self.output_text.append("Game Info Result:")
        self.output_text.append(f"  Found: {result['found']}")
        if result['found']:
            self.output_text.append(f"  Path: {result['path']}")
            self.output_text.append(f"  Version: {result['version']}")
            self.output_text.append(f"  Install Type: {result['install_type']}")
        if result['errors']:
            self.output_text.append(f"  Errors: {', '.join(result['errors'])}")
            
    @Slot(str, str)
    def on_error(self, error: str, traceback: str):
        """Handle worker errors."""
        self.output_text.append(f"ERROR: {error}")
        self.output_text.append("Traceback:")
        self.output_text.append(traceback)


def main():
    """Run the example application."""
    app = QApplication(sys.argv)
    
    window = ThreadingExampleWindow()
    window.show()
    
    sys.exit(app.exec())


if __name__ == "__main__":
    main()