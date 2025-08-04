"""
Qt Widget Helpers and Compatibility Layer for Tkinter Migration

This module provides helper classes and utilities to ease the transition
from Tkinter to PySide6, including variable replacements and common patterns.
"""

from collections.abc import Callable
from typing import Any, Literal

from PySide6.QtCore import QObject, Qt, QTimer, Signal
from PySide6.QtWidgets import (
	QAbstractButton,
	QCheckBox,
	QDialog,
	QHBoxLayout,
	QLabel,
	QLineEdit,
	QMessageBox,
	QProgressBar,
	QPushButton,
	QSlider,
	QSpinBox,
	QTextEdit,
	QVBoxLayout,
	QWidget,
)


class QtVariable(QObject):
	"""Base class for Qt-compatible variable types."""

	changed = Signal(object)

	def __init__(self, initial_value: object | None = None) -> None:
		super().__init__()
		self._value = initial_value
		self._traces: list[Callable[[], None]] = []
		self._suppress_signals = False
		self._callback_timer: QTimer | None = None
		self._pending_callbacks = False

	def get(self) -> object | None:
		"""Get the current value."""
		return self._value

	def set(self, value: object) -> None:
		"""Set a new value and emit change signal."""
		if self._value != value:
			self._value = value
			if not self._suppress_signals:
				self.changed.emit(value)
				# Batch callback execution to reduce overhead
				if self._traces and not self._pending_callbacks:
					self._pending_callbacks = True
					if self._callback_timer is None:
						self._callback_timer = QTimer()
						self._callback_timer.setSingleShot(True)
						self._callback_timer.timeout.connect(self._execute_callbacks)
					self._callback_timer.start(0)  # Execute on next event loop iteration

	def _execute_callbacks(self) -> None:
		"""Execute callbacks in batches to reduce overhead."""
		self._pending_callbacks = False
		for callback in self._traces:
			try:
				callback()
			except (RuntimeError, AttributeError) as e:
				# Avoid importing logger to prevent circular imports
				print(f"Warning: Callback error in QtVariable: {e}")

	def set_batch(self, values: list[object]) -> None:
		"""Set multiple values efficiently with only one signal emission."""
		if not values:
			return

		self._suppress_signals = True
		for value in values[:-1]:
			self.set(value)
		self._suppress_signals = False
		self.set(values[-1])  # Emit signal for last value only

	def trace_add(self, _mode: str, callback: Callable[[], None]) -> str:
		"""Add a trace callback (Tkinter compatibility)."""
		self._traces.append(callback)
		self.changed.connect(callback)
		return f"trace_{len(self._traces)}"

	def trace_remove(self, trace_id: str) -> None:
		"""Remove a trace callback (Tkinter compatibility)."""
		# Extract index from trace_id and remove if valid
		try:
			index = int(trace_id.split("_")[1]) - 1
			if 0 <= index < len(self._traces):
				callback = self._traces.pop(index)
				self.changed.disconnect(callback)
		except (ValueError, IndexError):
			pass  # Invalid trace_id


class QtStringVar(QtVariable):
	"""Qt-compatible StringVar replacement."""

	changed = Signal(str)

	def __init__(self, initial_value: str = "") -> None:
		super().__init__(initial_value or "")

	def get(self) -> str:
		return str(self._value)

	def set(self, value: object) -> None:
		"""Override to ensure string conversion before comparison."""
		str_value = str(value)
		if str(self._value) != str_value:
			self._value = str_value
			if not self._suppress_signals:
				self.changed.emit(str_value)
				# Batch callback execution to reduce overhead
				if self._traces and not self._pending_callbacks:
					self._pending_callbacks = True
					if self._callback_timer is None:
						self._callback_timer = QTimer()
						self._callback_timer.setSingleShot(True)
						self._callback_timer.timeout.connect(self._execute_callbacks)
					self._callback_timer.start(0)

	def trace_add(self, _mode: str, callback: Callable[[], None]) -> str:
		"""Add a trace callback (Tkinter compatibility)."""
		self._traces.append(callback)
		self.changed.connect(callback)
		return f"trace_{len(self._traces)}"


