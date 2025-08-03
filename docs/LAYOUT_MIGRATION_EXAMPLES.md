# Practical Layout Migration Examples

This document provides real-world examples of converting Tkinter layouts to PySide6, based on actual patterns from the Collective Modding Toolkit codebase.

## Example 1: Simple Form Layout (Grid)

### Tkinter Version
```python
# From settings tab - form with labels and entries
frame = ttk.Frame(parent)

ttk.Label(frame, text="Mod Manager:").grid(row=0, column=0, sticky="w", padx=5, pady=5)
ttk.Label(frame, text="Game Path:").grid(row=1, column=0, sticky="w", padx=5, pady=5)
ttk.Label(frame, text="F4SE Path:").grid(row=2, column=0, sticky="w", padx=5, pady=5)

self.manager_var = tk.StringVar()
self.path_var = tk.StringVar()
self.f4se_var = tk.StringVar()

ttk.Entry(frame, textvariable=self.manager_var, width=50).grid(row=0, column=1, sticky="ew", padx=5, pady=5)
ttk.Entry(frame, textvariable=self.path_var, width=50).grid(row=1, column=1, sticky="ew", padx=5, pady=5)
ttk.Entry(frame, textvariable=self.f4se_var, width=50).grid(row=2, column=1, sticky="ew", padx=5, pady=5)

ttk.Button(frame, text="Browse...", command=self.browse_path).grid(row=1, column=2, padx=5)

# Make column 1 expand
frame.columnconfigure(1, weight=1)
```

### PySide6 Version
```python
from PySide6.QtWidgets import QWidget, QGridLayout, QLabel, QLineEdit, QPushButton
from qt_widgets import QtStringVar

# Create widget and layout
frame = QWidget(parent)
layout = QGridLayout(frame)
layout.setContentsMargins(5, 5, 5, 5)
layout.setSpacing(5)

# Create labels
layout.addWidget(QLabel("Mod Manager:"), 0, 0, Qt.AlignLeft)
layout.addWidget(QLabel("Game Path:"), 1, 0, Qt.AlignLeft)
layout.addWidget(QLabel("F4SE Path:"), 2, 0, Qt.AlignLeft)

# Create variables and entries
self.manager_var = QtStringVar()
self.path_var = QtStringVar()
self.f4se_var = QtStringVar()

manager_entry = QLineEdit()
path_entry = QLineEdit()
f4se_entry = QLineEdit()

# Bind variables
self.manager_var.changed.connect(manager_entry.setText)
manager_entry.textChanged.connect(self.manager_var.set)

self.path_var.changed.connect(path_entry.setText)
path_entry.textChanged.connect(self.path_var.set)

self.f4se_var.changed.connect(f4se_entry.setText)
f4se_entry.textChanged.connect(self.f4se_var.set)

# Add entries to layout
layout.addWidget(manager_entry, 0, 1)
layout.addWidget(path_entry, 1, 1)
layout.addWidget(f4se_entry, 2, 1)

# Add browse button
browse_btn = QPushButton("Browse...")
browse_btn.clicked.connect(self.browse_path)
layout.addWidget(browse_btn, 1, 2)

# Make column 1 expand
layout.setColumnStretch(1, 1)
```

## Example 2: Toolbar with Buttons (Pack Horizontal)

### Tkinter Version
```python
# Toolbar frame with horizontal packing
toolbar = ttk.Frame(parent)
toolbar.pack(fill="x", padx=10, pady=5)

ttk.Button(toolbar, text="Refresh", command=self.refresh).pack(side="left", padx=2)
ttk.Button(toolbar, text="Scan", command=self.scan).pack(side="left", padx=2)
ttk.Button(toolbar, text="Export", command=self.export).pack(side="left", padx=2)

# Status label on the right
self.status_var = tk.StringVar(value="Ready")
ttk.Label(toolbar, textvariable=self.status_var).pack(side="right", padx=10)
```

