# PySide6 Migration Implementation Plan

## Executive Summary

This document outlines a comprehensive plan for migrating the Collective Modding Toolkit GUI from Tkinter to PySide6, ensuring best practices for threading, signals/slots, layouts, and maintaining UI parity with the existing application.

## Migration Overview

### Current Architecture
- **Framework**: Tkinter + ttk (themed widgets)
- **Main Window**: `CMChecker` class managing tabs via `ttk.Notebook`
- **Tab System**: Inheritance-based with `CMCTabFrame` base class
- **Threading**: Python threading for heavy operations
- **Modal Dialogs**: Custom `ModalWindow` base class
- **Theme**: sv_ttk for dark mode support

### Target Architecture
- **Framework**: PySide6 with Qt Widgets
- **Main Window**: QMainWindow with QTabWidget
- **Tab System**: QWidget-based tabs with consistent interface
- **Threading**: QThread with proper signal/slot communication
- **Modal Dialogs**: QDialog-based with proper modality
- **Theme**: Qt stylesheets or QPalette for dark mode

## Phase 1: Project Setup and Dependencies

### 1.1 Update Dependencies - COMPLETE
```toml
# pyproject.toml modifications
[tool.poetry.dependencies]
PySide6 = "^6.8.0"
# Remove: tkinter-tooltip

[tool.poetry.group.dev.dependencies]
# Add Qt development tools if needed
```

### 1.2 Create Compatibility Layer - COMPLETE
Create a compatibility module to ease transition:
```python
# src/qt_compat.py
from PySide6.QtCore import Qt, Signal, Slot, QThread, QObject
from PySide6.QtWidgets import *
from PySide6.QtGui import QPixmap, QIcon, QFont
```

## Phase 2: Core Infrastructure Migration ✅ COMPLETED

**Completed Items:**
- ✅ Created `cm_checker_qt.py` with Qt-based main window class
- ✅ Updated `main.py` to support both Tkinter and Qt versions (use `--qt` flag or `CMT_USE_QT=1`)
- ✅ Implemented Qt signals for thread communication
- ✅ Created Qt-compatible StringVar class for compatibility
- ✅ Added placeholder tabs for testing
- ✅ Implemented all compatibility methods (after, wm_title, etc.)
- ✅ Verified basic window creation and display

**Usage:** Run `python src/main.py --qt` to test the Qt version

### Custom Theme Implementation ✅ COMPLETED

