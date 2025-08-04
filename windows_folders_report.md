# Windows Special Folders Detection Report

## Executive Summary
The Collective Modding Toolkit currently uses Windows-specific APIs (via `ctypes.windll`) to detect special folders. This report outlines the current implementation and provides recommendations for making Windows dependencies optional while maintaining functionality.

## Current Implementation Analysis

### 1. Windows Dependencies Used

#### Core Windows APIs
- **SHGetFolderPathW** (`windll.shell32.SHGetFolderPathW`): Used in `src/utils.py:143` to retrieve special folder paths
- **win32api**: Imported for Windows-specific operations
- **winreg**: Used for registry access (game detection)
- **ctypes.windll**: Direct Windows API calls for:
  - Font resource management (`windll.gdi32.AddFontResourceExW`)
  - Window theming (`windll.dwmapi.DwmSetWindowAttribute`)
  - Wine detection (`windll.ntdll.wine_get_version`)

### 2. Special Folders Accessed

The application accesses these Windows special folders via CSIDL constants:

| Folder | CSIDL Value | Usage | Location |
|--------|-------------|-------|----------|
| Desktop | 0 | Defined but unused | `src/enums.py:30` |
| Documents | 5 | Game saves location | `src/game_info.py:79`, `src/game_info_qt.py:80` |
| AppData | 26 | Defined but unused | `src/enums.py:32` |
| AppDataLocal | 28 | Plugins.txt location | `src/tabs/_overview.py:862`, `src/tabs/_overview_qt.py:594` |

### 3. Current Folder Access Pattern

```python
# Current implementation in src/utils.py
def get_environment_path(location: CSIDL) -> Path:
    buf = create_unicode_buffer(wintypes.MAX_PATH)
    windll.shell32.SHGetFolderPathW(None, location, None, 0, buf)
    path = Path(buf.value)
    if not is_dir(path):
        msg = f"Folder does not exist:\n{path}"
        raise FileNotFoundError(msg)
    return path
```

## Recommendations for Optional Windows Dependencies

### Strategy 1: Try-Except Import Pattern (Recommended)

Create a platform-agnostic wrapper that gracefully falls back to cross-platform alternatives:

```python
# src/platform_utils.py
import os
import sys
from pathlib import Path
from typing import Optional

# Try to import Windows-specific modules
try:
    from ctypes import windll, create_unicode_buffer, wintypes
    HAS_WINDOWS_APIS = True
except (ImportError, AttributeError):
    HAS_WINDOWS_APIS = False

class SpecialFolders:
    """Cross-platform special folder detection"""
    
    @staticmethod
    def get_documents() -> Path:
        if HAS_WINDOWS_APIS and sys.platform == 'win32':
            # Use Windows API for accuracy
            buf = create_unicode_buffer(wintypes.MAX_PATH)
            windll.shell32.SHGetFolderPathW(None, 5, None, 0, buf)  # CSIDL_PERSONAL
            return Path(buf.value)
        else:
            # Cross-platform fallback
            if sys.platform == 'win32':
                # Windows without ctypes
                docs = os.environ.get('USERPROFILE', '')
                if docs:
                    return Path(docs) / 'Documents'
            # Unix-like systems
            return Path.home() / 'Documents'
    
    @staticmethod
    def get_local_appdata() -> Path:
        if HAS_WINDOWS_APIS and sys.platform == 'win32':
            buf = create_unicode_buffer(wintypes.MAX_PATH)
            windll.shell32.SHGetFolderPathW(None, 28, None, 0, buf)  # CSIDL_LOCAL_APPDATA
            return Path(buf.value)
        else:
            # Environment variable fallback
            if sys.platform == 'win32':
                return Path(os.environ.get('LOCALAPPDATA', Path.home() / 'AppData' / 'Local'))
            # Unix-like: use XDG standard
            return Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local' / 'share'))
```

### Strategy 2: Conditional Import with Feature Detection

```python
# src/utils.py modification
def get_environment_path(location: CSIDL) -> Path:
    """Get special folder path with graceful fallback"""
    
    # Try Windows API first
    if sys.platform == 'win32':
        try:
            buf = create_unicode_buffer(wintypes.MAX_PATH)
            windll.shell32.SHGetFolderPathW(None, location, None, 0, buf)
            return Path(buf.value)
        except (NameError, AttributeError, OSError):
            # Fall back to environment variables
            pass
    
    # Fallback mapping
    fallback_map = {
        CSIDL.Documents: Path.home() / 'Documents',
        CSIDL.Desktop: Path.home() / 'Desktop',
        CSIDL.AppData: Path(os.environ.get('APPDATA', Path.home() / 'AppData' / 'Roaming')),
        CSIDL.AppDataLocal: Path(os.environ.get('LOCALAPPDATA', Path.home() / 'AppData' / 'Local'))
    }
    
    if location in fallback_map:
        path = fallback_map[location]
        if path.exists():
            return path
    
    raise FileNotFoundError(f"Could not locate special folder: {location}")
```

