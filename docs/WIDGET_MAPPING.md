# Comprehensive Tkinter to PySide6 Widget Mapping Guide

This guide provides detailed mappings and practical examples for converting Tkinter widgets to PySide6 equivalents during the Collective Modding Toolkit migration.

## Table of Contents
- [Basic Widget Conversions](#basic-widget-conversions)
- [Layout System Migration](#layout-system-migration)
- [Variable System Replacement](#variable-system-replacement)
- [Complex Widget Examples](#complex-widget-examples)
- [Event Handling Patterns](#event-handling-patterns)
- [Common Pitfalls and Solutions](#common-pitfalls-and-solutions)

## Basic Widget Conversions

### Window Widgets

#### Main Window
```python
# Tkinter
root = tk.Tk()
root.title("My Application")
root.geometry("800x600")
root.minsize(400, 300)
root.configure(bg="#1c1c1c")

# PySide6
app = QApplication(sys.argv)
window = QMainWindow()
window.setWindowTitle("My Application")
window.resize(800, 600)
window.setMinimumSize(400, 300)
window.setStyleSheet("background-color: #1c1c1c;")
```

#### Toplevel Window
```python
# Tkinter
dialog = tk.Toplevel(parent)
dialog.title("Dialog")
dialog.transient(parent)
dialog.grab_set()

# PySide6
dialog = QDialog(parent)
dialog.setWindowTitle("Dialog")
dialog.setModal(True)
dialog.setWindowModality(Qt.WindowModal)
```

### Container Widgets

#### Frame
```python
# Tkinter
frame = ttk.Frame(parent, padding=10)
frame.configure(borderwidth=2, relief="solid")
frame.pack(fill="both", expand=True)

# PySide6
frame = QFrame(parent)
frame.setFrameStyle(QFrame.Box | QFrame.Raised)
frame.setContentsMargins(10, 10, 10, 10)
layout = QVBoxLayout()  # Parent layout
layout.addWidget(frame)
```

#### LabelFrame
```python
# Tkinter
labelframe = ttk.LabelFrame(parent, text="Options", padding=5)
labelframe.pack(padx=10, pady=10)

# PySide6
groupbox = QGroupBox("Options", parent)
groupbox.setContentsMargins(5, 5, 5, 5)
layout = QVBoxLayout()
layout.addWidget(groupbox)
layout.setContentsMargins(10, 10, 10, 10)
```

#### Notebook/Tabs
```python
# Tkinter
notebook = ttk.Notebook(parent)
tab1 = ttk.Frame(notebook)
tab2 = ttk.Frame(notebook)
notebook.add(tab1, text="Tab 1")
notebook.add(tab2, text="Tab 2")
notebook.pack(fill="both", expand=True)

# PySide6
tab_widget = QTabWidget(parent)
tab1 = QWidget()
tab2 = QWidget()
tab_widget.addTab(tab1, "Tab 1")
tab_widget.addTab(tab2, "Tab 2")
layout = QVBoxLayout()
layout.addWidget(tab_widget)
```

### Display Widgets

#### Label
```python
# Tkinter
label = ttk.Label(parent, text="Hello", font=("Arial", 12))
label.configure(foreground="#fafafa", background="#1c1c1c")
label.pack(pady=5)

# PySide6
label = QLabel("Hello", parent)
label.setFont(QFont("Arial", 12))
label.setStyleSheet("color: #fafafa; background-color: #1c1c1c;")
layout.addWidget(label)
layout.setSpacing(5)
```

#### Image Label
```python
# Tkinter
image = tk.PhotoImage(file="icon.png")
label = ttk.Label(parent, image=image)
label.image = image  # Keep reference
label.pack()

# PySide6
pixmap = QPixmap("icon.png")
label = QLabel(parent)
label.setPixmap(pixmap)
layout.addWidget(label)
```

### Input Widgets

#### Entry
```python
# Tkinter
var = tk.StringVar(value="default")
entry = ttk.Entry(parent, textvariable=var, width=30)
entry.bind("<Return>", on_enter)
entry.pack()

# PySide6
line_edit = QLineEdit("default", parent)
line_edit.setFixedWidth(300)  # Approximate width conversion
line_edit.returnPressed.connect(on_enter)
layout.addWidget(line_edit)
```

#### Text/TextEdit
```python
# Tkinter
text = tk.Text(parent, height=10, width=50)
text.insert("1.0", "Initial text")
text.configure(state="disabled")  # Read-only
scrollbar = ttk.Scrollbar(parent, command=text.yview)
text.configure(yscrollcommand=scrollbar.set)

# PySide6
text_edit = QTextEdit(parent)
text_edit.setPlainText("Initial text")
text_edit.setReadOnly(True)  # Read-only
# Scrollbar is automatic in Qt
```

#### Spinbox
```python
# Tkinter
var = tk.IntVar(value=5)
spinbox = ttk.Spinbox(parent, from_=0, to=100, textvariable=var)
spinbox.pack()

# PySide6
spinbox = QSpinBox(parent)
spinbox.setRange(0, 100)
spinbox.setValue(5)
layout.addWidget(spinbox)
```

### Button Widgets

#### Button
```python
# Tkinter
button = ttk.Button(parent, text="Click Me", command=on_click)
button.configure(state="disabled")  # Disable
button.pack(padx=5, pady=5)

# PySide6
button = QPushButton("Click Me", parent)
button.clicked.connect(on_click)
button.setEnabled(False)  # Disable
layout.addWidget(button)
layout.setContentsMargins(5, 5, 5, 5)
```

#### Checkbutton/Checkbox
```python
# Tkinter
var = tk.BooleanVar(value=True)
check = ttk.Checkbutton(parent, text="Enable", variable=var, command=on_toggle)
check.pack()

# PySide6
checkbox = QCheckBox("Enable", parent)
checkbox.setChecked(True)
checkbox.stateChanged.connect(on_toggle)
layout.addWidget(checkbox)
```

#### Radiobutton
```python
# Tkinter
var = tk.StringVar(value="option1")
radio1 = ttk.Radiobutton(parent, text="Option 1", variable=var, value="option1")
radio2 = ttk.Radiobutton(parent, text="Option 2", variable=var, value="option2")
radio1.pack()
radio2.pack()

# PySide6
radio1 = QRadioButton("Option 1", parent)
radio2 = QRadioButton("Option 2", parent)
radio1.setChecked(True)
# Use QButtonGroup for exclusive selection
group = QButtonGroup()
group.addButton(radio1, 1)
group.addButton(radio2, 2)
layout.addWidget(radio1)
layout.addWidget(radio2)
```

### Selection Widgets

#### Combobox
```python
# Tkinter
var = tk.StringVar()
combo = ttk.Combobox(parent, textvariable=var, values=["A", "B", "C"])
combo.set("A")  # Default selection
combo.bind("<<ComboboxSelected>>", on_select)
combo.pack()

# PySide6
combo = QComboBox(parent)
combo.addItems(["A", "B", "C"])
combo.setCurrentText("A")  # Default selection
combo.currentTextChanged.connect(on_select)
layout.addWidget(combo)
```

#### Listbox
```python
# Tkinter
listbox = tk.Listbox(parent, selectmode="single", height=10)
for item in ["Item 1", "Item 2", "Item 3"]:
    listbox.insert(tk.END, item)
listbox.bind("<<ListboxSelect>>", on_select)
scrollbar = ttk.Scrollbar(parent, command=listbox.yview)
listbox.configure(yscrollcommand=scrollbar.set)

# PySide6
list_widget = QListWidget(parent)
list_widget.setSelectionMode(QListWidget.SingleSelection)
for item in ["Item 1", "Item 2", "Item 3"]:
    list_widget.addItem(item)
list_widget.itemSelectionChanged.connect(on_select)
# Scrollbar is automatic
```

### Progress and Scale Widgets

#### Progressbar
```python
# Tkinter
progress = ttk.Progressbar(parent, maximum=100, value=0, mode="determinate")
progress.pack(fill="x", padx=10)
# Update: progress["value"] = 50

# PySide6
progress = QProgressBar(parent)
progress.setMaximum(100)
progress.setValue(0)
layout.addWidget(progress)
# Update: progress.setValue(50)
```

#### Scale/Slider
```python
# Tkinter
var = tk.IntVar(value=50)
scale = ttk.Scale(parent, from_=0, to=100, variable=var, orient="horizontal")
scale.pack(fill="x")

# PySide6
slider = QSlider(Qt.Horizontal, parent)
slider.setRange(0, 100)
slider.setValue(50)
layout.addWidget(slider)
```

## Layout System Migration

### Pack Layout → QVBoxLayout/QHBoxLayout

```python
# Tkinter pack (vertical)
label1.pack(side="top", fill="x", padx=10, pady=5)
label2.pack(side="top", fill="x", padx=10, pady=5)
button.pack(side="bottom", pady=10)

# PySide6 QVBoxLayout
layout = QVBoxLayout()
layout.setContentsMargins(10, 5, 10, 5)  # padx, pady
layout.addWidget(label1)
layout.addWidget(label2)
layout.addStretch()  # Push button to bottom
layout.addWidget(button)
layout.setSpacing(5)  # Between widgets
```

```python
# Tkinter pack (horizontal)
label.pack(side="left", padx=5)
entry.pack(side="left", fill="x", expand=True, padx=5)
button.pack(side="right", padx=5)

# PySide6 QHBoxLayout
layout = QHBoxLayout()
layout.setContentsMargins(5, 0, 5, 0)
layout.addWidget(label)
layout.addWidget(entry, 1)  # stretch=1 for expand
layout.addWidget(button)
layout.setSpacing(5)
```

### Grid Layout → QGridLayout

```python
# Tkinter grid
label1.grid(row=0, column=0, sticky="w", padx=5, pady=5)
entry1.grid(row=0, column=1, sticky="ew", padx=5, pady=5)
label2.grid(row=1, column=0, sticky="w", padx=5, pady=5)
entry2.grid(row=1, column=1, sticky="ew", padx=5, pady=5)
button.grid(row=2, column=0, columnspan=2, pady=10)

# Configure column weight
parent.columnconfigure(1, weight=1)

# PySide6 QGridLayout
layout = QGridLayout()
layout.setContentsMargins(5, 5, 5, 5)
layout.setSpacing(5)

layout.addWidget(label1, 0, 0, Qt.AlignLeft)
layout.addWidget(entry1, 0, 1)
layout.addWidget(label2, 1, 0, Qt.AlignLeft)
layout.addWidget(entry2, 1, 1)
layout.addWidget(button, 2, 0, 1, 2)  # rowspan=1, colspan=2

# Configure column stretch
layout.setColumnStretch(1, 1)  # Column 1 expands
```

### Sticky Alignment Mapping

| Tkinter sticky | Qt Alignment | Notes |
|---------------|--------------|-------|
| "n" | Qt.AlignTop | Align to top |
| "s" | Qt.AlignBottom | Align to bottom |
| "e" | Qt.AlignRight | Align to right |
| "w" | Qt.AlignLeft | Align to left |
| "ns" | Qt.AlignVCenter | Stretch vertically |
| "ew" | Qt.AlignHCenter | Stretch horizontally |
| "nsew" | No alignment | Widget fills cell |
| "nw" | Qt.AlignTop \| Qt.AlignLeft | Top-left corner |

## Variable System Replacement

### Creating Qt-Compatible Variables

```python
# src/qt_widgets.py
from PySide6.QtCore import QObject, Signal

class QtStringVar(QObject):
    changed = Signal(str)
    
    def __init__(self, initial_value=""):
        super().__init__()
        self._value = initial_value
    
    def get(self):
        return self._value
    
    def set(self, value):
        if self._value != value:
            self._value = value
            self.changed.emit(value)

class QtIntVar(QObject):
    changed = Signal(int)
    
    def __init__(self, initial_value=0):
        super().__init__()
        self._value = initial_value
    
    def get(self):
        return self._value
    
    def set(self, value):
        if self._value != value:
            self._value = value
            self.changed.emit(value)

class QtBoolVar(QObject):
    changed = Signal(bool)
    
    def __init__(self, initial_value=False):
        super().__init__()
        self._value = initial_value
    
    def get(self):
        return self._value
    
    def set(self, value):
        if self._value != value:
            self._value = value
            self.changed.emit(value)
```

### Using Variable Replacements

```python
# Tkinter with StringVar
var = tk.StringVar(value="Hello")
label = ttk.Label(parent, textvariable=var)
var.set("World")  # Label updates automatically

# PySide6 with QtStringVar
var = QtStringVar("Hello")
label = QLabel(var.get(), parent)
var.changed.connect(label.setText)  # Connect for auto-update
var.set("World")  # Label updates automatically
```

### Two-Way Binding Example

```python
# Tkinter two-way binding
var = tk.StringVar()
entry = ttk.Entry(parent, textvariable=var)
label = ttk.Label(parent, textvariable=var)

# PySide6 two-way binding
var = QtStringVar()
entry = QLineEdit(parent)
label = QLabel(parent)

# Set initial value
entry.setText(var.get())
label.setText(var.get())

# Connect changes
entry.textChanged.connect(var.set)
var.changed.connect(label.setText)
var.changed.connect(lambda v: entry.setText(v) if entry.text() != v else None)
```

## Complex Widget Examples

### Treeview → QTreeWidget

```python
# Tkinter Treeview
tree = ttk.Treeview(parent, columns=("Name", "Size", "Date"), show="tree headings")
tree.heading("#0", text="Type")
tree.heading("Name", text="Name")
tree.heading("Size", text="Size")
tree.heading("Date", text="Modified")

# Column configuration
tree.column("#0", width=100, minwidth=50)
tree.column("Name", width=200, anchor="w")
tree.column("Size", width=100, anchor="e")
tree.column("Date", width=150, anchor="center")

# Insert items
folder = tree.insert("", "end", text="Folder", values=("Documents", "4 KB", "2024-01-01"))
tree.insert(folder, "end", text="File", values=("document.txt", "2 KB", "2024-01-02"))

# Selection handling
tree.bind("<<TreeviewSelect>>", on_select)
selected = tree.selection()  # Get selected items

# PySide6 QTreeWidget
tree = QTreeWidget(parent)
tree.setHeaderLabels(["Type", "Name", "Size", "Modified"])

# Column configuration
tree.setColumnWidth(0, 100)
tree.setColumnWidth(1, 200)
tree.setColumnWidth(2, 100)
tree.setColumnWidth(3, 150)

# Insert items
folder = QTreeWidgetItem(tree, ["Folder", "Documents", "4 KB", "2024-01-01"])
file = QTreeWidgetItem(folder, ["File", "document.txt", "2 KB", "2024-01-02"])

# Selection handling
tree.itemSelectionChanged.connect(on_select)
selected = tree.selectedItems()  # Get selected items
```

### Text Widget with Syntax Highlighting

```python
# Tkinter Text with tags
text = tk.Text(parent)
text.insert("1.0", "def hello():\n    print('Hello')")

# Syntax highlighting with tags
text.tag_configure("keyword", foreground="blue")
text.tag_configure("string", foreground="green")
text.tag_add("keyword", "1.0", "1.3")  # "def"
text.tag_add("string", "2.10", "2.17")  # "'Hello'"

# PySide6 QTextEdit with syntax highlighter
from PySide6.QtGui import QSyntaxHighlighter, QTextCharFormat, QFont

class PythonHighlighter(QSyntaxHighlighter):
    def __init__(self, document):
        super().__init__(document)
        self.highlighting_rules = []
        
        # Keyword format
        keyword_format = QTextCharFormat()
        keyword_format.setForeground(QColor("blue"))
        keyword_format.setFontWeight(QFont.Bold)
        keywords = ["def", "class", "import", "from", "if", "else", "return"]
        for word in keywords:
            pattern = f"\\b{word}\\b"
            self.highlighting_rules.append((QRegularExpression(pattern), keyword_format))
        
        # String format
        string_format = QTextCharFormat()
        string_format.setForeground(QColor("green"))
        self.highlighting_rules.append((QRegularExpression("\".*\"|'.*'"), string_format))
    
    def highlightBlock(self, text):
        for pattern, format in self.highlighting_rules:
            expression = QRegularExpression(pattern)
            it = expression.globalMatch(text)
            while it.hasNext():
                match = it.next()
                self.setFormat(match.capturedStart(), match.capturedLength(), format)

# Usage
text_edit = QTextEdit(parent)
text_edit.setPlainText("def hello():\n    print('Hello')")
highlighter = PythonHighlighter(text_edit.document())
```

### Canvas → QGraphicsView

```python
# Tkinter Canvas
canvas = tk.Canvas(parent, width=400, height=300, bg="white")
canvas.pack()

# Draw shapes
rect = canvas.create_rectangle(10, 10, 100, 100, fill="blue", outline="black")
circle = canvas.create_oval(150, 50, 250, 150, fill="red")
line = canvas.create_line(0, 0, 400, 300, width=2, fill="green")
text = canvas.create_text(200, 200, text="Hello", font=("Arial", 16))

# Move items
canvas.move(rect, 50, 50)

# PySide6 QGraphicsView
from PySide6.QtWidgets import QGraphicsView, QGraphicsScene
from PySide6.QtGui import QBrush, QPen, QFont

scene = QGraphicsScene()
view = QGraphicsView(scene, parent)
view.setFixedSize(400, 300)
view.setBackgroundBrush(QBrush(Qt.white))

# Draw shapes
rect = scene.addRect(10, 10, 90, 90, QPen(Qt.black), QBrush(Qt.blue))
circle = scene.addEllipse(150, 50, 100, 100, QPen(Qt.black), QBrush(Qt.red))
line = scene.addLine(0, 0, 400, 300, QPen(Qt.green, 2))
text = scene.addText("Hello", QFont("Arial", 16))
text.setPos(200, 200)

# Move items
rect.moveBy(50, 50)
```

## Event Handling Patterns

### Mouse Events

```python
# Tkinter
widget.bind("<Button-1>", lambda e: on_left_click(e.x, e.y))
widget.bind("<Button-3>", on_right_click)
widget.bind("<Double-Button-1>", on_double_click)
widget.bind("<Enter>", on_mouse_enter)
widget.bind("<Leave>", on_mouse_leave)
widget.bind("<Motion>", lambda e: on_mouse_move(e.x, e.y))

# PySide6
class CustomWidget(QWidget):
    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            on_left_click(event.x(), event.y())
        elif event.button() == Qt.RightButton:
            on_right_click(event)
    
    def mouseDoubleClickEvent(self, event):
        on_double_click(event)
    
    def enterEvent(self, event):
        on_mouse_enter(event)
    
    def leaveEvent(self, event):
        on_mouse_leave(event)
    
    def mouseMoveEvent(self, event):
        on_mouse_move(event.x(), event.y())
```

### Keyboard Events

```python
# Tkinter
widget.bind("<KeyPress>", on_key_press)
widget.bind("<Return>", on_enter)
widget.bind("<Escape>", on_escape)
widget.bind("<Control-s>", on_save)

# PySide6
class CustomWidget(QWidget):
    def keyPressEvent(self, event):
        if event.key() == Qt.Key_Return:
            on_enter()
        elif event.key() == Qt.Key_Escape:
            on_escape()
        elif event.key() == Qt.Key_S and event.modifiers() & Qt.ControlModifier:
            on_save()
        else:
            on_key_press(event)
```

### Focus Events

```python
# Tkinter
widget.bind("<FocusIn>", on_focus_in)
widget.bind("<FocusOut>", on_focus_out)

# PySide6
class CustomWidget(QWidget):
    def focusInEvent(self, event):
        on_focus_in(event)
        super().focusInEvent(event)
    
    def focusOutEvent(self, event):
        on_focus_out(event)
        super().focusOutEvent(event)
```

## Common Pitfalls and Solutions

### 1. Widget Sizing

**Problem**: Tkinter uses character-based sizing, Qt uses pixels
```python
# Tkinter
entry = ttk.Entry(parent, width=30)  # 30 characters

# PySide6 - Wrong
entry = QLineEdit(parent)
entry.setFixedWidth(30)  # Too small! 30 pixels

# PySide6 - Correct
entry = QLineEdit(parent)
entry.setFixedWidth(250)  # Approximate pixel equivalent
# Or better: let layout handle sizing
entry.setMinimumWidth(200)
```

### 2. Widget Updates from Threads

**Problem**: Qt requires GUI updates from main thread only
```python
# Tkinter (works but not ideal)
def worker_thread():
    result = do_work()
    label.configure(text=result)  # Direct update

# PySide6 - Wrong
def worker_thread():
    result = do_work()
    label.setText(result)  # Will crash!

# PySide6 - Correct
class Worker(QThread):
    result_ready = Signal(str)
    
    def run(self):
        result = do_work()
        self.result_ready.emit(result)  # Signal to main thread

# In main thread
worker = Worker()
worker.result_ready.connect(label.setText)
worker.start()
```

### 3. Parent-Child Relationships

**Problem**: Qt's parent-child system differs from Tkinter
```python
# Tkinter
frame = ttk.Frame(parent)
label = ttk.Label(frame, text="Hello")  # Parent in constructor

# PySide6
frame = QFrame(parent)
layout = QVBoxLayout(frame)  # Layout manages children
label = QLabel("Hello")  # No parent needed
layout.addWidget(label)  # Layout sets parent
```

### 4. Event Propagation

**Problem**: Event handling differs between frameworks
```python
# Tkinter
def on_click(event):
    event.widget  # Widget that received event
    return "break"  # Stop propagation

# PySide6
def mousePressEvent(self, event):
    # Process event
    event.accept()  # Stop propagation
    # or
    event.ignore()  # Continue propagation
    # or
    super().mousePressEvent(event)  # Pass to parent
```

### 5. Modal Dialog Execution

**Problem**: Different modal dialog patterns
```python
# Tkinter
dialog = tk.Toplevel(parent)
dialog.transient(parent)
dialog.grab_set()
parent.wait_window(dialog)  # Blocks until closed

# PySide6
dialog = QDialog(parent)
dialog.setModal(True)
result = dialog.exec()  # Blocks and returns result
if result == QDialog.Accepted:
    # Handle OK
else:
    # Handle Cancel
```

### 6. Timer/After Equivalent

**Problem**: Tkinter's after() vs Qt's QTimer
```python
# Tkinter
def update():
    # Do something
    root.after(1000, update)  # Repeat every second

root.after(1000, update)  # Start timer

# PySide6
timer = QTimer()
timer.timeout.connect(update)
timer.start(1000)  # Repeat every second

# Or for single shot
QTimer.singleShot(1000, update)
```

## Best Practices Summary

1. **Always use layouts** - Never use absolute positioning
2. **Leverage Qt's signal/slot system** - Don't try to replicate Tkinter patterns
3. **Use Qt's built-in features** - Scrollbars, validators, completers
4. **Follow Qt naming conventions** - setXxx(), xxx() for properties
5. **Test on target platforms** - Qt can behave differently across OS
6. **Use Qt Designer for complex UIs** - Can save significant time
7. **Profile memory usage** - Qt widgets can be heavier than Tkinter

## Migration Checklist

- [ ] Identify all widget types used in current codebase
- [ ] Map each widget to Qt equivalent
- [ ] Convert variable bindings to signal/slot connections
- [ ] Replace pack/grid with appropriate Qt layouts
- [ ] Update event handlers to Qt event system
- [ ] Test each converted widget for functionality
- [ ] Verify visual appearance matches requirements
- [ ] Check memory usage and performance
- [ ] Update documentation and comments

## Next Steps

1. Create the `qt_widgets.py` helper module
2. Start with simple widgets (labels, buttons)
3. Progress to complex widgets (trees, text editors)
4. Test each conversion thoroughly
5. Document any framework-specific behavior