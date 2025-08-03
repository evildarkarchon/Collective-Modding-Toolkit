"""Threading infrastructure for the Scanner module.

This module provides specialized threading classes for the scanner tab,
including extended signals, worker implementation, and thread-safe data structures.
"""
from __future__ import annotations

import os
import logging
from pathlib import Path
from typing import TYPE_CHECKING, Optional
from dataclasses import dataclass, field
from threading import Lock

from PySide6.QtCore import Signal, QMutex, QMutexLocker

from qt_threading import BaseWorker, WorkerSignals
from helpers import ProblemInfo, SimpleProblemInfo
from enums import ProblemType, SolutionType, Tool
from globals import (
    RACE_SUBGRAPH_THRESHOLD,
    INFO_SCAN_RACE_SUBGRAPHS,
    F4SE_CRC,
    ARCHIVE_NAME_WHITELIST,
    FONT,
    FONT_SMALL,
    COLOR_NEUTRAL_2,
    TOOLTIP_LOCATION,
    WINDOW_WIDTH,
)
from scan_settings import (
    DATA_WHITELIST,
    JUNK_FILE_SUFFIXES,
    JUNK_FILES,
    PROPER_FORMATS,
    ModFiles,
    ScanSetting,
    ScanSettings,
)
from utils import (
    exists,
    is_dir,
    is_file,
    read_text_encoded,
    rglob,
)

if TYPE_CHECKING:
    from qt_helpers import CMCheckerInterface

logger = logging.getLogger(__name__)


class ScannerWorkerSignals(WorkerSignals):
    """Extended signals for scanner-specific operations."""
    
    # Progress updates
    progress_text = Signal(str)       # Status text updates
    progress_value = Signal(float)    # Progress bar updates (0-100)
    
    # Scan stage updates
    scan_stage = Signal(str)          # Current scan stage
    folder_update = Signal(tuple)     # Folder list updates
    current_folder = Signal(str)      # Current folder being scanned
    
    # Problem discovery
    problem_found = Signal(object)    # Single problem found
    problems_found = Signal(list)     # Batch of problems found
    
    # Scan statistics
    scan_stats = Signal(dict)         # Statistics about the scan


@dataclass
class ScanProgressState:
    """Thread-safe container for scan progress state."""
    _mutex: QMutex = field(default_factory=QMutex, init=False)
    current_folder: str = ""
    folder_index: int = 0
    total_folders: int = 0
    scan_stage: str = ""
    problems_found: int = 0
    files_scanned: int = 0
    
    def update_folder(self, folder: str, index: int, total: int):
        """Update current folder progress."""
        with QMutexLocker(self._mutex):
            self.current_folder = folder
            self.folder_index = index
            self.total_folders = total
            
    def update_stage(self, stage: str):
        """Update current scan stage."""
        with QMutexLocker(self._mutex):
            self.scan_stage = stage
            
    def increment_files(self, count: int = 1):
        """Increment files scanned counter."""
        with QMutexLocker(self._mutex):
            self.files_scanned += count
            
    def increment_problems(self, count: int = 1):
        """Increment problems found counter."""
        with QMutexLocker(self._mutex):
            self.problems_found += count
            
    def get_progress_percentage(self) -> float:
        """Calculate current progress percentage."""
        with QMutexLocker(self._mutex):
            if self.total_folders == 0:
                return 0.0
            return (self.folder_index / self.total_folders) * 100.0
            
    def get_stats(self) -> dict:
        """Get current scan statistics."""
        with QMutexLocker(self._mutex):
            return {
                'files_scanned': self.files_scanned,
                'problems_found': self.problems_found,
                'current_folder': self.current_folder,
                'progress': self.get_progress_percentage()
            }


