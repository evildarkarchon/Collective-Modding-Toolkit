# Scanner Tab Completion Report

**Date**: 2025-08-03  
**Project**: Collective Modding Toolkit PySide6 Migration  
**Component**: Scanner Tab

This document outlines the remaining work needed to complete the migration of the Scanner tab from Tkinter to PySide6/Qt. The Scanner tab is the most complex tab in the application, featuring multi-threading, side panes, and extensive file system scanning capabilities.

## Executive Summary

The PySide6 migration for the Collective Modding Toolkit is approximately 83% complete, with 5 of 6 tabs fully migrated. The Scanner tab remains partially implemented (30%) and requires an estimated 11-16 hours of development work to complete. This report provides a detailed roadmap for finishing the Scanner tab implementation.

## Current Implementation Status

### ✅ Completed
- Basic tab structure and layout
- Tree widget for results display
- Progress bar widget
- Side pane with scan settings (basic structure)
- Qt variable bindings
- Threading infrastructure (QThread skeleton)

### ❌ Not Implemented
- Full scanning logic
- Result population in tree widget
- Details pane for selected results
- Tree selection handling
- Context menus and actions
- Autofix functionality
- File operations and problem resolution
- Window positioning and management
- Complete threading implementation

## Major Components Requiring Implementation

### 1. Threading System

#### Current Tkinter Implementation
- Uses `threading.Thread` for background scanning
- Queue-based communication between threads
- Progress updates via queue polling

#### Required Qt Implementation
```python
class ScanThread(QThread):
    # Signals needed:
    progress_text = Signal(str)  # Status text updates
    progress_value = Signal(float)  # Progress bar updates
    folder_update = Signal(tuple)  # Folder list updates
    problem_found = Signal(list)  # Problem list updates
    scan_complete = Signal()
    
    def run(self):
        # Implement full scan_data_files logic
        # Emit signals instead of queue.put()
```

### 2. Result Tree Population

#### Required Implementation
- Convert problem types to tree hierarchy
- Group problems by type
- Display mod names in second column (if using staging)
- Color coding for problem severity
- Store problem data for selection handling

#### Key Methods to Implement
```python
def populate_results(self, scan_settings: ScanSettings) -> None:
    # Clear tree
    # Group problems by type
    # Create tree structure with parent/child items
    # Store problem info in tree_results_data dict
    # Enable selection mode
```

### 3. Side Pane Window Management

#### Current Issues
- Side pane needs proper window flags for Qt
- Position tracking relative to main window
- Show/hide behavior when switching tabs
- Focus management

#### Required Implementation
```python
class SidePane(QDialog):
    def __init__(self, scanner_tab):
        # Set window flags: Qt.Tool | Qt.WindowStaysOnTopHint
        # Remove window decorations if needed
        # Implement position tracking
        
    def update_geometry(self):
        # Position next to main window
        # Account for screen boundaries
        
    def show_event/hide_event overrides
```

### 4. Result Details Pane

#### Functionality Needed
- Display selected problem details
- Show mod name, file path, problem description
- Solution recommendations
- Action buttons (Browse, Autofix, Copy)
- Tooltips for long paths
- Dynamic layout based on problem type

#### Key Components
```python
class ResultDetailsPane(QDialog):
    # Display fields:
    - Mod name (if applicable)
    - File path (clickable)
    - Problem type
    - Problem summary
    - Solution text
    - Extra data display
    
    # Action buttons:
    - Browse to file
    - Autofix (if available)
    - Copy path
    - Show file list (if applicable)
```

### 5. Scanning Logic Implementation

#### Core Scanning Features to Port
1. **Mod File Indexing**
   - Build index of files from mod staging folders
   - Track which mod provides each file

2. **Problem Detection**
   - Junk files
   - Wrong file formats
   - Loose previs files
   - Animation text data issues
   - F4SE script overrides
   - Invalid archive names
   - Complex Sorter INI errors
   - Race subgraph records

3. **File System Traversal**
   - Efficient directory walking
   - Progress updates per folder
   - Skip patterns and whitelists

### 6. Tree Selection and Context Menus

