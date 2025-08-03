# Tkinter to PySide6 Property and Method Mapping Guide

This guide provides detailed mappings of widget properties and methods from Tkinter to PySide6.

## Common Widget Properties

### Configuration Methods

| Tkinter | PySide6 | Notes |
|---------|---------|-------|
| `widget.configure(option=value)` | `widget.setProperty(value)` | Use specific setter methods |
| `widget.cget("option")` | `widget.property()` | Use specific getter methods |
| `widget["option"] = value` | `widget.setProperty(value)` | Dictionary-style access not supported |

### State Management

| Tkinter State | PySide6 Method | Notes |
|---------------|----------------|-------|
| `state="normal"` | `setEnabled(True)` | Enable widget |
| `state="disabled"` | `setEnabled(False)` | Disable widget |
| `state="readonly"` | `setReadOnly(True)` | For text widgets only |
| `state="active"` | `setDown(True)` | For buttons |

## Widget-Specific Property Mappings

### Label (ttk.Label → QLabel)

| Tkinter Property | PySide6 Method | Example |
|-----------------|----------------|---------|
| `text="..."` | `setText("...")` | `label.setText("Hello")` |
| `textvariable=var` | Connect to variable signal | `var.changed.connect(label.setText)` |
| `font=("Arial", 12)` | `setFont(QFont("Arial", 12))` | Set font |
| `foreground="#fff"` | `setStyleSheet("color: #fff;")` | Text color |
| `background="#000"` | `setStyleSheet("background-color: #000;")` | Background |
| `anchor="w"` | `setAlignment(Qt.AlignLeft)` | Text alignment |
| `justify="center"` | `setAlignment(Qt.AlignCenter)` | Multi-line alignment |
| `wraplength=200` | `setWordWrap(True)` + `setFixedWidth(200)` | Text wrapping |
| `image=img` | `setPixmap(QPixmap)` | Display image |
| `compound="left"` | Use QHBoxLayout with icon and text | Image+text layout |

### Button (ttk.Button → QPushButton)

| Tkinter Property | PySide6 Method | Example |
|-----------------|----------------|---------|
| `text="..."` | `setText("...")` | Button label |
| `command=func` | `clicked.connect(func)` | Click handler |
| `state="disabled"` | `setEnabled(False)` | Disable button |
| `width=10` | `setFixedWidth(100)` | Approximate width |
| `image=img` | `setIcon(QIcon(QPixmap))` | Button icon |
| `default="active"` | `setDefault(True)` | Default button |

### Entry (ttk.Entry → QLineEdit)

| Tkinter Property/Method | PySide6 Method | Example |
|------------------------|----------------|---------|
| `textvariable=var` | Two-way binding needed | See variable binding section |
| `width=30` | `setFixedWidth(300)` | Approximate width |
| `show="*"` | `setEchoMode(QLineEdit.Password)` | Password field |
| `state="readonly"` | `setReadOnly(True)` | Read-only |
| `insert(0, "text")` | `insert("text")` | Insert text |
| `delete(0, "end")` | `clear()` | Clear all text |
| `get()` | `text()` | Get current text |
| `selection_range(0, "end")` | `selectAll()` | Select all text |
| `focus_set()` | `setFocus()` | Set keyboard focus |
| `validate="key"` | Use `QValidator` | Input validation |

### Text (tk.Text → QTextEdit/QPlainTextEdit)

| Tkinter Method | PySide6 Method | Example |
|----------------|----------------|---------|
| `insert("1.0", "text")` | `insertPlainText("text")` | Insert at cursor |
| `insert("end", "text")` | `append("text")` | Append text |
| `get("1.0", "end-1c")` | `toPlainText()` | Get all text |
| `delete("1.0", "end")` | `clear()` | Clear all text |
| `see("end")` | `moveCursor(QTextCursor.End)` | Scroll to position |
| `tag_configure(...)` | Use `QTextCharFormat` | Text formatting |
| `tag_add(...)` | Use `QTextCursor` selection | Apply formatting |
| `config(state="disabled")` | `setReadOnly(True)` | Make read-only |
| `config(wrap="word")` | `setLineWrapMode(QTextEdit.WidgetWidth)` | Word wrap |
| `yview_moveto(1.0)` | `verticalScrollBar().setValue(max)` | Scroll to bottom |

