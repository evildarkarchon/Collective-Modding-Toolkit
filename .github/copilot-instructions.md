# Copilot Instructions for Collective Modding Toolkit

## Project Overview

This is a **Fallout 4 mod management toolkit** currently undergoing migration from **Tkinter to PySide6**. The application uses a **dual-architecture approach** where both GUI frameworks coexist during the transition.

### Key Architecture Concepts

**Dual GUI Framework**: Run with `python src/main.py` (Tkinter) or `python src/main.py --qt` (PySide6). The `CMT_USE_QT=1` environment variable also enables Qt mode.

**Tab-Based Inheritance Pattern**: All tabs inherit from base classes:
- Tkinter: `CMCTabFrame` (in `src/helpers.py`) 
- PySide6: `CMCTabWidget` (in `src/qt_helpers.py`)
- Each tab has parallel implementations: `_scanner.py` (Tkinter) and `_scanner_qt.py` (PySide6)

**Interface Protocol**: `CMCheckerInterface` defines the contract between tabs and main controller, ensuring compatibility across both frameworks.

**Thread-Safe Operations**: 
- Tkinter version uses Python threading + queues
- PySide6 version uses `QThread` + signals/slots (see `src/qt_threading.py`)
- Heavy operations (file scanning, patching) must never block the UI thread

## Critical Development Patterns

### Tab Implementation Pattern
When creating new tabs, implement both versions:
```python
# Tkinter version in src/tabs/_newtab.py
class NewTab(CMCTabFrame):
    def __init__(self, cmc: CMCheckerInterface, notebook: ttk.Notebook):
        super().__init__(cmc, notebook, "New Tab")
        
# PySide6 version in src/tabs/_newtab_qt.py  
class NewTab(CMCTabWidget):
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget):
        super().__init__(cmc, tab_widget, "New Tab")
```

### Modal Dialog Pattern
Patcher dialogs inherit from framework-specific base classes:
- `PatcherBase` (Tkinter) in `src/patcher/_base.py`
- `QtPatcherBase` (PySide6) in `src/patcher/_qt_base.py`

Both use the **Template Method Pattern**: implement abstract properties (`about_title`, `files_to_patch`) and methods (`patch_files`, `build_gui_secondary`).

### Threading Best Practices
- **Never** access GUI elements from worker threads
- Use `BaseWorker` class for PySide6 threaded operations
- Scanner tab uses complex threading for file analysis - see `src/scanner_threading.py`
- File patching operations must update progress via signals/queues

## Project-Specific Conventions

**Widget Variable System**: Both frameworks use reactive variables:
- Tkinter: `StringVar`, `IntVar`, etc.
- PySide6: `QtStringVar`, `QtBoolVar` from `src/qt_widgets.py`

**File Dialog Compatibility**: Use `from qt_compat import filedialog` for cross-framework file dialogs.

**Theming**: PySide6 uses custom dark stylesheet in `src/qt_theme.py` to match sv_ttk appearance.

**Settings Management**: `AppSettings` class handles JSON persistence in `src/app_settings.py` - shared across both frameworks.

## Build and Development Workflow

**Dependencies**: Managed via Poetry (`poetry install`). Key deps: PySide6, pywin32, pyxdelta.

**Testing**: Run Qt tests with `python src/test_file_dialogs.py` or individual tab tests.

**Migration Status**: Overview, F4SE, Tools, Settings, About tabs are complete. **Scanner tab needs finishing** (60% complete).

**Packaging**: Uses `poetry-pyinstaller-plugin` - see `pyproject.toml` for build config.

## Integration Points

**Game Detection**: `src/game_info.py` handles Fallout 4 installation discovery and version checking.

**Mod Manager Integration**: `src/mod_manager_info.py` detects MO2/Vortex, handles staging paths.

**Archive Patching**: `src/patcher/` module uses pyxdelta for BA2 version conversion (OG ↔ NG).

**F4SE DLL Scanning**: Complex version compatibility checking in F4SE tab implementations.

## Essential Files for AI Context

- `docs/PYSIDE6_MIGRATION_PLAN.md` - Comprehensive migration roadmap
- `src/qt_compat.py` - Cross-framework compatibility layer
- `src/helpers.py` & `src/qt_helpers.py` - Base classes and protocols
- `src/tabs/_scanner.py` vs `src/tabs/_scanner_qt.py` - Compare implementation patterns
- `docs/WIDGET_MAPPING.md` - Widget conversion reference

When working on this codebase, **always consider both GUI frameworks** and maintain feature parity during the transition period.
