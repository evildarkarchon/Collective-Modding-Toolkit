"""
Qt-compatible helper classes for tab framework migration.

This module provides Qt-based replacements for the Tkinter tab framework,
maintaining the same interface while using PySide6 widgets.
"""

import logging
from abc import ABC, abstractmethod
from typing import Protocol, final

from PySide6.QtCore import Qt, QThread, Signal
from PySide6.QtWidgets import (
    QWidget, QLabel, QVBoxLayout, QGridLayout,
    QTabWidget, QApplication
)
from PySide6.QtGui import QFont, QPixmap

from qt_widgets import QtStringVar

logger = logging.getLogger(__name__)


class CMCheckerInterface(Protocol):
    """Interface for main application controller."""
    
    @abstractmethod
    def get_image(self, relative_path: str) -> QPixmap: ...


class CMCTabWidget(QWidget, ABC):
    """Base class for Qt-compatible tab widgets."""
    
    def __init__(self, cmc: CMCheckerInterface, tab_widget: QTabWidget, tab_title: str) -> None:
        super().__init__()
        tab_widget.addTab(self, tab_title)
        self.cmc = cmc
        self._loading = False
        self._loaded = False
        self.loading_text: str | None = None
        self.sv_loading_text = QtStringVar()
        self.loading_error: str | None = None
        self.label_loading: QLabel | None = None
        
        # Set up main layout
        self.main_layout = QGridLayout(self)
        self.main_layout.setContentsMargins(0, 0, 0, 0)
        
    def _load(self) -> bool:
        """Load any data needed for this tab. Return False on failure."""
        return True
    
    def switch_from(self) -> None:
        """Called when switching away from this tab."""
        return
    
    def _switch_to(self) -> None:
        """Called when switching to this tab."""
        return
    
    @abstractmethod
    def _build_gui(self) -> None:
        """Build the tab's GUI. Must be implemented by subclasses."""
        ...
    
    def refresh(self) -> None:
        """Refresh the tab's content."""
        raise NotImplementedError
    
    @final
    def load(self) -> None:
        """Load the tab, showing a loading message if needed."""
        logger.debug("Switch Tab : %s", self.__class__.__name__)
        if self._loaded:
            self._switch_to()
            return
        
        if self._loading:
            return
        
        if self.label_loading is not None:
            # Previously errored while loading.
            return
        
        logger.debug("Load Tab : %s", self.__class__.__name__)
        self._loading = True
        
        # Create loading label
        self.sv_loading_text.set(self.loading_text or "")
        self.label_loading = QLabel()
        self.label_loading.setAlignment(Qt.AlignCenter)
        
        # Set font size (approximate FONT_LARGE)
        font = QFont()
        font.setPointSize(14)
        self.label_loading.setFont(font)
        
        # Connect variable to label
        self.sv_loading_text.changed.connect(self.label_loading.setText)
        self.label_loading.setText(self.sv_loading_text.get())
        
        # Add to layout
        self.main_layout.addWidget(self.label_loading, 0, 0, Qt.AlignCenter)
        
        # Try to load
        if self._load():
            # Remove loading label
            self.main_layout.removeWidget(self.label_loading)
            self.label_loading.deleteLater()
            self.label_loading = None
            self._loaded = True
            self._build_gui()
            self._switch_to()
        else:
            logger.error("Load Tab : %s : Failed : %s", self.__class__.__name__, self.loading_error)
            self.sv_loading_text.set(self.loading_error or "Failed to load tab.")
            # Set error color (red)
            self.label_loading.setStyleSheet("color: #ff0000;")
        
        self._loading = False
    
    @final
    @property
    def is_loaded(self) -> bool:
        """Check if the tab has been loaded."""
        return self._loaded


class LoadingThread(QThread):
    """Thread for loading tab data without blocking the UI."""
    
    finished = Signal(bool, str)  # success, error_message
    
    def __init__(self, load_func):
        super().__init__()
        self.load_func = load_func
        
    def run(self):
        try:
            result = self.load_func()
            self.finished.emit(result, "")
        except Exception as e:
            logger.exception("Error during tab loading")
            self.finished.emit(False, str(e))