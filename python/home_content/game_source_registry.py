"""读取共享游戏来源字典；先匹配真实 EXE，再匹配安装路径中的完整目录名。"""

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
def _indexes() -> tuple[dict[str, GameSource], dict[str, GameSource]]:
    # 启动后只解析一次 JSON，后续请求直接查两个哈希表。
    payload = json.loads(files("home_content").joinpath("game_sources.json").read_text(encoding="utf-8"))
    by_exe: dict[str, GameSource] = {}
    by_folder: dict[str, GameSource] = {}
    game_ids: set[str] = set()
    for row in payload:
        game_id = str(row["gameId"]).strip()
        provider_id = str(row["providerId"]).strip()
        options = row["providerOptions"]
        if not game_id or game_id in game_ids or not provider_id or options.get("region") != "cn":
            raise ValueError(f"Invalid game source: {game_id}")
        game_ids.add(game_id)
        source = GameSource(game_id, provider_id, options)
        for names, index in ((row.get("exeNames", []), by_exe), (row.get("folderNames", []), by_folder)):
            # 重名配置属于维护错误，启动时明确报错，避免请求被随机路由到错误 Provider。
            for name in names:
                key = str(name).strip().casefold()
                if not key or key in index or "/" in key or "\\" in key:
                    raise ValueError(f"Duplicate or invalid game-source identifier: {name}")
                index[key] = source
    return by_exe, by_folder


def match_game_source(executable_path: str | None, install_directory: str | None) -> GameSource | None:
    """EXE 完整文件名优先；未命中才由近到远匹配安装目录段。"""
    by_exe, by_folder = _indexes()
    exe_segments = _segments(executable_path)
    if exe_segments and exe_segments[-1] in by_exe:
        return by_exe[exe_segments[-1]]
    for path in (executable_path, install_directory):
        for segment in reversed(_segments(path)):
            if segment in by_folder:
                return by_folder[segment]
    return None