### PySide6 Version
```python
from PySide6.QtWidgets import QWidget, QHBoxLayout, QPushButton, QLabel

# Create toolbar widget
toolbar = QWidget(parent)
toolbar_layout = QHBoxLayout(toolbar)
toolbar_layout.setContentsMargins(10, 5, 10, 5)

# Add buttons
refresh_btn = QPushButton("Refresh")
refresh_btn.clicked.connect(self.refresh)
toolbar_layout.addWidget(refresh_btn)

scan_btn = QPushButton("Scan")
scan_btn.clicked.connect(self.scan)
toolbar_layout.addWidget(scan_btn)

export_btn = QPushButton("Export")
export_btn.clicked.connect(self.export)
toolbar_layout.addWidget(export_btn)

# Add stretch to push status label to the right
toolbar_layout.addStretch()

# Status label
self.status_var = QtStringVar("Ready")
status_label = QLabel(self.status_var.get())
self.status_var.changed.connect(status_label.setText)
toolbar_layout.addWidget(status_label)

# Add toolbar to parent layout
parent_layout.addWidget(toolbar)
```

## Example 3: Complex Nested Layout (Overview Tab Style)

### Tkinter Version
```python
# Main container
main_frame = ttk.Frame(parent)
main_frame.pack(fill="both", expand=True, padx=10, pady=10)

# Top section with grid
info_frame = ttk.LabelFrame(main_frame, text="System Information", padding=10)
info_frame.pack(fill="x", pady=(0, 10))

# Labels column
labels_text = "Mod Manager:\nGame Path:\nGame Version:\nF4SE Version:"
ttk.Label(info_frame, text=labels_text, justify="right").grid(row=0, column=0, sticky="ne", padx=(0, 10))

# Values column
self.info_text = tk.Text(info_frame, height=4, width=60, state="disabled")
self.info_text.grid(row=0, column=1, sticky="ew")

info_frame.columnconfigure(1, weight=1)

# Middle section with buttons
button_frame = ttk.Frame(main_frame)
button_frame.pack(fill="x", pady=10)

ttk.Button(button_frame, text="Scan Mods", command=self.scan).pack(side="left", padx=5)
ttk.Button(button_frame, text="View Report", command=self.report).pack(side="left", padx=5)
ttk.Button(button_frame, text="Settings", command=self.settings).pack(side="right", padx=5)

# Bottom section with log
log_frame = ttk.LabelFrame(main_frame, text="Activity Log", padding=5)
log_frame.pack(fill="both", expand=True)

self.log_text = tk.Text(log_frame, height=10, wrap="word")
self.log_text.pack(fill="both", expand=True)
```

### PySide6 Version
```python
from PySide6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, 
                              QGroupBox, QGridLayout, QLabel, 
                              QTextEdit, QPushButton)

# Main container
main_widget = QWidget(parent)
main_layout = QVBoxLayout(main_widget)
main_layout.setContentsMargins(10, 10, 10, 10)

# Top section - System Information
info_group = QGroupBox("System Information")
info_layout = QGridLayout()
info_layout.setContentsMargins(10, 10, 10, 10)
info_group.setLayout(info_layout)

# Labels
labels = QLabel("Mod Manager:\nGame Path:\nGame Version:\nF4SE Version:")
labels.setAlignment(Qt.AlignRight | Qt.AlignTop)
info_layout.addWidget(labels, 0, 0)

# Values text area
self.info_text = QTextEdit()
self.info_text.setReadOnly(True)
self.info_text.setFixedHeight(80)  # Approximate 4 lines
info_layout.addWidget(self.info_text, 0, 1)

# Make column 1 expand
info_layout.setColumnStretch(1, 1)
main_layout.addWidget(info_group)

# Middle section - Buttons
button_widget = QWidget()
button_layout = QHBoxLayout(button_widget)
button_layout.setContentsMargins(0, 10, 0, 10)

scan_btn = QPushButton("Scan Mods")
scan_btn.clicked.connect(self.scan)
button_layout.addWidget(scan_btn)

report_btn = QPushButton("View Report")
report_btn.clicked.connect(self.report)
button_layout.addWidget(report_btn)

button_layout.addStretch()  # Push settings button to right

settings_btn = QPushButton("Settings")
settings_btn.clicked.connect(self.settings)
button_layout.addWidget(settings_btn)

main_layout.addWidget(button_widget)

# Bottom section - Activity Log
log_group = QGroupBox("Activity Log")
log_layout = QVBoxLayout()
log_layout.setContentsMargins(5, 5, 5, 5)
log_group.setLayout(log_layout)

self.log_text = QTextEdit()
self.log_text.setLineWrapMode(QTextEdit.WidgetWidth)
log_layout.addWidget(self.log_text)

main_layout.addWidget(log_group, 1)  # stretch=1 to expand

# Add main widget to parent
parent_layout.addWidget(main_widget)
```

