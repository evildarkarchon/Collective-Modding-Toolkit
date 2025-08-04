# Copilot Instructions for Collective Modding Toolkit

## Project Overview

This is a **Fallout 4 mod management toolkit** built with **PySide6** for analyzing mod setups, patching game files, and providing automated fixes for common issues. The application features a tab-based interface with powerful threading architecture for non-blocking operations.

### Core Architecture

**Main Application Flow**: `src/main.py` → `CMCheckerQt` (QMainWindow) → Tab widgets inheriting from `CMCTabWidget`

**Tab System**: All tabs inherit from `CMCTabWidget` in `src/qt_helpers.py` and implement the `CMCheckerInterface` protocol for consistent communication with the main controller.

**Threading Model**: Heavy operations use `BaseWorker` classes (from `src/qt_threading.py`) running in `QThread` with signals/slots for progress updates. **Never access GUI elements from worker threads.**

**Reactive Variables**: `QtStringVar`, `QtBoolVar` etc. from `src/qt_widgets.py` provide Tkinter-style reactive bindings that emit Qt signals on value changes.

## Critical Development Patterns

### Tab Implementation Template
```python
# All tabs follow this pattern in src/tabs/*_qt.py
class ExampleTab(CMCTabWidget):
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget):
        super().__init__(cmc, tab_widget, "Tab Title")
        
    def _build_gui(self) -> None:
        # Implement UI construction
        
    def refresh(self) -> None:
        # Implement refresh logic
```

### Threading Pattern for Long Operations
```python
# Worker class pattern from src/qt_threading.py
class ExampleWorker(BaseWorker):
    def execute(self) -> object:
        # Heavy work here, use self.update_progress(percentage)
        return result

# Usage in tabs
worker = ExampleWorker()
worker.signals.result_ready.connect(self.on_result)
worker.signals.progress.connect(self.update_progress_bar)
self.cmc.thread_manager.start_worker(worker)
```

### Autofix System Architecture
The `src/autofix_handlers.py` module implements a registry pattern for automated problem fixes:
- `AUTOFIX_REGISTRY` maps `SolutionType` enums to handler functions
- Each handler takes `ProblemInfo | SimpleProblemInfo` and returns `AutoFixResult`
- Handlers automatically create backups before modifying files
- Batch operations use `BatchAutofixWorker` for non-blocking execution

## Project-Specific Conventions

**Error Handling**: Use `src/qt_error_handler.py` for consistent error dialogs. All exceptions in workers are automatically caught and displayed.

**File Operations**: Always use `pathlib.Path` objects. Windows-specific operations use `pywin32` APIs.

**Game Integration**: 
- `src/game_info_qt.py` handles Fallout 4 detection and version analysis
- `src/mod_manager_info.py` integrates with MO2/Vortex staging directories
- Archive patching uses `pyxdelta` for BA2 format conversion (OG ↔ NG)

**Theming**: Dark theme in `src/qt_theme.py` matches original sv_ttk appearance with custom Cascadia Mono font.

## Development Workflow

**Running**: `poetry run python src/main.py` (Qt is now the default interface)

**Code Quality**: 
```bash
poetry run ruff check src/       # Linting
poetry run ruff format src/      # Formatting  
poetry run mypy src/            # Type checking
poetry run pytest              # Run tests
```

**Building**: Uses `poetry-pyinstaller-plugin` - see `pyproject.toml` for executable generation.

**Dependencies**: Windows-only application. Core deps: PySide6, pyxdelta, pywin32, requests.

## Key Integration Points

**Problem Detection**: Scanner tab analyzes mod files and populates `ProblemInfo` objects with detected issues and suggested `SolutionType` fixes.

**File Analysis**: Complex file scanning uses multiple worker threads coordinated through `ThreadManager` with progress aggregation.

**Modal Operations**: Archive patching and game downgrading use `QDialog` subclasses with embedded progress tracking.

**Settings Persistence**: `AppSettings` class in `src/app_settings.py` handles JSON-based configuration with automatic validation.

## Essential Reference Files

- `src/qt_helpers.py` - Base classes and protocols for tab system
- `src/qt_threading.py` - Worker base classes and thread management
- `src/autofix_handlers.py` - Automated fix system with registry pattern
- `src/enums.py` - All enums including `ProblemType` and `SolutionType`
- `docs/PYSIDE6_MIGRATION_PLAN.md` - Architecture decisions and patterns

**Performance Note**: Scanner operations can process thousands of files - always use progress reporting and ensure operations remain cancellable via `worker.stop()`.