class ThreadSafeResultAccumulator:
    """Thread-safe accumulator for scan results."""
    
    def __init__(self):
        self._mutex = QMutex()
        self._problems: list[ProblemInfo | SimpleProblemInfo] = []
        
    def add_problem(self, problem: ProblemInfo | SimpleProblemInfo):
        """Add a single problem to the results."""
        with QMutexLocker(self._mutex):
            self._problems.append(problem)
            
    def add_problems(self, problems: list[ProblemInfo | SimpleProblemInfo]):
        """Add multiple problems to the results."""
        with QMutexLocker(self._mutex):
            self._problems.extend(problems)
            
    def get_all(self) -> list[ProblemInfo | SimpleProblemInfo]:
        """Get a copy of all accumulated problems."""
        with QMutexLocker(self._mutex):
            return self._problems.copy()
            
    def clear(self):
        """Clear all accumulated problems."""
        with QMutexLocker(self._mutex):
            self._problems.clear()
            
    def count(self) -> int:
        """Get the current count of problems."""
        with QMutexLocker(self._mutex):
            return len(self._problems)


class ScanWorker(BaseWorker[list[ProblemInfo | SimpleProblemInfo]]):
    """Worker thread for performing file system scans."""
    
    def __init__(self, 
                 cmc: CMCheckerInterface,
                 scan_settings: ScanSettings,
                 overview_problems: list[ProblemInfo | SimpleProblemInfo] | None = None):
        super().__init__()
        # Replace standard signals with extended version
        self.signals = ScannerWorkerSignals()
        
        # Store references
        self.cmc = cmc
        self.scan_settings = scan_settings
        self.overview_problems = overview_problems or []
        
        # Internal state
        self.progress_state = ScanProgressState()
        self.result_accumulator = ThreadSafeResultAccumulator()
        
        # For batch processing
        self.problem_batch: list[ProblemInfo | SimpleProblemInfo] = []
        self.batch_size = 10  # Emit problems in batches for efficiency
        
    def execute(self) -> list[ProblemInfo | SimpleProblemInfo]:
        """Execute the scan operation."""
        try:
            # Add overview problems if enabled
            if self.scan_settings[ScanSetting.OverviewIssues] and self.overview_problems:
                self.result_accumulator.add_problems(self.overview_problems)
                self.progress_state.increment_problems(len(self.overview_problems))
                self.signals.problems_found.emit(self.overview_problems)
            
            # Skip data scan if requested
            if self.scan_settings.skip_data_scan:
                self.signals.progress_text.emit("Scan complete (data scan skipped)")
                self.signals.progress_value.emit(100.0)
                return self.result_accumulator.get_all()
            
            # Build mod file index
            self.update_scan_stage("Building mod file index...")
            mod_files = self.build_mod_file_list()
            
            # Perform the main scan
            self.scan_data_files(mod_files)
            
            # Emit final statistics
            self.signals.scan_stats.emit(self.progress_state.get_stats())
            
            return self.result_accumulator.get_all()
            
        except Exception as e:
            logger.exception("Error during scan")
            raise
            
    def update_scan_stage(self, stage: str):
        """Update the current scan stage."""
        self.progress_state.update_stage(stage)
        self.signals.scan_stage.emit(stage)
        self.update_status(stage)
        
    def add_problem(self, problem: ProblemInfo | SimpleProblemInfo):
        """Add a problem to the results and batch."""
        self.result_accumulator.add_problem(problem)
        self.problem_batch.append(problem)
        self.progress_state.increment_problems()
        
        # Emit batch if it's full
        if len(self.problem_batch) >= self.batch_size:
            self.emit_problem_batch()
            
    def emit_problem_batch(self):
        """Emit the current batch of problems."""
        if self.problem_batch:
            self.signals.problems_found.emit(self.problem_batch.copy())
            self.problem_batch.clear()
            
    def build_mod_file_list(self) -> ModFiles:
        """Build the mod file list (ported from scanner_tab)."""
        mod_files = ModFiles()
        
        if not self.scan_settings.using_stage or \
           not self.scan_settings.manager or \
           self.scan_settings.manager.name != "Mod Organizer":
            return mod_files
            
        # Get stage paths
        stage_paths = self.get_stage_paths()
        
        for mod_path in stage_paths:
            if not self.is_running:  # Check for cancellation
                break
                
            mod_name = mod_path.name
            for root, folders, files in os.walk(mod_path, topdown=True):
                if not self.is_running:
                    break
                    
                root_path = Path(root)
                root_is_mod_path = root_path == mod_path
                
                # Filter folders
                if folders:
                    last_index = len(folders) - 1
                    for i, folder in enumerate(reversed(folders)):
                        folder_lower = folder.lower()
                        if folder_lower in self.scan_settings.skip_directories:
                            del folders[last_index - i]
                            
                # Process folder
                if root_is_mod_path:
                    root_relative = Path()
                else:
                    root_relative = root_path.relative_to(mod_path)
                    mod_files.folders[root_relative] = (mod_name, root_path)
                    
                # Process files
                for file in files:
                    file_lower = file.lower()
                    if file_lower.endswith(self.scan_settings.skip_file_suffixes):
                        continue
                        
                    full_path = root_path / file
                    mod_files.files[root_relative / file] = (mod_name, full_path)
                    
                    if root_is_mod_path:
                        if file_lower.endswith((".esp", ".esl", ".esm")):
                            mod_files.modules[file] = (mod_name, full_path)
                        elif file_lower.endswith(".ba2"):
                            mod_files.archives[file] = (mod_name, full_path)
                            
                self.progress_state.increment_files(len(files))
                
        self.scan_settings.mod_files = mod_files
        return mod_files
        
    def get_stage_paths(self) -> list[Path]:
        """Get stage paths from mod manager (ported from scanner_tab)."""
        manager = self.scan_settings.manager
        if not (manager and manager.stage_path and 
                manager.profiles_path and manager.selected_profile and 
                manager.overwrite_path):
            raise ValueError(f"Missing MO2 settings")
            
        modlist_path = manager.profiles_path / manager.selected_profile / "modlist.txt"
        if not is_file(modlist_path):
            raise FileNotFoundError(f"File doesn't exist: {modlist_path}")
            
        stage_paths = [
            mod_path
            for mod in reversed(modlist_path.read_text("utf-8").splitlines())
            if mod[:1] == "+" and is_dir(mod_path := manager.stage_path / mod[1:])
        ]
        
        if is_dir(manager.overwrite_path):
            stage_paths.append(manager.overwrite_path)
            
        return stage_paths
        
    def scan_data_files(self, mod_files: ModFiles):
        """Main scanning logic (to be ported from scan_data_files)."""
        data_path = self.cmc.game.data_path
        if data_path is None:
            return
            
        # Scan for Complex Sorter errors if enabled
        if self.scan_settings[ScanSetting.Errors]:
            self.scan_complex_sorter_errors()
            
        # Scan for race subgraphs if enabled  
        if self.scan_settings[ScanSetting.RaceSubgraphs]:
            self.scan_race_subgraphs()
            
        # Main data folder scan
        if not self.scan_settings.skip_data_scan:
            self.scan_data_folder(data_path, mod_files)
            
        # Emit any remaining problems
        self.emit_problem_batch()
        
    def scan_complex_sorter_errors(self):
        """Scan for Complex Sorter INI errors."""
        if not self.scan_settings.manager:
            return
            
        if Tool.ComplexSorter not in self.scan_settings.manager.executables:
            return
            
        self.update_scan_stage("Checking Complex Sorter INIs...")
        
        for tool_path in self.scan_settings.manager.executables[Tool.ComplexSorter]:
            if not self.is_running:
                break
                
            for ini_path in rglob(tool_path.parent, "ini"):
                if not self.is_running:
                    break
                    
                ini_text, _ = read_text_encoded(ini_path)
                ini_lines = ini_text.splitlines(keepends=True)
                error_found = False
                
                for ini_line in ini_lines:
                    if not ini_line.startswith(";") and (
                        'FindNode OBTS(FindNode "Addon Index"' in ini_line
                        or "FindNode OBTS(FindNode 'Addon Index'" in ini_line
                    ):
                        error_found = True
                        break
                        
                if error_found:
                    problem = ProblemInfo(
                        ProblemType.ComplexSorter,
                        ini_path,
                        ini_path.relative_to(tool_path.parent),
                        tool_path.parent.name,
                        "INI uses an outdated field name. xEdit 4.1.5g changed the name of 'Addon Index' to 'Parent Combination Index'. Using outdated INIs with xEdit 4.1.5g+ results in broken output that may crash the game.",
                        SolutionType.ComplexSorterFix,
                    )
                    self.add_problem(problem)
                    
    def scan_race_subgraphs(self):
        """Scan for race subgraph records."""
        self.update_scan_stage("Scanning Race Subgraph Records...")
        
        sadd_modules: list[tuple[int, Path]] = []
        sadd_total = 0
        sadd_bytes = b"\x00\x53\x41\x44\x44"
        
        for module_path in self.cmc.game.modules_enabled:
            if not self.is_running:
                break
                
            try:
                module_bytes = module_path.read_bytes()
            except OSError:
                continue
                
            sadd_count = module_bytes.count(sadd_bytes)
            if sadd_count:
                sadd_modules.append((sadd_count, module_path))
                sadd_total += sadd_count
                
        if sadd_total > RACE_SUBGRAPH_THRESHOLD:
            problem = SimpleProblemInfo(
                f"{sadd_total} SADD Records from {len(sadd_modules)} modules",
                "Race Subgraph Record Count",
                INFO_SCAN_RACE_SUBGRAPHS,
                "IF you are experiencing stutter when moving between cells, removing some of these mods could alleviate performance issues.\nMerging them may also reduce stutter.",
                file_list=sadd_modules,
            )
            self.add_problem(problem)
            
    def scan_data_folder(self, data_path: Path, mod_files: ModFiles):
        """Scan the main data folder."""
        self.update_scan_stage("Scanning data folder...")
        
        data_root_lower = "Data"
        
        # Walk the data directory
        for root, folders, files in os.walk(data_path, topdown=True):
            if not self.is_running:
                break
                
            current_path = Path(root)
            current_path_relative = current_path.relative_to(data_path)
            mod_name, mod_path = mod_files.folders.get(current_path_relative) or ("", current_path)
            
            # Handle root data folder
            if current_path == data_path:
                folder_tuple = tuple(folders)
                self.signals.folder_update.emit(folder_tuple)
                self.progress_state.total_folders = len(folders)
                
            # Handle immediate subfolders of data
            if current_path.parent == data_path:
                folder_name = current_path.name
                self.signals.current_folder.emit(folder_name)
                
                # Update progress
                try:
                    folder_index = list(data_path.iterdir()).index(current_path)
                    self.progress_state.update_folder(folder_name, folder_index, self.progress_state.total_folders)
                    self.update_progress(int(self.progress_state.get_progress_percentage()))
                except ValueError:
                    pass
                    
                data_root_lower = folder_name.lower()
                
                # Check for junk folders
                if self.scan_settings[ScanSetting.JunkFiles] and data_root_lower == "fomod":
                    problem = ProblemInfo(
                        ProblemType.JunkFile,
                        mod_path,
                        current_path_relative,
                        mod_name,
                        "This is a junk folder not used by the game or mod managers.",
                        SolutionType.DeleteOrIgnoreFolder,
                    )
                    self.add_problem(problem)
                    folders.clear()
                    continue
                    
                # Skip non-whitelisted folders
                if data_root_lower not in DATA_WHITELIST:
                    folders.clear()
                    continue
                    
                # Check for loose previs
                if self.scan_settings[ScanSetting.LoosePrevis] and data_root_lower == "vis":
                    problem = ProblemInfo(
                        ProblemType.LoosePrevis,
                        mod_path,
                        current_path_relative,
                        mod_name,
                        "Loose previs files should be archived so they only win conflicts according to their plugin's load order.\nLoose previs files are also not supported by PJM's Previs Scripts.",
                        SolutionType.ArchiveOrDeleteFolder,
                    )
                    self.add_problem(problem)
                    folders.clear()
                    continue
                    
            # Process subfolders
            if folders:
                self._process_folders(folders, current_path, current_path_relative, data_root_lower, mod_files)
                
            # Process files
            whitelist = DATA_WHITELIST.get(data_root_lower)
            self._process_files(files, current_path, current_path_relative, data_root_lower, mod_files, whitelist)
            
        # Emit any remaining problems
        self.emit_problem_batch()
        
    def _process_folders(self, folders: list[str], current_path: Path, current_path_relative: Path, 
                        data_root_lower: str, mod_files: ModFiles):
        """Process folders within the current directory."""
        last_index = len(folders) - 1
        for i, folder in enumerate(reversed(folders)):
            if not self.is_running:
                break
                
            folder_lower = folder.lower()
            if folder_lower in self.scan_settings.skip_directories:
                del folders[last_index - i]
                continue
                
            folder_path_full = current_path / folder
            folder_path_relative = current_path_relative / folder
            mod_name_folder, mod_path_folder = mod_files.folders.get(folder_path_relative) or ("", folder_path_full)
            
            if data_root_lower == "meshes":
                # Check for precombined meshes
                if self.scan_settings[ScanSetting.LoosePrevis] and folder_lower == "precombined":
                    problem = ProblemInfo(
                        ProblemType.LoosePrevis,
                        mod_path_folder,
                        folder_path_relative,
                        mod_name_folder,
                        "Loose previs files should be archived so they only win conflicts according to their plugin's load order.\nLoose previs files are also not supported by PJM's Previs Scripts.",
                        SolutionType.ArchiveOrDeleteFolder,
                    )
                    self.add_problem(problem)
                    del folders[last_index - i]
                    continue
                    
                # Check for AnimTextData
                if self.scan_settings[ScanSetting.ProblemOverrides] and folder_lower == "animtextdata":
                    problem = ProblemInfo(
                        ProblemType.AnimTextDataFolder,
                        mod_path_folder,
                        folder_path_relative,
                        mod_name_folder,
                        "The existence of unpacked AnimTextData may cause the game to crash.",
                        SolutionType.ArchiveOrDeleteFolder,
                    )
                    self.add_problem(problem)
                    del folders[last_index - i]
                    continue
                    
    def _process_files(self, files: list[str], current_path: Path, current_path_relative: Path,
                      data_root_lower: str, mod_files: ModFiles, whitelist: set[str] | None):
        """Process files within the current directory."""
        for file in files:
            if not self.is_running:
                break
                
            file_lower = file.lower()
            if self.scan_settings.skip_file_suffixes and file_lower.endswith(self.scan_settings.skip_file_suffixes):
                continue
                
            file_path_full = current_path / file
            file_path_relative = current_path_relative / file
            mod_name_file, mod_path_file = mod_files.files.get(file_path_relative) or ("", file_path_full)
            
            # Check for junk files
            if self.scan_settings[ScanSetting.JunkFiles] and (
                file_lower in JUNK_FILES or file_lower.endswith(JUNK_FILE_SUFFIXES)
            ):
                problem = ProblemInfo(
                    ProblemType.JunkFile,
                    mod_path_file,
                    file_path_relative,
                    mod_name_file,
                    "This is a junk file not used by the game or mod managers.",
                    SolutionType.DeleteOrIgnoreFile,
                )
                self.add_problem(problem)
                continue
                
            # Check F4SE overrides
            if data_root_lower == "scripts" and current_path.parent == self.cmc.game.data_path:
                if mod_name_file and self.scan_settings[ScanSetting.ProblemOverrides] and file_lower in F4SE_CRC:
                    problem = ProblemInfo(
                        ProblemType.F4SEOverride,
                        mod_path_file,
                        file_path_relative,
                        mod_name_file,
                        "This is an override of an F4SE script. This could break F4SE if they aren't the same version or this mod isn't intended to override F4SE files.",
                        "Check if this mod is supposed to override F4SE Scripts.\nIf this is a script extender/library or requires one, this is likely intentional but it must support your game version explicitly.\nOtherwise, this mod or file may need to be deleted.",
                    )
                    self.add_problem(problem)
                    continue
                    
            # Process file extensions
            file_split = file_lower.rsplit(".", maxsplit=1)
            if len(file_split) == 1:
                continue
                
            file_ext = file_split[1]
            
            # Check Complex Sorter INIs in data folder
            if self.scan_settings[ScanSetting.Errors] and data_root_lower == "complex sorter" and file_ext == "ini":
                self._check_complex_sorter_ini(file_path_full, file_path_relative, mod_name_file, mod_path_file)
                
            # Check for wrong format files
            if self.scan_settings[ScanSetting.WrongFormat]:
                self._check_file_format(file_ext, file_split[0], file_path_full, file_path_relative, 
                                      mod_name_file, mod_path_file, data_root_lower, whitelist, current_path_relative)
                                      
            self.progress_state.increment_files()
            
    def _check_complex_sorter_ini(self, file_path_full: Path, file_path_relative: Path, 
                                 mod_name: str, mod_path: Path):
        """Check Complex Sorter INI for errors."""
        ini_text, _ = read_text_encoded(file_path_full)
        ini_lines = ini_text.splitlines(keepends=True)
        error_found = False
        
        for ini_line in ini_lines:
            if not ini_line.startswith(";") and (
                'FindNode OBTS(FindNode "Addon Index"' in ini_line
                or "FindNode OBTS(FindNode 'Addon Index'" in ini_line
            ):
                error_found = True
                break
                
        if error_found:
            problem = ProblemInfo(
                ProblemType.ComplexSorter,
                mod_path,
                file_path_relative,
                mod_name,
                "INI uses an outdated field name. xEdit 4.1.5g changed the name of 'Addon Index' to 'Parent Combination Index'. Using outdated INIs with xEdit 4.1.5g+ results in broken output that may crash the game.",
                SolutionType.ComplexSorterFix,
            )
            self.add_problem(problem)
            
    def _check_file_format(self, file_ext: str, file_base: str, file_path_full: Path, 
                          file_path_relative: Path, mod_name: str, mod_path: Path,
                          data_root_lower: str, whitelist: set[str] | None, current_path_relative: Path):
        """Check if file format is valid for its location."""
        # Check whitelist
        if (whitelist and file_ext not in whitelist) or (
            file_ext == "dll" and str(current_path_relative).lower() != "f4se\\plugins"
        ):
            solution = None
            if file_ext in PROPER_FORMATS:
                proper_found = [
                    p.name for e in PROPER_FORMATS[file_ext] 
                    if is_file(p := file_path_full.with_suffix(f".{e}"))
                ]
                if proper_found:
                    summary = f"Format not in whitelist for {data_root_lower}.\nA file with the expected format was found ({', '.join(proper_found)})."
                    solution = SolutionType.DeleteOrIgnoreFile
                else:
                    summary = f"Format not in whitelist for {data_root_lower}.\nA file with the expected format was NOT found ({', '.join(PROPER_FORMATS[file_ext])})."
                    solution = SolutionType.ConvertDeleteOrIgnoreFile
            else:
                summary = f"Format not in whitelist for {data_root_lower}.\nUnable to determine whether the game will use this file."
                solution = SolutionType.UnknownFormat
                
            problem = ProblemInfo(
                ProblemType.UnexpectedFormat,
                mod_path,
                file_path_relative,
                mod_name,
                summary,
                solution,
            )
            self.add_problem(problem)
            return
            
        # Check BA2 archive names
        if (file_ext == "ba2" and 
            file_base not in ARCHIVE_NAME_WHITELIST and
            file_path_full not in self.cmc.game.archives_enabled):
            
            ba2_name_split = file_base.rsplit(" - ", 1)
            no_suffix = len(ba2_name_split) == 1
            
            if no_suffix or ba2_name_split[1] not in self.cmc.game.ba2_suffixes:
                problem = ProblemInfo(
                    ProblemType.InvalidArchiveName,
                    mod_path,
                    file_path_relative,
                    mod_name,
                    "This is not a valid archive name and won't be loaded by the game.",
                    SolutionType.RenameArchive,
                    extra_data=[
                        f"\nValid Suffixes: {', '.join(self.cmc.game.ba2_suffixes)}",
                        f"Example: {ba2_name_split[0]} - Main.ba2",
                    ],
                )
                self.add_problem(problem)