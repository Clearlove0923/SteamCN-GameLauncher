"""KuroLauncherProvider unit tests driven by sanitized JSON fixtures.

The OS fixtures (sampled 2026-09-14 against ``en.json``) live at
``contracts/samples/kuro-{launcher-config,wallpapers-slogan,
news-notices}.json``. The CN fixtures (sampled 2026-09-25 against
``zh-Hans.json``) live at
``contracts/samples/kuro-cn-{launcher-config,bg-zh-Hans,
info-zh-Hans}.json``. Random CDN URL hashes are stripped by the
sampling script.

Tests use ``httpx.MockTransport`` to route each request to the right
fixture so the Provider can be exercised without touching the network.
"""

from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path
from typing import Any

import httpx
import pytest

from home_content.providers.kuro_launcher import (
    ALLOWED_HOST_SUFFIXES,
    DEFAULT_APP_ID,
    DEFAULT_APP_KEY,
    DEFAULT_CN_APP_ID,
    DEFAULT_CN_APP_KEY,
    DEFAULT_CN_GAME_ID,
    DEFAULT_GAME_ID,
    DEFAULT_LANGUAGE,
    DEFAULT_LANGUAGE_CN,
    DEFAULT_LANGUAGE_OS,
    DEFAULT_REGION,
    KuroLauncherProvider,
    _allowed,
    _build_background,
    _build_banners,
    _build_news_items,
    _language_candidates,
    _parse_mmdd,
    _resolve_region,
)
from home_content.models import HomeContentRequest

SAMPLE_ROOT = Path(__file__).resolve().parents[2] / "contracts" / "samples"


def _load(name: str) -> dict[str, Any]:
    return json.loads((SAMPLE_ROOT / name).read_text(encoding="utf-8"))


def _request(
    options: dict[str, Any] | None = None,
    *,
    locale: str = "en",
) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="kuro-1",
        game_id="wuthering-waves",
        provider_id="kuro-launcher",
        locale=locale,
        provider_options=options or {},
    )


def _build_provider(*responses: Any) -> tuple[KuroLauncherProvider, httpx.AsyncClient]:
    """Responses may be dicts (200) or Exception instances (propagated)."""
    queue = list(responses)

    def handler(request: httpx.Request) -> httpx.Response:
        if not queue:
            return httpx.Response(500, json={"error": "no more fixtures"})
        item = queue.pop(0)
        if isinstance(item, Exception):
            raise item
        return httpx.Response(200, json=item)

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    return KuroLauncherProvider(client=client), client


# ---------------------------------------------------------------------------
# OS (Global / en.json) — pre-existing tests
# ---------------------------------------------------------------------------


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
    # CN-side hosts must also pass.
    assert _allowed("https://prod-alicdn-community.kurobbs.com/x.jpg") is True
    assert _allowed("https://mc.kurogames.com/main/news/detail/5478") is True
    assert _allowed("https://pcdownload-huoshan.aki-game.com/launcher/x.mp4") is True
    assert _allowed("https://example.invalid/evil.mp4") is False
    assert _allowed(None) is False
    assert _allowed("not-a-url") is False


def test_default_launcher_constants() -> None:
    # Defaults now point at the CN region — that's the launcher's target
    # audience and the only region Kuro ships zh-Hans.json on.
    assert DEFAULT_REGION == "cn"
    assert DEFAULT_CN_APP_ID == "10003"
    assert DEFAULT_CN_APP_KEY == "Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5"
    assert DEFAULT_CN_GAME_ID == "G152"
    assert DEFAULT_LANGUAGE_CN == "zh-Hans"
    assert DEFAULT_LANGUAGE_OS == "en"
    # OS constants are kept around for backwards compatibility and the
    # `providerOptions['region']="os"` escape hatch.
    assert DEFAULT_APP_ID == "50004"
    assert DEFAULT_APP_KEY == "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c"
    assert DEFAULT_GAME_ID == "G153"
    assert DEFAULT_LANGUAGE == DEFAULT_LANGUAGE_OS


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


def test_parse_mmdd_stamps_current_year() -> None:
    parsed = _parse_mmdd("08-19")
    assert parsed is not None
    assert parsed.year == datetime.now().year
    assert parsed.month == 8
    assert parsed.day == 19


def test_parse_mmdd_returns_none_for_invalid() -> None:
    assert _parse_mmdd(None) is None
    assert _parse_mmdd("") is None
    assert _parse_mmdd("not-a-date") is None
    assert _parse_mmdd("13-40") is None
    assert _parse_mmdd("2026-08-19") is None  # ISO format rejected