class QtIntVar(QtVariable):
	"""Qt-compatible IntVar replacement."""

	changed = Signal(int)

	def __init__(self, initial_value: int | None = None) -> None:
		super().__init__(int(initial_value) if initial_value is not None else 0)

	def get(self) -> int:
		try:
			return int(self._value)  # pyright: ignore[reportArgumentType]
		except (ValueError, TypeError):
			return 0

	def set(self, value: object) -> None:
		"""Override to ensure int conversion before comparison."""
		try:
			int_value = int(value)  # pyright: ignore[reportArgumentType]
		except (ValueError, TypeError):
			int_value = 0

		if self.get() != int_value:
			self._value = int_value
			if not self._suppress_signals:
				self.changed.emit(int_value)
				# Batch callback execution to reduce overhead
				if self._traces and not self._pending_callbacks:
					self._pending_callbacks = True
					if self._callback_timer is None:
						self._callback_timer = QTimer()
						self._callback_timer.setSingleShot(True)
						self._callback_timer.timeout.connect(self._execute_callbacks)
					self._callback_timer.start(0)

	def trace_add(self, _mode: str, callback: Callable[[], None]) -> str:
		"""Add a trace callback (Tkinter compatibility)."""
		self._traces.append(callback)
		self.changed.connect(callback)
		return f"trace_{len(self._traces)}"


class QtBoolVar(QtVariable):
	"""Qt-compatible BooleanVar replacement."""

	changed = Signal(bool)

	def __init__(self, initial_value: bool = False) -> None:
		super().__init__(bool(initial_value))

	def get(self) -> bool:
		return bool(self._value)

	def set(self, value: object) -> None:
		"""Override to ensure bool conversion before comparison."""
		bool_value = bool(value)
		if self.get() != bool_value:
			self._value = bool_value
			if not self._suppress_signals:
				self.changed.emit(bool_value)
				# Batch callback execution to reduce overhead
				if self._traces and not self._pending_callbacks:
					self._pending_callbacks = True
					if self._callback_timer is None:
						self._callback_timer = QTimer()
						self._callback_timer.setSingleShot(True)
						self._callback_timer.timeout.connect(self._execute_callbacks)
					self._callback_timer.start(0)

	def trace_add(self, _mode: str, callback: Callable[[], None]) -> str:
		"""Add a trace callback (Tkinter compatibility)."""
		self._traces.append(callback)
		self.changed.connect(callback)
		return f"trace_{len(self._traces)}"


class QtDoubleVar(QtVariable):
	"""Qt-compatible DoubleVar replacement."""

	changed = Signal(float)

	def __init__(self, initial_value: float | None = None) -> None:
		super().__init__(initial_value if initial_value is not None else 0.0)

	def get(self) -> float:
		return float(self._value)  # pyright: ignore[reportArgumentType]

	def set(self, value: object) -> None:
		"""Override to ensure float conversion before comparison."""
		try:
			float_value = float(value)  # pyright: ignore[reportArgumentType]
		except (ValueError, TypeError):
			float_value = 0.0

		if self.get() != float_value:
			self._value = float_value
			if not self._suppress_signals:
				self.changed.emit(float_value)
				# Batch callback execution to reduce overhead
				if self._traces and not self._pending_callbacks:
					self._pending_callbacks = True
					if self._callback_timer is None:
						self._callback_timer = QTimer()
						self._callback_timer.setSingleShot(True)
						self._callback_timer.timeout.connect(self._execute_callbacks)
					self._callback_timer.start(0)

	def trace_add(self, _mode: str, callback: Callable[[], None]) -> str:
		"""Add a trace callback (Tkinter compatibility)."""
		self._traces.append(callback)
		self.changed.connect(callback)
		return f"trace_{len(self._traces)}"


class TkinterStyleDialog(QDialog):
	"""Base class for Tkinter-style modal dialogs."""

	def __init__(self, parent: QWidget | None = None, title: str = "", width: int = 400, height: int = 300) -> None:
		super().__init__(parent)
		self.setWindowTitle(title)
		self.setFixedSize(width, height)
		self.setModal(True)
		self.setWindowModality(Qt.WindowModality.WindowModal)

		# Result storage - use different name to avoid conflict with QDialog.result()
		self.dialog_result = None

		# Create main layout
		self.main_layout = QVBoxLayout(self)

	def grab_set(self) -> None:
		"""Tkinter compatibility - already modal in Qt."""

	def transient(self, parent: QWidget) -> None:
		"""Tkinter compatibility - parent already set."""

	def wait_window(self) -> None:
		"""Tkinter compatibility - use exec() instead."""
		self.exec()

	def destroy(self, destroyWindow: bool = True, destroySubWindows: bool = True) -> None:  # noqa: N803
		"""Tkinter compatibility - delegate to parent destroy method."""
		super().destroy(destroyWindow, destroySubWindows)


