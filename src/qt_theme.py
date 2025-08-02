"""Qt theme module to match sv_ttk dark theme.

This module provides a Qt stylesheet that closely matches the sv_ttk dark theme
used in the Tkinter version of the application.
"""

# Color constants from sv_ttk dark theme
COLORS = {
    "fg": "#fafafa",          # Main foreground/text color
    "bg": "#1c1c1c",          # Main background color
    "disfg": "#595959",       # Disabled foreground
    "selfg": "#ffffff",       # Selected foreground
    "selbg": "#05b62e",       # Selected background (green)
    "accent": "#57ff64",      # Accent color (light green)
    
    # Additional colors extracted from the theme
    "button_bg": "#2d2d2d",        # Button background
    "button_hover": "#404040",     # Button hover state
    "button_pressed": "#4a4a4a",   # Button pressed state
    "button_border": "#555555",    # Button border
    
    "input_bg": "#262626",         # Input field background
    "input_border": "#404040",     # Input field border
    "input_hover": "#303030",      # Input field hover
    "input_focus": "#05b62e",      # Input field focus border
    
    "disabled_fg": "#757575",      # Disabled text
    "disabled_bg": "#1a1a1a",      # Disabled background
    
    "tab_bg": "#2d2d2d",          # Inactive tab background
    "tab_selected": "#404040",     # Active tab background
    "tab_border": "#404040",       # Tab border
    
    "scrollbar_bg": "#1c1c1c",     # Scrollbar background
    "scrollbar_handle": "#404040", # Scrollbar handle
    "scrollbar_hover": "#4a4a4a",  # Scrollbar handle hover
}


