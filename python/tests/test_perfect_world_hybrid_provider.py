"""PerfectWorldHybridProvider unit tests driven by sanitized JS fixtures.

The fixtures live at
``contracts/samples/nte-game-swiper-{intl,cn}.json`` and
``contracts/samples/nte-news-data-{intl,cn}.json``. They capture real
payloads sampled on 2026-09-14 against the perfectworld.com /
wanmei.com / wmupd.com CDN endpoints. md5 fields are shortened; URL
hosts are kept (public CDN + official news domains) so the allow-list
exercises match the production path.

The Provider accepts the JS-shaped upstream response (``var NAME = {...};``)
and tests below replay those raw payloads through ``MockTransport`` so
the parsing helpers are exercised end-to-end without touching the
network.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import httpx
import pytest

from home_content.providers.perfect_world_hybrid import (
    ALLOWED_HOST_SUFFIXES,
    DEFAULT_APP_CODE,
    DEFAULT_BG_VIDEO_CN,
    DEFAULT_BG_VIDEO_OS,
    DEFAULT_LANGUAGE,
    DEFAULT_REGION,
    PerfectWorldHybridProvider,
    _absolutize_url,
    _allowed,
    _build_background,
    _build_banners,
    _build_news_items,
    _parse_date,
    _resolve_category,
    _select_banner_list,
    _select_news_blocks,
    _short_lang,
    extract_js_payload,
)
from home_content.models import HomeContentRequest

SAMPLE_ROOT = Path(__file__).resolve().parents[2] / "contracts" / "samples"


# ---------------------------------------------------------------------------
# Fixture helpers
# ---------------------------------------------------------------------------


def _load(name: str) -> dict[str, Any]:
    return json.loads((SAMPLE_ROOT / name).read_text(encoding="utf-8"))


def _js_payload(name: str) -> str:
    """Wrap a fixture dict in the upstream ``var NAME = {...};`` shell."""
    body = json.dumps(_load(name), indent=2, ensure_ascii=False)
    var_name = name.replace(".json", "").replace("-", "_")
    return f"var {var_name} = {body};\n"


def _request(options: dict[str, Any] | None = None) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="pw-1",
        game_id="neverness-to-everness",
        provider_id="perfect-world-hybrid",
        locale="en-US",
        provider_options=options or {},
    )


def _build_provider(swiper_payload: str | None, news_payload: str | None) -> tuple[PerfectWorldHybridProvider, httpx.AsyncClient]:
    def handler(request: httpx.Request) -> httpx.Response:
        url = str(request.url)
        if "nte-gameSwiper" in url or "yh-gameSwiper" in url:
            if swiper_payload is None:
                return httpx.Response(404, text="not found")
            return httpx.Response(200, text=swiper_payload)
        if "newsData" in url:
            if news_payload is None:
                return httpx.Response(404, text="not found")
            return httpx.Response(200, text=news_payload)
        return httpx.Response(404, text=f"unexpected url: {url}")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    return PerfectWorldHybridProvider(client=client), client


# ---------------------------------------------------------------------------
# JS payload extraction
# ---------------------------------------------------------------------------


def test_extract_js_payload_handles_var_assignment() -> None:
    raw = 'var foo = {"a": 1, "b": "x"};\n'
    assert extract_js_payload(raw) == {"a": 1, "b": "x"}


def test_extract_js_payload_handles_bare_object() -> None:
    raw = '{"only": "this"}'
    assert extract_js_payload(raw) == {"only": "this"}


def test_extract_js_payload_strips_trailing_commas() -> None:
    raw = '{"items": [{"a": 1,},\n{"b": 2,},\n]}'
    data = extract_js_payload(raw)
    assert data == {"items": [{"a": 1}, {"b": 2}]}


def test_extract_js_payload_collapses_double_commas() -> None:
    raw = '{"items": [{"a": 1,},\n            ,\n{"b": 2}]}'
    data = extract_js_payload(raw)
    assert data == {"items": [{"a": 1}, {"b": 2}]}


def test_extract_js_payload_rejects_non_object_body() -> None:
    with pytest.raises(ValueError, match="not a JS object assignment"):
        extract_js_payload("var foo = [1, 2, 3];")


def test_extract_js_payload_handles_real_intl_banner_payload() -> None:
    raw = _js_payload("nte-game-swiper-intl.json")
    data = extract_js_payload(raw)
    assert "lb1_en" in data
    assert isinstance(data["lb1_en"], list)
    assert data["lb1_en"][0]["bigpic"].startswith("https://")


def test_extract_js_payload_handles_real_intl_news_payload() -> None:
    raw = _js_payload("nte-news-data-intl.json")
    data = extract_js_payload(raw)
    assert "en" in data
    en = data["en"]
    assert {"news", "gamenews", "gamebroad", "gameevent"} <= set(en.keys())


# ---------------------------------------------------------------------------
# Constants & allow-list
# ---------------------------------------------------------------------------


def test_default_constants() -> None:
    assert DEFAULT_APP_CODE == "YDUTE5gscDZ229CW"
    assert DEFAULT_LANGUAGE == "en-us"
    assert DEFAULT_REGION == "os"
    assert DEFAULT_BG_VIDEO_OS.startswith("https://ntevmg.perfectworld.com/")
    assert DEFAULT_BG_VIDEO_CN.startswith("https://yhvmg.wmupd.com/")


def test_allowed_host_suffixes_only_known_cdn() -> None:
    assert _allowed("https://ntevmg.perfectworld.com/bg.mp4") is True
    assert _allowed("https://yhvmg.wmupd.com/bg.mp4") is True
    assert _allowed("https://nte.perfectworld.com/en/article/news/1") is True
    assert _allowed("https://yh.wanmei.com/news/gamenews/1") is True
    assert _allowed("https://example.invalid/evil.mp4") is False
    assert _allowed(None) is False
    assert _allowed("not-a-url") is False
    assert _allowed("ftp://ntevmg.perfectworld.com/foo") is False


def test_short_lang_drops_region_segment() -> None:
    assert _short_lang("en-us") == "en"
    assert _short_lang("zh-cn") == "cn"
    assert _short_lang("zh-tw") == "cn"
    assert _short_lang("ja-jp") == "jp"
    assert _short_lang("ko-kr") == "kr"
    assert _short_lang("de") == "de"
    assert _short_lang("") == "en"


# ---------------------------------------------------------------------------
# Background
# ---------------------------------------------------------------------------


def test_build_background_uses_local_path_when_provided() -> None:
    bg = _build_background({
        "backgroundVideoPath": "C:/Games/NTE/Client/bg.mp4",
        "backgroundImagePath": "C:/Games/NTE/Client/bg.jpg",
    }, region="os")
    assert bg is not None
    assert bg.video_url == "C:/Games/NTE/Client/bg.mp4"
    assert bg.image_url == "C:/Games/NTE/Client/bg.jpg"
    assert bg.local_path is None


def test_build_background_uses_network_fallback_for_os() -> None:
    bg = _build_background({}, region="os")
    assert bg is not None
    assert bg.video_url == DEFAULT_BG_VIDEO_OS


def test_build_background_uses_network_fallback_for_cn() -> None:
    bg = _build_background({}, region="cn")
    assert bg is not None
    assert bg.video_url == DEFAULT_BG_VIDEO_CN


def test_build_background_rejects_non_allowlisted_video_url() -> None:
    bg = _build_background({
        "backgroundVideoUrl": "https://example.invalid/bg.mp4",
    }, region="os")
    assert bg is not None
    # Falls back to default when the override host is rejected.
    assert bg.video_url == DEFAULT_BG_VIDEO_OS


# ---------------------------------------------------------------------------
# Banners
# ---------------------------------------------------------------------------


def test_select_banner_list_picks_lang_keyed_array_for_os() -> None:
    payload = _load("nte-game-swiper-intl.json")
    banners = _select_banner_list(payload, region="os", language="en-us")
    assert len(banners) == 3  # trimmed fixture
    assert all(b.get("bigpic", "").startswith("https://") for b in banners)


def test_select_banner_list_falls_back_to_lb1_when_lang_key_missing() -> None:
    payload = _load("nte-game-swiper-intl.json")
    banners = _select_banner_list(payload, region="os", language="fr-fr")
    # ``lb1_fr`` isn't in the fixture; we fall through to ``lb1`` which is
    # also empty for this fixture — still returns an empty list rather
    # than raising.
    assert banners == []


def test_select_banner_list_uses_lb1_for_cn() -> None:
    payload = _load("nte-game-swiper-cn.json")
    banners = _select_banner_list(payload, region="cn", language="zh-cn")
    assert len(banners) == 2
    assert banners[0]["bigpic"].endswith("1068x540.jpg")


def test_build_banners_filters_unknown_hosts() -> None:
    raw = {
        "lb1_en": [
            {"bigpic": "https://ntevmg.perfectworld.com/a.jpg", "link": "https://nte.perfectworld.com/en/article/news/1"},
            {"bigpic": "https://example.invalid/evil.jpg", "link": "https://nte.perfectworld.com/en/article/news/2"},
        ]
    }
    banners = _build_banners(raw, region="os", language="en-us")
    assert len(banners) == 1
    assert banners[0].target_url == "https://nte.perfectworld.com/en/article/news/1"


def test_build_banners_drops_jump_url_when_host_unknown() -> None:
    raw = {
        "lb1_en": [
            {"bigpic": "https://ntevmg.perfectworld.com/a.jpg", "link": "https://example.invalid/track"},
        ]
    }
    banners = _build_banners(raw, region="os", language="en-us")
    assert len(banners) == 1
    assert banners[0].target_url is None


def test_build_banners_uses_index_in_id_when_real_id_missing() -> None:
    raw = {
        "lb1_en": [
            {"bigpic": "https://ntevmg.perfectworld.com/a.jpg"},
            {"bigpic": "https://ntevmg.perfectworld.com/b.jpg"},
        ]
    }
    banners = _build_banners(raw, region="os", language="en-us")
    # The ID embeds the full ``language`` string (e.g. ``en-us``); the
    # short-lang ``en`` only drives which JSON bucket is read.
    assert banners[0].id == "pw-banner-os-en-us-0"
    assert banners[1].id == "pw-banner-os-en-us-1"


# ---------------------------------------------------------------------------
# News items
# ---------------------------------------------------------------------------


def test_select_news_blocks_picks_pc_bucket_for_cn() -> None:
    payload = _load("nte-news-data-cn.json")
    blocks = _select_news_blocks(payload, region="cn", language="zh-cn")
    keys = [k for k, _ in blocks]
    assert keys == ["news", "notice", "event", "media"]


def test_select_news_blocks_picks_short_lang_bucket_for_os() -> None:
    payload = _load("nte-news-data-intl.json")
    blocks = _select_news_blocks(payload, region="os", language="en-us")
    keys = [k for k, _ in blocks]
    assert keys == ["news", "notice", "event", "media"]


def test_select_news_blocks_falls_back_to_en_when_lang_missing() -> None:
    payload = _load("nte-news-data-intl.json")
    # ``en`` is the fixture language; request something it does not contain.
    blocks = _select_news_blocks(payload, region="os", language="zz-zz")
    assert len(blocks) == 4
    assert any(isinstance(items, list) for _, items in blocks)


def test_absolutize_url_keeps_absolute_https() -> None:
    assert _absolutize_url("https://example.com/x", region="os", short_lang="en") == "https://example.com/x"


def test_absolutize_url_prefixes_cn_host_for_relative_path() -> None:
    assert _absolutize_url("/news/gamebroad/20260909/1.html", region="cn", short_lang="cn") == \
        "https://yh.wanmei.com/news/gamebroad/20260909/1.html"


def test_absolutize_url_prefixes_os_host_with_lang_segment() -> None:
    assert _absolutize_url("/en/article/news/gamenews/1.html", region="os", short_lang="en") == \
        "https://nte.perfectworld.com/en/article/news/gamenews/1.html"


def test_absolutize_url_returns_none_for_empty() -> None:
    assert _absolutize_url(None, region="os", short_lang="en") is None
    assert _absolutize_url("", region="os", short_lang="en") is None


def test_resolve_category_uses_channel_cn_name_for_cn() -> None:
    assert _resolve_category({"channelCnName": "公告"}, bucket_key="news", region="cn") == "公告"
    assert _resolve_category({"channelCnName": "活动"}, bucket_key="event", region="cn") == "活动"


def test_resolve_category_maps_english_descriptions_for_os() -> None:
    assert _resolve_category({"channelDescription": "News"}, bucket_key="news", region="os") == "资讯"
    assert _resolve_category({"channelDescription": "Notices"}, bucket_key="notice", region="os") == "公告"
    assert _resolve_category({"channelDescription": "Events"}, bucket_key="event", region="os") == "活动"


def test_resolve_category_falls_back_to_bucket_name() -> None:
    assert _resolve_category({}, bucket_key="event", region="os") == "活动"
    assert _resolve_category({}, bucket_key="media", region="os") == "资讯"


def test_build_news_items_skips_blank_titles() -> None:
    payload = {"en": {"news": [
        {"title": "  ", "url": "/en/article/news/gamenews/1.html"},
        {"title": "Real title", "url": "/en/article/news/gamenews/2.html"},
    ]}}
    items = _build_news_items(payload, region="os", language="en-us")
    assert len(items) == 1
    assert items[0].title == "Real title"


def test_build_news_items_drops_non_allowlisted_url() -> None:
    payload = {"en": {"news": [
        {"title": "Evil", "url": "https://example.invalid/track"},
    ]}}
    items = _build_news_items(payload, region="os", language="en-us")
    assert len(items) == 1
    assert items[0].target_url is None


def test_build_news_items_assigns_unique_ids() -> None:
    payload = {"en": {"news": [
        {"title": "First", "url": "/en/article/news/gamenews/1.html"},
        {"title": "Second", "url": "/en/article/news/gamenews/2.html"},
    ]}}
    items = _build_news_items(payload, region="os", language="en-us")
    ids = [item.id for item in items]
    assert len(ids) == len(set(ids))


def test_parse_date_accepts_iso_yyyy_mm_dd() -> None:
    from datetime import datetime, timezone
    result = _parse_date("2026-09-14")
    assert result == datetime(2026, 9, 14, tzinfo=timezone.utc)


def test_parse_date_returns_none_for_invalid_value() -> None:
    assert _parse_date(None) is None
    assert _parse_date("") is None
    assert _parse_date("not-a-date") is None


# ---------------------------------------------------------------------------
# End-to-end fetch
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_fetch_os_returns_full_content() -> None:
    swiper = _js_payload("nte-game-swiper-intl.json")
    news = _js_payload("nte-news-data-intl.json")

    provider, client = _build_provider(swiper, news)
    try:
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url == DEFAULT_BG_VIDEO_OS
    assert len(result.banners) == 3  # trimmed fixture
    assert all(b.image_url.startswith("https://") for b in result.banners)
    assert len(result.news) == 12  # 3 items × 4 buckets
    categories = {item.category for item in result.news}
    # Categories are channelDescription-driven; English tabs map to 公告/资讯/活动.
    assert categories == {"公告", "资讯", "活动"}


@pytest.mark.asyncio
async def test_fetch_cn_uses_yh_endpoints() -> None:
    swiper = _js_payload("nte-game-swiper-cn.json")
    news = _js_payload("nte-news-data-cn.json")

    seen: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(str(request.url))
        url = str(request.url)
        if "yh-gameSwiper" in url:
            return httpx.Response(200, text=swiper)
        if "newsData" in url:
            return httpx.Response(200, text=news)
        return httpx.Response(404)

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = PerfectWorldHybridProvider(client=client)
    try:
        result = await provider.fetch(_request({"region": "cn", "language": "zh-cn"}))
    finally:
        await client.aclose()

    assert any("static.games.wanmei.com" in u for u in seen)
    assert any("yh.wanmei.com" in u for u in seen)
    assert result.background is not None
    assert result.background.video_url == DEFAULT_BG_VIDEO_CN
    assert len(result.banners) == 2
    assert len(result.news) == 12
    # CN news URLs should be absolutized against yh.wanmei.com.
    for item in result.news:
        if item.target_url:
            assert item.target_url.startswith("https://yh.wanmei.com/")


@pytest.mark.asyncio
async def test_fetch_overrides_endpoints_via_provider_options() -> None:
    seen: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(str(request.url))
        return httpx.Response(200, text=_js_payload("nte-game-swiper-intl.json")
                              if "swiper" in str(request.url) else _js_payload("nte-news-data-intl.json"))

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = PerfectWorldHybridProvider(client=client)
    try:
        await provider.fetch(_request({
            "bannerSwiperUrl": "https://custom.example.test/swiper.js",
            "newsDataUrl": "https://custom.example.test/news.js",
        }))
    finally:
        await client.aclose()

    assert any("custom.example.test/swiper.js" in u for u in seen)
    assert any("custom.example.test/news.js" in u for u in seen)


@pytest.mark.asyncio
async def test_fetch_returns_empty_when_both_endpoints_fail() -> None:
    """HTTP errors must not raise; the Provider returns empty content."""

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="upstream unavailable")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = PerfectWorldHybridProvider(client=client)
    try:
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url == DEFAULT_BG_VIDEO_OS
    assert result.banners == []
    assert result.news == []


@pytest.mark.asyncio
async def test_fetch_handles_malformed_payload_gracefully() -> None:
    """A garbage payload should not crash — empty lists, no raise."""

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, text="not json at all")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = PerfectWorldHybridProvider(client=client)
    try:
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.banners == []
    assert result.news == []


@pytest.mark.asyncio
async def test_fetch_local_background_path_wins_over_network_url() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="nope")  # Network must not be consulted for bg

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = PerfectWorldHybridProvider(client=client)
    try:
        result = await provider.fetch(_request({
            "backgroundVideoPath": "C:/Games/NTE/Client/bg.mp4",
        }))
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url == "C:/Games/NTE/Client/bg.mp4"