class BindingHelper:
	"""Helper class for converting Tkinter event bindings to Qt."""

	@staticmethod
	def bind_to_widget(widget: QWidget, event: str, callback: Callable[[], None]) -> None:
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
			# Check if widget has clicked signal (buttons, checkboxes, etc.)
			if isinstance(widget, QAbstractButton):
				widget.clicked.connect(callback)
			else:
				# For widgets without clicked signal, install event filter
				# This would require a custom event filter implementation
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
	def pack_to_layout(
		pack_options: dict[str, str | int | float | bool | tuple[int | float, ...] | list[int | float]],
		_parent_layout: QVBoxLayout | QHBoxLayout,
	) -> dict[str, int | tuple[int, int, int, int]]:
		"""
		Convert Tkinter pack options to Qt layout settings.

		pack_options: dict with keys like side, fill, expand, padx, pady
		Returns: dict with Qt layout parameters
		"""
		qt_options: dict[str, int | tuple[int, int, int, int]] = {}

		# Handle side option
		side = pack_options.get("side", "top")
		if side in {"top", "bottom"}:
			# Use QVBoxLayout
			qt_options["stretch"] = 1 if pack_options.get("expand") else 0
		elif side in {"left", "right"}:
			# Use QHBoxLayout
			qt_options["stretch"] = 1 if pack_options.get("expand") else 0

		# Handle padding
		padx: int | float | tuple[int | float, ...] | list[int | float] = pack_options.get("padx", 0)
		pady: int | float | tuple[int | float, ...] | list[int | float] = pack_options.get("pady", 0)

		# Convert padx to int, handling tuple case
		if isinstance(padx, (tuple, list)) and len(padx) > 0:
			padx = padx[0]  # Use first value
		elif not isinstance(padx, (int, float)):
			padx = 0

		# Convert pady to int, handling tuple case
		if isinstance(pady, (tuple, list)) and len(pady) > 0:
			pady = pady[0]  # Use first value
		elif not isinstance(pady, (int, float)):
			pady = 0

		qt_options["margins"] = (int(padx), int(pady), int(padx), int(pady))

		return qt_options

	@staticmethod
	def grid_to_layout(grid_options: dict[str, Any]) -> dict[str, int | Qt.AlignmentFlag]:
		"""
		Convert Tkinter grid options to Qt grid layout settings.

		grid_options: dict with keys like row, column, rowspan, columnspan, sticky, padx, pady
		Returns: dict with Qt grid parameters
		"""
		qt_options = {
			"row": grid_options.get("row", 0),
			"column": grid_options.get("column", 0),
			"rowspan": grid_options.get("rowspan", 1),
			"columnspan": grid_options.get("columnspan", 1),
		}

		# Handle sticky alignment
		sticky = grid_options.get("sticky", "")
		alignment: Qt.AlignmentFlag = Qt.AlignmentFlag.AlignCenter  # Default

		if "n" in sticky and "s" in sticky:
			alignment |= Qt.AlignmentFlag.AlignVCenter
		elif "n" in sticky:
			alignment |= Qt.AlignmentFlag.AlignTop
		elif "s" in sticky:
			alignment |= Qt.AlignmentFlag.AlignBottom

		if "e" in sticky and "w" in sticky:
			alignment |= Qt.AlignmentFlag.AlignHCenter
		elif "e" in sticky:
			alignment |= Qt.AlignmentFlag.AlignRight
		elif "w" in sticky:
			alignment |= Qt.AlignmentFlag.AlignLeft

		qt_options["alignment"] = alignment

		return qt_options


class MessageBoxWrapper:
	"""Wrapper for Tkinter-style message boxes."""

	@staticmethod
	def showinfo(title: str, message: str, parent: QWidget | None = None) -> None:
		"""Show an information message box."""
		QMessageBox.information(parent, title, message)

	@staticmethod
	def showwarning(title: str, message: str, parent: QWidget | None = None) -> None:
		"""Show a warning message box."""
		QMessageBox.warning(parent, title, message)

	@staticmethod
	def showerror(title: str, message: str, parent: QWidget | None = None) -> None:
		"""Show an error message box."""
		QMessageBox.critical(parent, title, message)

	@staticmethod
	def askyesno(title: str, message: str, parent: QWidget | None = None) -> bool:
		"""Show a yes/no question box."""
		reply = QMessageBox.question(
			parent,
			title,
			message,
			QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
			QMessageBox.StandardButton.No,
		)
		return reply == QMessageBox.StandardButton.Yes

	@staticmethod
	def askokcancel(title: str, message: str, parent: QWidget | None = None) -> bool:
		"""Show an OK/Cancel question box."""
		reply = QMessageBox.question(
			parent,
			title,
			message,
			QMessageBox.StandardButton.Ok | QMessageBox.StandardButton.Cancel,
			QMessageBox.StandardButton.Cancel,
		)
		return reply == QMessageBox.StandardButton.Ok


