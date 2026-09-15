"""Next.js ``__NEXT_DATA__`` Provider for 无限暖暖 (Infinity Nikki).

Discovery surface
-----------------
The official 无限暖暖 site is a Next.js SSR app with a JSON envelope
embedded in the homepage HTML. The full home-content payload is split
between two channels:

  1. ``__NEXT_DATA__.props.pageProps.pageData.{page, newsbanner,
     banner}`` — homepage background + top carousel + activity banners.
     The OS build also surfaces the locale-aware tab list and pagination
     strings under ``_nextI18Next.initialI18nStore.<locale>.page``.
  2. ``GET /api/news?section={0,1,2}&offset=&limit=&locale=`` —
     paginated news list for the home-page tabbed feed
     (新闻 / 公告 / 活动).

The OS site is built by INFOLD PTE. LTD. and the CN site by 上海暖叠
网络科技有限公司 (Papergames). They share the same response shapes but
live on different hostnames.

### Network endpoints

| Region | Endpoint                                                       |
|--------|----------------------------------------------------------------|
| OS     | ``https://infinitynikki.infoldgames.com/{locale}/home``         |
| OS     | ``https://infinitynikki.infoldgames.com/api/news?...``         |
| CN     | ``https://infinitynikki.nuanpaper.com/home``                   |
| CN     | ``https://infinitynikki.nuanpaper.com/api/news?...``           |

### Response layout

* **homepage HTML** — Next.js ``__NEXT_DATA__`` JSON plus a hard-coded
  ``<video src=...>`` (OS) or a ``pv_list`` config entry (CN, where the
  markup only renders a ``poster`` image).
* **homepage banners**
  * ``pageData.newsbanner[]`` — top carousel. ``id``, ``title``,
    ``address`` (PC image), ``address_h5`` (mobile image),
    ``stime``/``etime`` (ISO 8601). No click-through URL is exposed.
  * ``pageData.page.actBannerlist[]`` — runtime activity banners. Each
    row carries ``label`` (free text) and a ``value`` field whose body
    is a JSON-encoded object literal with ``bannerimg``, ``link``,
    ``starttime`` (``YYYY-MM-DD HH:MM:SS``), ``endtime``.
* **news list** — ``{data: {total, data: [...]}, ret, msg, timestamp}``.
  Each row has ``id``, ``title``, ``section`` (``0``=新闻, ``1``=公告,
  ``2``=活动), ``publish_time`` (ISO 8601 with milliseconds),
  ``cover`` (image URL), ``abstract`` (lead paragraph).

### Per-game mapping

Defaults match 无限暖暖 / 叠纸. ``providerOptions`` accepts:

  * ``region`` — ``os`` (INFOLD global) / ``cn`` (国服 / 上海暖叠).
  * ``locale`` — ``zh-TW`` / ``en`` / ``ja`` / ``kr`` for the OS build;
    ignored on CN (the CN build is always simplified Chinese).
  * ``pagePath`` — override the homepage path (default ``"/home"``).
  * ``newsLimit`` — per-section page size (default ``4``).
  * ``homePageUrl`` / ``newsApiBase`` — replace the network endpoints.

Verification
------------
Sampled 2026-09-15. Both sites were reachable from the dev machine at
that time. The shape, host names and JSON keys are not part of a
documented public API contract — the launcher relies on the same
embedded markup the marketing site renders.
"""

from __future__ import annotations

import json
import logging
import re
from datetime import datetime, timezone
from typing import Any, Optional

import httpx

from ..models import (
    HomeBackground,
    HomeBanner,
    HomeContent,
    HomeContentRequest,
    HomeNewsItem,
)
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.nextjs_data")


# ----- Region endpoints --------------------------------------------------

DEFAULT_BASE_OS = "https://infinitynikki.infoldgames.com"
DEFAULT_BASE_CN = "https://infinitynikki.nuanpaper.com"

DEFAULT_LOCALE_OS = "zh-TW"
DEFAULT_LOCALE_CN = "zh-CN"
DEFAULT_PAGE_PATH = "/home"
DEFAULT_NEWS_LIMIT = 4

DEFAULT_REGION = "os"