def test_build_news_items_parses_mmdd_time_to_published_at() -> None:
    payload = {
        "guidance": {
            "notice": {
                "title": "公告",
                "functionSwitch": 1,
                "contents": [
                    {"content": "公告一", "jumpUrl": "https://x/", "time": "08-19"},
                    {"content": "公告二", "jumpUrl": "https://y/", "time": "09-30"},
                ],
            }
        }
    }
    items = _build_news_items(payload)
    assert len(items) == 2
    assert items[0].published_at is not None
    assert items[0].published_at.month == 8 and items[0].published_at.day == 19
    assert items[1].published_at.month == 9 and items[1].published_at.day == 30


@pytest.mark.asyncio
async def test_fetch_walks_three_layers_and_returns_full_content() -> None:
    config = _load("kuro-launcher-config.json")
    wallpaper = _load("kuro-wallpapers-slogan.json")
    news = _load("kuro-news-notices.json")

    provider, client = _build_provider(config, wallpaper, news)
    try:
        result = await provider.fetch(_request(locale="en", options={"region": "os"}))
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
            await provider.fetch(_request(locale="en", options={"region": "os"}))
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
            await provider.fetch(_request(locale="en", options={"region": "os"}))
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
        result = await provider.fetch(_request(locale="en", options={
            "region": "os",
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


# ---------------------------------------------------------------------------
# CN region — sampled 2026-09-25 against ``zh-Hans.json``
# ---------------------------------------------------------------------------


def test_loads_all_cn_fixtures() -> None:
    config = _load("kuro-cn-launcher-config.json")
    wallpaper = _load("kuro-cn-bg-zh-Hans.json")
    news = _load("kuro-cn-info-zh-Hans.json")
    assert config["functionCode"]["background"] == "HASH32_CN"
    assert wallpaper["backgroundFile"].startswith("https://")
    assert news["guidance"]
    # All three CN categories are populated.
    assert len(news["guidance"]["activity"]["contents"]) >= 1
    assert len(news["guidance"]["notice"]["contents"]) >= 1
    assert len(news["guidance"]["news"]["contents"]) >= 1


def test_resolve_region_defaults_to_cn_and_falls_back_on_unknown() -> None:
    assert _resolve_region({}) == "cn"
    assert _resolve_region({"region": "CN"}) == "cn"  # case-insensitive
    assert _resolve_region({"region": "os"}) == "os"
    # Unknown values fall back to the default region (cn).
    assert _resolve_region({"region": "jp"}) == "cn"
    assert _resolve_region({"region": ""}) == "cn"


def test_language_candidates_cn_with_zh_locale_prefers_zh_hans() -> None:
    req = _request(locale="zh-CN")
    assert _language_candidates("cn", req) == ["zh-Hans", "en"]
    # Any zh-* locale — including zh-TW / zh-HK — collapses to zh-Hans
    # because Kuro CN only ships zh-Hans.json.
    assert _language_candidates("cn", _request(locale="zh-TW")) == ["zh-Hans", "en"]
    assert _language_candidates("cn", _request(locale="ja-JP")) == ["zh-Hans", "en"]


def test_language_candidates_cn_with_en_locale_prefers_en() -> None:
    req = _request(locale="en-US")
    assert _language_candidates("cn", req) == ["en", "zh-Hans"]


def test_language_candidates_os_only_supports_en() -> None:
    assert _language_candidates("os", _request(locale="zh-CN")) == ["en"]
    assert _language_candidates("os", _request(locale="en-US")) == ["en"]


def test_language_candidates_explicit_override_wins() -> None:
    # Even with locale=zh-CN, providerOptions['language'] always wins.
    req = _request(locale="zh-CN", options={"language": "ja"})
    assert _language_candidates("cn", req) == ["ja"]


def test_build_banners_cn_returns_chinese_subtitles() -> None:
    payload = _load("kuro-cn-info-zh-Hans.json")
    banners = _build_banners(payload)
    assert len(banners) == 5
    notes = {b.title for b in banners}
    # All banner subtitles are Chinese in the CN feed.
    assert {"景燃pv", "景燃战斗演示", "3.6版本pv", "库洛充值中心", "鸣潮雷蛇联名款键鼠"} == notes
    for b in banners:
        assert b.image_url.startswith("https://")
        host = b.image_url.split("//", 1)[1].split("/", 1)[0]
        assert any(host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)


def test_build_news_items_cn_parses_chinese_titles_and_dates() -> None:
    payload = _load("kuro-cn-info-zh-Hans.json")
    items = _build_news_items(payload)
    # 2 activities + 3 notices + 2 news = 7 items.
    assert len(items) == 7
    titles = [item.title for item in items]
    assert "《鸣潮》3.6 版本创作激励计划" in titles
    assert "关于客户端资源分级功能的相关说明" in titles
    assert "档案公开 | 幽炁诀——景燃" in titles
    # All titles should carry published_at — Kuro CN stamps "MM-DD".
    for item in items:
        assert item.published_at is not None
        assert item.published_at.year == datetime.now().year
    # Categories preserved in our canonical mapping.
    categories = {item.category for item in items}
    assert categories == {"活动", "公告", "资讯"}


@pytest.mark.asyncio
async def test_fetch_cn_returns_chinese_content_for_zh_locale() -> None:
    config = _load("kuro-cn-launcher-config.json")
    wallpaper = _load("kuro-cn-bg-zh-Hans.json")
    news = _load("kuro-cn-info-zh-Hans.json")

    seen_urls: list[str] = []
    queue = [config, wallpaper, news]

    def handler(request: httpx.Request) -> httpx.Response:
        seen_urls.append(str(request.url))
        if not queue:
            return httpx.Response(500, json={"error": "exhausted"})
        return httpx.Response(200, json=queue.pop(0))

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = KuroLauncherProvider(client=client)
    try:
        result = await provider.fetch(_request(locale="zh-CN"))
    finally:
        await client.aclose()

    # Background points at the CN huoshan CDN.
    assert result.background is not None
    assert "huoshan.aki-game.com" in (result.background.video_url or "")
    # Banners carry Chinese subtitles.
    banner_titles = {b.title for b in result.banners}
    assert {"景燃pv", "景燃战斗演示", "3.6版本pv"}.issubset(banner_titles)
    # News items are Chinese and stamped with the current year.
    news_titles = [item.title for item in result.news]
    assert "档案公开 | 幽炁诀——景燃" in news_titles
    assert all(item.published_at is not None for item in result.news)
    # URLs hit CN endpoints (the implicit region=cn default).
    assert any("prod-cn-alicdn-gamestarter.kurogame.com" in u for u in seen_urls)
    assert any("/zh-Hans.json" in u for u in seen_urls)
    # CN appId/appKey/gameId are in the path.
    assert any("10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/G152" in u for u in seen_urls)


@pytest.mark.asyncio
async def test_fetch_cn_falls_back_to_en_when_zh_hans_404s() -> None:
    config = _load("kuro-cn-launcher-config.json")
    os_wallpaper = _load("kuro-wallpapers-slogan.json")
    os_news = _load("kuro-news-notices.json")

    def handler(request: httpx.Request) -> httpx.Response:
        path = request.url.path
        # First pair of wallpaper/news is for zh-Hans → 404.
        # Second pair is for en → 200 with OS fixtures.
        if "/zh-Hans.json" in path:
            return httpx.Response(404, text="NoSuchKey")
        if "/background/" in path and path.endswith("/en.json"):
            return httpx.Response(200, json=os_wallpaper)
        if "/information/" in path and path.endswith("/en.json"):
            return httpx.Response(200, json=os_news)
        if "/index.json" in path:
            return httpx.Response(200, json=config)
        return httpx.Response(404, text="unexpected")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = KuroLauncherProvider(client=client)
    try:
        result = await provider.fetch(_request(locale="zh-CN"))
    finally:
        await client.aclose()

    # The fallback populated English content.
    assert result.background is not None
    assert len(result.banners) >= 1
    assert len(result.news) >= 1
    # News titles are English (from the OS fixture).
    assert any("Convene" in item.title for item in result.news)


@pytest.mark.asyncio
async def test_fetch_raises_when_all_languages_404() -> None:
    config = _load("kuro-cn-launcher-config.json")

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path.endswith("/index.json"):
            return httpx.Response(200, json=config)
        return httpx.Response(404, text="NoSuchKey")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = KuroLauncherProvider(client=client)
    try:
        with pytest.raises(httpx.HTTPStatusError):
            await provider.fetch(_request(locale="zh-CN"))
    finally:
        await client.aclose()


@pytest.mark.asyncio
async def test_fetch_cn_partial_data_when_one_endpoint_404s() -> None:
    """If wallpaper 404s but news 200s, return wallpaper=None + populated
    news. The UI is allowed to render news on top of a static fallback
    background."""
    config = _load("kuro-cn-launcher-config.json")
    news = _load("kuro-cn-info-zh-Hans.json")

    def handler(request: httpx.Request) -> httpx.Response:
        path = request.url.path
        if path.endswith("/index.json"):
            return httpx.Response(200, json=config)
        if "/background/" in path:
            return httpx.Response(404, text="NoSuchKey")
        if "/information/" in path and path.endswith("/zh-Hans.json"):
            return httpx.Response(200, json=news)
        return httpx.Response(404, text="unexpected")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = KuroLauncherProvider(client=client)
    try:
        result = await provider.fetch(_request(locale="zh-CN"))
    finally:
        await client.aclose()

    assert result.background is None
    assert len(result.news) >= 1
    assert any("景燃" in item.title for item in result.news)