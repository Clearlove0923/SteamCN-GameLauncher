"""读取共享游戏来源字典；先匹配安装目录，再用真实 EXE 名称兜底。"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from functools import lru_cache
from importlib.resources import files
from typing import Any


@dataclass(frozen=True)
class GameSource:
    game_id: str
    provider_id: str
    provider_options: dict[str, Any]


def _segments(path: str | None) -> list[str]:
    # Windows 路径与接口上传的斜杠路径统一切段，不对磁盘做递归扫描。
    return [part.strip().casefold() for part in re.split(r"[\\/]", path or "") if part.strip()]


@lru_cache(maxsize=1)
def _indexes() -> tuple[dict[str, list[GameSource]], dict[str, GameSource], dict[str, GameSource]]:
    # 启动后只解析一次 JSON。EXE 可有多个所属游戏，目录名必须唯一。
    payload = json.loads(files("home_content").joinpath("game_sources.json").read_text(encoding="utf-8"))
    by_exe: dict[str, list[GameSource]] = {}
    by_folder: dict[str, GameSource] = {}
    by_game_id: dict[str, GameSource] = {}
    game_ids: set[str] = set()
    for row in payload:
        game_id = str(row["gameId"]).strip()
        provider_id = str(row["providerId"]).strip()
        options = row["providerOptions"]
        if not game_id or game_id in game_ids or not provider_id or options.get("region") != "cn":
            raise ValueError(f"Invalid game source: {game_id}")
        game_ids.add(game_id)
        source = GameSource(game_id, provider_id, options)
        by_game_id[game_id.casefold()] = source
        app_id = str(row.get("appId", "")).strip()
        if app_id:
            by_game_id[app_id.casefold()] = source
        for name in row.get("exeNames", []):
            key = str(name).strip().casefold()
            if not key or "/" in key or "\\" in key:
                raise ValueError(f"Invalid game-source EXE name: {name}")
            owners = by_exe.setdefault(key, [])
            if source in owners:
                raise ValueError(f"Duplicate game-source EXE name: {name}")
            owners.append(source)
        for name in row.get("folderNames", []):
            key = str(name).strip().casefold()
            if not key or key in by_folder or "/" in key or "\\" in key:
                raise ValueError(f"Duplicate or invalid game-source folder: {name}")
            by_folder[key] = source
    return by_exe, by_folder, by_game_id


def get_game_source_by_id(game_id: str | None) -> GameSource | None:
    """Resolve a company's game selection from the same shared source file."""
    return _indexes()[2].get((game_id or "").strip().casefold())


def match_game_source(executable_path: str | None, install_directory: str | None) -> GameSource | None:
    """EXE 路径和安装路径中的目录由近到远优先；最后匹配 EXE 完整文件名。"""
    by_exe, by_folder, _ = _indexes()
    exe_segments = _segments(executable_path)
    for segments in (exe_segments[:-1], _segments(install_directory)):
        for segment in reversed(segments):
            if segment in by_folder:
                return by_folder[segment]
    if exe_segments and len(owners := by_exe.get(exe_segments[-1], [])) == 1:
        return owners[0]
    return None
