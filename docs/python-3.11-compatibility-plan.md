# Python 3.11 Compatibility Implementation Plan for AI Assistants

## Overview
The Collective Modding Toolkit uses Python 3.12-specific features that need to be replaced for Python 3.11 compatibility. This plan provides step-by-step instructions for AI assistants to make the necessary changes.

## Python 3.12+ Features Found in Codebase

### Critical Features Requiring Changes:
1. **Path.walk() method** (Python 3.12) - Used in 3 locations
2. **Nested f-strings** (Python 3.12 improvement) - Used in 1 location

### Features That Work in Python 3.11 (No Changes Needed):
- Union type syntax (X | Y) - Python 3.10+
- Match-case statements - Python 3.10+
- String.removeprefix() - Python 3.9+

## Critical Changes Required

### 1. Replace Path.walk() with os.walk() (Python 3.12 → 3.11)

**Files to modify:**
- `src/utils.py:61`
- `src/tabs/_scanner.py:344, 449`

**Implementation steps:**

#### a) `src/utils.py:61`:
```python
# BEFORE (Python 3.12):
for root, _, files in path.walk():

# AFTER (Python 3.11):
import os
for root, _, files in os.walk(path):
```

#### b) `src/tabs/_scanner.py:344`:
```python
# BEFORE (Python 3.12):
for root, folders, files in mod_path.walk(top_down=True):

# AFTER (Python 3.11):
import os
for root, folders, files in os.walk(mod_path, topdown=True):
```

#### c) `src/tabs/_scanner.py:449`:
```python
# BEFORE (Python 3.12):
for current_path, folders, files in data_path.walk(top_down=True):

# AFTER (Python 3.11):
import os
for current_path, folders, files in os.walk(data_path, topdown=True):
```

**Note:** Ensure `import os` is added at the top of each file if not already present.

### 2. Replace Nested F-strings (Python 3.12 → 3.11)

**File to modify:**
- `src/tabs/_overview.py:113-114`

**Implementation steps:**
```python
# BEFORE (Python 3.12 nested f-strings):
f"Portable: {manager.portable}\n{f'Portable.txt: {manager.portable_txt_path}\n' if manager.portable_txt_path else ''}"
f"{'\n'.join([f'{k.rjust(max_len)}: {v}' for k, v in manager.mo2_settings.items()])}"

# AFTER (Python 3.11 compatible):
# For the first f-string:
portable_txt_line = f'Portable.txt: {manager.portable_txt_path}\n' if manager.portable_txt_path else ''
f"Portable: {manager.portable}\n{portable_txt_line}"

# The second f-string is already Python 3.11 compatible (no nested f-strings)
```

## Configuration Updates

### 3. Update pyproject.toml

Change line 8:
```toml
# BEFORE:
requires-python = ">=3.11,<3.13"

# AFTER:
requires-python = ">=3.11,<3.12"
```

Change line 18:
```toml
# BEFORE:
python = ">=3.11,<3.13"

# AFTER:
python = ">=3.11,<3.12"
```

### 4. Update CLAUDE.md

Update line 73:
```markdown
# BEFORE:
- Requires Python 3.11-3.12 (configured in pyproject.toml)

# AFTER:
- Requires Python 3.11 (configured in pyproject.toml)
```

## Verification Steps

After making all changes, run these commands to verify compatibility:

### 1. Run type checking:
```bash
poetry run mypy src
poetry run pyright
```

### 2. Run linting:
```bash
poetry run ruff check src
poetry run ruff format --check src
```

### 3. Test the application:
```bash
poetry run python src/main.py
```

### 4. Build the executable:
```bash
poetry run pyinstaller --distpath dist --workpath build/pyinstaller --specpath build --clean --onedir --windowed --icon=src/icon.ico --add-data="src/assets;assets" --name="cm-toolkit" src/main.py
```

## Summary for AI Assistants

When implementing these changes:

1. **Start with Path.walk() replacements** - This is the most critical change
2. **Fix the nested f-strings issue** - Simple variable extraction
3. **Update configuration files** - Ensure version constraints reflect Python 3.11 only
4. **Run all verification commands** - Ensure no regressions
5. **Keep all other modern features** - Union types, match-case work in Python 3.11

### Key Points:
- Only 2 Python 3.12-specific features need modification
- The codebase's Python 3.10+ features (union types, match-case) remain unchanged
- Total changes required: 4 code locations + 2 configuration files
- All changes are straightforward replacements with no algorithmic modifications needed

### Time Estimate:
- Code changes: 10-15 minutes
- Testing and verification: 10-15 minutes
- Total: 20-30 minutes for complete implementation