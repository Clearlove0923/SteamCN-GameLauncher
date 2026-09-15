"""NextJsDataProvider unit tests driven by sanitized JSON fixtures.

The fixtures live in ``contracts/samples/infinity-nikki-*.json``. They
are sanitized captures of:

  * the ``<html lang="zh-TW">`` / ``<html>`` homepage on OS and CN
    (Next.js ``__NEXT_DATA__`` envelope extracted from the markup);
  * the 6 ``/api/news?section={0,1,2}`` list responses;
  * 2 ``/api/news/<id>`` detail responses.

All CDN hosts in the fixtures are real, list sizes are trimmed to two
rows. Tests use ``httpx.MockTransport`` so no network access happens.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any, Callable
from urllib.parse import parse_qs, urlparse

import httpx
import pytest

from home_content.models import HomeContentRequest
from home_content.providers.nextjs_data import (
    ALLOWED_HOST_SUFFIXES,
    DEFAULT_BASE_CN,
    DEFAULT_BASE_OS,
    DEFAULT_LOCALE_CN,
    DEFAULT_LOCALE_OS,
    DEFAULT_NEWS_LIMIT,
    DEFAULT_REGION,
    NextJsDataProvider,
    SECTION_CATEGORY,
    _allowed,
    _build_activity_banners,
    _build_background,
    _build_news_items,
    _build_top_banners,
    _parse_iso,
    _parse_naive,
    _resolve_pv_video,
    parse_home_html,
)

SAMPLE_ROOT = Path(__file__).resolve().parents[2] / "contracts" / "samples"


def _load(name: str) -> dict[str, Any]:
    return json.loads((SAMPLE_ROOT / name).read_text(encoding="utf-8"))


def _request(
    options: dict[str, Any] | None = None,
    *,
    game_id: str = "infinity-nikki",
    locale: str = "zh-CN",
) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="sn-1",
        game_id=game_id,
        provider_id="nextjs-data",
        locale=locale,
        provider_options=options or {},
    )


# ---------------------------------------------------------------------------
# Defaults / static constants
# ---------------------------------------------------------------------------


def test_provider_id_constant() -> None:
    provider = NextJsDataProvider()
    assert provider.provider_id == "nextjs-data"


def test_default_region_and_endpoints() -> None:
    assert DEFAULT_REGION == "os"
    assert DEFAULT_BASE_OS == "https://infinitynikki.infoldgames.com"
    assert DEFAULT_BASE_CN == "https://infinitynikki.nuanpaper.com"
    assert DEFAULT_LOCALE_OS == "zh-TW"
    assert DEFAULT_LOCALE_CN == "zh-CN"
    assert DEFAULT_NEWS_LIMIT == 4


def test_allowed_host_suffixes() -> None:
    assert _allowed("https://assets.infoldgames.com/x.mp4") is True
    assert _allowed("https://webstatic.infoldgames.com/x.jpg") is True
    assert _allowed("https://infinitynikki.infoldgames.com/news/1") is True
    assert _allowed("https://assets.papegames.com/x.png") is True
    assert _allowed("https://webstatic.papegames.com/x.jpg") is True
    assert _allowed("https://assets.nuanpaper.com/x.png") is True
    assert _allowed("https://infinitynikki.nuanpaper.com/news/1031") is True
    # No CDN / unknown host.
    assert _allowed("https://example.invalid/x.png") is False
    assert _allowed("https://evil.com/infoldgames.com/x.png") is False
    # Bad scheme or empty input.
    assert _allowed(None) is False
    assert _allowed("not-a-url") is False
    assert _allowed("ftp://assets.infoldgames.com/x.mp4") is False


def test_section_category_map() -> None:
    assert SECTION_CATEGORY["0"] == "资讯"
    assert SECTION_CATEGORY["1"] == "公告"
    assert SECTION_CATEGORY["2"] == "活动"


# ---------------------------------------------------------------------------
# Date helpers
# ---------------------------------------------------------------------------


def test_parse_iso_accepts_z_suffix() -> None:
    parsed = _parse_iso("2026-09-02T10:00:00.000Z")
    assert parsed is not None
    assert parsed.year == 2026 and parsed.month == 9 and parsed.day == 2
    assert parsed.utcoffset() is not None
    assert parsed.utcoffset().total_seconds() == 0


def test_parse_iso_accepts_offset() -> None:
    parsed = _parse_iso("2026-09-02T18:00:00+08:00")
    assert parsed is not None
    # Normalised to UTC.
    assert parsed.hour == 10


def test_parse_iso_returns_none_for_garbage() -> None:
    assert _parse_iso(None) is None
    assert _parse_iso("") is None
    assert _parse_iso("not a date") is None


def test_parse_naive_act_banner_format() -> None:
    parsed = _parse_naive("2026-07-12 10:00:00")
    assert parsed is not None
    assert parsed.tzinfo is not None
    assert parsed.utcoffset().total_seconds() == 0
    assert parsed.year == 2026


def test_parse_naive_accepts_iso_too() -> None:
    parsed = _parse_naive("2026-07-12T10:00:00")
    assert parsed is not None


def test_parse_naive_returns_none_for_garbage() -> None:
    assert _parse_naive("") is None
    assert _parse_naive("tomorrow") is None
    assert _parse_naive(None) is None


# ---------------------------------------------------------------------------
# parse_home_html
# ---------------------------------------------------------------------------


def test_parse_home_html_os_fixture() -> None:
    fixture = _load("infinity-nikki-home-os.json")
    video_url = fixture["backgroundVideoUrl"]
    video_poster = fixture["backgroundPosterUrl"]
    next_data = fixture["nextData"]
    raw_html = _render_minimal_html(next_data, video_url, video_poster)
    parsed_video, parsed_poster, page_data = parse_home_html(raw_html)
    assert parsed_video == video_url
    assert parsed_poster == video_poster
    assert "page" in page_data
    assert isinstance(page_data.get("newsbanner"), list)
    assert isinstance(page_data.get("page", {}).get("actBannerlist"), list)


def test_parse_home_html_missing_blob_raises() -> None:
    with pytest.raises(ValueError, match="__NEXT_DATA__"):
        parse_home_html("<html><body>nothing</body></html>")


def test_parse_home_html_invalid_json_raises() -> None:
    bad = '<html><body><script id="__NEXT_DATA__" type="application/json">{not json}</script></body></html>'
    with pytest.raises(ValueError, match="JSON"):
        parse_home_html(bad)


def test_resolve_pv_video_picks_first_label_match() -> None:
    page = {
        "pv_list": [
            {"label": "pc pv链接", "value": "https://example.com/pv.mp4"},
            {"label": "pc 首屏背景视频", "value": "https://assets.papegames.com/bg.mp4"},
        ]
    }
    assert _resolve_pv_video(page) == "https://assets.papegames.com/bg.mp4"


def test_resolve_pv_video_returns_none_when_missing() -> None:
    assert _resolve_pv_video({}) is None
    assert _resolve_pv_video({"pv_list": []}) is None
    assert _resolve_pv_video({"pv_list": [{"label": "other"}]}) is None


# ---------------------------------------------------------------------------
# _build_background
# ---------------------------------------------------------------------------


def test_build_background_uses_video_url() -> None:
    bg = _build_background("https://assets.infoldgames.com/x.mp4", None)
    assert bg is not None
    assert bg.video_url == "https://assets.infoldgames.com/x.mp4"
    assert bg.image_url is None
    assert bg.local_path is None


def test_build_background_uses_poster_when_video_missing() -> None:
    bg = _build_background(None, "https://assets.infoldgames.com/poster.jpg")
    assert bg is not None
    # Without a video, we still surface the poster for the fallback.
    assert bg.image_url == "https://assets.infoldgames.com/poster.jpg"


# ---------------------------------------------------------------------------
# _build_top_banners / _build_activity_banners
# ---------------------------------------------------------------------------


def test_build_top_banners_from_os_fixture() -> None:
    payload = _load("infinity-nikki-home-os.json")["nextData"]["props"]["pageProps"]["pageData"]
    banners = _build_top_banners(payload.get("newsbanner") or [])
    assert len(banners) == 1
    banner = banners[0]
    assert banner.id == "in-newsbanner-1"
    assert banner.title == "事前預約開啟"
    assert banner.image_url == "https://assets.infoldgames.com/nikkiweb/papegame/infinitynikkitw/material/18mcb16r/2652d6bfd0e4d89b.jpeg"
    # newsbanner has no click-through URL exposed upstream.
    assert banner.target_url is None
    assert banner.starts_at is not None
    assert banner.starts_at.year == 2024


def test_build_top_banners_rejects_disallowed_host() -> None:
    banners = _build_top_banners([
        {"id": 9, "title": "T", "address": "https://example.invalid/x.jpg"},
    ])
    assert banners == []


def test_build_activity_banners_decodes_json_value() -> None:
    payload = _load("infinity-nikki-home-os.json")["nextData"]["props"]["pageProps"]["pageData"]
    banners = _build_activity_banners(payload["page"].get("actBannerlist") or [])
    assert len(banners) == 1
    banner = banners[0]
    assert banner.title == "春节"
    assert banner.image_url == "https://assets.papegames.com/resources/cdn/20260709/0dafcf4561c505c7.png"
    assert banner.target_url == "https://infinitynikki.infoldgames.com/proj/tw/cozyshroomlingshome_cTxLps.html?source_entry=Official_Website"
    assert banner.starts_at is not None
    assert banner.starts_at.year == 2026
    assert banner.starts_at.month == 7
    assert banner.starts_at.day == 12


def test_build_activity_banners_skips_invalid_json() -> None:
    banners = _build_activity_banners([
        {"label": "broken", "value": "this is not json"},
        {"label": "no value", "value": ""},
        {"label": "non-dict"},
    ])
    assert banners == []


def test_build_activity_banners_strips_disallowed_target() -> None:
    banners = _build_activity_banners([
        {
            "label": "x",
            "value": json.dumps(
                {"bannerimg": "https://assets.infoldgames.com/x.png", "link": "https://example.invalid/track"}
            ),
        }
    ])
    assert len(banners) == 1
    assert banners[0].image_url == "https://assets.infoldgames.com/x.png"
    assert banners[0].target_url is None


# ---------------------------------------------------------------------------
# _build_news_items
# ---------------------------------------------------------------------------


def test_build_news_items_os_section0() -> None:
    payload = _load("infinity-nikki-news-list-os-s0.json")
    items = _build_news_items(payload, region="os", base=DEFAULT_BASE_OS, section_key="0")
    assert len(items) == 2
    assert all(item.category == "资讯" for item in items)
    assert items[0].title.startswith("【幕後專欄】")
    assert items[0].image_url is not None
    assert items[0].image_url.endswith(".jpg") or items[0].image_url.endswith(".png")
    assert items[0].target_url == f"{DEFAULT_BASE_OS}/news/132"
    assert items[0].published_at is not None
    assert items[0].published_at.year == 2026
    assert items[0].summary == "2.9版本五星套裝設計思路展示"


def test_build_news_items_cn_section1() -> None:
    payload = _load("infinity-nikki-news-list-cn-s1.json")
    items = _build_news_items(payload, region="cn", base=DEFAULT_BASE_CN, section_key="1")
    assert len(items) == 2
    assert all(item.category == "公告" for item in items)
    assert items[0].target_url == f"{DEFAULT_BASE_CN}/news/1030"
    assert items[0].image_url and items[0].image_url.endswith(".jpg")


def test_build_news_items_section2_uses_event_category() -> None:
    payload = _load("infinity-nikki-news-list-os-s2.json")
    items = _build_news_items(payload, region="os", base=DEFAULT_BASE_OS, section_key="2")
    assert all(item.category == "活动" for item in items)


def test_build_news_items_drops_blank_title() -> None:
    payload = {
        "data": {
            "total": 1,
            "data": [
                {"id": 1, "title": "   ", "section": 0, "publish_time": None, "cover": "https://assets.infoldgames.com/x.jpg", "abstract": ""},
                {"id": 2, "title": "OK", "section": 0, "publish_time": "2026-09-02T10:00:00.000Z", "cover": "https://assets.infoldgames.com/x.jpg", "abstract": "lead"},
            ],
        }
    }
    items = _build_news_items(payload, region="os", base=DEFAULT_BASE_OS, section_key="0")
    assert len(items) == 1
    assert items[0].id == "in-news-os-0-2"


def test_build_news_items_strips_disallowed_cover() -> None:
    payload = {
        "data": {
            "total": 1,
            "data": [
                {"id": 99, "title": "T", "section": 0, "publish_time": None, "cover": "https://example.invalid/x.jpg", "abstract": "a"},
            ],
        }
    }
    items = _build_news_items(payload, region="os", base=DEFAULT_BASE_OS, section_key="0")
    assert len(items) == 1
    assert items[0].image_url is None


def test_build_news_items_handles_missing_data_block() -> None:
    assert _build_news_items({}, region="os", base=DEFAULT_BASE_OS, section_key="0") == []
    assert _build_news_items({"data": None}, region="os", base=DEFAULT_BASE_OS, section_key="0") == []
    assert _build_news_items(
        {"data": {"data": "not a list"}}, region="os", base=DEFAULT_BASE_OS, section_key="0"
    ) == []


# ---------------------------------------------------------------------------
# End-to-end fetch (httpx MockTransport)
# ---------------------------------------------------------------------------


def _render_minimal_html(next_data: dict[str, Any], video_url: str | None, poster_url: str | None) -> str:
    """Wrap a fixture ``nextData`` back into a small HTML document so
    :func:`parse_home_html` can run on it."""
    video_tag = ""
    if video_url:
        video_tag = f'<video src="{video_url}"'
        if poster_url:
            video_tag += f' poster="{poster_url}"'
        video_tag += "></video>"
    return (
        "<!DOCTYPE html><html><head><title>stub</title></head><body>"
        f"{video_tag}"
        f'<script id="__NEXT_DATA__" type="application/json">{json.dumps(next_data)}</script>'
        "</body></html>"
    )


def _make_provider(
    handler: Callable[[httpx.Request], httpx.Response],
) -> tuple[NextJsDataProvider, httpx.AsyncClient]:
    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    return NextJsDataProvider(client=client), client


@pytest.mark.asyncio
async def test_fetch_os_pulls_home_and_three_sections() -> None:
    os_home = _load("infinity-nikki-home-os.json")
    fixtures = {
        "home": os_home,
        "0": _load("infinity-nikki-news-list-os-s0.json"),
        "1": _load("infinity-nikki-news-list-os-s1.json"),
        "2": _load("infinity-nikki-news-list-os-s2.json"),
    }
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        if request.url.path.endswith("/zh-TW/home"):
            html = _render_minimal_html(
                fixtures["home"]["nextData"],
                fixtures["home"]["backgroundVideoUrl"],
                fixtures["home"]["backgroundPosterUrl"],
            )
            return httpx.Response(200, text=html, headers={"content-type": "text/html"})
        if request.url.path.endswith("/api/news"):
            section = parse_qs(request.url.query.decode("ascii")).get("section", [""])[0]
            payload = fixtures.get(section)
            if payload is None:
                return httpx.Response(404)
            return httpx.Response(200, json=payload, headers={"content-type": "application/json"})
        return httpx.Response(404)

    provider, client = _make_provider(handler)
    try:
        result = await provider.fetch(_request({"region": "os", "locale": "zh-TW"}))
    finally:
        await client.aclose()

    # 1 home + 3 news endpoints
    assert sum(r.url.path.endswith("/zh-TW/home") for r in seen) == 1
    assert sum(r.url.path.endswith("/api/news") for r in seen) == 3

    # Background video comes from the <video src> in markup.
    assert result.background is not None
    assert result.background.video_url and result.background.video_url.endswith(".mp4")
    assert result.background.video_url.startswith("https://assets.infoldgames.com/")

    # newsbanner → 1 banner + actBannerlist → 1 banner.
    assert len(result.banners) == 2
    banner_ids = sorted(b.id for b in result.banners)
    assert any(b.startswith("in-newsbanner-") for b in banner_ids)
    assert any(b.startswith("in-actbanner-") for b in banner_ids)

    # News: 2 per section × 3 sections = 6 rows.
    assert len(result.news) == 6
    categories = sorted({item.category for item in result.news})
    assert categories == ["公告", "活动", "资讯"]


@pytest.mark.asyncio
async def test_fetch_cn_uses_pv_list_when_video_tag_missing() -> None:
    cn_home = _load("infinity-nikki-home-cn.json")
    cn_s1 = _load("infinity-nikki-news-list-cn-s1.json")
    cn_s0 = _load("infinity-nikki-news-list-cn-s0.json")
    cn_s2 = _load("infinity-nikki-news-list-cn-s2.json")

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path.endswith("/home"):
            # CN markup has only <video poster=...> with no src; reproduce that.
            html = (
                "<!DOCTYPE html><html><head></head><body>"
                f'<video poster="{cn_home["backgroundPosterUrl"]}"></video>'
                f'<script id="__NEXT_DATA__" type="application/json">'
                f"{json.dumps(cn_home['nextData'])}</script>"
                "</body></html>"
            )
            return httpx.Response(200, text=html)
        if request.url.path.endswith("/api/news"):
            qs = parse_qs(request.url.query.decode("ascii"))
            section = qs.get("section", [""])[0]
            payload = {"0": cn_s0, "1": cn_s1, "2": cn_s2}.get(section)
            if payload is None:
                return httpx.Response(404)
            return httpx.Response(200, json=payload)
        return httpx.Response(404)

    provider, client = _make_provider(handler)
    try:
        result = await provider.fetch(_request({"region": "cn"}))
    finally:
        await client.aclose()

    assert result.background is not None
    # Video URL is recovered from pv_list, not the markup.
    assert result.background.video_url == "https://assets.papegames.com/nikkiweb/papegame/infinitynikkicn/material/0_8mzq2g/1122_A_x264.mp4"
    # CN has 2 newsbanner rows in the fixture.
    newsbanner_banners = [b for b in result.banners if b.id.startswith("in-newsbanner-")]
    assert len(newsbanner_banners) == 2
    assert newsbanner_banners[0].title.startswith("【心意限定")


@pytest.mark.asyncio
async def test_fetch_degrades_to_empty_when_home_unavailable() -> None:
    """If the homepage HTML fails we still try the news API and surface what we can."""

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path.endswith("/home"):
            return httpx.Response(500)
        if request.url.path.endswith("/api/news"):
            return httpx.Response(200, json=_load("infinity-nikki-news-list-os-s1.json"))
        return httpx.Response(404)

    provider, client = _make_provider(handler)
    try:
        result = await provider.fetch(_request({"region": "os", "locale": "zh-TW"}))
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.video_url is None
    assert result.banners == []
    # Same fixture is returned for all three sections ⇒ 3 × 2 = 6 rows,
    # but the *category* is whatever the SECTION_CATEGORY map says per
    # section — i.e. 资讯, 公告, 活动 each appear once per upstream row.
    assert len(result.news) == 6
    categories = sorted({item.category for item in result.news})
    assert categories == ["公告", "活动", "资讯"]


@pytest.mark.asyncio
async def test_fetch_returns_empty_when_all_endpoints_fail() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(500)

    provider, client = _make_provider(handler)
    try:
        result = await provider.fetch(_request({"region": "os", "locale": "zh-TW"}))
    finally:
        await client.aclose()

    assert result.banners == []
    assert result.news == []
    assert result.background is not None
    assert result.background.video_url is None


@pytest.mark.asyncio
async def test_fetch_respects_provider_options_home_page_url() -> None:
    captured: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        captured.append(str(request.url))
        if request.url.host == "custom.example.com":
            return httpx.Response(200, text="<html><body>noop</body></html>")
        if request.url.path.endswith("/api/news"):
            return httpx.Response(200, json=_load("infinity-nikki-news-list-os-s0.json"))
        return httpx.Response(404)

    provider, client = _make_provider(handler)
    try:
        await provider.fetch(_request({
            "region": "os",
            "locale": "zh-TW",
            "homePageUrl": "https://custom.example.com/landing",
        }))
    finally:
        await client.aclose()

    # homePageUrl is used verbatim — no locale / pagePath prefixing.
    assert any(u == "https://custom.example.com/landing" for u in captured)
    # News endpoints still hit the original base.
    assert any(DEFAULT_BASE_OS + "/api/news" in u for u in captured)


@pytest.mark.asyncio
async def test_fetch_news_payload_must_be_object() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path.endswith("/home"):
            html = _render_minimal_html(
                _load("infinity-nikki-home-os.json")["nextData"],
                _load("infinity-nikki-home-os.json")["backgroundVideoUrl"],
                _load("infinity-nikki-home-os.json")["backgroundPosterUrl"],
            )
            return httpx.Response(200, text=html)
        if request.url.path.endswith("/api/news"):
            return httpx.Response(200, text="[1,2,3]")
        return httpx.Response(404)

    provider, client = _make_provider(handler)
    try:
        result = await provider.fetch(_request({"region": "os", "locale": "zh-TW"}))
    finally:
        await client.aclose()

    # Bad JSON / unexpected shape ⇒ no news items but the homepage still surfaces.
    assert result.news == []
    assert any(b.id.startswith("in-newsbanner-") for b in result.banners)


def test_home_fixture_round_trip_with_minimal_html() -> None:
    """End-to-end: build a small HTML wrapper around the OS fixture, parse it,
    and verify the recovered payload matches the original fixture."""
    fixture = _load("infinity-nikki-home-os.json")
    html = _render_minimal_html(
        fixture["nextData"],
        fixture["backgroundVideoUrl"],
        fixture["backgroundPosterUrl"],
    )
    video_url, poster_url, page_data = parse_home_html(html)
    assert video_url == fixture["backgroundVideoUrl"]
    assert poster_url == fixture["backgroundPosterUrl"]
    assert (
        page_data["newsbanner"]
        == fixture["nextData"]["props"]["pageProps"]["pageData"]["newsbanner"]
    )


# Sanity: confirm we do not regress on the helper detection of the
# ``<script id="__NEXT_DATA__" type="application/json">`` blob.
def test_parse_home_html_handles_script_with_attributes() -> None:
    payload = {"props": {"pageProps": {"pageData": {"page": {}, "newsbanner": [], "banner": []}}}}
    html = (
        '<html><body>'
        f'<script id="__NEXT_DATA__" type="application/json">{json.dumps(payload)}</script>'
        '</body></html>'
    )
    _, _, page_data = parse_home_html(html)
    assert page_data["newsbanner"] == []


# Guard against the regex accidentally matching a script tag with a
# different id (e.g. the Sentry loader script).
def test_parse_home_html_does_not_match_other_scripts() -> None:
    payload = {"props": {"pageProps": {"pageData": {"page": {}, "newsbanner": [], "banner": []}}}}
    html = (
        '<html><body>'
        '<script id="hotjar-marker" type="text/plain">{"hello": 1}</script>'
        f'<script id="__NEXT_DATA__" type="application/json">{json.dumps(payload)}</script>'
        '</body></html>'
    )
    _, _, page_data = parse_home_html(html)
    # We read only the __NEXT_DATA__ blob, not the hotjar marker.
    assert "newsbanner" in page_data
    assert page_data["newsbanner"] == []
    assert page_data["banner"] == []


# The provider must not surface the host allow-list suffix the C# side does
# not yet know about (so we surface them in tests if the list ever drifts).
def test_allowed_host_suffixes_includes_all_documented_cdns() -> None:
    for suffix in (
        ".infoldgames.com",
        ".papegames.com",
        ".nuanpaper.com",
        ".webstatic.infoldgames.com",
        ".webstatic.papegames.com",
        ".assets.infoldgames.com",
        ".assets.papegames.com",
        ".assets.nuanpaper.com",
    ):
        assert suffix in ALLOWED_HOST_SUFFIXES, suffix


def test_allowed_host_suffixes_excludes_unknown_hosts() -> None:
    # An attacker-controlled host pretending to be a CDN must not pass.
    assert _allowed("https://evil.com/assets.infoldgames.com/x.png") is False
    # A subdomain of an allowed suffix is fine.
    assert _allowed("https://eng.papegames.com/page") is True