### Checkbutton (ttk.Checkbutton → QCheckBox)

| Tkinter Property | PySide6 Method | Example |
|-----------------|----------------|---------|
| `text="..."` | `setText("...")` | Checkbox label |
| `variable=var` | Two-way binding | See variable section |
| `command=func` | `stateChanged.connect(func)` | Change handler |
| `onvalue=1` | Use `isChecked()` | Checked = True |
| `offvalue=0` | Use `isChecked()` | Unchecked = False |

### Radiobutton (ttk.Radiobutton → QRadioButton)

| Tkinter Property | PySide6 Method | Example |
|-----------------|----------------|---------|
| `text="..."` | `setText("...")` | Radio label |
| `variable=var` | Use `QButtonGroup` | Group management |
| `value="opt1"` | `setObjectName("opt1")` | Identify option |
| `command=func` | `toggled.connect(func)` | Change handler |

### Combobox (ttk.Combobox → QComboBox)

| Tkinter Property/Method | PySide6 Method | Example |
|------------------------|----------------|---------|
| `values=["a", "b"]` | `addItems(["a", "b"])` | Set options |
| `textvariable=var` | `currentTextChanged.connect()` | Text binding |
| `current(0)` | `setCurrentIndex(0)` | Select by index |
| `get()` | `currentText()` | Get selected text |
| `set("value")` | `setCurrentText("value")` | Set by text |
| `state="readonly"` | `setEditable(False)` | Prevent editing |
| `<<ComboboxSelected>>` | `currentIndexChanged` | Selection event |

### Listbox (tk.Listbox → QListWidget)

| Tkinter Method | PySide6 Method | Example |
|----------------|----------------|---------|
| `insert(END, "item")` | `addItem("item")` | Add item |
| `delete(0, END)` | `clear()` | Remove all |
| `get(0, END)` | `[item.text() for item in ...]` | Get all items |
| `curselection()` | `selectedIndexes()` | Get selection |
| `selection_set(0)` | `setCurrentRow(0)` | Select item |
| `see(index)` | `scrollToItem(item)` | Scroll to item |
| `size()` | `count()` | Number of items |

### Scale (ttk.Scale → QSlider)

| Tkinter Property | PySide6 Method | Example |
|-----------------|----------------|---------|
| `from_=0` | `setMinimum(0)` | Min value |
| `to=100` | `setMaximum(100)` | Max value |
| `orient="horizontal"` | `Qt.Horizontal` in constructor | Orientation |
| `variable=var` | `valueChanged.connect()` | Value binding |
| `command=func` | `valueChanged.connect(func)` | Change handler |
| `get()` | `value()` | Get current value |
| `set(50)` | `setValue(50)` | Set value |

### Progressbar (ttk.Progressbar → QProgressBar)

| Tkinter Property | PySide6 Method | Example |
|-----------------|----------------|---------|
| `maximum=100` | `setMaximum(100)` | Max value |
| `value=50` | `setValue(50)` | Current value |
| `mode="indeterminate"` | `setMaximum(0)` | Indeterminate |
| `start()` | Use `QTimer` for animation | Start animation |
| `stop()` | Stop timer | Stop animation |

### Treeview (ttk.Treeview → QTreeWidget)

| Tkinter Method | PySide6 Method | Example |
|----------------|----------------|---------|
| `heading("col", text="...")` | `setHeaderLabel()` | Column header |
| `column("col", width=100)` | `setColumnWidth(idx, 100)` | Column width |
| `insert("", "end", values=(...))` | `QTreeWidgetItem(values)` | Add item |
| `item(id, values=(...))` | `item.setText(col, text)` | Update item |
| `delete(item)` | `takeTopLevelItem(idx)` | Remove item |
| `selection()` | `selectedItems()` | Get selection |
| `focus(item)` | `setCurrentItem(item)` | Focus item |
| `see(item)` | `scrollToItem(item)` | Scroll to item |

## Event Binding Mappings

### Mouse Events

