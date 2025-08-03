"""Test the Qt threading infrastructure."""
import sys
import time
from pathlib import Path

# Add src to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent / "src"))

from PySide6.QtCore import QCoreApplication, QTimer
from qt_threading import BaseWorker, ThreadManager, ProgressTracker


class TestWorker(BaseWorker[int]):
    """Test worker that counts and returns sum."""
    
    def __init__(self, count_to: int):
        super().__init__()
        self.count_to = count_to
        
    def execute(self) -> int:
        total = 0
        for i in range(1, self.count_to + 1):
            if not self.is_running:
                self.update_status("Cancelled")
                return -1
                
            total += i
            progress = int((i / self.count_to) * 100)
            self.update_progress(progress)
            self.update_status(f"Processing {i}/{self.count_to}")
            time.sleep(0.01)  # Simulate work
            
        return total


def test_basic_worker():
    """Test basic worker functionality."""
    print("Testing basic worker...")
    
    app = QCoreApplication([])
    manager = ThreadManager()
    
    # Test results storage
    results = {"value": None, "finished": False, "error": None}
    
    # Create worker
    worker = TestWorker(10)
    
    # Connect signals
    worker.signals.result_ready.connect(lambda r: results.update({"value": r}))
    worker.signals.finished.connect(lambda: results.update({"finished": True}))
    worker.signals.error_occurred.connect(lambda e, tb: results.update({"error": e}))
    worker.signals.progress.connect(lambda p: print(f"  Progress: {p}%"))
    worker.signals.status.connect(lambda s: print(f"  Status: {s}"))
    
    # Start worker
    manager.start_worker(worker, "test_worker")
    
    # Wait for completion (with timeout)
    def check_finished():
        if results["finished"]:
            app.quit()
            
    timer = QTimer()
    timer.timeout.connect(check_finished)
    timer.start(100)
    
    # Timeout after 5 seconds
    QTimer.singleShot(5000, app.quit)
    
    app.exec()
    
    # Check results
    assert results["finished"], "Worker did not finish"
    assert results["value"] == 55, f"Expected 55, got {results['value']}"
    assert results["error"] is None, f"Unexpected error: {results['error']}"
    
    print("✓ Basic worker test passed")


def test_worker_cancellation():
    """Test worker cancellation."""
    print("\nTesting worker cancellation...")
    
    app = QCoreApplication([])
    manager = ThreadManager()
    
    results = {"value": None, "finished": False}
    
    # Create worker that counts to 100
    worker = TestWorker(100)
    
    worker.signals.result_ready.connect(lambda r: results.update({"value": r}))
    worker.signals.finished.connect(lambda: results.update({"finished": True}))
    
    # Start worker
    manager.start_worker(worker, "cancel_test")
    
    # Cancel after 200ms
    QTimer.singleShot(200, lambda: manager.stop_thread("cancel_test", wait=False))
    
    # Wait for completion
    def check_finished():
        if results["finished"]:
            app.quit()
            
    timer = QTimer()
    timer.timeout.connect(check_finished)
    timer.start(100)
    
    QTimer.singleShot(2000, app.quit)
    
    app.exec()
    
    # Check that worker was cancelled (returns -1)
    assert results["finished"], "Worker did not finish after cancellation"
    assert results["value"] == -1, f"Worker was not cancelled properly, got {results['value']}"
    
    print("✓ Worker cancellation test passed")


def test_multiple_workers():
    """Test running multiple workers concurrently."""
    print("\nTesting multiple workers...")
    
    app = QCoreApplication([])
    manager = ThreadManager()
    
    results = []
    finished_count = 0
    
    def on_result(worker_id, result):
        results.append((worker_id, result))
        
    def on_finished():
        nonlocal finished_count
        finished_count += 1
        if finished_count == 3:
            app.quit()
    
    # Start 3 workers
    for i in range(3):
        worker = TestWorker(5 + i * 5)  # Count to 5, 10, 15
        worker.signals.result_ready.connect(lambda r, wid=i: on_result(wid, r))
        worker.signals.finished.connect(on_finished)
        manager.start_worker(worker, f"worker_{i}")
    
    # Timeout after 5 seconds
    QTimer.singleShot(5000, app.quit)
    
    app.exec()
    
    # Check results
    assert len(results) == 3, f"Expected 3 results, got {len(results)}"
    assert finished_count == 3, f"Expected 3 workers to finish, got {finished_count}"
    
    # Sort results by worker ID
    results.sort(key=lambda x: x[0])
    expected = [(0, 15), (1, 55), (2, 120)]  # Sum of 1..5, 1..10, 1..15
    
    for (worker_id, result), (exp_id, exp_result) in zip(results, expected):
        assert worker_id == exp_id, f"Worker ID mismatch"
        assert result == exp_result, f"Worker {worker_id}: expected {exp_result}, got {result}"
    
    print("✓ Multiple workers test passed")


def test_progress_tracker():
    """Test the progress tracker utility."""
    print("\nTesting progress tracker...")
    
    progress_values = []
    
    def track_progress(value):
        progress_values.append(value)
    
    tracker = ProgressTracker(10, track_progress)
    
    # Test increment
    for _ in range(5):
        tracker.increment()
    
    assert progress_values[-1] == 50, f"Expected 50% progress, got {progress_values[-1]}"
    
    # Test set_step
    tracker.set_step(8)
    assert progress_values[-1] == 80, f"Expected 80% progress, got {progress_values[-1]}"
    
    # Test overflow protection
    tracker.increment(5)  # Should cap at 100%
    assert progress_values[-1] == 100, f"Expected 100% progress, got {progress_values[-1]}"
    
    print("✓ Progress tracker test passed")


def main():
    """Run all tests."""
    print("Running Qt threading tests...\n")
    
    try:
        test_basic_worker()
        test_worker_cancellation()
        test_multiple_workers()
        test_progress_tracker()
        
        print("\n✅ All tests passed!")
        
    except AssertionError as e:
        print(f"\n❌ Test failed: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()