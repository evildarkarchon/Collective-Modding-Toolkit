"""Qt-based threading infrastructure for the Collective Modding Toolkit.

This module provides base classes and utilities for thread-safe operations in PySide6.
"""
from __future__ import annotations

import logging
import sys
import traceback
from abc import ABC, abstractmethod
from typing import Any, Callable, Optional, TypeVar, Generic
from functools import wraps

from PySide6.QtCore import QObject, QThread, Signal, Slot, QMutex, QMutexLocker

logger = logging.getLogger(__name__)

T = TypeVar('T')


class WorkerSignals(QObject):
    """Standard signals for worker threads."""
    started = Signal()
    progress = Signal(int)  # Progress percentage (0-100)
    status = Signal(str)    # Status message
    result_ready = Signal(object)  # Result data
    error_occurred = Signal(str, str)  # Error message, traceback
    finished = Signal()
    

class BaseWorker(QObject, ABC, Generic[T]):
    """Base worker class for operations that run in separate threads.
    
    This class provides:
    - Standard signals for communication
    - Built-in exception handling
    - Progress reporting utilities
    - Thread-safe result handling
    """
    
    def __init__(self):
        super().__init__()
        self.signals = WorkerSignals()
        self._is_running = False
        self._mutex = QMutex()
        
    @property
    def is_running(self) -> bool:
        """Thread-safe check if worker is running."""
        with QMutexLocker(self._mutex):
            return self._is_running
            
    def stop(self):
        """Request the worker to stop."""
        with QMutexLocker(self._mutex):
            self._is_running = False
            
    @Slot()
    def run(self):
        """Main worker execution method."""
        try:
            with QMutexLocker(self._mutex):
                self._is_running = True
                
            self.signals.started.emit()
            result = self.execute()
            
            if self.is_running:  # Only emit result if not stopped
                self.signals.result_ready.emit(result)
                
        except Exception as e:
            logger.exception("Worker error")
            self.signals.error_occurred.emit(
                str(e), 
                traceback.format_exc()
            )
        finally:
            with QMutexLocker(self._mutex):
                self._is_running = False
            self.signals.finished.emit()
            
    @abstractmethod
    def execute(self) -> T:
        """Execute the worker's task. Must be implemented by subclasses."""
        pass
        
    def update_progress(self, value: int):
        """Update progress (0-100)."""
        if self.is_running:
            self.signals.progress.emit(value)
            
    def update_status(self, message: str):
        """Update status message."""
        if self.is_running:
            self.signals.status.emit(message)
            

class ThreadManager(QObject):
    """Manages active threads and provides cleanup functionality."""
    
    def __init__(self):
        super().__init__()
        self._threads: dict[str, tuple[QThread, BaseWorker]] = {}
        self._mutex = QMutex()
        
    def start_worker(self, 
                    worker: BaseWorker,
                    thread_name: str,
                    priority: QThread.Priority = QThread.Priority.NormalPriority) -> QThread:
        """Start a worker in a new thread.
        
        Args:
            worker: The worker to run
            thread_name: Unique name for the thread
            priority: Thread priority
            
        Returns:
            The QThread object
        """
        with QMutexLocker(self._mutex):
            # Stop any existing thread with the same name
            if thread_name in self._threads:
                self.stop_thread(thread_name)
                
            # Create and configure thread
            thread = QThread()
            thread.setObjectName(thread_name)
            worker.moveToThread(thread)
            
            # Connect signals
            thread.started.connect(worker.run)
            worker.signals.finished.connect(thread.quit)
            worker.signals.finished.connect(lambda: self._cleanup_thread(thread_name))
            thread.finished.connect(thread.deleteLater)
            
            # Store reference
            self._threads[thread_name] = (thread, worker)
            
            # Start thread
            thread.setPriority(priority)
            thread.start()
            
            logger.debug(f"Started thread: {thread_name}")
            return thread
            
    def stop_thread(self, thread_name: str, wait: bool = True, timeout: int = 5000):
        """Stop a specific thread.
        
        Args:
            thread_name: Name of the thread to stop
            wait: Whether to wait for thread to finish
            timeout: Maximum time to wait in milliseconds
        """
        with QMutexLocker(self._mutex):
            if thread_name not in self._threads:
                return
                
            thread, worker = self._threads[thread_name]
            
        # Request worker to stop
        worker.stop()
        
        if wait and thread.isRunning():
            if not thread.wait(timeout):
                logger.warning(f"Thread {thread_name} did not stop gracefully")
                thread.terminate()
                thread.wait()
                
    def stop_all(self, wait: bool = True, timeout: int = 5000):
        """Stop all managed threads."""
        with QMutexLocker(self._mutex):
            thread_names = list(self._threads.keys())
            
        for name in thread_names:
            self.stop_thread(name, wait, timeout)
            
    def get_active_threads(self) -> list[str]:
        """Get list of active thread names."""
        with QMutexLocker(self._mutex):
            return [name for name, (thread, _) in self._threads.items() 
                   if thread.isRunning()]
            
    @Slot(str)
    def _cleanup_thread(self, thread_name: str):
        """Clean up thread reference after it finishes."""
        with QMutexLocker(self._mutex):
            if thread_name in self._threads:
                del self._threads[thread_name]
                logger.debug(f"Cleaned up thread: {thread_name}")
                

def run_in_thread(thread_manager: ThreadManager, 
                  thread_name: str = None,
                  priority: QThread.Priority = QThread.Priority.NormalPriority):
    """Decorator to run a function in a separate thread.
    
    Usage:
        @run_in_thread(thread_manager, "my_operation")
        def long_running_operation(progress_callback, status_callback):
            for i in range(100):
                progress_callback(i)
                status_callback(f"Processing {i}%")
            return "Result"
    """
    def decorator(func: Callable) -> Callable:
        @wraps(func)
        def wrapper(*args, **kwargs):
            class FunctionWorker(BaseWorker):
                def execute(self):
                    return func(
                        self.update_progress,
                        self.update_status,
                        *args, 
                        **kwargs
                    )
                    
            worker = FunctionWorker()
            name = thread_name or f"{func.__name__}_thread"
            thread_manager.start_worker(worker, name, priority)
            return worker
            
        return wrapper
    return decorator
    

class ProgressTracker:
    """Utility class for tracking progress across multiple operations."""
    
    def __init__(self, total_steps: int, progress_callback: Callable[[int], None]):
        self.total_steps = total_steps
        self.current_step = 0
        self.progress_callback = progress_callback
        self._mutex = QMutex()
        
    def increment(self, steps: int = 1):
        """Increment progress by given number of steps."""
        with QMutexLocker(self._mutex):
            self.current_step = min(self.current_step + steps, self.total_steps)
            percentage = int((self.current_step / self.total_steps) * 100)
            self.progress_callback(percentage)
            
    def set_step(self, step: int):
        """Set current step directly."""
        with QMutexLocker(self._mutex):
            self.current_step = min(step, self.total_steps)
            percentage = int((self.current_step / self.total_steps) * 100)
            self.progress_callback(percentage)