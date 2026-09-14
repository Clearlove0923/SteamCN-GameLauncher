"""KuroLauncherProvider unit tests driven by sanitized JSON fixtures.

The fixtures live at contracts/samples/kuro-{launcher-config,
wallpapers-slogan,news-notices}.json. They capture the upstream responses
sampled on 2026-09-14 against the Wuthering Waves Global launcher
(`G153`, `en.json`). Random CDN URL hashes are stripped by the sampling
script.

Tests use httpx MockTransport to route each request to the right
fixture so the Provider can be exercised without touching the network.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import httpx
import pytest

from home_content.providers.kuro_launcher import (
    ALLOWED_HOST_SUFFIXES,
    DEFAULT_APP_ID,
    DEFAULT_APP_KEY,
    DEFAULT_GAME_ID,
    DEFAULT_LANGUAGE,
    KuroLauncherProvider,
    _allowed,
    _build_background,
    _build_banners,
    _build_news_items,
)
from home_content.models import HomeContentRequest

SAMPLE_ROOT = Path(__file__).resolve().parents[2] / "contracts" / "samples"


def _load(name: str) -> dict[str, Any]:
    return json.loads((SAMPLE_ROOT / name).read_text(encoding="utf-8"))


def _request(options: dict[str, Any] | None = None) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="kuro-1",
        game_id="wuthering-waves",
        provider_id="kuro-launcher",
        locale="en",
        provider_options=options or {},
    )


def _build_provider(*responses: dict[str, Any]) -> tuple[KuroLauncherProvider, httpx.AsyncClient]:
    queue = list(responses)

    def handler(request: httpx.Request) -> httpx.Response:
        if not queue:
            return httpx.Response(500, json={"error": "no more fixtures"})
        return httpx.Response(200, json=queue.pop(0))

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    return KuroLauncherProvider(client=client), client


def test_loads_all_fixtures() -> None:
    config = _load("kuro-launcher-config.json")
    wallpaper = _load("kuro-wallpapers-slogan.json")
    news = _load("kuro-news-notices.json")
    assert config["functionCode"]["background"]
    assert wallpaper["backgroundFile"]
    assert news["guidance"]


def test_allowed_host_suffixes_only_known_cdn() -> None:
    assert _allowed("https://hw-pcdownload-qcloud.aki-game.net/foo.mp4") is True
    assert _allowed("https://prod-alicdn-gamestarter.kurogame.com/x.json") is True
    assert _allowed("https://example.invalid/evil.mp4") is False
    assert _allowed(None) is False
    assert _allowed("not-a-url") is False


def test_default_launcher_constants() -> None:
    assert DEFAULT_APP_ID == "50004"
    assert DEFAULT_APP_KEY == "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c"
    assert DEFAULT_GAME_ID == "G153"
    assert DEFAULT_LANGUAGE == "en"


def test_build_background_prefers_video() -> None:
    payload = _load("kuro-wallpapers-slogan.json")
    bg = _build_background(payload)
    assert bg is not None
    assert bg.video_url is not None
    assert bg.video_url.endswith(".mp4")
    assert bg.image_url is not None
    assert bg.image_url.endswith(".webp")


def test_build_background_falls_back_to_image_when_video_missing() -> None:
    payload = {
        "backgroundFile": "",
        "firstFrameImage": "https://hw-pcdownload-aws.aki-game.net/poster.webp",
    }
    bg = _build_background(payload)
    assert bg is not None
    assert bg.video_url is None
    assert bg.image_url.endswith(".webp")


def test_build_background_returns_none_when_url_unknown_host() -> None:
    payload = {
        "backgroundFile": "https://example.invalid/evil.mp4",
        "firstFrameImage": "https://example.invalid/poster.webp",
    }
    assert _build_background(payload) is None


def test_build_banners_filters_unknown_hosts() -> None:
    payload = _load("kuro-news-notices.json")
    banners = _build_banners(payload)
    assert len(banners) >= 1
    assert all(b.image_url for b in banners)
    for b in banners:
        host = b.image_url.split("//", 1)[1].split("/", 1)[0]
        assert any(host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)


def test_build_news_items_skips_disabled_categories() -> None:
    payload = _load("kuro-news-notices.json")
    # The fixture's activity bucket has functionSwitch=0; it must be skipped.
    items = _build_news_items(payload)
    categories = {item.category for item in items}
    assert "活动" not in categories
    assert "公告" in categories
    assert "资讯" in categories


def test_build_news_items_assigns_stable_ids() -> None:
    payload = _load("kuro-news-notices.json")
    items = _build_news_items(payload)
    ids = [item.id for item in items]
    assert len(ids) == len(set(ids))  # all unique


@pytest.mark.asyncio
async def test_fetch_walks_three_layers_and_returns_full_content() -> None:
    config = _load("kuro-launcher-config.json")
    wallpaper = _load("kuro-wallpapers-slogan.json")
    news = _load("kuro-news-notices.json")

    provider, client = _build_provider(config, wallpaper, news)
    try:
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url is not None
    assert len(result.banners) >= 1
    assert len(result.news) >= 1


@pytest.mark.asyncio
async def test_fetch_raises_when_background_hash_missing() -> None:
    bad_config = {"functionCode": {}}  # no background key
    wallpaper = _load("kuro-wallpapers-slogan.json")
    news = _load("kuro-news-notices.json")

    provider, client = _build_provider(bad_config, wallpaper, news)
    try:
        with pytest.raises(ValueError, match="background"):
            await provider.fetch(_request())
    finally:
        await client.aclose()


@pytest.mark.asyncio
async def test_fetch_propagates_httpx_errors() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="upstream unavailable")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = KuroLauncherProvider(client=client)
    try:
        with pytest.raises(httpx.HTTPStatusError):
            await provider.fetch(_request())
    finally:
        await client.aclose()


@pytest.mark.asyncio
async def test_fetch_uses_provider_options_overrides() -> None:
    seen_urls: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen_urls.append(str(request.url))
        if "/launcher/" in request.url.path and request.url.path.endswith("/index.json"):
            return httpx.Response(200, json={"functionCode": {"background": "abc123"}})
        # Wallpaper/news return shapes that produce empty content.
        return httpx.Response(200, json={})

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = KuroLauncherProvider(client=client)
    try:
        # Empty responses yield background=None + empty lists but the
        # fetch itself does not raise.
        result = await provider.fetch(_request({
            "appId": "99999",
            "appKey": "DIFFERENT_KEY",
            "gameId": "G999",
            "language": "zh-cn",
        }))
    finally:
        await client.aclose()

    assert result.background is None
    assert result.banners == []
    assert result.news == []
    assert any("99999_DIFFERENT_KEY/G999" in url for url in seen_urls)
    assert any("zh-cn.json" in url for url in seen_urls)