# Hosts we trust for media + jump URLs. The OS site and the CN site share
# the same Next.js / 叠纸 CDN set; we accept either suffix so an OS news
# article can link to assets hosted on the OS CDN while CN stays on the
# Papergames CDN.
ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".infoldgames.com",
    ".papegames.com",
    ".nuanpaper.com",
    ".webstatic.infoldgames.com",
    ".webstatic.papegames.com",
    ".assets.infoldgames.com",
    ".assets.papegames.com",
    ".assets.nuanpaper.com",
)

# News section (server-side tab) → normalized Chinese category. The tab
# order on the marketing site is 新闻 (0) / 公告 (1) / 活动 (2); we keep
# the upstream numbering so future tab additions map cleanly.
SECTION_CATEGORY: dict[str, str] = {
    "0": "资讯",
    "1": "公告",
    "2": "活动",
}

DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (NextJsDataProvider)",
    "Accept": "application/json, text/html, */*",
    "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.5",
}


# ---------------------------------------------------------------------------
# Provider
# ---------------------------------------------------------------------------


class NextJsDataProvider(HomeContentProvider):
    provider_id = "nextjs-data"

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
        home_url_override = options.get("homePageUrl")
        if region == "cn":
            news_base = str(
                options.get("newsApiBase") or DEFAULT_BASE_CN
            )
            locale = str(options.get("locale") or DEFAULT_LOCALE_CN)
            page_path = str(options.get("pagePath") or DEFAULT_PAGE_PATH)
            if isinstance(home_url_override, str) and home_url_override:
                home_url = home_url_override
            else:
                home_url = f"{news_base.rstrip('/')}{page_path}"
            news_locale: Optional[str] = None  # CN ignores locale param
        else:
            news_base = str(
                options.get("newsApiBase") or DEFAULT_BASE_OS
            )
            locale = str(options.get("locale") or DEFAULT_LOCALE_OS)
            page_path = str(options.get("pagePath") or DEFAULT_PAGE_PATH)
            if isinstance(home_url_override, str) and home_url_override:
                home_url = home_url_override
            else:
                home_url = f"{news_base.rstrip('/')}/{locale.lstrip('/')}{page_path}"
            news_locale = locale

        limit = int(options.get("newsLimit") or DEFAULT_NEWS_LIMIT)

        # Homepage: background + newsbanner + actBannerlist.
        html = await self._fetch_text(home_url)
        video_url: Optional[str] = None
        video_poster: Optional[str] = None
        page_data: dict[str, Any] = {}
        if html is not None:
            try:
                video_url, video_poster, page_data = parse_home_html(html)
            except ValueError as exc:
                logger.warning("Next.js home payload from %s could not be parsed: %s", home_url, exc)

        # When the markup lacks a <video src> (CN build), fall back to the
        # ``pc 首屏背景视频`` entry embedded in ``pageData.page.pv_list``.
        if not video_url:
            video_url = _resolve_pv_video(page_data.get("page") or {})

        # Banner/news endpoints.
        banners = _build_top_banners(page_data.get("newsbanner") or [])
        banners.extend(_build_activity_banners(page_data.get("page", {}).get("actBannerlist") or []))

        # News list: section 0 (资讯) + 1 (公告) + 2 (活动).
        news_items: list[HomeNewsItem] = []
        for section_key in ("0", "1", "2"):
            payload = await self._fetch_news(news_base, section_key, limit, news_locale, region)
            news_items.extend(_build_news_items(payload, region, news_base, section_key))

        background = _build_background(video_url, video_poster)

        return HomeContent(
            background=background,
            banners=banners,
            news=news_items,
            update_info=None,
        )

    async def _fetch_text(self, url: str) -> Optional[str]:
        try:
            if self._client is not None:
                response = await self._client.get(url)
            else:
                async with httpx.AsyncClient(timeout=self._timeout, headers=DEFAULT_HEADERS) as client:
                    response = await client.get(url)
            response.raise_for_status()
            return response.text
        except (httpx.HTTPError, ValueError) as exc:
            logger.warning("Failed to fetch %s: %s", url, exc)
            return None

    async def _fetch_news(
        self,
        base: str,
        section: str,
        limit: int,
        locale: Optional[str],
        region: str,
    ) -> dict[str, Any]:
        url = f"{base.rstrip('/')}/api/news"
        params: list[tuple[str, str]] = [
            ("section", section),
            ("offset", "0"),
            ("limit", str(limit)),
        ]
        if locale:
            params.append(("locale", locale))
        try:
            if self._client is not None:
                response = await self._client.get(url, params=params)
            else:
                async with httpx.AsyncClient(timeout=self._timeout, headers=DEFAULT_HEADERS) as client:
                    response = await client.get(url, params=params)
            response.raise_for_status()
            payload = response.json()
            if not isinstance(payload, dict):
                raise ValueError("news response is not a JSON object")
            return payload
        except (httpx.HTTPError, ValueError, json.JSONDecodeError) as exc:
            logger.warning("Failed to fetch news section=%s region=%s: %s", section, region, exc)
            return {}


