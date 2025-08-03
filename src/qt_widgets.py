"""
Qt Widget Helpers and Compatibility Layer for Tkinter Migration

This module provides helper classes and utilities to ease the transition
from Tkinter to PySide6, including variable replacements and common patterns.
"""

from typing import Any, Callable, Optional, Union
from PySide6.QtCore import QObject, Signal, QTimer, Qt
from PySide6.QtWidgets import (
    QWidget, QLabel, QLineEdit, QPushButton, QCheckBox, 
    QComboBox, QTextEdit, QProgressBar, QMessageBox,
    QDialog, QVBoxLayout, QHBoxLayout, QGridLayout,
    QApplication
)
from PySide6.QtGui import QFont, QPixmap


class QtVariable(QObject):
    """Base class for Qt-compatible variable types."""
    
    changed = Signal(object)
    
    def __init__(self, initial_value: Any = None):
        super().__init__()
        self._value = initial_value
        self._traces: list[Callable] = []
    
    def get(self) -> Any:
        """Get the current value."""
        return self._value
    
    def set(self, value: Any) -> None:
        """Set a new value and emit change signal."""
        if self._value != value:
            self._value = value
            self.changed.emit(value)
            # Call trace callbacks for Tkinter compatibility
            for callback in self._traces:
                callback()
    
    def trace_add(self, mode: str, callback: Callable) -> str:
        """Add a trace callback (Tkinter compatibility)."""
        self._traces.append(callback)
        self.changed.connect(lambda _: callback())
        return f"trace_{len(self._traces)}"
    
    def trace_remove(self, trace_id: str) -> None:
        """Remove a trace callback (Tkinter compatibility)."""
        # Simplified implementation
        pass


class QtStringVar(QtVariable):
    """Qt-compatible StringVar replacement."""
    
    changed = Signal(str)
    
    def __init__(self, initial_value: str = ""):
        super().__init__(initial_value or "")
    
    def get(self) -> str:
        return str(self._value)
    
    def set(self, value: str) -> None:
        super().set(str(value))


class QtIntVar(QtVariable):
    """Qt-compatible IntVar replacement."""
    
    changed = Signal(int)
    
    def __init__(self, initial_value: int = 0):
        super().__init__(int(initial_value) if initial_value is not None else 0)
    
    def get(self) -> int:
        return int(self._value)
    
    def set(self, value: int) -> None:
        super().set(int(value))


class QtBoolVar(QtVariable):
    """Qt-compatible BooleanVar replacement."""
    
    changed = Signal(bool)
    
    def __init__(self, initial_value: bool = False):
        super().__init__(bool(initial_value))
    
    def get(self) -> bool:
        return bool(self._value)
    
    def set(self, value: bool) -> None:
        super().set(bool(value))


class QtDoubleVar(QtVariable):
    """Qt-compatible DoubleVar replacement."""
    
    changed = Signal(float)
    
    def __init__(self, initial_value: float = 0.0):
        super().__init__(float(initial_value) if initial_value is not None else 0.0)
    
    def get(self) -> float:
        return float(self._value)
    
    def set(self, value: float) -> None:
        super().set(float(value))


class TkinterStyleDialog(QDialog):
    """Base class for Tkinter-style modal dialogs."""
    
    def __init__(self, parent: Optional[QWidget] = None, title: str = "", 
                 width: int = 400, height: int = 300):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.setFixedSize(width, height)
        self.setModal(True)
        self.setWindowModality(Qt.WindowModal)
        
        # Result storage
        self.result = None
        
        # Create main layout
        self.main_layout = QVBoxLayout(self)
        
    def grab_set(self) -> None:
        """Tkinter compatibility - already modal in Qt."""
        pass
    
    def transient(self, parent: QWidget) -> None:
        """Tkinter compatibility - parent already set."""
        pass
    
    def wait_window(self) -> None:
        """Tkinter compatibility - use exec() instead."""
        self.exec()
    
    def destroy(self) -> None:
        """Tkinter compatibility."""
        self.close()