def get_dark_stylesheet():
    """Return the complete Qt stylesheet matching sv_ttk dark theme."""
    return f"""
    /* ==================== Main Application ==================== */
    QMainWindow {{
        background-color: {COLORS['bg']};
        color: {COLORS['fg']};
    }}
    
    QWidget {{
        background-color: {COLORS['bg']};
        color: {COLORS['fg']};
        font-family: "Cascadia Mono", "Consolas", monospace;
        font-size: 10pt;
    }}
    
    /* ==================== Tab Widget ==================== */
    QTabWidget::pane {{
        background-color: {COLORS['bg']};
        border: 1px solid {COLORS['tab_border']};
        border-top: none;
    }}
    
    QTabWidget::tab-bar {{
        left: 0px;
    }}
    
    QTabBar::tab {{
        background-color: {COLORS['tab_bg']};
        color: {COLORS['fg']};
        padding: 8px 16px;
        margin-right: 2px;
        border: 1px solid {COLORS['tab_border']};
        border-bottom: none;
        border-top-left-radius: 4px;
        border-top-right-radius: 4px;
    }}
    
    QTabBar::tab:selected {{
        background-color: {COLORS['tab_selected']};
        color: {COLORS['selfg']};
    }}
    
    QTabBar::tab:hover:!selected {{
        background-color: {COLORS['button_hover']};
    }}
    
    /* ==================== Buttons ==================== */
    QPushButton {{
        background-color: {COLORS['button_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['button_border']};
        padding: 6px 16px;
        border-radius: 4px;
        min-height: 20px;
    }}
    
    QPushButton:hover {{
        background-color: {COLORS['button_hover']};
        border-color: {COLORS['accent']};
    }}
    
    QPushButton:pressed {{
        background-color: {COLORS['button_pressed']};
        color: #d0d0d0;
    }}
    
    QPushButton:disabled {{
        background-color: {COLORS['disabled_bg']};
        color: {COLORS['disabled_fg']};
        border-color: #333333;
    }}
    
    QPushButton:focus {{
        border: 2px solid {COLORS['accent']};
        outline: none;
    }}
    
    /* Accent buttons (if needed) */
    QPushButton[class="accent"] {{
        background-color: {COLORS['selbg']};
        color: #000000;
        border: 1px solid {COLORS['selbg']};
    }}
    
    QPushButton[class="accent"]:hover {{
        background-color: {COLORS['accent']};
        border-color: {COLORS['accent']};
    }}
    
    QPushButton[class="accent"]:pressed {{
        background-color: #04a226;
        border-color: #04a226;
    }}
    
    QPushButton[class="accent"]:disabled {{
        background-color: #2a4a2f;
        color: #7a7a7a;
        border-color: #2a4a2f;
    }}
    
    /* ==================== Labels ==================== */
    QLabel {{
        background-color: transparent;
        color: {COLORS['fg']};
        padding: 2px;
    }}
    
    QLabel:disabled {{
        color: {COLORS['disabled_fg']};
    }}
    
    /* ==================== Input Fields ==================== */
    QLineEdit, QTextEdit, QPlainTextEdit {{
        background-color: {COLORS['input_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        border-radius: 4px;
        padding: 6px;
        selection-background-color: {COLORS['selbg']};
        selection-color: {COLORS['selfg']};
    }}
    
    QLineEdit:hover, QTextEdit:hover, QPlainTextEdit:hover {{
        background-color: {COLORS['input_hover']};
        border-color: #505050;
    }}
    
    QLineEdit:focus, QTextEdit:focus, QPlainTextEdit:focus {{
        border: 2px solid {COLORS['input_focus']};
        outline: none;
    }}
    
    QLineEdit:disabled, QTextEdit:disabled, QPlainTextEdit:disabled {{
        background-color: {COLORS['disabled_bg']};
        color: {COLORS['disabled_fg']};
        border-color: #333333;
    }}
    
    /* ==================== ComboBox ==================== */
    QComboBox {{
        background-color: {COLORS['input_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        border-radius: 4px;
        padding: 6px;
        min-width: 100px;
    }}
    
    QComboBox:hover {{
        background-color: {COLORS['input_hover']};
        border-color: #505050;
    }}
    
    QComboBox:focus {{
        border: 2px solid {COLORS['input_focus']};
        outline: none;
    }}
    
    QComboBox:disabled {{
        background-color: {COLORS['disabled_bg']};
        color: {COLORS['disabled_fg']};
        border-color: #333333;
    }}
    
    QComboBox::drop-down {{
        border: none;
        width: 20px;
    }}
    
    QComboBox::down-arrow {{
        image: none;
        border-left: 5px solid transparent;
        border-right: 5px solid transparent;
        border-top: 5px solid {COLORS['fg']};
        margin-right: 5px;
    }}
    
    QComboBox::down-arrow:disabled {{
        border-top-color: {COLORS['disabled_fg']};
    }}
    
    QComboBox QAbstractItemView {{
        background-color: {COLORS['input_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        selection-background-color: {COLORS['selbg']};
        selection-color: {COLORS['selfg']};
        outline: none;
    }}
    
    /* ==================== CheckBox & RadioButton ==================== */
    QCheckBox, QRadioButton {{
        color: {COLORS['fg']};
        spacing: 8px;
    }}
    
    QCheckBox:disabled, QRadioButton:disabled {{
        color: {COLORS['disabled_fg']};
    }}
    
    QCheckBox::indicator, QRadioButton::indicator {{
        width: 18px;
        height: 18px;
        background-color: {COLORS['input_bg']};
        border: 1px solid {COLORS['input_border']};
    }}
    
    QCheckBox::indicator {{
        border-radius: 3px;
    }}
    
    QRadioButton::indicator {{
        border-radius: 9px;
    }}
    
    QCheckBox::indicator:hover, QRadioButton::indicator:hover {{
        background-color: {COLORS['input_hover']};
        border-color: #505050;
    }}
    
    QCheckBox::indicator:checked, QRadioButton::indicator:checked {{
        background-color: {COLORS['selbg']};
        border-color: {COLORS['selbg']};
    }}
    
    QCheckBox::indicator:checked {{
        image: none;
        background-color: {COLORS['selbg']};
    }}
    
    QCheckBox::indicator:checked:after {{
        content: "";
        position: absolute;
        width: 5px;
        height: 10px;
        border: solid {COLORS['selfg']};
        border-width: 0 2px 2px 0;
        transform: rotate(45deg);
        left: 6px;
        top: 2px;
    }}
    
    QRadioButton::indicator:checked {{
        background-color: {COLORS['selbg']};
    }}
    
    QCheckBox::indicator:disabled, QRadioButton::indicator:disabled {{
        background-color: {COLORS['disabled_bg']};
        border-color: #333333;
    }}
    
    /* ==================== Progress Bar ==================== */
    QProgressBar {{
        background-color: {COLORS['input_bg']};
        border: 1px solid {COLORS['input_border']};
        border-radius: 4px;
        text-align: center;
        color: {COLORS['fg']};
        min-height: 20px;
    }}
    
    QProgressBar::chunk {{
        background-color: {COLORS['selbg']};
        border-radius: 3px;
    }}
    
    /* ==================== Scrollbars ==================== */
    QScrollBar:vertical {{
        background-color: {COLORS['scrollbar_bg']};
        width: 12px;
        border: none;
    }}
    
    QScrollBar:horizontal {{
        background-color: {COLORS['scrollbar_bg']};
        height: 12px;
        border: none;
    }}
    
    QScrollBar::handle:vertical, QScrollBar::handle:horizontal {{
        background-color: {COLORS['scrollbar_handle']};
        border-radius: 6px;
        min-height: 20px;
        min-width: 20px;
    }}
    
    QScrollBar::handle:vertical:hover, QScrollBar::handle:horizontal:hover {{
        background-color: {COLORS['scrollbar_hover']};
    }}
    
    QScrollBar::add-line, QScrollBar::sub-line {{
        border: none;
        background: none;
    }}
    
    QScrollBar::add-page, QScrollBar::sub-page {{
        background: none;
    }}
    
    /* ==================== TreeView / TreeWidget ==================== */
    QTreeView, QTreeWidget {{
        background-color: {COLORS['bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        selection-background-color: #292929;
        selection-color: {COLORS['selfg']};
        outline: none;
    }}
    
    QTreeView::item, QTreeWidget::item {{
        padding: 4px;
        border: none;
    }}
    
    QTreeView::item:selected, QTreeWidget::item:selected {{
        background-color: #292929;
        color: {COLORS['selfg']};
    }}
    
    QTreeView::item:hover, QTreeWidget::item:hover {{
        background-color: {COLORS['button_bg']};
    }}
    
    QHeaderView::section {{
        background-color: {COLORS['button_bg']};
        color: {COLORS['fg']};
        padding: 6px;
        border: none;
        border-right: 1px solid {COLORS['input_border']};
        border-bottom: 1px solid {COLORS['input_border']};
    }}
    
    QHeaderView::section:hover {{
        background-color: {COLORS['button_hover']};
    }}
    
    /* ==================== Menu Bar ==================== */
    QMenuBar {{
        background-color: {COLORS['bg']};
        color: {COLORS['fg']};
        border-bottom: 1px solid {COLORS['input_border']};
    }}
    
    QMenuBar::item {{
        padding: 6px 12px;
        background-color: transparent;
    }}
    
    QMenuBar::item:selected {{
        background-color: {COLORS['button_hover']};
    }}
    
    QMenu {{
        background-color: {COLORS['input_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
    }}
    
    QMenu::item {{
        padding: 6px 24px;
    }}
    
    QMenu::item:selected {{
        background-color: {COLORS['selbg']};
        color: {COLORS['selfg']};
    }}
    
    /* ==================== Tool Tips ==================== */
    QToolTip {{
        background-color: {COLORS['input_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        padding: 4px;
    }}
    
    /* ==================== Group Box ==================== */
    QGroupBox {{
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        border-radius: 4px;
        margin-top: 12px;
        padding-top: 12px;
    }}
    
    QGroupBox::title {{
        subcontrol-origin: margin;
        left: 10px;
        padding: 0 5px 0 5px;
        background-color: {COLORS['bg']};
    }}
    
    /* ==================== Spin Box ==================== */
    QSpinBox, QDoubleSpinBox {{
        background-color: {COLORS['input_bg']};
        color: {COLORS['fg']};
        border: 1px solid {COLORS['input_border']};
        border-radius: 4px;
        padding: 4px;
    }}
    
    QSpinBox:hover, QDoubleSpinBox:hover {{
        background-color: {COLORS['input_hover']};
        border-color: #505050;
    }}
    
    QSpinBox:focus, QDoubleSpinBox:focus {{
        border: 2px solid {COLORS['input_focus']};
        outline: none;
    }}
    
    QSpinBox::up-button, QSpinBox::down-button,
    QDoubleSpinBox::up-button, QDoubleSpinBox::down-button {{
        background-color: transparent;
        border: none;
        width: 16px;
    }}
    
    QSpinBox::up-arrow, QDoubleSpinBox::up-arrow {{
        image: none;
        border-left: 4px solid transparent;
        border-right: 4px solid transparent;
        border-bottom: 4px solid {COLORS['fg']};
        width: 0;
        height: 0;
    }}
    
    QSpinBox::down-arrow, QDoubleSpinBox::down-arrow {{
        image: none;
        border-left: 4px solid transparent;
        border-right: 4px solid transparent;
        border-top: 4px solid {COLORS['fg']};
        width: 0;
        height: 0;
    }}
    
    /* ==================== Slider ==================== */
    QSlider::groove:horizontal {{
        background-color: {COLORS['input_bg']};
        height: 6px;
        border-radius: 3px;
    }}
    
    QSlider::handle:horizontal {{
        background-color: {COLORS['selbg']};
        width: 16px;
        height: 16px;
        margin: -5px 0;
        border-radius: 8px;
    }}
    
    QSlider::handle:horizontal:hover {{
        background-color: {COLORS['accent']};
    }}
    
    QSlider::groove:vertical {{
        background-color: {COLORS['input_bg']};
        width: 6px;
        border-radius: 3px;
    }}
    
    QSlider::handle:vertical {{
        background-color: {COLORS['selbg']};
        width: 16px;
        height: 16px;
        margin: 0 -5px;
        border-radius: 8px;
    }}
    
    QSlider::handle:vertical:hover {{
        background-color: {COLORS['accent']};
    }}
    
    /* ==================== Status Bar ==================== */
    QStatusBar {{
        background-color: {COLORS['bg']};
        color: {COLORS['fg']};
        border-top: 1px solid {COLORS['input_border']};
    }}
    
    /* ==================== Frame (for cards/panels) ==================== */
    QFrame[class="card"] {{
        background-color: {COLORS['button_bg']};
        border: 1px solid {COLORS['input_border']};
        border-radius: 8px;
        padding: 10px;
    }}
    
    /* ==================== Update Banner ==================== */
    #updateBanner {{
        background-color: #90EE90;
        border-bottom: 1px solid #228B22;
        padding: 5px;
    }}
    
    #updateBanner QLabel {{
        color: #228B22;
        background-color: transparent;
    }}
    
    #updateBanner QLabel[class="link"] {{
        color: #4682B4;
        text-decoration: underline;
    }}
    
    #updateBanner QLabel[class="link"]:hover {{
        color: #5A9BFF;
    }}
    """