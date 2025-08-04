#
# Collective Modding Toolkit
# Copyright (C) 2024, 2025  wxMichael
#
# This program is free software; you can redistribute it and/or
# modify it under the terms of the GNU General Public License
# as published by the Free Software Foundation; either version 2
# of the License, or (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program; if not, see <https://www.gnu.org/licenses/>.
#


import logging
import platform
import re
import sys
from abc import ABC, abstractmethod
from pathlib import Path

# from tkinter import BOTH, CENTER, PhotoImage, StringVar, Text, Tk, Toplevel, ttk  # Tkinter removed
from typing import TYPE_CHECKING, NotRequired, TypedDict

import psutil  # pyright: ignore[reportMissingModuleSource]

# Windows-specific imports
if platform.system() == "Windows":
    import winreg
    from ctypes import windll  # pyright: ignore[reportUnknownVariableType, reportAttributeAccessIssue]
else:
    winreg = None  # type: ignore[assignment]
    windll = None  # type: ignore[assignment]

# from cmt_globals import COLOR_BAD, FONT_LARGE  # Tkinter-related, removed
from enums import InstallType, ProblemType, SolutionType, Tab

if TYPE_CHECKING:
	import psutil._pswindows as pswin  # pyright: ignore[reportMissingModuleSource]
	from PySide6.QtGui import QPixmap

	from app_settings import AppSettings
	from autofix_types import AutoFixResult
	# from game_info import GameInfo  # Tkinter version removed

logger = logging.getLogger(__name__)


pattern_cpu = re.compile(r"(?:\d+(?:th|rd|nd) Gen| ?Processor| ?CPU|\d*[- ]Core|\(TM\)|\(R\))")
pattern_whitespace = re.compile(r"\s+")

os_versions = {
	"18362": "1903",
	"18363": "1909",
	"19041": "2004",
	"19042": "20H2",
	"19043": "21H1",
	"19044": "21H2",
	"19045": "22H2",
	"22000": "21H2",
	"22621": "22H2",
	"22631": "23H2",
	"26100": "24H2",
}


class PCInfo:
	def __init__(self) -> None:
		self.using_wine = platform.system() == "Windows" and windll and hasattr(windll.ntdll, "wine_get_version") # pyright: ignore[reportUnknownArgumentType]
		self.os = self._get_os() if not self.using_wine else "Linux (WINE)"
		self.ram = self._get_ram()
		self.cpu = self._get_cpu()
		self.gpu, self.vram = self._get_gpu()

	@staticmethod
	def _get_os() -> str:
		os = platform.system()
		release = platform.release()
		version = os_versions.get(str(sys.getwindowsversion().build), "") if os == "Windows" else "" # pyright: ignore[reportUnknownArgumentType, reportAttributeAccessIssue]
		return f"{os} {release} {version}"

	@staticmethod
	def _get_ram() -> int:
		mem: pswin.svmem = psutil.virtual_memory()  # type: ignore[reportUnknownVariableType]
		if TYPE_CHECKING:
			assert isinstance(mem, pswin.svmem)
		return round(mem.total / 1024**3)

	@staticmethod
	def _get_cpu() -> str:
		if platform.system() != "Windows" or not winreg:
			return "Unknown CPU (non-Windows)"
		
		cpu_model = "Unknown CPU"
		try:
			with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, R"Hardware\Description\System\CentralProcessor\0") as key:  # type: ignore[attr-defined]
				model, value_type = winreg.QueryValueEx(key, "ProcessorNameString")  # type: ignore[attr-defined]
			if value_type == winreg.REG_SZ and isinstance(model, str):  # type: ignore[attr-defined]
				cpu_model = model
		except OSError:
			logger.exception("get_cpu():")
		else:
			if "Intel" in cpu_model and not cpu_model.startswith("Intel"):
				cpu_model = f"Intel {cpu_model.replace('Intel', '')}"
			cpu_model = pattern_cpu.sub("", cpu_model)
			cpu_model = pattern_whitespace.sub(" ", cpu_model)
			cpu_model = cpu_model.rsplit("@", 1)[0].strip()
		return cpu_model

	@staticmethod
	def _get_gpu() -> tuple[str, int]:
		if platform.system() != "Windows" or not winreg:
			return "Unknown GPU (non-Windows)", 0
		
		gpu_model = "Unknown GPU"
		gpu_memory = 0
		try:
			with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, R"HARDWARE\DEVICEMAP\VIDEO") as key:  # type: ignore[attr-defined]
				video_device, value_type = winreg.QueryValueEx(key, R"\Device\Video0")  # type: ignore[attr-defined]
			if value_type == winreg.REG_SZ and isinstance(video_device, str):  # type: ignore[attr-defined]
				video_device = video_device.removeprefix("\\Registry\\Machine\\")
				with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, video_device) as key:  # type: ignore[attr-defined]
					model, value_type_1 = winreg.QueryValueEx(key, "HardwareInformation.AdapterString")  # type: ignore[attr-defined]
					memory, value_type_2 = winreg.QueryValueEx(key, "HardwareInformation.qwMemorySize")  # type: ignore[attr-defined]
				if value_type_1 == winreg.REG_SZ and isinstance(model, str):  # type: ignore[attr-defined]
					gpu_model = model.strip()
				if value_type_2 == winreg.REG_QWORD and isinstance(memory, int):  # type: ignore[attr-defined]
					gpu_memory = round(memory / 1024**3)
		except OSError:
			logger.exception("get_gpu():")
		return gpu_model, gpu_memory