### Strategy 3: Abstract Platform Interface

```python
# src/platform_interface.py
from abc import ABC, abstractmethod
from pathlib import Path

class PlatformInterface(ABC):
    @abstractmethod
    def get_documents_folder(self) -> Path:
        pass
    
    @abstractmethod
    def get_local_appdata_folder(self) -> Path:
        pass

class WindowsPlatform(PlatformInterface):
    def __init__(self):
        self.has_winapi = self._check_winapi()
    
    def _check_winapi(self) -> bool:
        try:
            from ctypes import windll
            return True
        except ImportError:
            return False
    
    def get_documents_folder(self) -> Path:
        if self.has_winapi:
            # Use SHGetFolderPathW
            ...
        else:
            # Use environment variables
            return Path(os.environ.get('USERPROFILE', Path.home())) / 'Documents'

class UnixPlatform(PlatformInterface):
    def get_documents_folder(self) -> Path:
        return Path.home() / 'Documents'
    
    def get_local_appdata_folder(self) -> Path:
        return Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local' / 'share'))

# Factory function
def get_platform() -> PlatformInterface:
    if sys.platform == 'win32':
        return WindowsPlatform()
    else:
        return UnixPlatform()
```

## Implementation Priority

### High Priority Changes
1. **Wrap `windll.shell32.SHGetFolderPathW` calls** in try-except blocks
2. **Add environment variable fallbacks** for LOCALAPPDATA and USERPROFILE
3. **Use `Path.home()`** as ultimate fallback for user directories

### Medium Priority Changes
1. **Create platform detection module** to centralize OS-specific code
2. **Add configuration option** to force fallback mode for testing
3. **Implement logging** for fallback path usage

### Low Priority Changes
1. **Support XDG Base Directory** specification for Linux compatibility
2. **Add macOS-specific paths** (~/Library/Application Support)
3. **Create unit tests** for each platform scenario

## Testing Recommendations

### Test Scenarios
1. **Windows with ctypes available** (normal case)
2. **Windows without ctypes** (restricted Python environment)
3. **Windows with missing environment variables**
4. **Wine environment** (Linux running Windows apps)
5. **Pure Linux/macOS** (future compatibility)

### Test Implementation
```python
# tests/test_platform_folders.py
import unittest
from unittest.mock import patch, MagicMock
from pathlib import Path

class TestFolderDetection(unittest.TestCase):
    @patch('sys.platform', 'win32')
    @patch('ctypes.windll')
    def test_windows_api_available(self, mock_windll):
        # Test with Windows API available
        ...
    
    @patch('sys.platform', 'win32')
    @patch.dict('sys.modules', {'ctypes': None})
    def test_windows_api_unavailable(self):
        # Test fallback to environment variables
        ...
    
    @patch.dict('os.environ', {'LOCALAPPDATA': 'C:\\TestAppData\\Local'})
    def test_environment_fallback(self):
        # Test environment variable usage
        ...
```

## Migration Path

### Phase 1: Immediate (Non-Breaking)
1. Add try-except around existing `windll` calls
2. Implement environment variable fallbacks
3. Add debug logging for fallback usage

### Phase 2: Refactor (Minor Breaking)
1. Create `platform_utils.py` module
2. Replace direct `get_environment_path()` calls with new API
3. Update all imports

### Phase 3: Enhancement (Feature Addition)
1. Add cross-platform support
2. Implement configuration options
3. Add comprehensive test suite

## Benefits of Implementation

1. **Increased Portability**: Code can run in restricted Python environments
2. **Better Testing**: Easier to mock and test without Windows dependencies
3. **Future Compatibility**: Preparation for potential cross-platform support
4. **Graceful Degradation**: Application remains functional even if Windows APIs fail
5. **Cleaner Architecture**: Separation of platform-specific code

## Potential Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Incorrect folder paths from environment variables | Medium | Validate paths exist before use |
| Performance overhead from try-except | Low | Cache results after first successful call |
| Different behavior between API and fallback | Medium | Comprehensive testing and logging |
| User confusion if paths differ | Low | Clear error messages and documentation |

## Conclusion

Making Windows dependencies optional is achievable with minimal code changes. The recommended approach (Strategy 1) provides:
- **Immediate compatibility** with restricted environments
- **Minimal performance impact**
- **Clear upgrade path** for future enhancements
- **Maintained accuracy** when Windows APIs are available

The implementation can be done incrementally without breaking existing functionality, making it a low-risk, high-value improvement to the codebase.