#### Required Features
- Single selection mode
- Display details pane on selection
- Right-click context menu:
  - Browse to file
  - Copy path
  - Autofix (if available)
  - Ignore problem

### 7. Autofix System

#### Implementation Needs
- Port autofix functions for each problem type
- Confirmation dialogs
- Error handling
- Result feedback

## Implementation Priority

### Phase 1: Core Functionality
1. Complete `scan_data_files` method in `ScanThread`
2. Implement `populate_results` with basic tree population
3. Add tree selection handling
4. Basic details pane display

### Phase 2: Window Management
1. Fix side pane positioning and behavior
2. Implement details pane with full functionality
3. Handle tab switching properly
4. Focus management between windows

### Phase 3: Advanced Features
1. Context menus
2. Autofix implementation
3. File operations (browse, copy)
4. Performance optimizations

### Phase 4: Polish
1. Progress feedback improvements
2. Error handling
3. Tooltips and help text
4. Keyboard shortcuts

## Technical Considerations

### Threading
- Use Qt signals for all thread communication
- No direct UI updates from worker thread
- Proper cleanup on scan cancellation

### Performance
- Consider using `QTreeWidgetItem` data roles for problem storage
- Lazy loading for large result sets
- Efficient string operations for path handling

### Platform Compatibility
- File path handling for Windows
- Proper Unicode support
- Case-insensitive path comparisons

## Testing Requirements

### Functional Tests
- Scan with various setting combinations
- Large directory structures
- Unicode file names
- Missing permissions
- Interrupted scans

### UI Tests
- Window positioning on different screen sizes
- Tab switching behavior
- Selection and detail display
- Progress updates
- Error states

### Integration Tests
- Interaction with game detection
- Mod manager integration
- Autofix operations
- File system operations

## Estimated Effort

Based on the complexity and amount of functionality:

- **Core scanning logic**: 4-6 hours
- **UI implementation**: 3-4 hours  
- **Window management**: 2-3 hours
- **Testing and debugging**: 2-3 hours
- **Total estimate**: 11-16 hours

## Dependencies

### Required Imports
```python
from PySide6.QtCore import QThread, Signal, Slot, QTimer, Qt
from PySide6.QtWidgets import (
    QTreeWidget, QTreeWidgetItem, QDialog, QMenu,
    QFileDialog, QMessageBox
)
from PySide6.QtGui import QAction, QCursor
```

### External Dependencies
- File system access
- Path manipulation
- Text encoding detection
- CRC32 calculations

## Overall Migration Progress

### Tab Migration Status
- ✅ **About Tab** - 100% complete
- ✅ **Settings Tab** - 100% complete
- ✅ **Overview Tab** - 100% complete
- ✅ **F4SE Tab** - 100% complete
- ✅ **Tools Tab** - 100% complete
- 🟡 **Scanner Tab** - 30% complete (basic structure only)

### Remaining Major Tasks
1. **Scanner Tab Completion** (11-16 hours)
   - Core scanning logic
   - Threading implementation
   - Window management
   - Result display and interaction
   
2. **Modal Dialogs** (4-6 hours)
   - Downgrader dialog
   - Archive Patcher dialog
   - About/Tree windows
   - Message boxes

3. **Testing & Polish** (2-4 hours)
   - Integration testing
   - Theme consistency
   - Performance optimization
   - Bug fixes

**Total Remaining Effort**: ~17-26 hours

## Conclusion

The Scanner tab is approximately 30% complete in terms of Qt migration. The basic structure is in place, but the core functionality needs to be implemented. The main challenges are:

1. Converting the threading model from Python threads/queues to Qt threads/signals
2. Implementing the complex file scanning logic
3. Managing multiple windows (main, side pane, details pane)
4. Preserving all the problem detection logic

The implementation should focus on maintaining feature parity with the Tkinter version while taking advantage of Qt's superior threading and event handling capabilities.

Five of the six tabs are fully migrated and functional. The Scanner tab remains the primary challenge due to its complexity, but with the patterns established in the other tabs and this detailed roadmap, completion is straightforward albeit time-consuming.