# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architecture Overview

### Core Application Structure
The Collective Modding Toolkit is a Qt-based (PySide6) GUI application for Fallout 4 mod management, built using a tab-based interface pattern.

**Main Components:**
- `src/main.py` - Entry point that initializes logging, loads settings, creates QApplication, and launches CMCheckerQt
- `src/cm_checker_qt.py` - Main application controller using QMainWindow, manages tabs and coordinates between components
- `src/tabs/*_qt.py` - Qt tab implementations (overview, f4se, scanner, tools, settings, about)
- `src/game_info_qt.py` - Handles Fallout 4 game detection, version checking, and file analysis
- `src/mod_manager_info.py` - Detects and interfaces with mod managers (MO2, Vortex)

**Qt Infrastructure:**
- `src/qt_compat.py` - Centralized Qt imports for easy switching between PySide6/PyQt6
- `src/qt_workers.py` - QThread worker implementations for async operations (scanning, downloading, file operations)
- `src/qt_threading.py` - Base worker classes and thread management utilities
- `src/qt_widgets.py` - Custom Qt widgets including QtStringVar for reactive UI updates
- `src/qt_modal_dialogs.py` - Reusable modal dialogs for user interactions
- `src/qt_theme.py` - Dark theme implementation and styling
- `src/qt_downgrader.py` - Qt-based downgrader dialog for game version management
- `src/qt_error_handler.py` - Centralized error handling with Qt dialogs

**Key Patterns:**
- Tabs inherit from QWidget and follow Qt's signal/slot mechanism for communication
- `QtStringVar` provides reactive string variables that emit signals on change
- Heavy operations use `BaseWorker` subclasses running in QThread for non-blocking UI
- Thread safety managed through QMutex and proper signal/slot connections
- Modal operations use QDialog subclasses with proper event handling

### Important Implementation Details

**File Operations:**
- Uses Windows-specific paths and APIs (pywin32) - application is Windows-only
- Archive patching handled by `src/patcher/_qt_*.py` modules using pyxdelta
- F4SE DLL scanning checks for game version compatibility

**Settings Management:**
- Settings stored in `src/settings.json`
- `AppSettings` class handles loading/saving with validation
- Per-scan settings in `src/scan_settings.py`
- Qt widgets bind to settings through QtStringVar for automatic updates

**Threading Architecture:**
- All long-running operations must use Qt workers to avoid blocking the UI
- Workers emit progress signals for UI updates
- ThreadManager handles worker lifecycle and cleanup
- Workers check `is_running` flag for cancellation support

## Development Commands

All commands should be run through Poetry to ensure proper dependency management.

**Running the Application:**
```bash
poetry run python src/main.py --qt
```

**Linting:**
```bash
poetry run ruff check src/
poetry run ruff check tests/
```

**Formatting:**
```bash
poetry run ruff format src/
poetry run ruff format tests/
```

**Type Checking:**
```bash
poetry run mypy src/
poetry run pyright src/
```

**Running Tests:**
```bash
poetry run pytest
poetry run pytest tests/test_qt_threading.py  # Run specific test file
```

**Installing Dependencies:**
```bash
poetry install
poetry install --with win32  # Include Windows-specific dependencies
```

**Building Executable:**
```bash
poetry run pyinstaller --distpath dist --workpath build src/main.spec
```

## Notes for Development

- Application is Windows-only due to Fallout 4 being a Windows game
- Requires Python 3.11-3.13 (configured in pyproject.toml)
- Uses PySide6 for Qt bindings - all Qt imports should go through qt_compat.py
- Follow existing signal/slot patterns for UI reactivity
- Use QtStringVar for binding UI elements to data that needs automatic updates
- Always run long operations in worker threads to keep UI responsive