| Tkinter Binding | PySide6 Method | Notes |
|-----------------|----------------|-------|
| `bind("<Button-1>", func)` | `mousePressEvent()` | Override method |
| `bind("<ButtonRelease-1>", func)` | `mouseReleaseEvent()` | Override method |
| `bind("<Double-Button-1>", func)` | `mouseDoubleClickEvent()` | Override method |
| `bind("<B1-Motion>", func)` | `mouseMoveEvent()` | With button check |
| `bind("<Enter>", func)` | `enterEvent()` | Mouse enters widget |
| `bind("<Leave>", func)` | `leaveEvent()` | Mouse leaves widget |
| `bind("<MouseWheel>", func)` | `wheelEvent()` | Mouse wheel |

### Keyboard Events

| Tkinter Binding | PySide6 Method | Notes |
|-----------------|----------------|-------|
| `bind("<KeyPress>", func)` | `keyPressEvent()` | Override method |
| `bind("<KeyRelease>", func)` | `keyReleaseEvent()` | Override method |
| `bind("<Return>", func)` | Check `event.key() == Qt.Key_Return` | In keyPressEvent |
| `bind("<Escape>", func)` | Check `event.key() == Qt.Key_Escape` | In keyPressEvent |
| `bind("<Control-a>", func)` | Check modifiers & `Qt.ControlModifier` | With key check |

### Focus Events

| Tkinter Binding | PySide6 Method | Notes |
|-----------------|----------------|-------|
| `bind("<FocusIn>", func)` | `focusInEvent()` | Override method |
| `bind("<FocusOut>", func)` | `focusOutEvent()` | Override method |
| `bind("<Tab>", func)` | `keyPressEvent()` with Tab check | Handle tab key |

## Window and Dialog Methods

### Window Management

| Tkinter Method | PySide6 Method | Example |
|----------------|----------------|---------|
| `title("...")` | `setWindowTitle("...")` | Window title |
| `geometry("800x600+100+100")` | `setGeometry(100, 100, 800, 600)` | Size and position |
| `minsize(400, 300)` | `setMinimumSize(400, 300)` | Minimum size |
| `maxsize(1200, 900)` | `setMaximumSize(1200, 900)` | Maximum size |
| `resizable(False, False)` | `setFixedSize(width, height)` | Prevent resize |
| `withdraw()` | `hide()` | Hide window |
| `deiconify()` | `show()` | Show window |
| `iconify()` | `showMinimized()` | Minimize |
| `state("zoomed")` | `showMaximized()` | Maximize |
| `lift()` | `raise_()` | Bring to front |
| `lower()` | `lower()` | Send to back |
| `focus_force()` | `activateWindow()` + `setFocus()` | Force focus |
| `grab_set()` | `setModal(True)` | Modal behavior |
| `transient(parent)` | Set parent in constructor | Child window |
| `protocol("WM_DELETE_WINDOW", func)` | `closeEvent()` override | Handle close |

### Dialog Methods

| Tkinter | PySide6 | Notes |
|---------|---------|-------|
| `tk.messagebox.showinfo()` | `QMessageBox.information()` | Info dialog |
| `tk.messagebox.showwarning()` | `QMessageBox.warning()` | Warning dialog |
| `tk.messagebox.showerror()` | `QMessageBox.critical()` | Error dialog |
| `tk.messagebox.askyesno()` | `QMessageBox.question()` | Yes/No dialog |
| `tk.filedialog.askopenfilename()` | `QFileDialog.getOpenFileName()` | File open |
| `tk.filedialog.asksaveasfilename()` | `QFileDialog.getSaveFileName()` | File save |
| `tk.filedialog.askdirectory()` | `QFileDialog.getExistingDirectory()` | Folder select |

## Timer and Scheduling

| Tkinter Method | PySide6 Method | Example |
|----------------|----------------|---------|
| `after(1000, func)` | `QTimer.singleShot(1000, func)` | One-time timer |
| `after(1000, func, arg)` | `QTimer.singleShot(1000, lambda: func(arg))` | With arguments |
| `after_idle(func)` | `QTimer.singleShot(0, func)` | Run when idle |
| `after_cancel(id)` | `timer.stop()` | Cancel timer |
| Repeating `after()` | `QTimer` with `start()` | Repeating timer |

## Geometry Management

