"""HoYoPlayJsonProvider unit tests driven by a sanitized JSON fixture.

The fixture lives at contracts/samples/hoyoplay-cn-launcher-info.json.
It captures the shape of the upstream response observed on 2026-09-14,
with URL hashes redacted to a placeholder so the sample is safe to keep
in git history.

These tests do not touch the network. The Provider is constructed with
an httpx MockTransport that returns the fixture regardless of request
URL, so we can exercise biz filtering, host allow-listing and video
preference deterministically.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Iterable

import httpx
import pytest

from home_content.providers.hoyoplay_json import (
    ALLOWED_HOST_SUFFIXES,
    HoYoPlayJsonProvider,
    _allowed,
    _game_biz_from_game_id,
    _pick_background,
)
from home_content.models import HomeContentRequest

SAMPLE_PATH = Path(__file__).resolve().parents[2] / "contracts" / "samples" / "hoyoplay-cn-launcher-info.json"


def _load_fixture() -> dict[str, Any]:
    return json.loads(SAMPLE_PATH.read_text(encoding="utf-8"))


def test_steam_app_ids_map_to_hoyoplay_business_ids() -> None:
    assert _game_biz_from_game_id("1671200") == "bh3_cn"
    assert _game_biz_from_game_id(" 4162040 ") == "nap_cn"
    assert _game_biz_from_game_id("unknown") == ""


def _request(game_id: str, options: dict[str, Any] | None = None) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="fixture-1",
        game_id=game_id,
        provider_id="hoyoplay-json",
        locale="zh-CN",
        provider_options=options or {"region": "cn"},
    )


def _build_provider(fixture: dict[str, Any]) -> HoYoPlayJsonProvider:
    """Build a Provider backed by an httpx MockTransport serving the fixture."""

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=fixture)

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    return HoYoPlayJsonProvider(client=client), client


def test_fixture_loads() -> None:
    data = _load_fixture()
    assert "data" in data
    assert "game_info_list" in data["data"]
    assert len(data["data"]["game_info_list"]) >= 1


def test_allowed_host_suffixes_only_known_cdn() -> None:
    assert _allowed("https://launcher-webstatic.mihoyo.com/foo.webp") is True
    assert _allowed("https://example.invalid/background.webp") is False
    assert _allowed("not-a-url") is False
    assert _allowed(None) is False
    assert _allowed("") is False


def test_pick_background_prefers_video_when_available() -> None:
    payload = {
        "data": {
            "game_info_list": [
                {
                    "game": {"id": "x", "biz": "nap_cn"},
                    "backgrounds": [
                        {
                            "background": {"url": "https://launcher-webstatic.mihoyo.com/img.webp"},
                            "icon": {"url": ""},
                            "video": {"url": "https://launcher-webstatic.mihoyo.com/vid.webm"},
                        },
                        {
                            "background": {"url": "https://launcher-webstatic.mihoyo.com/img2.webp"},
                            "icon": {"url": ""},
                            "video": {"url": ""},
                        },
                    ],
                }
            ]
        }
    }
    bg = _pick_background(payload, "nap_cn")
    assert bg is not None
    assert bg.video_url == "https://launcher-webstatic.mihoyo.com/vid.webm"
    assert bg.image_url == "https://launcher-webstatic.mihoyo.com/img.webp"


def test_pick_background_falls_back_to_image_when_no_video() -> None:
    payload = {
        "data": {
            "game_info_list": [
                {
                    "game": {"id": "x", "biz": "hk4e_cn"},
                    "backgrounds": [
                        {
                            "background": {"url": "https://launcher-webstatic.mihoyo.com/img.webp"},
                            "icon": {"url": ""},
                            "video": {"url": ""},
                        }
                    ],
                }
            ]
        }
    }
    bg = _pick_background(payload, "hk4e_cn")
    assert bg is not None
    assert bg.video_url is None
    assert bg.image_url == "https://launcher-webstatic.mihoyo.com/img.webp"


def test_pick_background_drops_unknown_host() -> None:
    payload = {
        "data": {
            "game_info_list": [
                {
                    "game": {"id": "x", "biz": "nap_cn"},
                    "backgrounds": [
                        {
                            "background": {"url": "https://example.invalid/evil.webp"},
                            "icon": {"url": ""},
                            "video": {"url": "https://example.invalid/evil.webm"},
                        }
                    ],
                }
            ]
        }
    }
    bg = _pick_background(payload, "nap_cn")
    assert bg is None


def test_pick_background_unknown_biz_falls_back_to_first_game() -> None:
    fixture = _load_fixture()
    bg = _pick_background(fixture, "nonexistent-game")
    assert bg is not None  # falls back to first game in fixture


@pytest.mark.asyncio
async def test_provider_fetch_returns_video_for_nap_cn() -> None:
    fixture = _load_fixture()
    provider, client = _build_provider(fixture)
    try:
        result = await provider.fetch(_request("nap_cn"))
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url is not None
    assert result.background.video_url.endswith(".webm")
    assert any(result.background.video_url.endswith(s) or result.background.video_url.endswith(".mp4") for s in (".webm", ".mp4"))


@pytest.mark.asyncio
async def test_provider_fetch_returns_image_only_for_hk4e_cn() -> None:
    fixture = _load_fixture()
    # Force hk4e_cn to be image-only by zeroing its video URLs in the fixture copy.
    modified = json.loads(json.dumps(fixture))
    for entry in modified["data"]["game_info_list"]:
        if entry.get("game", {}).get("biz") == "hk4e_cn":
            for bg in entry.get("backgrounds", []):
                bg.setdefault("video", {})["url"] = ""

    provider, client = _build_provider(modified)
    try:
        result = await provider.fetch(_request("hk4e_cn"))
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url is None
    assert result.background.image_url is not None


@pytest.mark.asyncio
async def test_provider_fetch_returns_empty_when_fixture_empty() -> None:
    empty = {"data": {"game_info_list": []}}
    provider, client = _build_provider(empty)
    try:
        result = await provider.fetch(_request("hk4e_cn"))
    finally:
        await client.aclose()

    assert result.background is None


@pytest.mark.asyncio
async def test_provider_fetch_handles_non_zero_retcode() -> None:
    payload = {"retcode": 1024, "message": "Invalid launcher_id", "data": {}}
    provider, client = _build_provider(payload)
    try:
        with pytest.raises(ValueError, match="retcode"):
            await provider.fetch(_request("hk4e_cn"))
    finally:
        await client.aclose()