class BindingHelper:
    """Helper class for converting Tkinter event bindings to Qt."""
    
    @staticmethod
    def bind_to_widget(widget: QWidget, event: str, callback: Callable) -> None:
        """
        Bind a Tkinter-style event to a Qt widget.
        
        Common events:
        - "<Button-1>": Left mouse click
        - "<Button-3>": Right mouse click  
        - "<Double-Button-1>": Double click
        - "<Return>": Enter key
        - "<Escape>": Escape key
        - "<FocusIn>": Focus gained
        - "<FocusOut>": Focus lost
        """
        if event == "<Button-1>":
            if hasattr(widget, 'clicked'):
                widget.clicked.connect(callback)
            else:
                # Custom implementation needed for non-button widgets
                pass
        
        elif event == "<Return>":
            if isinstance(widget, QLineEdit):
                widget.returnPressed.connect(callback)
            else:
                # Install event filter for other widgets
                pass
        
        elif event == "<Escape>":
            # Install event filter for escape key
            pass
        
        # Add more event mappings as needed


class LayoutConverter:
    """Helper class for converting Tkinter layouts to Qt."""
    
    @staticmethod
    def pack_to_layout(pack_options: dict, parent_layout: Union[QVBoxLayout, QHBoxLayout]) -> dict:
        """
        Convert Tkinter pack options to Qt layout settings.
        
        pack_options: dict with keys like side, fill, expand, padx, pady
        Returns: dict with Qt layout parameters
        """
        qt_options = {}
        
        # Handle side option
        side = pack_options.get('side', 'top')
        if side in ['top', 'bottom']:
            # Use QVBoxLayout
            qt_options['stretch'] = 1 if pack_options.get('expand', False) else 0
        elif side in ['left', 'right']:
            # Use QHBoxLayout
            qt_options['stretch'] = 1 if pack_options.get('expand', False) else 0
        
        # Handle padding
        padx = pack_options.get('padx', 0)
        pady = pack_options.get('pady', 0)
        if isinstance(padx, tuple):
            padx = padx[0]  # Use first value
        if isinstance(pady, tuple):
            pady = pady[0]
        
        qt_options['margins'] = (padx, pady, padx, pady)
        
        return qt_options
    
    @staticmethod
    def grid_to_layout(grid_options: dict) -> dict:
        """
        Convert Tkinter grid options to Qt grid layout settings.
        
        grid_options: dict with keys like row, column, rowspan, columnspan, sticky, padx, pady
        Returns: dict with Qt grid parameters
        """
        qt_options = {
            'row': grid_options.get('row', 0),
            'column': grid_options.get('column', 0),
            'rowspan': grid_options.get('rowspan', 1),
            'columnspan': grid_options.get('columnspan', 1)
        }
        
        # Handle sticky alignment
        sticky = grid_options.get('sticky', '')
        alignment = Qt.AlignCenter  # Default
        
        if 'n' in sticky and 's' in sticky:
            alignment |= Qt.AlignVCenter
        elif 'n' in sticky:
            alignment |= Qt.AlignTop
        elif 's' in sticky:
            alignment |= Qt.AlignBottom
        
        if 'e' in sticky and 'w' in sticky:
            alignment |= Qt.AlignHCenter
        elif 'e' in sticky:
            alignment |= Qt.AlignRight
        elif 'w' in sticky:
            alignment |= Qt.AlignLeft
        
        qt_options['alignment'] = alignment
        
        return qt_options


class MessageBoxWrapper:
    """Wrapper for Tkinter-style message boxes."""
    
    @staticmethod
    def showinfo(title: str, message: str, parent: Optional[QWidget] = None) -> None:
        """Show an information message box."""
        QMessageBox.information(parent, title, message)
    
    @staticmethod
    def showwarning(title: str, message: str, parent: Optional[QWidget] = None) -> None:
        """Show a warning message box."""
        QMessageBox.warning(parent, title, message)
    
    @staticmethod
    def showerror(title: str, message: str, parent: Optional[QWidget] = None) -> None:
        """Show an error message box."""
        QMessageBox.critical(parent, title, message)
    
    @staticmethod
    def askyesno(title: str, message: str, parent: Optional[QWidget] = None) -> bool:
        """Show a yes/no question box."""
        reply = QMessageBox.question(parent, title, message,
                                   QMessageBox.Yes | QMessageBox.No,
                                   QMessageBox.No)
        return reply == QMessageBox.Yes
    
    @staticmethod
    def askokcancel(title: str, message: str, parent: Optional[QWidget] = None) -> bool:
        """Show an OK/Cancel question box."""
        reply = QMessageBox.question(parent, title, message,
                                   QMessageBox.Ok | QMessageBox.Cancel,
                                   QMessageBox.Cancel)
        return reply == QMessageBox.Ok