class CMCheckerInterface(ABC):
	def __init__(self) -> None:
		# self.root: Tk  # Tkinter removed
		# self.install_type_sv: StringVar  # Tkinter removed
		# self.game_path_sv: StringVar  # Tkinter removed
		# self.specs_sv_1: StringVar  # Tkinter removed
		# self.specs_sv_2: StringVar  # Tkinter removed
		# self.game: GameInfo  # Tkinter GameInfo removed - use Qt version
		self.pc: PCInfo
		self.overview_problems: list[ProblemInfo | SimpleProblemInfo]
		self.settings: AppSettings

	@abstractmethod
	def refresh_tab(self, tab: Tab) -> None: ...

	@abstractmethod
	def get_image(self, relative_path: str) -> "QPixmap": ...  # Return type changed - was PhotoImage (tkinter)


# CMCTabFrame class removed - was tkinter-specific
# Qt version uses different base classes and implementation


class FileInfo(TypedDict):
	File: Path | None
	Version: tuple[int, int, int, int] | str | None
	InstallType: InstallType | None


class DLLInfo(TypedDict):
	IsF4SE: bool
	SupportsOG: NotRequired[bool]
	SupportsNG: NotRequired[bool]


class ProblemInfo:
	def __init__(
		self,
		problem: ProblemType,
		path: Path,
		relative_path: Path,
		mod: str | None,
		summary: str,
		solution: SolutionType | str | None,
		*,
		file_list: list[tuple[int, Path]] | list[tuple[float, Path]] | list[tuple[str, Path]] | None = None,
		extra_data: list[str] | None = None,
	) -> None:
		self.type = problem
		self.path = path
		self.relative_path = relative_path
		self.mod = mod or ("<Unmanaged>" if problem != ProblemType.FileNotFound else "")
		self.summary = summary
		self.solution = solution
		self.file_list = file_list
		self.extra_data = extra_data
		self.autofix_result: AutoFixResult | None = None


class SimpleProblemInfo:
	def __init__(
		self,
		path: str,
		problem: str,
		summary: str,
		solution: str,
		*,
		file_list: list[tuple[int, Path]] | list[tuple[float, Path]] | list[tuple[str, Path]] | None = None,
		extra_data: list[str] | None = None,
	) -> None:
		self.path = path
		self.problem = problem
		self.summary = summary
		self.solution = solution
		self.type = problem
		self.relative_path = path
		self.mod = ""
		self.file_list = file_list
		self.extra_data = extra_data
		self.autofix_result: AutoFixResult | None = None


# StdErr class removed - was tkinter-specific
# Qt version uses different error handling approach
