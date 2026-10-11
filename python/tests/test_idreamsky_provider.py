from __future__ import annotations

import os
from pathlib import Path

import pytest

from home_content.models import HomeContentRequest
from home_content.providers.idreamsky import IDreamSkyProvider


def _request(executable: Path, install: Path | None = None) -> HomeContentRequest:
    return HomeContentRequest(
        requestId="idreamsky-test",
        gameId="calabiyau",
        executablePath=str(executable),
        installDirectory=str(install) if install else None,
        providerId="idreamsky",
        providerOptions={"region": "cn"},
    )


def _mp4(path: Path, size: int = 300 * 1024) -> None:
    path.write_bytes(b"\x00\x00\x00\x18ftypisom" + b"\x00" * (size - 12))


@pytest.mark.asyncio
async def test_finds_verified_launcher_cache_from_nested_game_executable(tmp_path: Path) -> None:
    launcher = tmp_path / "卡拉彼丘" / "CalabiYau"
    executable = launcher / "Calabiyau" / "CalabiyauGame" / "Calabiyau.exe"
    executable.parent.mkdir(parents=True)
    executable.write_bytes(b"MZ")
    cache = launcher / "ImageCache"
    cache.mkdir()
    older = cache / "older.mp4"
    newer = cache / "newer.mp4"
    _mp4(older)
    _mp4(newer)
    os.utime(older, (1, 1))
    os.utime(newer, (2, 2))

    content = await IDreamSkyProvider().fetch(_request(executable))

    assert content.background is not None
    assert content.background.local_path == str(newer.resolve())


@pytest.mark.asyncio
async def test_install_root_can_contain_calabiyau_launcher_directory(tmp_path: Path) -> None:
    cache = tmp_path / "卡拉彼丘" / "CalabiYau" / "ImageCache"
    cache.mkdir(parents=True)
    video = cache / "current.mp4"
    _mp4(video)

    content = await IDreamSkyProvider().fetch(
        _request(tmp_path / "unknown.exe", tmp_path / "卡拉彼丘")
    )

    assert content.background is not None
    assert content.background.local_path == str(video.resolve())


@pytest.mark.asyncio
async def test_rejects_small_or_invalid_video_and_does_not_scan_movies(tmp_path: Path) -> None:
    launcher = tmp_path / "CalabiYau"
    cache = launcher / "ImageCache"
    cache.mkdir(parents=True)
    (cache / "invalid.mp4").write_bytes(b"not-an-mp4")
    movies = launcher / "Calabiyau" / "CalabiyauGame" / "PM" / "Content" / "Movies"
    movies.mkdir(parents=True)
    _mp4(movies / "UI_Hall.mp4")

    content = await IDreamSkyProvider().fetch(_request(launcher / "CalabiYau.exe"))

    assert content.background is None