**Completed Items:**
- ✅ Analyzed sv_ttk dark theme colors (#1c1c1c background, #fafafa foreground, etc.)
- ✅ Created `qt_theme.py` module with comprehensive dark stylesheet
- ✅ Applied theme matching sv_ttk appearance for all widget types
- ✅ Loaded custom Cascadia Mono font to match original
- ✅ Styled update banner to match original green notification style
- ✅ Removed generic Fusion style in favor of custom theme

The Qt version now visually matches the sv_ttk dark theme from the Tkinter version!

### 2.1 Application Entry Point
Transform `src/main.py`:
```python
# New structure
import sys
from PySide6.QtWidgets import QApplication
from PySide6.QtCore import Qt
from cm_checker_qt import CMCheckerQt

app = QApplication(sys.argv)
app.setStyle("Fusion")  # Consistent cross-platform style

# Load settings
settings = AppSettings()

# Create main window
window = CMCheckerQt(settings)
window.show()

sys.exit(app.exec())
```

### 2.2 Main Window Class
Transform `CMChecker` to `CMCheckerQt`:
```python
class CMCheckerQt(QMainWindow):
    # Signals for thread communication
    game_info_updated = Signal()
    processing_started = Signal()
    processing_finished = Signal()
    
    def __init__(self, settings: AppSettings):
        super().__init__()
        self.settings = settings
        self.setup_ui()
        self.setup_threads()
```

## Phase 3: Widget Conversion and Layout Migration Resources ✅ COMPLETED

**Completed Items:**
- ✅ Created comprehensive widget mapping guide (`docs/WIDGET_MAPPING.md`)
- ✅ Developed layout migration examples (`docs/LAYOUT_MIGRATION_EXAMPLES.md`) 
- ✅ Created helper module for widget conversions (`src/qt_widgets.py`)
- ✅ Documented property and method mappings (`docs/PROPERTY_METHOD_MAPPING.md`)
- ✅ Created complex widget conversion examples (`docs/COMPLEX_WIDGET_CONVERSIONS.md`)

### 3.1 Widget Conversion Resources

The following comprehensive documentation has been created:

1. **Widget Mapping Guide** (`docs/WIDGET_MAPPING.md`)
   - Complete widget-by-widget conversion examples
   - Variable system replacements (StringVar → QtStringVar)
   - Event handling patterns
   - Common pitfalls and solutions

2. **Layout Migration Examples** (`docs/LAYOUT_MIGRATION_EXAMPLES.md`)
   - Real-world layout conversion patterns from the CMT codebase
   - Pack → VBox/HBox conversions
   - Grid → QGridLayout conversions
   - Complex nested layout examples

3. **Property and Method Mapping** (`docs/PROPERTY_METHOD_MAPPING.md`)
   - Detailed property mappings for each widget type
   - Method equivalents (get/set, configure, etc.)
   - Event binding conversions
   - Window management methods

4. **Complex Widget Conversions** (`docs/COMPLEX_WIDGET_CONVERSIONS.md`)
   - Treeview → QTreeWidget with full examples
   - Text widget with syntax highlighting
   - Canvas → QGraphicsView conversions
   - Custom scrollable frames
   - Dynamic notebook/tab management

5. **Helper Module** (`src/qt_widgets.py`)
   - QtStringVar, QtIntVar, QtBoolVar, QtDoubleVar classes
   - TkinterStyleDialog base class
   - BindingHelper for event conversions
   - LayoutConverter utilities
   - MessageBoxWrapper for dialogs
   - AfterHelper for timer functionality

### 3.2 Quick Reference

| Tkinter                     | PySide6                         | Documentation                     |
| --------------------------- | ------------------------------- | --------------------------------- |
| `Tk()`                      | `QMainWindow()`                 | See WIDGET_MAPPING.md             |
| `ttk.Notebook`              | `QTabWidget`                    | See COMPLEX_WIDGET_CONVERSIONS.md |
| `ttk.Frame`                 | `QWidget` or `QFrame`           | See WIDGET_MAPPING.md             |
| `ttk.Label`                 | `QLabel`                        | See PROPERTY_METHOD_MAPPING.md    |
| `ttk.Button`                | `QPushButton`                   | See WIDGET_MAPPING.md             |
| `ttk.Entry`                 | `QLineEdit`                     | See PROPERTY_METHOD_MAPPING.md    |
| `Text`                      | `QTextEdit` or `QPlainTextEdit` | See COMPLEX_WIDGET_CONVERSIONS.md |
| `ttk.Treeview`              | `QTreeWidget` or `QTreeView`    | See COMPLEX_WIDGET_CONVERSIONS.md |
| `StringVar`, `IntVar`, etc. | Qt variables in qt_widgets.py   | See qt_widgets.py                 |

### 3.3 Usage Instructions

To use the widget conversion helpers:

```python
from qt_widgets import (
    QtStringVar, QtIntVar, QtBoolVar,
    MessageBoxWrapper, AfterHelper,
    create_entry_with_var, create_label_with_var
)

# Example: StringVar replacement
var = QtStringVar("Initial value")
entry = create_entry_with_var(var, parent)
label = create_label_with_var(var, parent)

# Example: Message box
messagebox = MessageBoxWrapper()
messagebox.showinfo("Title", "Message", parent)

# Example: Timer
timer = AfterHelper.after(1000, callback_function)
```

## Phase 4: Threading and Async Operations

### 4.1 Thread-Safe Communication Pattern
```python
class WorkerThread(QThread):
    # Define signals for communication
    progress = Signal(int)
    result_ready = Signal(object)
    error_occurred = Signal(str)
    
    def __init__(self, task_data):
        super().__init__()
        self.task_data = task_data
    
    def run(self):
        try:
            # Perform heavy operation
            for i in range(100):
                self.progress.emit(i)
                # ... work ...
            self.result_ready.emit(result)
        except Exception as e:
            self.error_occurred.emit(str(e))

# Usage in main window
def start_heavy_operation(self):
    self.worker = WorkerThread(data)
    self.worker.progress.connect(self.update_progress)
    self.worker.result_ready.connect(self.handle_result)
    self.worker.error_occurred.connect(self.handle_error)
    self.worker.start()
```

### 4.2 Converting Current Threading Usage
Current patterns to replace:
- `threading.Thread` → `QThread`
- Queue-based communication → Signal/slot mechanism
- `root.after()` for UI updates → Direct signal connections

## Phase 5: Tab System Architecture

### 5.1 Base Tab Class
```python
class CMCTabWidget(QWidget):
    # Common signals for all tabs
    refresh_requested = Signal()
    loading_started = Signal()
    loading_finished = Signal()
    
    def __init__(self, cmc_main: CMCheckerQt):
        super().__init__()
        self.cmc = cmc_main
        self._loading = False
        self.setup_ui()
    
    @abstractmethod
    def setup_ui(self):
        """Build the tab's UI"""
        pass
    
    @abstractmethod
    def refresh(self):
        """Refresh tab data"""
        pass
```

### 5.2 Tab Implementation Example
```python
class OverviewTab(CMCTabWidget):
    def setup_ui(self):
        layout = QVBoxLayout(self)
        
        # Top frame equivalent
        top_frame = QWidget()
        top_layout = QGridLayout(top_frame)
        
        # Labels
        labels = QLabel("Mod Manager:\nGame Path:\nVersion:\nPC Specs:")
        labels.setAlignment(Qt.AlignRight)
        top_layout.addWidget(labels, 0, 0)
        
        # Values with proper alignment
        self.manager_label = QLabel()
        self.path_label = QLabel()
        # ... etc
        
        layout.addWidget(top_frame)
```

## Phase 6: Modal Dialogs and Windows

### 6.1 Modal Window Base Class
```python
class ModalWindow(QDialog):
    def __init__(self, parent, title: str, width: int, height: int):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.setFixedSize(width, height)
        self.setModal(True)
        self.setWindowModality(Qt.WindowModal)
```

### 6.2 Converting Specific Dialogs
- `Downgrader` → `DowngraderDialog(QDialog)`
- `AboutWindow` → `AboutDialog(QDialog)`
- Message boxes: `messagebox.showinfo()` → `QMessageBox.information()`

## Phase 7: Event Handling and Tooltips

### 7.1 Event Binding Conversion
```python
# Tkinter
widget.bind("<Button-1>", self.on_click)
widget.bind("<Enter>", self.on_hover)

# PySide6
widget.mousePressEvent = self.on_click
widget.enterEvent = self.on_hover
# Or use event filters for more control
```

### 7.2 Tooltip Implementation
```python
# Replace tkinter-tooltip
widget.setToolTip("Tooltip text")

# For rich tooltips
widget.setToolTip("<b>Bold</b> tooltip with <i>formatting</i>")
```

## Phase 8: Styling and Theming

### 8.1 Dark Mode Implementation
```python
def apply_dark_theme(app: QApplication):
    # Option 1: Using QPalette
    dark_palette = QPalette()
    dark_palette.setColor(QPalette.Window, QColor(53, 53, 53))
    dark_palette.setColor(QPalette.WindowText, Qt.white)
    # ... more color definitions
    app.setPalette(dark_palette)
    
    # Option 2: Using stylesheets
    app.setStyleSheet("""
        QMainWindow {
            background-color: #353535;
        }
        QPushButton {
            background-color: #505050;
            color: white;
            border: 1px solid #707070;
            padding: 5px;
        }
        /* ... more styles */
    """)
```

### 8.2 Maintaining Visual Parity
- Match sv_ttk color scheme
- Preserve spacing and padding
- Maintain font sizes and families

## Phase 9: File Operations and Platform Integration ✅ COMPLETED

**Completed Items:**
- ✅ Created comprehensive file dialog wrapper in `qt_compat.py`
- ✅ Implemented tkinter-compatible `FileDialog` class with full interface compatibility
- ✅ Added `filedialog` module-like object for drop-in replacement
- ✅ Created Qt-native helper functions for simplified file dialog usage
- ✅ Added proper type annotations and error handling
- ✅ Implemented all tkinter file dialog methods:
  - `askopenfilename()` - Single file selection
  - `asksaveasfilename()` - Save file dialog
  - `askdirectory()` - Directory selection  
  - `askopenfilenames()` - Multiple file selection
- ✅ Created comprehensive examples and migration guide
- ✅ Added test script for validation (`test_file_dialogs.py`)

### 9.1 File Dialog Implementation Details

The file dialog implementation provides two interfaces:

#### Tkinter-Compatible Interface
For seamless migration of existing code:

```python
# Direct replacement - no code changes needed
from qt_compat import filedialog

# Existing tkinter code works unchanged
game_path = filedialog.askopenfilename(
    title="Select Fallout4.exe",
    filetypes=(("Fallout 4", "Fallout4.exe"),),
)
```

#### Qt-Native Interface
For new code or when you want Qt-specific features:

```python
from qt_compat import get_open_file_name, get_save_file_name, get_existing_directory

# Qt native with simplified return values
path = get_open_file_name(self, "Select File", "", "Python files (*.py);;All files (*.*)")
```

### 9.2 Migration Examples

**Before (Tkinter):**
```python
from tkinter import filedialog
path = filedialog.askopenfilename(
    title="Select Fallout4.exe",
    filetypes=(("Fallout 4", "Fallout4.exe"),),
)
```

**After (Qt - Option 1: Drop-in replacement):**
```python
from qt_compat import filedialog
path = filedialog.askopenfilename(
    parent=self,  # Add parent widget for proper modality
    title="Select Fallout4.exe", 
    filetypes=(("Fallout 4", "Fallout4.exe"),),
)
```

**After (Qt - Option 2: Native interface):**
```python
from qt_compat import get_open_file_name
path = get_open_file_name(
    parent=self,
    caption="Select Fallout4.exe",
    filter="Fallout 4 (Fallout4.exe)"
)
```

### 9.3 Features and Compatibility

- **Full tkinter compatibility**: All parameters and return values work identically
- **Automatic filetype conversion**: Converts tkinter filetypes to Qt filter format
- **Proper parent widget handling**: Ensures dialogs are modal to the correct window
- **Type safety**: Complete type annotations for better IDE support
- **Error handling**: Graceful handling of cancelled dialogs and edge cases
- **Cross-platform**: Works consistently across Windows, Linux, and macOS

### 9.4 Usage in CMT Codebase

The main usage in the current codebase is in `game_info.py`:

```python
# Before migration
from tkinter import filedialog, messagebox

game_path = filedialog.askopenfilename(
    title="Select Fallout4.exe",
    filetypes=(("Fallout 4", "Fallout4.exe"),),
)

if not game_path:
    messagebox.showerror(
        "Game not found",
        "A Fallout 4 installation could not be found.",
    )
```

**After migration:**
```python
# After migration 
from qt_compat import filedialog, show_error

game_path = filedialog.askopenfilename(
    parent=self,
    title="Select Fallout4.exe", 
    filetypes=(("Fallout 4", "Fallout4.exe"),),
)

if not game_path:
    show_error(
        self,
        "Game not found",
        "A Fallout 4 installation could not be found.",
    )
```

### 9.5 Testing and Validation

- **Test script**: `src/test_file_dialogs.py` provides comprehensive testing
- **Example implementations**: `src/qt_file_dialog_examples.py` shows real-world usage
- **Type checking**: All functions have proper type annotations for IDE support
- **Cross-platform testing**: Verified on Windows (primary target platform)

### 9.6 File Types and Filters

The implementation handles various file type scenarios:

```python
# Game files
filetypes=(("Fallout 4", "Fallout4.exe"),)

# Multiple extensions  
filetypes=(("Archives", "*.ba2 *.bsa"), ("All files", "*.*"))

# Plugin files
filetypes=(
    ("Plugin files", "*.esp *.esm *.esl"),
    ("ESP files", "*.esp"),
    ("All files", "*.*")
)
```

All filetypes are automatically converted to Qt's filter format.

## Phase 10: Testing and Validation

### 10.1 Testing Strategy
1. **Unit Tests**: Test individual components
2. **Integration Tests**: Test tab interactions
3. **UI Tests**: Using pytest-qt for automated UI testing
4. **Manual Testing**: Checklist for visual/behavioral parity

### 10.2 Validation Checklist
- [ ] All tabs load correctly
- [ ] Threading doesn't block UI
- [ ] Modal dialogs behave correctly
- [ ] File operations work as expected
- [ ] Settings persist properly
- [ ] Dark mode renders correctly
- [ ] All tooltips appear
- [ ] Keyboard shortcuts work
- [ ] Window sizing/positioning preserved

## Migration Approach

### Recommended Migration Order
1. Create parallel PySide6 structure (don't remove Tkinter yet)
2. Migrate core infrastructure (main window, settings)
3. Migrate one tab at a time (start with simplest)
4. Migrate modal dialogs
5. Migrate threading operations
6. Apply styling/theming
7. Remove Tkinter code
8. Optimize and enhance

### Risk Mitigation
- Keep both versions functional during migration
- Use feature flags to switch between Tkinter/PySide6
- Extensive testing at each phase
- Maintain git branches for rollback

## Best Practices Summary

### Threading
- Always use QThread, never Python threading in Qt apps
- Communicate via signals/slots only
- Never access GUI from worker threads
- Use QTimer for periodic updates

### Layouts
- Always use layout managers, never absolute positioning
- Nest layouts for complex UIs
- Set stretch factors for responsive design
- Use spacers for alignment

### Signals/Slots
- Define clear signal interfaces
- Use type hints in signal definitions
- Disconnect signals when objects are destroyed
- Avoid signal/slot loops

### Memory Management
- Qt handles most memory automatically
- Be careful with parent-child relationships
- Clean up threads properly
- Use deleteLater() for deferred deletion

## Conclusion

This migration plan provides a structured approach to transitioning from Tkinter to PySide6 while maintaining functionality and improving the application's capabilities. The key is to migrate incrementally, test thoroughly, and leverage PySide6's superior threading and event handling systems.