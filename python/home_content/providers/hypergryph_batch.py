"""Hypergryph batch_proxy Provider for Arknights: Endfield (and other Hypergryph titles).

Discovery surface
-----------------
Hypergryph's official launcher uses a single batch endpoint:

    POST https://launcher.gryphline.com/api/proxy/web/batch_proxy   (Global)
    POST https://launcher.hypergryph.com/api/proxy/web/batch_proxy   (CN)

The body wraps multiple ``proxy_reqs[]`` items. Each item has a ``kind``
and a kind-specific request payload. We send the three kinds that map to
the home content layout:

  * ``get_main_bg_image`` → video_url (MP4) + url (PNG fallback)
  * ``get_banner``        → banners[].url + jump_url
  * ``get_announcement``  → tabs[].announcements[] with start_ts and jump_url

The endpoint is shipped inside the Hypergryph desktop launcher (and the
mobile web portal) — it is **not** a public API. Sampled 2026-09-14
against the Global Endfield build (appCode ``YDUTE5gscDZ229CW``,
channel/subChannel ``6``, language ``en-us``). Behaviour, host names and
the JSON shape are all subject to change without notice.

Response layout
---------------
The server returns one element per requested kind in ``proxy_rsps[]``
(not a single element containing all ``*_rsp`` keys):

    proxy_rsps[0] = {kind: "get_main_bg_image", get_main_bg_image_rsp: {...}}
    proxy_rsps[1] = {kind: "get_banner",        get_banner_rsp: {...}}
    proxy_rsps[2] = {kind: "get_announcement",  get_announcement_rsp: {...}}

Per-game mapping
----------------
The triple ``appCode``, ``channel``, ``subChannel`` is taken from the
launcher's own bundled config (see daydreamer-json's archive). For
Endfield the defaults are:

    Global: appCode=YDUTE5gscDZ229CW channel=6 subChannel=6 language=en-us
    CN:     appCode=6LL0KJuqHBVz33WK channel=1 subChannel=1 language=zh-cn

Other Hypergryph titles share the same shape but a different triple,
which callers pass via ``providerOptions``.

Tab name mapping (English UI → normalized Chinese category)
----------------------------------------------------------
    Notices → 公告
    Events  → 活动
    News    → 资讯
    other   → kept raw (lands in the generic "其他" group in UI)

Fallback surface
----------------
On HTTP failure the provider raises and the Worker's envelope handler
converts the failure into ``envelope.errors``. No silent fallback.
"""

from __future__ import annotations

import json
import logging
from datetime import datetime, timezone
from typing import Any, Optional

import httpx

from ..models import HomeBackground, HomeBanner, HomeContent, HomeContentRequest, HomeNewsItem
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.hypergryph_batch")


# Base URLs (Global / CN). Decoded from daydreamer-json's archive config.ts.
DEFAULT_BASE_OS = "https://launcher.gryphline.com/api"
DEFAULT_BASE_CN = "https://launcher.hypergryph.com/api"

# Defaults for Endfield Global — sampled 2026-09-14.
DEFAULT_APP_CODE = "YDUTE5gscDZ229CW"
DEFAULT_CHANNEL = "6"
DEFAULT_SUB_CHANNEL = "6"
DEFAULT_LANGUAGE = "en-us"
DEFAULT_REGION = "os"

# Allow-list of hosts we trust for media & jump URLs.
ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".hg-cdn.com",
    ".hycdn.cn",
    ".gryphline.com",
    ".hypergryph.com",
    ".skport.com",
    ".skland.com",
)

# Kinds we want, paired with the request-payload key the API expects.
KINDS: tuple[tuple[str, str], ...] = (
    ("get_main_bg_image", "get_main_bg_image_req"),
    ("get_banner", "get_banner_req"),
    ("get_announcement", "get_announcement_req"),
)

TAB_NAME_MAP: dict[str, str] = {
    "Notices": "公告",
    "Events": "活动",
    "News": "资讯",
}

DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (HypergryphBatchProvider)",
    "Accept": "application/json",
    "Content-Type": "application/json",
}


class HypergryphBatchProvider(HomeContentProvider):
    provider_id = "hypergryph-batch"

    def __init__(
        self,
        client: Optional[httpx.AsyncClient] = None,
        timeout: httpx.Timeout = DEFAULT_TIMEOUT,
    ) -> None:
        self._client = client
        self._timeout = timeout

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        options = request.provider_options or {}
        region = str(options.get("region", DEFAULT_REGION)).lower()
        base_url = str(options.get("baseUrl", DEFAULT_BASE_CN if region == "cn" else DEFAULT_BASE_OS))
        app_code = str(options.get("appCode", DEFAULT_APP_CODE))
        channel = str(options.get("channel", DEFAULT_CHANNEL))
        sub_channel = str(options.get("subChannel", DEFAULT_SUB_CHANNEL))
        language = str(options.get("language", DEFAULT_LANGUAGE))

        url = f"{base_url.rstrip('/')}/proxy/web/batch_proxy"
        body = _build_request_body(app_code, channel, sub_channel, language)

        envelope = await self._post_json(url, body)
        responses = _extract_responses(envelope)

        background = _build_background(responses.get("get_main_bg_image_rsp"))
        banners = _build_banners(responses.get("get_banner_rsp"))
        news_items = _build_news_items(responses.get("get_announcement_rsp"))

        return HomeContent(
            background=background,
            banners=banners,
            news=news_items,
            update_info=None,
        )

    async def _post_json(self, url: str, body: dict[str, Any]) -> dict[str, Any]:
        if self._client is not None:
            response = await self._client.post(url, json=body)
        else:
            async with httpx.AsyncClient(timeout=self._timeout, headers=DEFAULT_HEADERS) as client:
                response = await client.post(url, json=body)
        response.raise_for_status()
        try:
            data = response.json()
        except json.JSONDecodeError as exc:
            raise ValueError(f"Hypergryph 端点返回了非 JSON 内容:{url}:{exc.msg}") from exc
        if not isinstance(data, dict):
            raise ValueError(f"Hypergryph 端点未返回 JSON 对象:{url}")
        return data