### Pack Geometry Manager

| Tkinter Pack Option | Qt Layout Equivalent | Notes |
|--------------------|---------------------|-------|
| `side="top"/"bottom"` | `QVBoxLayout` | Vertical stacking |
| `side="left"/"right"` | `QHBoxLayout` | Horizontal stacking |
| `fill="x"` | Widget fills horizontally | Default in layouts |
| `fill="y"` | Use stretch factors | Vertical expansion |
| `fill="both"` | Stretch factor > 0 | Full expansion |
| `expand=True` | `addWidget(w, stretch=1)` | Use remaining space |
| `padx=10` | `layout.setContentsMargins()` | Horizontal padding |
| `pady=10` | `layout.setContentsMargins()` | Vertical padding |

### Grid Geometry Manager

| Tkinter Grid Option | Qt Grid Method | Notes |
|--------------------|----------------|-------|
| `row=0, column=1` | `addWidget(w, 0, 1)` | Position |
| `rowspan=2` | `addWidget(w, 0, 1, 2, 1)` | Span rows |
| `columnspan=2` | `addWidget(w, 0, 1, 1, 2)` | Span columns |
| `sticky="nsew"` | Default behavior | Fill cell |
| `sticky="w"` | `Qt.AlignLeft` | Align left |
| `padx/pady` | `layout.setSpacing()` | Cell spacing |
| `ipadx/ipady` | Widget margins | Internal padding |

## Variable Binding Patterns

### StringVar Replacement
```python
# Tkinter
var = tk.StringVar(value="Hello")
label = ttk.Label(parent, textvariable=var)
entry = ttk.Entry(parent, textvariable=var)
var.set("World")  # Updates both widgets

# PySide6
from qt_widgets import QtStringVar

var = QtStringVar("Hello")
label = QLabel(var.get())
entry = QLineEdit(var.get())

# Set up two-way binding
var.changed.connect(label.setText)
var.changed.connect(lambda v: entry.setText(v) if entry.text() != v else None)
entry.textChanged.connect(var.set)

var.set("World")  # Updates both widgets
```

### IntVar Replacement
```python
# Tkinter
var = tk.IntVar(value=42)
label = ttk.Label(parent, textvariable=var)
spinbox = ttk.Spinbox(parent, textvariable=var, from_=0, to=100)

# PySide6
from qt_widgets import QtIntVar

var = QtIntVar(42)
label = QLabel(str(var.get()))
spinbox = QSpinBox()
spinbox.setRange(0, 100)
spinbox.setValue(var.get())

# Set up binding
var.changed.connect(lambda v: label.setText(str(v)))
var.changed.connect(spinbox.setValue)
spinbox.valueChanged.connect(var.set)
```

## Method Chaining

Note: Qt typically doesn't support method chaining like Tkinter.

```python
# Tkinter (method chaining)
label = ttk.Label(parent, text="Hello").pack(side="left", padx=5)

# PySide6 (separate statements)
label = QLabel("Hello", parent)
layout.addWidget(label)
```

## Common Patterns

### Getting Widget Values
```python
# Tkinter
text_value = entry.get()
combo_value = combobox.get()
check_value = checkvar.get()
text_content = text_widget.get("1.0", "end-1c")

# PySide6
text_value = entry.text()
combo_value = combobox.currentText()
check_value = checkbox.isChecked()
text_content = text_widget.toPlainText()
```

### Setting Widget Values
```python
# Tkinter
entry.delete(0, "end")
entry.insert(0, "New text")
combobox.set("Option")
checkvar.set(True)
text_widget.delete("1.0", "end")
text_widget.insert("1.0", "New content")

# PySide6
entry.setText("New text")
combobox.setCurrentText("Option")
checkbox.setChecked(True)
text_widget.setPlainText("New content")
```

### Enabling/Disabling Widgets
```python
# Tkinter
widget.configure(state="disabled")
widget.configure(state="normal")
widget["state"] = "readonly"

# PySide6
widget.setEnabled(False)
widget.setEnabled(True)
widget.setReadOnly(True)  # For text widgets
```

This guide covers the most common property and method mappings. Always refer to the Qt documentation for additional methods and properties specific to each widget class.