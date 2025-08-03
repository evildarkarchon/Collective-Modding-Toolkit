# Complex Widget Conversion Examples

This guide focuses on converting complex Tkinter widgets to PySide6, with complete working examples.

## Table of Contents
- [Treeview to QTreeWidget](#treeview-to-qtreewidget)
- [Text Widget with Tags](#text-widget-with-tags)
- [Canvas to QGraphicsView](#canvas-to-qgraphicsview)
- [Custom Scrollable Frame](#custom-scrollable-frame)
- [Notebook with Dynamic Tabs](#notebook-with-dynamic-tabs)
- [Complex Event Handling](#complex-event-handling)

## Treeview to QTreeWidget

### Basic Treeview with Columns

#### Tkinter Implementation
```python
import tkinter as tk
from tkinter import ttk

class FileExplorer:
    def __init__(self, parent):
        # Create treeview with columns
        self.tree = ttk.Treeview(parent, columns=("Size", "Modified", "Type"), 
                                show="tree headings", height=15)
        
        # Configure columns
        self.tree.heading("#0", text="Name", anchor="w")
        self.tree.heading("Size", text="Size", anchor="e")
        self.tree.heading("Modified", text="Modified", anchor="center")
        self.tree.heading("Type", text="Type", anchor="w")
        
        # Set column widths
        self.tree.column("#0", width=300, minwidth=100)
        self.tree.column("Size", width=100, minwidth=60)
        self.tree.column("Modified", width=150, minwidth=100)
        self.tree.column("Type", width=100, minwidth=60)
        
        # Add scrollbars
        vsb = ttk.Scrollbar(parent, orient="vertical", command=self.tree.yview)
        hsb = ttk.Scrollbar(parent, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)
        
        # Grid layout
        self.tree.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")
        
        # Populate with sample data
        self.populate_tree()
        
        # Bind events
        self.tree.bind("<<TreeviewSelect>>", self.on_select)
        self.tree.bind("<Double-1>", self.on_double_click)
        self.tree.bind("<Button-3>", self.on_right_click)
    
    def populate_tree(self):
        # Add root items
        folder1 = self.tree.insert("", "end", text="Documents", 
                                  values=("", "2024-01-15", "Folder"),
                                  open=True, tags=("folder",))
        folder2 = self.tree.insert("", "end", text="Pictures", 
                                  values=("", "2024-01-10", "Folder"),
                                  tags=("folder",))
        
        # Add child items
        self.tree.insert(folder1, "end", text="report.pdf", 
                        values=("2.5 MB", "2024-01-14", "PDF"),
                        tags=("file", "pdf"))
        self.tree.insert(folder1, "end", text="notes.txt", 
                        values=("15 KB", "2024-01-13", "Text"),
                        tags=("file", "text"))
        
        # Configure tags for styling
        self.tree.tag_configure("folder", foreground="blue")
        self.tree.tag_configure("pdf", foreground="red")
        self.tree.tag_configure("text", foreground="green")
    
    def on_select(self, event):
        selection = self.tree.selection()
        if selection:
            item = selection[0]
            values = self.tree.item(item)
            print(f"Selected: {values['text']}")
    
    def on_double_click(self, event):
        item = self.tree.identify("item", event.x, event.y)
        if item:
            self.tree.item(item, open=not self.tree.item(item, "open"))
    
    def on_right_click(self, event):
        item = self.tree.identify("item", event.x, event.y)
        if item:
            self.tree.selection_set(item)
            # Show context menu
```

#### PySide6 Implementation
```python
from PySide6.QtWidgets import (QTreeWidget, QTreeWidgetItem, QVBoxLayout, 
                              QWidget, QMenu, QHeaderView)
from PySide6.QtCore import Qt, QDateTime
from PySide6.QtGui import QColor, QAction

class FileExplorer(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        
        # Create tree widget
        self.tree = QTreeWidget()
        self.tree.setColumnCount(4)
        self.tree.setHeaderLabels(["Name", "Size", "Modified", "Type"])
        
        # Configure columns
        header = self.tree.header()
        header.setDefaultAlignment(Qt.AlignLeft)
        self.tree.header().setSectionResizeMode(0, QHeaderView.Interactive)
        
        # Set column widths
        self.tree.setColumnWidth(0, 300)
        self.tree.setColumnWidth(1, 100)
        self.tree.setColumnWidth(2, 150)
        self.tree.setColumnWidth(3, 100)
        
        # Column alignments
        self.tree.headerItem().setTextAlignment(1, Qt.AlignRight)
        self.tree.headerItem().setTextAlignment(2, Qt.AlignCenter)
        
        layout.addWidget(self.tree)
        
        # Populate with sample data
        self.populate_tree()
        
        # Connect events
        self.tree.itemSelectionChanged.connect(self.on_select)
        self.tree.itemDoubleClicked.connect(self.on_double_click)
        self.tree.setContextMenuPolicy(Qt.CustomContextMenu)
        self.tree.customContextMenuRequested.connect(self.on_right_click)
    
    def populate_tree(self):
        # Add root items
        folder1 = QTreeWidgetItem(self.tree)
        folder1.setText(0, "Documents")
        folder1.setText(2, "2024-01-15")
        folder1.setText(3, "Folder")
        folder1.setExpanded(True)
        folder1.setForeground(0, QColor("blue"))
        folder1.setData(0, Qt.UserRole, "folder")  # Store tag data
        
        folder2 = QTreeWidgetItem(self.tree)
        folder2.setText(0, "Pictures")
        folder2.setText(2, "2024-01-10")
        folder2.setText(3, "Folder")
        folder2.setForeground(0, QColor("blue"))
        folder2.setData(0, Qt.UserRole, "folder")
        
        # Add child items
        file1 = QTreeWidgetItem(folder1)
        file1.setText(0, "report.pdf")
        file1.setText(1, "2.5 MB")
        file1.setText(2, "2024-01-14")
        file1.setText(3, "PDF")
        file1.setTextAlignment(1, Qt.AlignRight)
        file1.setForeground(0, QColor("red"))
        file1.setData(0, Qt.UserRole, "pdf")
        
        file2 = QTreeWidgetItem(folder1)
        file2.setText(0, "notes.txt")
        file2.setText(1, "15 KB")
        file2.setText(2, "2024-01-13")
        file2.setText(3, "Text")
        file2.setTextAlignment(1, Qt.AlignRight)
        file2.setForeground(0, QColor("green"))
        file2.setData(0, Qt.UserRole, "text")
    
    def on_select(self):
        items = self.tree.selectedItems()
        if items:
            item = items[0]
            print(f"Selected: {item.text(0)}")
    
    def on_double_click(self, item, column):
        if item.childCount() > 0:
            item.setExpanded(not item.isExpanded())
    
    def on_right_click(self, position):
        item = self.tree.itemAt(position)
        if item:
            menu = QMenu()
            open_action = menu.addAction("Open")
            delete_action = menu.addAction("Delete")
            
            action = menu.exec(self.tree.mapToGlobal(position))
            if action == open_action:
                print(f"Open {item.text(0)}")
            elif action == delete_action:
                print(f"Delete {item.text(0)}")
```

### Advanced Treeview Features

#### Sorting and Filtering
```python
# Tkinter - Manual sorting
def sort_tree(tree, col, descending):
    data = [(tree.set(child, col), child) for child in tree.get_children('')]
    data.sort(reverse=descending)
    for index, (val, child) in enumerate(data):
        tree.move(child, '', index)

# PySide6 - Built-in sorting
tree.setSortingEnabled(True)
tree.sortByColumn(0, Qt.AscendingOrder)

# Custom sorting
class TreeWidgetItem(QTreeWidgetItem):
    def __lt__(self, other):
        column = self.treeWidget().sortColumn()
        if column == 1:  # Size column - sort numerically
            return self.data(column, Qt.UserRole) < other.data(column, Qt.UserRole)
        return super().__lt__(other)
```

#### Drag and Drop
```python
# PySide6 implementation
class DragDropTree(QTreeWidget):
    def __init__(self):
        super().__init__()
        self.setDragDropMode(QTreeWidget.InternalMove)
        self.setDefaultDropAction(Qt.MoveAction)
    
    def dragEnterEvent(self, event):
        if event.mimeData().hasFormat("application/x-qabstractitemmodeldatalist"):
            event.accept()
        else:
            event.ignore()
    
    def dropEvent(self, event):
        # Handle drop logic
        super().dropEvent(event)
```

## Text Widget with Tags

### Syntax Highlighting Example

#### Tkinter Implementation
```python
class CodeEditor:
    def __init__(self, parent):
        self.text = tk.Text(parent, wrap="none", undo=True)
        self.text.pack(fill="both", expand=True)
        
        # Configure tags for syntax highlighting
        self.text.tag_configure("keyword", foreground="blue", font=("Courier", 10, "bold"))
        self.text.tag_configure("string", foreground="green")
        self.text.tag_configure("comment", foreground="gray", font=("Courier", 10, "italic"))
        self.text.tag_configure("function", foreground="purple")
        
        # Bind events for live highlighting
        self.text.bind("<KeyRelease>", self.highlight_syntax)
        
    def highlight_syntax(self, event=None):
        # Remove all tags
        self.text.tag_remove("keyword", "1.0", "end")
        self.text.tag_remove("string", "1.0", "end")
        self.text.tag_remove("comment", "1.0", "end")
        
        # Python keywords
        keywords = ["def", "class", "import", "from", "if", "else", "return", "for", "while"]
        
        content = self.text.get("1.0", "end-1c")
        
        # Highlight keywords
        for keyword in keywords:
            start = "1.0"
            while True:
                pos = self.text.search(f"\\b{keyword}\\b", start, "end", regexp=True)
                if not pos:
                    break
                end = f"{pos}+{len(keyword)}c"
                self.text.tag_add("keyword", pos, end)
                start = end
        
        # Highlight strings
        for quote in ['"', "'"]:
            start = "1.0"
            while True:
                pos = self.text.search(quote, start, "end")
                if not pos:
                    break
                end = self.text.search(quote, f"{pos}+1c", "end")
                if not end:
                    break
                self.text.tag_add("string", pos, f"{end}+1c")
                start = f"{end}+1c"
```

#### PySide6 Implementation
```python
from PySide6.QtWidgets import QTextEdit, QVBoxLayout
from PySide6.QtGui import QSyntaxHighlighter, QTextCharFormat, QFont, QColor
from PySide6.QtCore import QRegularExpression

class PythonHighlighter(QSyntaxHighlighter):
    def __init__(self, document):
        super().__init__(document)
        
        # Define formats
        self.keyword_format = QTextCharFormat()
        self.keyword_format.setForeground(QColor("blue"))
        self.keyword_format.setFontWeight(QFont.Bold)
        
        self.string_format = QTextCharFormat()
        self.string_format.setForeground(QColor("green"))
        
        self.comment_format = QTextCharFormat()
        self.comment_format.setForeground(QColor("gray"))
        self.comment_format.setFontItalic(True)
        
        self.function_format = QTextCharFormat()
        self.function_format.setForeground(QColor("purple"))
        
        # Define patterns
        self.highlighting_rules = []
        
        # Keywords
        keywords = ["def", "class", "import", "from", "if", "else", "return", "for", "while"]
        for word in keywords:
            pattern = QRegularExpression(f"\\b{word}\\b")
            self.highlighting_rules.append((pattern, self.keyword_format))
        
        # Function definitions
        function_pattern = QRegularExpression(r"\bdef\s+(\w+)")
        self.highlighting_rules.append((function_pattern, self.function_format))
        
        # Strings
        string_pattern = QRegularExpression(r"\".*\"|\'.*\'")
        self.highlighting_rules.append((string_pattern, self.string_format))
        
        # Comments
        comment_pattern = QRegularExpression(r"#[^\n]*")
        self.highlighting_rules.append((comment_pattern, self.comment_format))
    
    def highlightBlock(self, text):
        for pattern, format in self.highlighting_rules:
            expression = QRegularExpression(pattern)
            it = expression.globalMatch(text)
            while it.hasNext():
                match = it.next()
                self.setFormat(match.capturedStart(), match.capturedLength(), format)

class CodeEditor(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        
        self.text_edit = QTextEdit()
        self.text_edit.setLineWrapMode(QTextEdit.NoWrap)
        self.text_edit.setUndoRedoEnabled(True)
        
        # Apply syntax highlighter
        self.highlighter = PythonHighlighter(self.text_edit.document())
        
        layout.addWidget(self.text_edit)
```

### Text Widget with Line Numbers

```python
# PySide6 implementation with line numbers
from PySide6.QtWidgets import QPlainTextEdit, QWidget, QTextEdit
from PySide6.QtCore import QRect, Qt
from PySide6.QtGui import QPainter, QTextFormat, QColor

class LineNumberArea(QWidget):
    def __init__(self, editor):
        super().__init__(editor)
        self.code_editor = editor

    def sizeHint(self):
        return QSize(self.code_editor.line_number_area_width(), 0)

    def paintEvent(self, event):
        self.code_editor.line_number_area_paint_event(event)

class CodeEditorWithNumbers(QPlainTextEdit):
    def __init__(self):
        super().__init__()
        self.line_number_area = LineNumberArea(self)
        
        self.blockCountChanged.connect(self.update_line_number_area_width)
        self.updateRequest.connect(self.update_line_number_area)
        self.cursorPositionChanged.connect(self.highlight_current_line)
        
        self.update_line_number_area_width(0)
        self.highlight_current_line()
    
    def line_number_area_width(self):
        digits = 1
        max_value = max(1, self.blockCount())
        while max_value >= 10:
            max_value /= 10
            digits += 1
        space = 3 + self.fontMetrics().horizontalAdvance('9') * digits
        return space
    
    def update_line_number_area_width(self, _):
        self.setViewportMargins(self.line_number_area_width(), 0, 0, 0)
    
    def update_line_number_area(self, rect, dy):
        if dy:
            self.line_number_area.scroll(0, dy)
        else:
            self.line_number_area.update(0, rect.y(), 
                                       self.line_number_area.width(), 
                                       rect.height())
        
        if rect.contains(self.viewport().rect()):
            self.update_line_number_area_width(0)
    
    def resizeEvent(self, event):
        super().resizeEvent(event)
        cr = self.contentsRect()
        self.line_number_area.setGeometry(QRect(cr.left(), cr.top(),
                                              self.line_number_area_width(),
                                              cr.height()))
    
    def line_number_area_paint_event(self, event):
        painter = QPainter(self.line_number_area)
        painter.fillRect(event.rect(), Qt.lightGray)
        
        block = self.firstVisibleBlock()
        block_number = block.blockNumber()
        top = self.blockBoundingGeometry(block).translated(self.contentOffset()).top()
        bottom = top + self.blockBoundingRect(block).height()
        
        while block.isValid() and top <= event.rect().bottom():
            if block.isVisible() and bottom >= event.rect().top():
                number = str(block_number + 1)
                painter.setPen(Qt.black)
                painter.drawText(0, top, self.line_number_area.width(), 
                               self.fontMetrics().height(),
                               Qt.AlignRight, number)
            
            block = block.next()
            top = bottom
            bottom = top + self.blockBoundingRect(block).height()
            block_number += 1
    
    def highlight_current_line(self):
        extra_selections = []
        
        if not self.isReadOnly():
            selection = QTextEdit.ExtraSelection()
            
            line_color = QColor(Qt.yellow).lighter(160)
            selection.format.setBackground(line_color)
            selection.format.setProperty(QTextFormat.FullWidthSelection, True)
            selection.cursor = self.textCursor()
            selection.cursor.clearSelection()
            extra_selections.append(selection)
        
        self.setExtraSelections(extra_selections)
```

## Canvas to QGraphicsView

### Basic Drawing Example

#### Tkinter Implementation
```python
class DrawingCanvas:
    def __init__(self, parent):
        self.canvas = tk.Canvas(parent, bg="white", width=600, height=400)
        self.canvas.pack(fill="both", expand=True)
        
        # Drawing state
        self.drawing = False
        self.current_item = None
        self.start_x = None
        self.start_y = None
        
        # Bind mouse events
        self.canvas.bind("<Button-1>", self.start_draw)
        self.canvas.bind("<B1-Motion>", self.draw)
        self.canvas.bind("<ButtonRelease-1>", self.end_draw)
        
        # Create some shapes
        self.canvas.create_rectangle(50, 50, 150, 100, fill="blue", tags="rect")
        self.canvas.create_oval(200, 50, 300, 150, fill="red", tags="circle")
        self.canvas.create_line(350, 100, 450, 150, width=3, fill="green", tags="line")
        
        # Enable item dragging
        self.canvas.tag_bind("rect", "<Button-1>", self.on_item_click)
        self.canvas.tag_bind("circle", "<Button-1>", self.on_item_click)
        
    def start_draw(self, event):
        self.drawing = True
        self.start_x = event.x
        self.start_y = event.y
        self.current_item = self.canvas.create_rectangle(
            event.x, event.y, event.x, event.y, outline="black"
        )
    
    def draw(self, event):
        if self.drawing and self.current_item:
            self.canvas.coords(self.current_item, 
                             self.start_x, self.start_y, event.x, event.y)
    
    def end_draw(self, event):
        self.drawing = False
        self.current_item = None
```

#### PySide6 Implementation
```python
from PySide6.QtWidgets import QGraphicsView, QGraphicsScene, QGraphicsItem
from PySide6.QtGui import QPen, QBrush, QColor
from PySide6.QtCore import QPointF, QRectF

class DrawingCanvas(QGraphicsView):
    def __init__(self, parent=None):
        super().__init__(parent)
        
        # Create scene
        self.scene = QGraphicsScene()
        self.setScene(self.scene)
        self.setSceneRect(0, 0, 600, 400)
        
        # Drawing state
        self.drawing = False
        self.current_item = None
        self.start_pos = None
        
        # Create some shapes
        rect = self.scene.addRect(50, 50, 100, 50, QPen(Qt.black), QBrush(QColor("blue")))
        rect.setFlag(QGraphicsItem.ItemIsMovable)
        rect.setFlag(QGraphicsItem.ItemIsSelectable)
        
        ellipse = self.scene.addEllipse(200, 50, 100, 100, QPen(Qt.black), QBrush(QColor("red")))
        ellipse.setFlag(QGraphicsItem.ItemIsMovable)
        ellipse.setFlag(QGraphicsItem.ItemIsSelectable)
        
        line = self.scene.addLine(350, 100, 450, 150, QPen(QColor("green"), 3))
        
    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            # Check if clicking on empty space
            scene_pos = self.mapToScene(event.pos())
            item = self.scene.itemAt(scene_pos, self.transform())
            
            if not item:
                self.drawing = True
                self.start_pos = scene_pos
                self.current_item = self.scene.addRect(
                    QRectF(scene_pos, scene_pos), QPen(Qt.black)
                )
        
        super().mousePressEvent(event)
    
    def mouseMoveEvent(self, event):
        if self.drawing and self.current_item:
            scene_pos = self.mapToScene(event.pos())
            rect = QRectF(self.start_pos, scene_pos).normalized()
            self.current_item.setRect(rect)
        
        super().mouseMoveEvent(event)
    
    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton:
            self.drawing = False
            self.current_item = None
        
        super().mouseReleaseEvent(event)
```

## Custom Scrollable Frame

### Tkinter Implementation
```python
class ScrollableFrame(ttk.Frame):
    def __init__(self, container, *args, **kwargs):
        super().__init__(container, *args, **kwargs)
        
        canvas = tk.Canvas(self)
        scrollbar = ttk.Scrollbar(self, orient="vertical", command=canvas.yview)
        self.scrollable_frame = ttk.Frame(canvas)
        
        self.scrollable_frame.bind(
            "<Configure>",
            lambda e: canvas.configure(scrollregion=canvas.bbox("all"))
        )
        
        canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        canvas.configure(yscrollcommand=scrollbar.set)
        
        canvas.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")
        
        # Bind mousewheel
        canvas.bind_all("<MouseWheel>", lambda e: canvas.yview_scroll(
            int(-1*(e.delta/120)), "units"))
```

### PySide6 Implementation
```python
class ScrollableFrame(QScrollArea):
    def __init__(self, parent=None):
        super().__init__(parent)
        
        # Create widget to hold content
        self.scroll_widget = QWidget()
        self.scroll_layout = QVBoxLayout(self.scroll_widget)
        
        # Set the widget as scroll area's widget
        self.setWidget(self.scroll_widget)
        self.setWidgetResizable(True)
        
        # Enable mouse wheel scrolling
        self.setVerticalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAsNeeded)
    
    def add_widget(self, widget):
        """Helper method to add widgets to the scrollable area"""
        self.scroll_layout.addWidget(widget)
```

## Notebook with Dynamic Tabs

### Tkinter Implementation
```python
class DynamicNotebook:
    def __init__(self, parent):
        self.notebook = ttk.Notebook(parent)
        self.notebook.pack(fill="both", expand=True)
        
        # Add initial tabs
        self.tab_counter = 0
        self.add_tab()
        
        # Add control buttons
        control_frame = ttk.Frame(parent)
        control_frame.pack(side="bottom", fill="x")
        
        ttk.Button(control_frame, text="Add Tab", command=self.add_tab).pack(side="left")
        ttk.Button(control_frame, text="Remove Tab", command=self.remove_tab).pack(side="left")
        
        # Enable tab reordering (custom implementation needed)
        self.notebook.bind("<ButtonPress-1>", self.on_tab_press)
        self.notebook.bind("<B1-Motion>", self.on_tab_drag)
        self.notebook.bind("<ButtonRelease-1>", self.on_tab_release)
    
    def add_tab(self):
        self.tab_counter += 1
        frame = ttk.Frame(self.notebook)
        self.notebook.add(frame, text=f"Tab {self.tab_counter}")
        
        # Add content to tab
        ttk.Label(frame, text=f"Content of Tab {self.tab_counter}").pack(pady=20)
    
    def remove_tab(self):
        current = self.notebook.index("current")
        if self.notebook.index("end") > 1:  # Keep at least one tab
            self.notebook.forget(current)
```

### PySide6 Implementation
```python
from PySide6.QtWidgets import QTabWidget, QWidget, QPushButton, QHBoxLayout

class DynamicNotebook(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        
        # Create tab widget
        self.tab_widget = QTabWidget()
        self.tab_widget.setTabsClosable(True)
        self.tab_widget.setMovable(True)  # Enable tab reordering
        self.tab_widget.tabCloseRequested.connect(self.close_tab)
        
        layout.addWidget(self.tab_widget)
        
        # Control buttons
        button_layout = QHBoxLayout()
        add_button = QPushButton("Add Tab")
        add_button.clicked.connect(self.add_tab)
        button_layout.addWidget(add_button)
        button_layout.addStretch()
        
        layout.addLayout(button_layout)
        
        # Add initial tab
        self.tab_counter = 0
        self.add_tab()
    
    def add_tab(self):
        self.tab_counter += 1
        tab = QWidget()
        tab_layout = QVBoxLayout(tab)
        
        label = QLabel(f"Content of Tab {self.tab_counter}")
        tab_layout.addWidget(label)
        tab_layout.addStretch()
        
        self.tab_widget.addTab(tab, f"Tab {self.tab_counter}")
        self.tab_widget.setCurrentWidget(tab)
    
    def close_tab(self, index):
        if self.tab_widget.count() > 1:  # Keep at least one tab
            self.tab_widget.removeTab(index)
```

## Complex Event Handling

### Multi-Button Mouse Events

```python
# PySide6 implementation with complex mouse handling
class InteractiveWidget(QWidget):
    def __init__(self):
        super().__init__()
        self.setMouseTracking(True)  # Track mouse without button press
        
        # State tracking
        self.left_pressed = False
        self.right_pressed = False
        self.middle_pressed = False
        self.last_pos = None
        
    def mousePressEvent(self, event):
        self.last_pos = event.pos()
        
        if event.button() == Qt.LeftButton:
            self.left_pressed = True
            if event.modifiers() & Qt.ControlModifier:
                print("Ctrl+Left click")
            elif event.modifiers() & Qt.ShiftModifier:
                print("Shift+Left click")
            else:
                print("Left click")
                
        elif event.button() == Qt.RightButton:
            self.right_pressed = True
            self.show_context_menu(event.globalPos())
            
        elif event.button() == Qt.MiddleButton:
            self.middle_pressed = True
            print("Middle click")
    
    def mouseMoveEvent(self, event):
        if self.last_pos:
            delta = event.pos() - self.last_pos
            
            if self.left_pressed:
                print(f"Dragging: {delta}")
            elif self.middle_pressed:
                print(f"Panning: {delta}")
        
        self.last_pos = event.pos()
        
        # Hover effects
        self.update()  # Trigger repaint for hover visualization
    
    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton:
            self.left_pressed = False
        elif event.button() == Qt.RightButton:
            self.right_pressed = False
        elif event.button() == Qt.MiddleButton:
            self.middle_pressed = False
    
    def wheelEvent(self, event):
        delta = event.angleDelta().y()
        if event.modifiers() & Qt.ControlModifier:
            # Zoom
            zoom_factor = 1.1 if delta > 0 else 0.9
            print(f"Zoom: {zoom_factor}")
        else:
            # Scroll
            print(f"Scroll: {delta}")
    
    def show_context_menu(self, pos):
        menu = QMenu(self)
        menu.addAction("Copy", self.copy)
        menu.addAction("Paste", self.paste)
        menu.addSeparator()
        menu.addAction("Delete", self.delete)
        menu.exec(pos)
```

### Keyboard Shortcuts and Accelerators

```python
# PySide6 implementation
from PySide6.QtGui import QKeySequence, QShortcut

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        
        # Menu shortcuts
        menu_bar = self.menuBar()
        file_menu = menu_bar.addMenu("File")
        
        new_action = file_menu.addAction("New")
        new_action.setShortcut(QKeySequence.New)  # Ctrl+N
        new_action.triggered.connect(self.new_file)
        
        # Custom shortcuts
        save_shortcut = QShortcut(QKeySequence("Ctrl+S"), self)
        save_shortcut.activated.connect(self.save_file)
        
        # Multiple key sequences
        quit_shortcut = QShortcut(self)
        quit_shortcut.setKeys([QKeySequence("Ctrl+Q"), QKeySequence("Alt+F4")])
        quit_shortcut.activated.connect(self.close)
        
    def keyPressEvent(self, event):
        # Handle special keys
        if event.key() == Qt.Key_F1:
            self.show_help()
        elif event.key() == Qt.Key_Escape:
            self.cancel_operation()
        elif event.matches(QKeySequence.Find):  # Ctrl+F
            self.show_find_dialog()
        
        super().keyPressEvent(event)
```

## Migration Tips for Complex Widgets

1. **Event Handling**: Qt's event system is more granular - use specific event methods
2. **Custom Drawing**: Use QGraphicsView for complex graphics instead of Canvas
3. **Performance**: Qt widgets generally perform better with large datasets
4. **Styling**: Use stylesheets for complex styling instead of tag configurations
5. **Data Models**: Consider using Qt's Model/View architecture for complex data displays

## Testing Complex Conversions

Always test:
- Event propagation and handling
- Performance with large datasets
- Custom painting and animations
- Drag and drop functionality
- Keyboard navigation
- Context menus and tooltips
- State persistence across operations