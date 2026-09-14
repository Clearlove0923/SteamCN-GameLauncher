"""HypergryphBatchProvider unit tests driven by sanitized JSON fixtures.

The fixture lives at contracts/samples/hypergryph-batch-envelope.json. It
captures a real ``proxy_rsps[]`` response sampled on 2026-09-14 against
the Global Endfield launcher (appCode ``YDUTE5gscDZ229CW``, channel 6,
language ``en-us``). md5 fields are shortened to 8 chars; the URL hosts
are kept (they are public CDN / news domains) but list lengths are
trimmed to keep the file small.

Tests use httpx MockTransport to return canned envelopes so the Provider
can be exercised without touching the network.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import httpx
import pytest

from home_content.providers.hypergryph_batch import (
    ALLOWED_HOST_SUFFIXES,
    DEFAULT_APP_CODE,
    DEFAULT_CHANNEL,
    DEFAULT_LANGUAGE,
    DEFAULT_REGION,
    HypergryphBatchProvider,
    _allowed,
    _build_background,
    _build_banners,
    _build_news_items,
    _extract_responses,
)
from home_content.models import HomeContentRequest

SAMPLE_ROOT = Path(__file__).resolve().parents[2] / "contracts" / "samples"


def _load(name: str) -> dict[str, Any]:
    return json.loads((SAMPLE_ROOT / name).read_text(encoding="utf-8"))


def _request(options: dict[str, Any] | None = None) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="hg-1",
        game_id="endfield",
        provider_id="hypergryph-batch",
        locale="en",
        provider_options=options or {},
    )


def _build_provider(envelope: dict[str, Any]) -> tuple[HypergryphBatchProvider, httpx.AsyncClient]:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=envelope)

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    return HypergryphBatchProvider(client=client), client


def test_loads_fixture() -> None:
    envelope = _load("hypergryph-batch-envelope.json")
    proxy_rsps = envelope.get("proxy_rsps")
    assert isinstance(proxy_rsps, list) and len(proxy_rsps) >= 3


def test_allowed_host_suffixes_only_known_cdn() -> None:
    assert _allowed("https://gl-utils-public.hg-cdn.com/foo.png") is True
    assert _allowed("https://hg-utils-public.hycdn.cn/foo.mp4") is True
    assert _allowed("https://endfield.gryphline.com/news/1") is True
    assert _allowed("https://endfield.hypergryph.com/news/1") is True
    assert _allowed("https://www.skport.com/article?id=1") is True
    assert _allowed("https://example.invalid/evil.mp4") is False
    assert _allowed(None) is False
    assert _allowed("not-a-url") is False
    assert _allowed("ftp://gl-utils-public.hg-cdn.com/foo") is False


def test_default_constants() -> None:
    assert DEFAULT_APP_CODE == "YDUTE5gscDZ229CW"
    assert DEFAULT_CHANNEL == "6"
    assert DEFAULT_LANGUAGE == "en-us"
    assert DEFAULT_REGION == "os"
    assert ".hg-cdn.com" in ALLOWED_HOST_SUFFIXES


def test_extract_responses_collects_all_kinds() -> None:
    envelope = _load("hypergryph-batch-envelope.json")
    extracted = _extract_responses(envelope)
    assert "get_main_bg_image_rsp" in extracted
    assert "get_banner_rsp" in extracted
    assert "get_announcement_rsp" in extracted


def test_extract_responses_handles_single_element_shape() -> None:
    # The archiver code does single-kind POSTs that return
    # {proxy_rsps: [{kind, <kind>_rsp}]}. _extract_responses must
    # still pick the inner dict up.
    inner = {"kind": "get_banner", "get_banner_rsp": {"banners": []}}
    extracted = _extract_responses({"proxy_rsps": [inner]})
    assert "get_banner_rsp" in extracted
    assert extracted["get_banner_rsp"] == {"banners": []}


def test_build_background_prefers_video() -> None:
    rsp = {
        "main_bg_image": {
            "url": "https://gl-utils-public.hg-cdn.com/poster.png",
            "md5": "x",
            "video_url": "https://gl-utils-public.hg-cdn.com/bg.mp4",
        }
    }
    bg = _build_background(rsp)
    assert bg is not None
    assert bg.video_url and bg.video_url.endswith(".mp4")
    assert bg.image_url and bg.image_url.endswith(".png")


def test_build_background_falls_back_to_image_when_video_missing() -> None:
    rsp = {
        "main_bg_image": {
            "url": "https://gl-utils-public.hg-cdn.com/poster.png",
            "video_url": "",
        }
    }
    bg = _build_background(rsp)
    assert bg is not None
    assert bg.video_url is None
    assert bg.image_url and bg.image_url.endswith(".png")


def test_build_background_returns_none_when_url_unknown_host() -> None:
    rsp = {
        "main_bg_image": {
            "url": "https://example.invalid/poster.png",
            "video_url": "https://example.invalid/bg.mp4",
        }
    }
    assert _build_background(rsp) is None


def test_build_background_returns_none_when_rsp_missing() -> None:
    assert _build_background(None) is None
    assert _build_background({}) is None
    assert _build_background({"main_bg_image": "not-a-dict"}) is None


def test_build_banners_filters_unknown_hosts() -> None:
    rsp = {
        "banners": [
            {
                "url": "https://gl-utils-public.hg-cdn.com/a.jpg",
                "jump_url": "https://endfield.gryphline.com/news/1",
                "id": "1",
            },
            {
                "url": "https://example.invalid/evil.jpg",
                "jump_url": "https://endfield.gryphline.com/news/2",
                "id": "2",
            },
        ]
    }
    banners = _build_banners(rsp)
    assert len(banners) == 1
    assert banners[0].id == "hg-banner-1"


def test_build_banners_drops_jump_url_when_host_unknown() -> None:
    rsp = {
        "banners": [
            {
                "url": "https://gl-utils-public.hg-cdn.com/a.jpg",
                "jump_url": "https://example.invalid/track",
                "id": "1",
            },
        ]
    }
    banners = _build_banners(rsp)
    assert len(banners) == 1
    assert banners[0].target_url is None


def test_build_news_items_maps_english_tabs_to_chinese_categories() -> None:
    rsp = _load("hypergryph-batch-envelope.json")["proxy_rsps"][2]["get_announcement_rsp"]
    items = _build_news_items(rsp)
    categories = {item.category for item in items}
    assert "公告" in categories
    assert "活动" in categories
    assert "资讯" in categories


def test_build_news_items_keeps_unknown_tab_raw() -> None:
    rsp = {
        "tabs": [
            {
                "tabName": "Misc",
                "announcements": [
                    {"content": "Hello", "jump_url": "https://endfield.gryphline.com/x", "id": "7"},
                ],
            }
        ]
    }
    items = _build_news_items(rsp)
    assert len(items) == 1
    assert items[0].category == "Misc"


def test_build_news_items_drops_blank_titles() -> None:
    rsp = {
        "tabs": [
            {
                "tabName": "Notices",
                "announcements": [
                    {"content": "   ", "jump_url": "https://endfield.gryphline.com/x", "id": "1"},
                    {"content": "Real title", "jump_url": "https://endfield.gryphline.com/x", "id": "2"},
                ],
            }
        ]
    }
    items = _build_news_items(rsp)
    assert len(items) == 1
    assert items[0].title == "Real title"


def test_build_news_items_drops_jump_url_when_host_unknown() -> None:
    rsp = {
        "tabs": [
            {
                "tabName": "Notices",
                "announcements": [
                    {
                        "content": "Hello",
                        "jump_url": "https://example.invalid/track",
                        "id": "9",
                    }
                ],
            }
        ]
    }
    items = _build_news_items(rsp)
    assert len(items) == 1
    assert items[0].target_url is None


def test_build_news_items_assigns_stable_unique_ids() -> None:
    rsp = _load("hypergryph-batch-envelope.json")["proxy_rsps"][2]["get_announcement_rsp"]
    items = _build_news_items(rsp)
    ids = [item.id for item in items]
    assert len(ids) == len(set(ids))


def test_build_news_items_parses_start_ts_to_utc_datetime() -> None:
    rsp = {
        "tabs": [
            {
                "tabName": "Notices",
                "announcements": [
                    {
                        "content": "Ts",
                        "jump_url": "https://endfield.gryphline.com/x",
                        "id": "11",
                        "start_ts": "1788235200000",
                    }
                ],
            }
        ]
    }
    items = _build_news_items(rsp)
    assert items[0].published_at is not None
    assert items[0].published_at.year == 2026


@pytest.mark.asyncio
async def test_fetch_sends_one_post_with_three_proxy_reqs() -> None:
    envelope = _load("hypergryph-batch-envelope.json")
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json=envelope)

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = HypergryphBatchProvider(client=client)
    try:
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    # Exactly one POST issued.
    assert len(seen) == 1
    request = seen[0]
    assert request.method == "POST"
    body = json.loads(request.content)
    kinds = [item["kind"] for item in body["proxy_reqs"]]
    assert kinds == ["get_main_bg_image", "get_banner", "get_announcement"]
    # Defaults flowed into every sub-request payload.
    for item in body["proxy_reqs"]:
        req = next(v for v in item.values() if isinstance(v, dict))
        assert req["appcode"] == DEFAULT_APP_CODE
        assert req["channel"] == "6"
        assert req["sub_channel"] == "6"
        assert req["language"] == "en-us"

    # Result is well-formed.
    assert result.background is not None
    assert result.background.video_url and result.background.video_url.endswith(".mp4")
    assert result.background.image_url and result.background.image_url.endswith(".png")
    assert len(result.banners) == 3  # fixture has 3 banners
    assert all(b.image_url.startswith("https://") for b in result.banners)
    assert len(result.news) == 6  # fixture has 2 announcements per tab × 3 tabs
    categories = {item.category for item in result.news}
    assert categories == {"公告", "活动", "资讯"}


@pytest.mark.asyncio
async def test_fetch_uses_provider_options_overrides() -> None:
    envelope = _load("hypergryph-batch-envelope.json")
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json=envelope)

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = HypergryphBatchProvider(client=client)
    try:
        result = await provider.fetch(_request({
            "appCode": "6LL0KJuqHBVz33WK",
            "channel": "1",
            "subChannel": "1",
            "language": "zh-cn",
            "region": "cn",
            "baseUrl": "https://launcher.hypergryph.com/api",
        }))
    finally:
        await client.aclose()

    assert len(seen) == 1
    body = json.loads(seen[0].content)
    assert "launcher.hypergryph.com/api/proxy/web/batch_proxy" in str(seen[0].url)
    for item in body["proxy_reqs"]:
        req = next(v for v in item.values() if isinstance(v, dict))
        assert req["appcode"] == "6LL0KJuqHBVz33WK"
        assert req["channel"] == "1"
        assert req["sub_channel"] == "1"
        assert req["language"] == "zh-cn"
    # Result is still parsed (the envelope is the same shape regardless of region).
    assert result.background is not None


@pytest.mark.asyncio
async def test_fetch_propagates_httpx_errors() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="upstream unavailable")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = HypergryphBatchProvider(client=client)
    try:
        with pytest.raises(httpx.HTTPStatusError):
            await provider.fetch(_request())
    finally:
        await client.aclose()


@pytest.mark.asyncio
async def test_fetch_handles_empty_envelope_gracefully() -> None:
    """No banner / no announcement → background may still load, lists are empty."""
    envelope = {
        "proxy_rsps": [
            {
                "kind": "get_main_bg_image",
                "get_main_bg_image_rsp": {
                    "main_bg_image": {
                        "url": "https://gl-utils-public.hg-cdn.com/only.png",
                        "video_url": "",
                    }
                },
            },
            {"kind": "get_banner", "get_banner_rsp": {"banners": []}},
            {"kind": "get_announcement", "get_announcement_rsp": {"tabs": []}},
        ]
    }

    provider, client = _build_provider(envelope)
    try:
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    assert result.background is not None
    assert result.background.image_url and result.background.image_url.endswith(".png")
    assert result.background.video_url is None
    assert result.banners == []
    assert result.news == []
    assert result.update_info is None


@pytest.mark.asyncio
async def test_fetch_rejects_envelope_without_proxy_rsps() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"unexpected": "shape"})

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = HypergryphBatchProvider(client=client)
    try:
        # Empty lists / None background — fetch must not raise.
        result = await provider.fetch(_request())
    finally:
        await client.aclose()

    assert result.background is None
    assert result.banners == []
    assert result.news == []


@pytest.mark.asyncio
async def test_fetch_rejects_non_json_response() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, text="not json")

    transport = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=transport)
    provider = HypergryphBatchProvider(client=client)
    try:
        with pytest.raises(ValueError, match="JSON"):
            await provider.fetch(_request())
    finally:
        await client.aclose()