## Example 4: Tab Layout with Scrollable Content

### Tkinter Version
```python
# Create notebook
notebook = ttk.Notebook(parent)
notebook.pack(fill="both", expand=True)

# Create scrollable tab
tab_frame = ttk.Frame(notebook)
notebook.add(tab_frame, text="Mods")

# Create canvas and scrollbar for scrolling
canvas = tk.Canvas(tab_frame)
scrollbar = ttk.Scrollbar(tab_frame, orient="vertical", command=canvas.yview)
scrollable_frame = ttk.Frame(canvas)

scrollable_frame.bind(
    "<Configure>",
    lambda e: canvas.configure(scrollregion=canvas.bbox("all"))
)

canvas.create_window((0, 0), window=scrollable_frame, anchor="nw")
canvas.configure(yscrollcommand=scrollbar.set)

# Pack canvas and scrollbar
canvas.pack(side="left", fill="both", expand=True)
scrollbar.pack(side="right", fill="y")

# Add content to scrollable frame
for i in range(50):
    ttk.Label(scrollable_frame, text=f"Mod {i}").pack(pady=2)
```

### PySide6 Version
```python
from PySide6.QtWidgets import QTabWidget, QScrollArea, QWidget, QVBoxLayout, QLabel

# Create tab widget
tab_widget = QTabWidget(parent)

# Create scrollable tab
scroll_area = QScrollArea()
scroll_content = QWidget()
scroll_layout = QVBoxLayout(scroll_content)

# Add content
for i in range(50):
    scroll_layout.addWidget(QLabel(f"Mod {i}"))

# Set up scroll area
scroll_area.setWidget(scroll_content)
scroll_area.setWidgetResizable(True)

# Add to tab widget
tab_widget.addTab(scroll_area, "Mods")

# Add to parent
parent_layout.addWidget(tab_widget)
```

## Example 5: Dialog Window Layout

### Tkinter Version
```python
class SettingsDialog(tk.Toplevel):
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Settings")
        self.geometry("400x300")
        self.transient(parent)
        self.grab_set()
        
        # Main frame
        main_frame = ttk.Frame(self, padding=10)
        main_frame.pack(fill="both", expand=True)
        
        # Settings options
        ttk.Label(main_frame, text="Options", font=("Arial", 14, "bold")).pack(pady=(0, 10))
        
        self.auto_scan = tk.BooleanVar()
        ttk.Checkbutton(main_frame, text="Auto-scan on startup", 
                       variable=self.auto_scan).pack(anchor="w", pady=2)
        
        self.dark_mode = tk.BooleanVar()
        ttk.Checkbutton(main_frame, text="Dark mode", 
                       variable=self.dark_mode).pack(anchor="w", pady=2)
        
        # Separator
        ttk.Separator(main_frame, orient="horizontal").pack(fill="x", pady=10)
        
        # Buttons at bottom
        button_frame = ttk.Frame(main_frame)
        button_frame.pack(side="bottom", fill="x", pady=(10, 0))
        
        ttk.Button(button_frame, text="Cancel", command=self.destroy).pack(side="right", padx=5)
        ttk.Button(button_frame, text="OK", command=self.save).pack(side="right")
```

