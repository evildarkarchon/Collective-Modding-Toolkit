# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architecture Overview

### Core Application Structure
The Collective Modding Toolkit is a Tkinter-based GUI application for Fallout 4 mod management, built using a tab-based interface pattern.

**Main Components:**
- `src/main.py` - Entry point that initializes logging, loads settings, creates the Tk root window, and launches CMChecker
- `src/cm_checker.py` - Main application controller that manages the GUI window, tabs, and coordinates between components
- `src/tabs/` - Contains individual tab implementations (_overview, _f4se, _scanner, _tools, _settings, _about)
- `src/game_info.py` - Handles Fallout 4 game detection, version checking, and file analysis
- `src/mod_manager_info.py` - Detects and interfaces with mod managers (MO2, Vortex)

**Key Patterns:**
- Each tab inherits from `CMCTabFrame` base class for consistent behavior
- `CMCheckerInterface` protocol defines the interface between tabs and main controller
- Global state is managed through `app_settings.py` and shared via the main CMChecker instance
- Heavy operations use threading to keep UI responsive

### Important Implementation Details

**File Operations:**
- Uses Windows-specific paths and APIs (pywin32)
- Archive patching handled by `src/patcher/` module using pyxdelta
- F4SE DLL scanning checks for game version compatibility

**UI Framework:**
- Custom ttk theme system in `src/sv_ttk/` for dark mode support
- Modal dialogs for operations like downgrading (see `src/downgrader.py`)
- Tooltips throughout UI using tkinter-tooltip

**Settings Management:**
- Settings stored in `src/settings.json`
- `AppSettings` class handles loading/saving with validation
- Per-scan settings in `src/scan_settings.py`

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
```

**Running Tests:**
```bash
poetry run pytest
```

**Installing Dependencies:**
```bash
poetry install
```

## Notes for Development

- Application is Windows-only due to Fallout 4 being a Windows game
- Requires Python 3.11 (configured in pyproject.toml)