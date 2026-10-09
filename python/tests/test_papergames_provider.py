"""PaperGames company Provider uses official launcher media first."""

from __future__ import annotations

import os
from pathlib import Path

import pytest

from home_content.models import HomeBackground, HomeContent, HomeContentRequest
from home_content.providers.company import (
    PaperGamesProvider,
    _find_infinity_nikki_launcher_background,
)
from home_content.providers.nextjs_data import NextJsDataProvider


def _request(cache_directory: Path, game_id: str = "infinity-nikki") -> HomeContentRequest:
    return HomeContentRequest(
        request_id="paper-test",
        game_id=game_id,
        provider_id="papergames",
        provider_options={
            "region": "cn",
            "launcherCacheDirectory": str(cache_directory),
        },
    )


def _write_webm(path: Path, size: int = 1 * 1024 * 1024) -> None:
    path.write_bytes(b"\x1a\x45\xdf\xa3" + b"\0" * (size - 4))


def test_launcher_background_selects_newest_valid_webm(tmp_path: Path) -> None:
    older = tmp_path / "older.webm"
    newer = tmp_path / "newer.webm"
    _write_webm(older)
    _write_webm(newer)
    os.utime(older, ns=(1_000_000_000, 1_000_000_000))
    os.utime(newer, ns=(2_000_000_000, 2_000_000_000))

    assert _find_infinity_nikki_launcher_background(
        {"launcherCacheDirectory": str(tmp_path)}
    ) == newer.resolve()


def test_launcher_background_rejects_tiny_or_non_webm_files(tmp_path: Path) -> None:
    (tmp_path / "tiny.webm").write_bytes(b"\x1a\x45\xdf\xa3")
    (tmp_path / "fake.webm").write_bytes(b"not-webm" + b"\0" * (1024 * 1024))

    assert _find_infinity_nikki_launcher_background(
        {"launcherCacheDirectory": str(tmp_path)}
    ) is None


@pytest.mark.asyncio
async def test_company_provider_replaces_website_video_with_launcher_cache(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    launcher_video = tmp_path / "current.webm"
    _write_webm(launcher_video)

    async def fake_fetch(self, request):
        return HomeContent(
            background=HomeBackground(
                video_url="https://assets.papegames.com/marketing-grass.mp4",
                image_url="https://assets.papegames.com/marketing-grass.jpg",
            )
        )

    monkeypatch.setattr(NextJsDataProvider, "fetch", fake_fetch)
    result = await PaperGamesProvider().fetch(_request(tmp_path))

    assert result.background is not None
    assert result.background.local_path == str(launcher_video.resolve())
    assert result.background.video_url is None
    assert result.background.image_url is None


@pytest.mark.asyncio
async def test_company_provider_does_not_fall_back_to_website_background(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    async def fake_fetch(self, request):
        return HomeContent(
            background=HomeBackground(
                video_url="https://assets.papegames.com/marketing-grass.mp4"
            )
        )

    monkeypatch.setattr(NextJsDataProvider, "fetch", fake_fetch)
    result = await PaperGamesProvider().fetch(_request(tmp_path))

    assert result.background is None


@pytest.mark.asyncio
async def test_company_provider_rejects_another_game(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="Unknown Papergames game"):
        await PaperGamesProvider().fetch(_request(tmp_path, "wuthering-waves"))