### PySide6 Version
```python
from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout,
                              QLabel, QCheckBox, QFrame, QPushButton)
from PySide6.QtCore import Qt

class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setFixedSize(400, 300)
        self.setModal(True)
        
        # Main layout
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(10, 10, 10, 10)
        
        # Title
        title = QLabel("Options")
        title.setStyleSheet("font-size: 14px; font-weight: bold;")
        main_layout.addWidget(title)
        main_layout.addSpacing(10)
        
        # Settings options
        self.auto_scan = QCheckBox("Auto-scan on startup")
        main_layout.addWidget(self.auto_scan)
        
        self.dark_mode = QCheckBox("Dark mode")
        main_layout.addWidget(self.dark_mode)
        
        # Separator
        separator = QFrame()
        separator.setFrameShape(QFrame.HLine)
        separator.setFrameShadow(QFrame.Sunken)
        main_layout.addWidget(separator)
        
        # Add stretch to push buttons to bottom
        main_layout.addStretch()
        
        # Button layout
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        cancel_btn = QPushButton("Cancel")
        cancel_btn.clicked.connect(self.reject)
        button_layout.addWidget(cancel_btn)
        
        ok_btn = QPushButton("OK")
        ok_btn.clicked.connect(self.accept)
        button_layout.addWidget(ok_btn)
        
        main_layout.addLayout(button_layout)
```

## Key Layout Migration Patterns

### 1. Pack → VBox/HBox Conversion
- `side="top"` or `side="bottom"` → Use `QVBoxLayout`
- `side="left"` or `side="right"` → Use `QHBoxLayout`
- `fill="x"` → Widget expands horizontally (default in layouts)
- `fill="y"` → Widget expands vertically (use stretch factor)
- `fill="both"` + `expand=True` → Use stretch factor > 0
- `padx/pady` → Use `setContentsMargins()` and `setSpacing()`

### 2. Grid → QGridLayout Conversion
- `sticky="ew"` → Column stretch for horizontal expansion
- `sticky="ns"` → Row stretch for vertical expansion
- `sticky="w"` → `Qt.AlignLeft`
- `sticky="e"` → `Qt.AlignRight`
- `columnspan/rowspan` → Use additional parameters in `addWidget()`
- `columnconfigure(weight=1)` → `setColumnStretch(column, 1)`

### 3. Widget Spacing Best Practices
```python
# Tkinter spacing
widget.pack(padx=10, pady=5)  # External padding
frame = ttk.Frame(parent, padding=10)  # Internal padding

# PySide6 spacing
layout.setContentsMargins(10, 5, 10, 5)  # External margins
layout.setSpacing(5)  # Space between widgets
widget.setContentsMargins(10, 10, 10, 10)  # Internal margins
```

### 4. Dynamic Layout Updates
```python
# Tkinter - destroy and recreate
for widget in frame.winfo_children():
    widget.destroy()
# Add new widgets

# PySide6 - clear layout
while layout.count():
    item = layout.takeAt(0)
    if item.widget():
        item.widget().deleteLater()
# Add new widgets
```

### 5. Responsive Design
```python
# Tkinter
parent.columnconfigure(0, weight=1)  # Column 0 expands
parent.rowconfigure(1, weight=1)     # Row 1 expands

# PySide6
layout.setColumnStretch(0, 1)  # Column 0 expands
layout.setRowStretch(1, 1)     # Row 1 expands
```

## Common Gotchas

1. **Widget Parenting**: In Qt, widgets need proper parent-child relationships
2. **Layout Ownership**: Layouts take ownership of widgets added to them
3. **Minimum Sizes**: Qt widgets may have different default minimum sizes
4. **Stretch Factors**: 0 = no stretch, higher numbers = more stretch priority
5. **Margin Inheritance**: Child layouts don't inherit parent margins

## Testing Your Layout Conversions

Always test:
- Window resizing behavior
- Minimum window size
- Widget alignment and spacing
- Tab order (keyboard navigation)
- Visual consistency with original design