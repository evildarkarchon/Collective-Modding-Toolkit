"""Example worker implementations for common CMT operations.

These workers demonstrate how to use the Qt threading infrastructure
for various long-running operations in the Collective Modding Toolkit.
"""
from __future__ import annotations

import time
import logging
from pathlib import Path
from typing import Optional, List, Dict, Any

from qt_threading import BaseWorker
from game_info import GameInfo
from mod_manager_info import MO2Info, VortexInfo
from scan_settings import ScanSettings
from scanner import Scanner

logger = logging.getLogger(__name__)


class GameInfoWorker(BaseWorker[Dict[str, Any]]):
    """Worker for loading game information in the background."""
    
    def __init__(self, game_info: GameInfo):
        super().__init__()
        self.game_info = game_info
        
    def execute(self) -> Dict[str, Any]:
        """Load game information and return results."""
        self.update_status("Detecting Fallout 4 installation...")
        self.update_progress(10)
        
        # Find game installation
        result = {
            "found": False,
            "path": None,
            "version": None,
            "exe_version": None,
            "install_type": None,
            "errors": []
        }
        
        try:
            # Check for game
            if self.game_info.find_game():
                result["found"] = True
                result["path"] = str(self.game_info.game_path)
                result["install_type"] = self.game_info.install_type
                
                self.update_progress(40)
                self.update_status("Reading game version...")
                
                # Get version info
                result["version"] = self.game_info.version
                result["exe_version"] = self.game_info.exe_version
                
                self.update_progress(70)
                self.update_status("Checking game files...")
                
                # Additional checks could go here
                
                self.update_progress(100)
                self.update_status("Game information loaded successfully")
            else:
                result["errors"].append("Fallout 4 installation not found")
                self.update_status("Fallout 4 not found")
                
        except Exception as e:
            logger.exception("Error loading game info")
            result["errors"].append(str(e))
            
        return result


class ModManagerWorker(BaseWorker[Dict[str, Any]]):
    """Worker for detecting mod managers."""
    
    def execute(self) -> Dict[str, Any]:
        """Detect installed mod managers."""
        self.update_status("Detecting mod managers...")
        self.update_progress(20)
        
        result = {
            "mo2": None,
            "vortex": None
        }
        
        # Check for MO2
        self.update_status("Checking for Mod Organizer 2...")
        mo2 = MO2Info()
        if mo2.find_mo2():
            result["mo2"] = {
                "path": str(mo2.mo2_path),
                "profile": mo2.profile,
                "profiles": mo2.get_profiles()
            }
        
        self.update_progress(60)
        
        # Check for Vortex
        self.update_status("Checking for Vortex...")
        vortex = VortexInfo()
        if vortex.find_vortex():
            result["vortex"] = {
                "path": str(vortex.vortex_path),
                "mods_path": str(vortex.mods_path) if vortex.mods_path else None
            }
            
        self.update_progress(100)
        self.update_status("Mod manager detection complete")
        
        return result


class ScannerWorker(BaseWorker[List[Dict[str, Any]]]):
    """Worker for scanning mods for issues."""
    
    def __init__(self, 
                 scanner: Scanner,
                 scan_settings: ScanSettings,
                 paths_to_scan: List[Path]):
        super().__init__()
        self.scanner = scanner
        self.scan_settings = scan_settings
        self.paths_to_scan = paths_to_scan
        
    def execute(self) -> List[Dict[str, Any]]:
        """Perform mod scanning."""
        results = []
        total_paths = len(self.paths_to_scan)
        
        for idx, path in enumerate(self.paths_to_scan):
            if not self.is_running:  # Check if we should stop
                break
                
            # Update progress
            progress = int((idx / total_paths) * 100)
            self.update_progress(progress)
            self.update_status(f"Scanning {path.name}...")
            
            try:
                # Perform scan (simplified example)
                scan_result = self._scan_path(path)
                if scan_result:
                    results.append(scan_result)
                    
            except Exception as e:
                logger.error(f"Error scanning {path}: {e}")
                
        self.update_progress(100)
        self.update_status(f"Scan complete. Found {len(results)} issues.")
        
        return results
        
    def _scan_path(self, path: Path) -> Optional[Dict[str, Any]]:
        """Scan a single path for issues."""
        # This is a simplified example - real implementation would use Scanner class
        issues = []
        
        # Example checks
        if path.suffix.lower() == ".dll":
            # Check DLL compatibility
            issues.append({
                "type": "dll_check",
                "severity": "warning",
                "message": "DLL requires compatibility check"
            })
            
        if issues:
            return {
                "path": str(path),
                "name": path.name,
                "issues": issues
            }
            
        return None


class FileOperationWorker(BaseWorker[bool]):
    """Worker for file operations like copying, moving, or deleting."""
    
    def __init__(self, 
                 operation: str,
                 source_files: List[Path],
                 destination: Optional[Path] = None):
        super().__init__()
        self.operation = operation
        self.source_files = source_files
        self.destination = destination
        
    def execute(self) -> bool:
        """Perform file operations."""
        total_files = len(self.source_files)
        
        for idx, file_path in enumerate(self.source_files):
            if not self.is_running:
                return False
                
            progress = int((idx / total_files) * 100)
            self.update_progress(progress)
            self.update_status(f"{self.operation} {file_path.name}...")
            
            try:
                if self.operation == "copy":
                    self._copy_file(file_path)
                elif self.operation == "move":
                    self._move_file(file_path)
                elif self.operation == "delete":
                    self._delete_file(file_path)
                    
            except Exception as e:
                logger.error(f"Error during {self.operation} of {file_path}: {e}")
                return False
                
        self.update_progress(100)
        self.update_status(f"{self.operation} completed successfully")
        return True
        
    def _copy_file(self, source: Path):
        """Copy a file to destination."""
        if self.destination:
            import shutil
            dest_file = self.destination / source.name
            shutil.copy2(source, dest_file)
            
    def _move_file(self, source: Path):
        """Move a file to destination."""
        if self.destination:
            import shutil
            dest_file = self.destination / source.name
            shutil.move(str(source), str(dest_file))
            
    def _delete_file(self, file_path: Path):
        """Delete a file."""
        file_path.unlink()


class DownloadWorker(BaseWorker[Path]):
    """Worker for downloading files with progress tracking."""
    
    def __init__(self, url: str, destination: Path):
        super().__init__()
        self.url = url
        self.destination = destination
        
    def execute(self) -> Path:
        """Download file from URL."""
        import urllib.request
        
        self.update_status(f"Downloading from {self.url}...")
        
        def download_progress(block_num, block_size, total_size):
            if not self.is_running:
                raise Exception("Download cancelled")
                
            downloaded = block_num * block_size
            if total_size > 0:
                progress = min(int((downloaded / total_size) * 100), 100)
                self.update_progress(progress)
                
        try:
            urllib.request.urlretrieve(
                self.url, 
                self.destination,
                reporthook=download_progress
            )
            
            self.update_progress(100)
            self.update_status("Download complete")
            return self.destination
            
        except Exception as e:
            # Clean up partial download
            if self.destination.exists():
                self.destination.unlink()
            raise