class AfterHelper:
	"""Helper for Tkinter's after() functionality."""

	@staticmethod
	def after(ms: int, callback: Callable[..., None], *args) -> QTimer:  # pyright: ignore[reportUnknownParameterType]  # noqa: ANN002
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
	def after_idle(callback: Callable[..., None], *args) -> None:  # pyright: ignore[reportUnknownParameterType]  # noqa: ANN002
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
			# Only set readonly to False for widgets that support it
			if isinstance(widget, (QLineEdit, QTextEdit)):
				widget.setReadOnly(False)
		elif state == "disabled":
			widget.setEnabled(False)
		elif state == "readonly":
			# Only set readonly for widgets that support it
			if isinstance(widget, (QLineEdit, QTextEdit)):
				widget.setReadOnly(True)
			else:
				# For widgets without readonly, disable but keep appearance
				widget.setEnabled(False)

	@staticmethod
	def cget(widget: QWidget, option: str) -> str | int | Literal["normal", "disabled"] | None:
		"""
		Get widget configuration option (Tkinter cget equivalent).
		"""
		# Handle text-related options
		if option == "text":
			if isinstance(widget, (QLabel, QPushButton, QCheckBox, QLineEdit)):
				return widget.text()
			if isinstance(widget, QTextEdit):
				return widget.toPlainText()
			return None

		# Handle other options using a mapping approach
		option_handlers: dict[str, Callable[[QWidget], Literal["normal", "disabled"] | int | None]] = {
			"state": lambda w: "normal" if w.isEnabled() else "disabled",
			"value": lambda w: w.value() if isinstance(w, (QSpinBox, QSlider, QProgressBar)) else None,
		}

		handler: Callable[[QWidget], int | Literal["normal", "disabled"] | None] | None = option_handlers.get(option)
		return handler(widget) if handler else None


class GeometryHelper:
	"""Helper for window geometry management."""

	@staticmethod
	def parse_geometry(geometry: str) -> tuple[int, int, int, int]:
		"""
		Parse Tkinter geometry string (WxH+X+Y).

		Returns: (width, height, x, y)
		"""
		import re  # noqa: PLC0415

		match = re.match(r"(\d+)x(\d+)\+(\d+)\+(\d+)", geometry)
		if match:
			return tuple(map(int, match.groups()))

		match = re.match(r"(\d+)x(\d+)", geometry)
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


def create_button_with_var(text: str, variable: QtBoolVar, parent: QWidget | None = None) -> QCheckBox:
	"""
	Create a checkbox bound to a QtBoolVar.

	Provides Tkinter Checkbutton-like behavior.
	"""
	checkbox = QCheckBox(text, parent)
	checkbox.setChecked(variable.get())

	# Two-way binding
	checkbox.stateChanged.connect(lambda state: variable.set(state == Qt.CheckState.Checked.value))  # pyright: ignore[reportUnknownArgumentType, reportUnknownLambdaType]
	variable.changed.connect(checkbox.setChecked)

	return checkbox


def create_entry_with_var(variable: QtStringVar, parent: QWidget | None = None, width: int | None = None) -> QLineEdit:
	"""
	Create a line edit bound to a QtStringVar.

	Provides Tkinter Entry-like behavior.
	"""
	entry = QLineEdit(variable.get(), parent)
	if width:
		entry.setFixedWidth(width * 10)  # Approximate character to pixel

	# Two-way binding
	entry.textChanged.connect(variable.set)
	variable.changed.connect(lambda v: entry.setText(v) if entry.text() != v else None)  # pyright: ignore[reportUnknownLambdaType, reportUnknownArgumentType]

	return entry


def create_label_with_var(variable: QtStringVar | QtIntVar, parent: QWidget | None = None) -> QLabel:
	"""
	Create a label bound to a Qt variable.

	Provides Tkinter Label-like behavior with textvariable.
	"""
	label = QLabel(str(variable.get()), parent)
	variable.changed.connect(lambda v: label.setText(str(v)))  # pyright: ignore[reportUnknownLambdaType, reportUnknownArgumentType]
	return label