def _build_request_body(app_code: str, channel: str, sub_channel: str, language: str) -> dict[str, Any]:
    proxy_reqs: list[dict[str, Any]] = []
    for kind, req_key in KINDS:
        proxy_reqs.append({
            "kind": kind,
            req_key: {
                "appcode": app_code,
                "channel": channel,
                "sub_channel": sub_channel,
                "language": language,
                "platform": "Windows",
                "source": "launcher",
            },
        })
    return {"proxy_reqs": proxy_reqs}


def _extract_responses(envelope: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """Collect every ``*_rsp`` payload from ``proxy_rsps[]`` into one dict.

    The API may return one element per kind, or — when a single-kind call
    is made — a single element containing the matching ``*_rsp`` key.
    This helper handles both shapes uniformly.
    """
    out: dict[str, dict[str, Any]] = {}
    proxy_rsps = envelope.get("proxy_rsps")
    if not isinstance(proxy_rsps, list):
        return out
    for inner in proxy_rsps:
        if not isinstance(inner, dict):
            continue
        for key, value in inner.items():
            if key.endswith("_rsp") and isinstance(value, dict):
                out[key] = value
    return out


def _build_background(rsp: Optional[dict[str, Any]]) -> Optional[HomeBackground]:
    if not rsp:
        return None
    img = rsp.get("main_bg_image")
    if not isinstance(img, dict):
        return None
    video_url = img.get("video_url") if isinstance(img.get("video_url"), str) else None
    image_url = img.get("url") if isinstance(img.get("url"), str) else None
    # Prefer video, then image. Reject anything not on the allow-list.
    if _allowed(video_url):
        return HomeBackground(
            video_url=video_url,
            image_url=image_url if _allowed(image_url) else None,
            local_path=None,
        )
    if _allowed(image_url):
        return HomeBackground(video_url=None, image_url=image_url, local_path=None)
    return None


def _build_banners(rsp: Optional[dict[str, Any]]) -> list[HomeBanner]:
    if not rsp:
        return []
    raw = rsp.get("banners") or []
    if not isinstance(raw, list):
        return []
    banners: list[HomeBanner] = []
    for index, item in enumerate(raw):
        if not isinstance(item, dict):
            continue
        image_url = item.get("url") if isinstance(item.get("url"), str) else None
        if not _allowed(image_url):
            continue
        jump_url = item.get("jump_url") if isinstance(item.get("jump_url"), str) else None
        if jump_url is not None and not _allowed(jump_url):
            jump_url = None
        banner_id = item.get("id")
        banners.append(HomeBanner(
            id=_banner_id(banner_id, index),
            title=None,
            image_url=image_url,
            local_path=None,
            target_url=jump_url,
        ))
    return banners


def _banner_id(value: Any, index: int) -> str:
    if isinstance(value, str) and value:
        return f"hg-banner-{value}"
    if isinstance(value, int):
        return f"hg-banner-{value}"
    return f"hg-banner-{index}"


def _build_news_items(rsp: Optional[dict[str, Any]]) -> list[HomeNewsItem]:
    if not rsp:
        return []
    tabs = rsp.get("tabs") or []
    if not isinstance(tabs, list):
        return []
    items: list[HomeNewsItem] = []
    for tab in tabs:
        if not isinstance(tab, dict):
            continue
        raw_tab = tab.get("tabName")
        tab_name = raw_tab.strip() if isinstance(raw_tab, str) else ""
        category = TAB_NAME_MAP.get(tab_name, tab_name or "其他")
        announcements = tab.get("announcements") or []
        if not isinstance(announcements, list):
            continue
        for index, item in enumerate(announcements):
            if not isinstance(item, dict):
                continue
            content = item.get("content") if isinstance(item.get("content"), str) else ""
            title = content.strip()
            if not title:
                continue
            jump_url = item.get("jump_url") if isinstance(item.get("jump_url"), str) else None
            if jump_url is not None and not _allowed(jump_url):
                jump_url = None
            items.append(HomeNewsItem(
                id=_news_id(item.get("id"), category, index),
                category=category,
                title=title,
                summary=None,
                image_url=None,
                target_url=jump_url,
                published_at=_parse_ts(item.get("start_ts")),
            ))
    return items


def _news_id(value: Any, category: str, index: int) -> str:
    if isinstance(value, str) and value:
        return f"hg-news-{value}"
    if isinstance(value, int):
        return f"hg-news-{value}"
    return f"hg-news-{category}-{index}"


def _parse_ts(value: Any) -> Optional[datetime]:
    if isinstance(value, str) and value.isdigit():
        value = int(value)
    if not isinstance(value, (int, float)):
        return None
    try:
        return datetime.fromtimestamp(int(value) / 1000.0, tz=timezone.utc)
    except (OverflowError, OSError, ValueError):
        return None


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not (lowered.startswith("http://") or lowered.startswith("https://")):
        return False
    host = lowered.split("//", 1)[1].split("/", 1)[0]
    return any(host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)