class AfterHelper:
    """Helper for Tkinter's after() functionality."""
    
    @staticmethod
    def after(ms: int, callback: Callable, *args) -> QTimer:
        """
        Schedule a callback after specified milliseconds.
        
        Returns QTimer instance that can be used to cancel.
        """
        timer = QTimer()
        timer.setSingleShot(True)
        timer.timeout.connect(lambda: callback(*args))
        timer.start(ms)
        return timer
    
    @staticmethod
    def after_idle(callback: Callable, *args) -> None:
        """
        Schedule a callback to run when idle.
        
        In Qt, this uses a 0ms timer.
        """
        QTimer.singleShot(0, lambda: callback(*args))
    
    @staticmethod
    def after_cancel(timer: QTimer) -> None:
        """Cancel a scheduled callback."""
        if timer and timer.isActive():
            timer.stop()


class WidgetStateHelper:
    """Helper for managing widget states."""
    
    @staticmethod
    def configure_state(widget: QWidget, state: str) -> None:
        """
        Configure widget state (normal, disabled, readonly).
        
        Maps Tkinter states to Qt equivalents.
        """
        if state == "normal":
            widget.setEnabled(True)
            if hasattr(widget, 'setReadOnly'):
                widget.setReadOnly(False)
        elif state == "disabled":
            widget.setEnabled(False)
        elif state == "readonly":
            if hasattr(widget, 'setReadOnly'):
                widget.setReadOnly(True)
            else:
                # For widgets without readonly, disable but keep appearance
                widget.setEnabled(False)
    
    @staticmethod
    def cget(widget: QWidget, option: str) -> Any:
        """
        Get widget configuration option (Tkinter cget equivalent).
        """
        if option == "text":
            if hasattr(widget, 'text'):
                return widget.text()
            elif hasattr(widget, 'toPlainText'):
                return widget.toPlainText()
        elif option == "state":
            return "normal" if widget.isEnabled() else "disabled"
        elif option == "value":
            if hasattr(widget, 'value'):
                return widget.value()
        
        return None


class GeometryHelper:
    """Helper for window geometry management."""
    
    @staticmethod
    def parse_geometry(geometry: str) -> tuple[int, int, int, int]:
        """
        Parse Tkinter geometry string (WxH+X+Y).
        
        Returns: (width, height, x, y)
        """
        import re
        match = re.match(r'(\d+)x(\d+)\+(\d+)\+(\d+)', geometry)
        if match:
            return tuple(map(int, match.groups()))
        
        match = re.match(r'(\d+)x(\d+)', geometry)
        if match:
            w, h = map(int, match.groups())
            return (w, h, 100, 100)  # Default position
        
        return (800, 600, 100, 100)  # Default size and position
    
    @staticmethod
    def set_window_geometry(window: QWidget, geometry: str) -> None:
        """Set window geometry from Tkinter-style string."""
        w, h, x, y = GeometryHelper.parse_geometry(geometry)
        window.setGeometry(x, y, w, h)


# Convenience imports for migration
messagebox = MessageBoxWrapper()


def create_button_with_var(text: str, variable: QtBoolVar, 
                          parent: Optional[QWidget] = None) -> QCheckBox:
    """
    Create a checkbox bound to a QtBoolVar.
    
    Provides Tkinter Checkbutton-like behavior.
    """
    checkbox = QCheckBox(text, parent)
    checkbox.setChecked(variable.get())
    
    # Two-way binding
    checkbox.stateChanged.connect(lambda state: variable.set(state == Qt.Checked))
    variable.changed.connect(checkbox.setChecked)
    
    return checkbox


def create_entry_with_var(variable: QtStringVar, 
                         parent: Optional[QWidget] = None,
                         width: Optional[int] = None) -> QLineEdit:
    """
    Create a line edit bound to a QtStringVar.
    
    Provides Tkinter Entry-like behavior.
    """
    entry = QLineEdit(variable.get(), parent)
    if width:
        entry.setFixedWidth(width * 10)  # Approximate character to pixel
    
    # Two-way binding
    entry.textChanged.connect(variable.set)
    variable.changed.connect(lambda v: entry.setText(v) if entry.text() != v else None)
    
    return entry


def create_label_with_var(variable: Union[QtStringVar, QtIntVar], 
                         parent: Optional[QWidget] = None) -> QLabel:
    """
    Create a label bound to a Qt variable.
    
    Provides Tkinter Label-like behavior with textvariable.
    """
    label = QLabel(str(variable.get()), parent)
    variable.changed.connect(lambda v: label.setText(str(v)))
    return label