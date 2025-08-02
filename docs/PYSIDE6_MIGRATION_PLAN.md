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

## Phase 3: Widget Mapping Guide

### 3.1 Basic Widget Conversions

| Tkinter | PySide6 | Notes |
|---------|---------|-------|
| `Tk()` | `QMainWindow()` | Main application window |
| `ttk.Notebook` | `QTabWidget` | Tab container |
| `ttk.Frame` | `QWidget` or `QFrame` | Container widgets |
| `ttk.Label` | `QLabel` | Text/image display |
| `ttk.Button` | `QPushButton` | Standard buttons |
| `ttk.Entry` | `QLineEdit` | Single-line text input |
| `Text` | `QTextEdit` or `QPlainTextEdit` | Multi-line text |
| `ttk.Treeview` | `QTreeWidget` or `QTreeView` | Tree/table display |
| `ttk.Progressbar` | `QProgressBar` | Progress indication |
| `ttk.Combobox` | `QComboBox` | Dropdown selection |
| `ttk.Checkbutton` | `QCheckBox` | Checkbox |
| `ttk.Radiobutton` | `QRadioButton` | Radio button |
| `StringVar`, `IntVar`, etc. | Properties + signals | Use Qt's property system |
| `PhotoImage` | `QPixmap` + `QIcon` | Image handling |

### 3.2 Layout System Migration

#### Tkinter Layout → Qt Layout
- `pack()` → `QVBoxLayout` or `QHBoxLayout`
- `grid()` → `QGridLayout`
- `place()` → Avoid; use layouts or `QGraphicsView` if needed

#### Layout Best Practices
```python
# Example: Converting a typical Tkinter grid layout
# Tkinter:
# label.grid(row=0, column=0, sticky="ew", padx=5, pady=5)

# PySide6:
layout = QGridLayout()
layout.addWidget(label, 0, 0)
layout.setContentsMargins(5, 5, 5, 5)
layout.setSpacing(5)
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

## Phase 9: File Operations and Platform Integration

### 9.1 File Dialogs
```python
# Tkinter
from tkinter import filedialog
path = filedialog.askopenfilename()

# PySide6
from PySide6.QtWidgets import QFileDialog
path, _ = QFileDialog.getOpenFileName(self, "Select File")
```

### 9.2 System Tray Integration (Enhancement)
```python
# Optional enhancement for PySide6
self.tray_icon = QSystemTrayIcon(QIcon("icon.png"), self)
self.tray_icon.setToolTip("CM Toolkit")
```

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