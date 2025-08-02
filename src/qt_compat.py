"""Qt compatibility module for PySide6 migration.

This module provides a centralized import location for all PySide6 components,
making it easier to manage imports across the codebase during migration.
"""

# Core Qt modules
from PySide6.QtCore import (
    Qt, Signal, Slot, QThread, QObject, QTimer, QSize, QPoint, QRect,
    QEvent, QEventLoop, QUrl, QDateTime, QDate, QTime,
    Property, QAbstractListModel, QModelIndex, QSettings,
    QCoreApplication, QMetaObject, QRunnable, QThreadPool
)

# Widget module imports
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QDialog, QFrame,
    QLabel, QPushButton, QLineEdit, QTextEdit, QPlainTextEdit,
    QComboBox, QCheckBox, QRadioButton, QSpinBox, QSlider,
    QProgressBar, QTabWidget, QTreeWidget, QTreeWidgetItem,
    QListWidget, QListWidgetItem, QTableWidget, QTableWidgetItem,
    QGroupBox, QScrollArea, QSplitter, QToolBar, QStatusBar,
    QMenuBar, QMenu,
    QFileDialog, QMessageBox, QInputDialog, QColorDialog, QFontDialog,
    QVBoxLayout, QHBoxLayout, QGridLayout, QFormLayout, QStackedLayout,
    QSizePolicy, QSpacerItem, QLayoutItem,
    QSystemTrayIcon, QStyle, QStyleOption, QProxyStyle,
    QGraphicsView, QGraphicsScene, QGraphicsItem,
    QToolTip, QWhatsThis
)

# GUI module imports
from PySide6.QtGui import (
    QPixmap, QIcon, QFont, QFontMetrics, QColor, QPalette,
    QPainter, QPen, QBrush, QGradient, QLinearGradient,
    QImage, QBitmap, QCursor, QKeySequence,
    QAction, QActionGroup,
    QClipboard, QGuiApplication, QScreen,
    QStandardItemModel, QStandardItem,
    QTextCursor, QTextDocument, QTextCharFormat,
    QValidator, QIntValidator, QDoubleValidator, QRegularExpressionValidator,
    QCloseEvent, QShortcut, QFontDatabase
)

# Convenience type aliases for migration
pyqtSignal = Signal
pyqtSlot = Slot

# Common dialog shortcuts
def get_open_file_name(parent=None, caption="", directory="", filter=""):
    """Wrapper for QFileDialog.getOpenFileName with simpler return."""
    path, _ = QFileDialog.getOpenFileName(parent, caption, directory, filter)
    return path

def get_save_file_name(parent=None, caption="", directory="", filter=""):
    """Wrapper for QFileDialog.getSaveFileName with simpler return."""
    path, _ = QFileDialog.getSaveFileName(parent, caption, directory, filter)
    return path

def get_existing_directory(parent=None, caption="", directory=""):
    """Wrapper for QFileDialog.getExistingDirectory."""
    return QFileDialog.getExistingDirectory(parent, caption, directory)

# Message box shortcuts
def show_info(parent, title, message):
    """Show information message box."""
    QMessageBox.information(parent, title, message)

def show_warning(parent, title, message):
    """Show warning message box."""
    QMessageBox.warning(parent, title, message)

def show_error(parent, title, message):
    """Show error message box."""
    QMessageBox.critical(parent, title, message)

def ask_yes_no(parent, title, message):
    """Show yes/no question dialog."""
    reply = QMessageBox.question(parent, title, message,
                                QMessageBox.Yes | QMessageBox.No,
                                QMessageBox.No)
    return reply == QMessageBox.Yes

# Common style constants
BUTTON_STYLE = """
    QPushButton {
        padding: 5px 15px;
        border-radius: 3px;
    }
    QPushButton:hover {
        background-color: palette(midlight);
    }
"""

# Layout helper functions
def clear_layout(layout):
    """Clear all widgets from a layout."""
    while layout.count():
        child = layout.takeAt(0)
        if child.widget():
            child.widget().deleteLater()

def set_margins(layout, margin):
    """Set all margins of a layout to the same value."""
    layout.setContentsMargins(margin, margin, margin, margin)