# ---------------------------------------------------------------------------
# HTML / JSON helpers
# ---------------------------------------------------------------------------


_NEXT_DATA_RE = re.compile(
    r'<script id="__NEXT_DATA__" type="application/json">(.+?)</script>',
    re.DOTALL,
)
_VIDEO_SRC_RE = re.compile(
    r'<video[^>]*\bsrc="(https?://[^"]+\.(?:mp4|webm|mov))"',
    re.IGNORECASE,
)
_VIDEO_POSTER_RE = re.compile(
    r'<video[^>]*\bposter="(https?://[^"]+)"',
    re.IGNORECASE,
)


def parse_home_html(html: str) -> tuple[Optional[str], Optional[str], dict[str, Any]]:
    """Return ``(video_url, video_poster, pageData)``.

    ``pageData`` mirrors ``__NEXT_DATA__.props.pageProps.pageData`` (a
    dict containing the ``page`` config object, the ``newsbanner``
    carousel, and an empty ``banner`` array the upstream reserved for
    future use). Raises :class:`ValueError` when ``__NEXT_DATA__`` is
    missing or unparsable.
    """
    video_match = _VIDEO_SRC_RE.search(html)
    poster_match = _VIDEO_POSTER_RE.search(html)
    next_data_match = _NEXT_DATA_RE.search(html)
    if not next_data_match:
        raise ValueError("__NEXT_DATA__ blob not found in homepage HTML")
    try:
        next_data = json.loads(next_data_match.group(1))
    except json.JSONDecodeError as exc:
        raise ValueError(f"__NEXT_DATA__ is not valid JSON: {exc}")
    page_data = (
        next_data.get("props", {})
        .get("pageProps", {})
        .get("pageData", {})
    )
    if not isinstance(page_data, dict):
        page_data = {}
    return (
        video_match.group(1) if video_match else None,
        poster_match.group(1) if poster_match else None,
        page_data,
    )


def _resolve_pv_video(page: dict[str, Any]) -> Optional[str]:
    """Fallback when the upstream markup omits a ``<video src>`` (CN).

    The CN build encodes the homepage background under
    ``pv_list[]`` with the label ``pc 首屏背景视频``.
    """
    for entry in page.get("pv_list") or []:
        if isinstance(entry, dict) and entry.get("label") == "pc 首屏背景视频":
            value = entry.get("value")
            if isinstance(value, str) and value:
                return value
    return None


# ---------------------------------------------------------------------------
# Background / banner / news builders
# ---------------------------------------------------------------------------


def _build_background(video_url: Optional[str], video_poster: Optional[str]) -> HomeBackground:
    video = video_url
    poster = video_poster
    if not video:
        # Caller did not pass an HTML snippet; rely on whatever was provided.
        pass
    return HomeBackground(video_url=video, image_url=poster, local_path=None)


def _build_top_banners(newsbanner: list[Any]) -> list[HomeBanner]:
    """Map ``pageData.newsbanner[]`` to ``HomeBanner`` rows.

    The upstream carousel is image-only — there is no click-through URL
    in the payload. The C# UI therefore uses ``localPath`` or
    ``image_url`` as the primary affordance, so we drop any banner whose
    image URL is missing or hosted on an unknown domain.
    """
    banners: list[HomeBanner] = []
    for index, item in enumerate(newsbanner):
        if not isinstance(item, dict):
            continue
        image_url = item.get("address") if isinstance(item.get("address"), str) else None
        if not image_url or not _allowed(image_url):
            continue
        title = item.get("title") if isinstance(item.get("title"), str) else None
        if isinstance(title, str):
            title = title.strip() or None
        banners.append(HomeBanner(
            id=f"in-newsbanner-{item.get('id', index)}",
            title=title,
            image_url=image_url,
            local_path=None,
            target_url=None,
            starts_at=_parse_iso(item.get("stime")),
            ends_at=_parse_iso(item.get("etime")),
        ))
    return banners


def _build_activity_banners(act_banner_list: list[Any]) -> list[HomeBanner]:
    """Map ``pageData.page.actBannerlist[]`` rows.

    Each entry is a ``{label, value}`` pair where ``value`` is a JSON
    string with ``bannerimg``, ``link`` and ``starttime`` / ``endtime``.
    Banners without an allowed ``bannerimg`` are dropped so the UI does
    not show empty rows.
    """
    banners: list[HomeBanner] = []
    for index, item in enumerate(act_banner_list):
        if not isinstance(item, dict):
            continue
        raw_value = item.get("value")
        if not isinstance(raw_value, str) or not raw_value.strip():
            continue
        try:
            payload = json.loads(raw_value)
        except json.JSONDecodeError as exc:
            logger.warning("actBannerlist entry %d has invalid JSON value: %s", index, exc)
            continue
        if not isinstance(payload, dict):
            continue
        image_url = payload.get("bannerimg") if isinstance(payload.get("bannerimg"), str) else None
        if not image_url or not _allowed(image_url):
            continue
        link = payload.get("link") if isinstance(payload.get("link"), str) else None
        if link is not None and not _allowed(link):
            link = None
        label = item.get("label") if isinstance(item.get("label"), str) else None
        banners.append(HomeBanner(
            id=f"in-actbanner-{index}-{abs(hash(image_url)) % 100000}",
            title=(label or "").strip() or None,
            image_url=image_url,
            local_path=None,
            target_url=link,
            starts_at=_parse_naive(payload.get("starttime")),
            ends_at=_parse_naive(payload.get("endtime")),
        ))
    return banners


def _build_news_items(
    payload: dict[str, Any],
    region: str,
    base: str,
    section_key: str,
) -> list[HomeNewsItem]:
    """Convert one ``/api/news`` response into ``HomeNewsItem`` rows."""
    rows = (
        payload.get("data", {}).get("data")
        if isinstance(payload.get("data"), dict)
        else None
    )
    if not isinstance(rows, list):
        return []
    items: list[HomeNewsItem] = []
    category = SECTION_CATEGORY.get(section_key, "其他")
    article_base = base.rstrip("/")
    for index, raw in enumerate(rows):
        if not isinstance(raw, dict):
            continue
        title = raw.get("title")
        if not isinstance(title, str):
            continue
        title = title.strip()
        if not title:
            continue
        image_url = raw.get("cover") if isinstance(raw.get("cover"), str) else None
        if image_url and not _allowed(image_url):
            image_url = None
        item_id = raw.get("id")
        article_path = f"/news/{item_id}" if item_id is not None else ""
        target_url = f"{article_base}{article_path}" if article_path else None
        summary = raw.get("abstract") if isinstance(raw.get("abstract"), str) else None
        items.append(HomeNewsItem(
            id=f"in-news-{region}-{section_key}-{item_id if item_id is not None else index}",
            category=category,
            title=title,
            summary=(summary or "").strip() or None,
            image_url=image_url,
            target_url=target_url,
            published_at=_parse_iso(raw.get("publish_time")),
        ))
    return items


# ---------------------------------------------------------------------------
# Date / URL helpers
# ---------------------------------------------------------------------------


def _parse_iso(value: Any) -> Optional[datetime]:
    if not isinstance(value, str) or not value:
        return None
    raw = value.strip()
    if not raw:
        return None
    # The marketing site uses ``Z`` for UTC and ``.000Z`` for milliseconds;
    # :func:`datetime.fromisoformat` does not accept ``Z`` before 3.11 so we
    # normalize it explicitly.
    candidate = raw.replace("Z", "+00:00") if raw.endswith("Z") else raw
    try:
        parsed = datetime.fromisoformat(candidate)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def _parse_naive(value: Any) -> Optional[datetime]:
    """Parse ``YYYY-MM-DD HH:MM:SS`` style strings emitted by actBannerlist."""
    if not isinstance(value, str) or not value:
        return None
    raw = value.strip()
    if not raw:
        return None
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S", "%Y-%m-%d"):
        try:
            return datetime.strptime(raw, fmt).replace(tzinfo=timezone.utc)
        except ValueError:
            continue
    return _parse_iso(raw)


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not (lowered.startswith("http://") or lowered.startswith("https://")):
        return False
    host = lowered.split("//", 1)[1].split("/", 1)[0]
    return any(host == suffix.lstrip(".") or host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)
