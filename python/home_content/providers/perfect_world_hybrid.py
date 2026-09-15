"""Perfect World hybrid Provider for 异环 (Neverness to Everness).

Discovery surface
-----------------
异环 桌面启动器(QtQuick + CEF)在 `~/.minimax` 风格的本地安装目录里读背景视频;
剩余的 Banner / 资讯则由完美世界官网的 JS 数据端点提供。本 Provider 把两个来源
拼成统一的 ``HomeContent``。

The provider currently fans out two HTTP requests per fetch — both are
small (≤ 50 KB) public JS payloads that the launcher CEF already
downloads on every page load, so adding them to our refresh loop does
not create a new load on the upstream.

### Network endpoints

| Region | Endpoint                                                                              |
|--------|---------------------------------------------------------------------------------------|
| OS     | ``https://www.perfectworld.com/public/commonData/gamesData/gameSwiper/nte-gameSwiper.js`` |
| OS     | ``https://nte.perfectworld.com/include/newsData20260112.js``                              |
| CN     | ``https://static.games.wanmei.com/public/commonData/gamesData/gameSwiper/yh-gameSwiper.js`` |
| CN     | ``https://yh.wanmei.com/include/newsData20260112.js``                                      |

Both endpoints return a JS assignment ``var NAME = {...};`` whose body
is JSON-shaped but uses JS-only quirks (one trailing comma per item,
blank-line item separators). ``_extract_js_payload`` strips the wrapper
and normalizes the body so ``json.loads`` accepts it.

### Response layout

**OS banner swiper**: ``{"lb1_<lang>": [{title, viceTitle, bigpic, viewpic, link, mlink}, ...], ...}``
**CN banner swiper**: ``{"lb1": [...], "launcherPic": [...]}``
**OS news data**: ``{"<lang>": {"news": [...], "gamenews": [...], "gamebroad": [...], "gameevent": [...]}}``
**CN news data**: ``{"pc": {"news": [...], "gamenews": [...], "gamebroad": [...], "gameevent": [...]}, "m": {...}}``

Each news item: ``{title, url, time, channelDescription/channelCnName, channelName}``.
Each banner: ``{title, viceTitle, bigpic, viewpic, link, mlink}``.

### Local launcher resources

Background video is loaded from the network by default. When
``providerOptions.backgroundVideoPath`` (or
``providerOptions.backgroundVideoUrl``) is supplied the provider treats
the local file path / URL as the canonical background and the network
URL as a fallback. ``providerOptions.backgroundImagePath`` similarly
overrides the image fallback.

### Per-game mapping

Defaults match Endfield. ``providerOptions`` accepts:

  * ``region``  — ``os`` (Global) / ``cn`` (国服) / ``tw`` (台港澳 — not yet
    implemented; the OS launcher currently serves tw regions via
    ``nte.perfectworld.com`` anyway)
  * ``language`` — drives both ``lb1_<lang>`` and the OS news lang key;
    CN provider passes through to ``pc.*`` (no language gating)
  * ``installDir`` — overrides local background discovery
  * ``bannerSwiperUrl`` / ``newsDataUrl`` — replace the network endpoints

Verification
------------
Sampled 2026-09-14. JS endpoints confirmed reachable from the dev
machine; structure may shift at any time and is not part of an open
API contract.
"""

from __future__ import annotations

import json
import logging
import re
from datetime import datetime, timezone
from typing import Any, Optional

import httpx

from ..models import HomeBackground, HomeBanner, HomeContent, HomeContentRequest, HomeNewsItem
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.perfect_world_hybrid")


# Background video URLs are scraped from the live homepage HTML and
# pinned here. They are not covered by the allow-list because we
# always fall back to the network URL when the local file is missing.
DEFAULT_BG_VIDEO_OS = "https://ntevmg.perfectworld.com/webops/nte/nte_bgvideo_20260418.mp4"
DEFAULT_BG_VIDEO_CN = "https://yhvmg.wmupd.com/webops/yh/yh_bgvideo_20260418.mp4"

# Region-aware JS endpoints (HTTP + JSON-shaped JS payload).
DEFAULT_ENDPOINTS_OS = {
    "banner_swiper": "https://www.perfectworld.com/public/commonData/gamesData/gameSwiper/nte-gameSwiper.js",
    "news_data": "https://nte.perfectworld.com/include/newsData20260112.js",
}
DEFAULT_ENDPOINTS_CN = {
    "banner_swiper": "https://static.games.wanmei.com/public/commonData/gamesData/gameSwiper/yh-gameSwiper.js",
    "news_data": "https://yh.wanmei.com/include/newsData20260112.js",
}

# Allow-list of host suffixes for media + jump URLs.
ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".perfectworld.com",
    ".wanmei.com",
    ".wmupd.com",
    ".games.wanmei.com",
    ".static.pwsdk.com",
)

# Defaults for Endfield Global.
DEFAULT_APP_CODE = "YDUTE5gscDZ229CW"
DEFAULT_LANGUAGE = "en-us"
DEFAULT_REGION = "os"

DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (PerfectWorldHybridProvider)",
    "Accept": "application/json, text/javascript, */*",
}

# English tab names → normalized Chinese category. Kept as a documented
# mapping of the per-language channelDescription values seen in the
# upstream news payload; the runtime category is derived from the JSON
# bucket key (news / gamebroad / gameevent / gamenews) below.
TAB_NAME_MAP: dict[str, str] = {
    "Notices": "公告",
    "Mitteilungen": "公告",
    "Annonces": "公告",
    "Avisos": "公告",
    "공지": "公告",
    "お知らせ": "公告",
    "News": "资讯",
    "Nachrichten": "资讯",
    "Actualités": "资讯",
    "Noticias": "资讯",
    "Noticias ": "资讯",
    "Berita": "资讯",
    "ข่าวสาร": "资讯",
    "Notícias": "资讯",
    "Новости": "资讯",
    "Events": "活动",
    "Événements": "活动",
    "Eventos": "活动",
    "이벤트": "活动",
    "イベント": "活动",
    "События": "活动",
}


class PerfectWorldHybridProvider(HomeContentProvider):
    provider_id = "perfect-world-hybrid"

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
        language = str(options.get("language", DEFAULT_LANGUAGE)).lower()
        swiper_url, news_url = _resolve_endpoints(options, region)

        swiper_payload: dict[str, Any] = {}
        news_payload: dict[str, Any] = {}
        if swiper_url:
            raw = await self._fetch_text(swiper_url)
            if raw is not None:
                try:
                    swiper_payload = extract_js_payload(raw)
                except ValueError as exc:
                    logger.warning("Banner swiper payload from %s could not be parsed: %s", swiper_url, exc)
        if news_url:
            raw = await self._fetch_text(news_url)
            if raw is not None:
                try:
                    news_payload = extract_js_payload(raw)
                except ValueError as exc:
                    logger.warning("News data payload from %s could not be parsed: %s", news_url, exc)

        background = _build_background(options, region)
        banners = _build_banners(swiper_payload, region, language)
        news_items = _build_news_items(news_payload, region, language)

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


# ---------------------------------------------------------------------------
# Payload helpers
# ---------------------------------------------------------------------------


_ASSIGN_RE = re.compile(r"=\s*(\{.*\})\s*;?\s*$", re.DOTALL)
_DOUBLE_COMMA_RE = re.compile(r",\s*,")
_TRAILING_COMMA_RE = re.compile(r",(\s*[}\]])")
_BLANK_LINE_RE = re.compile(r"\n\s*\n")


def extract_js_payload(raw: str) -> dict[str, Any]:
    """Pull the JSON object literal out of ``var NAME = {...};`` and parse it.

    The launcher-loaded files look like JSON but use JS-only artifacts
    (one trailing comma per list item, blank lines between items, optional
    ``var NAME = `` wrapper). We strip them before handing the body to
    ``json.loads``.
    """
    body = raw.strip()
    m = _ASSIGN_RE.search(body)
    if m:
        body = m.group(1)
    if not body.startswith("{"):
        raise ValueError("payload is not a JS object assignment")
    # Tidy JS-only artifacts so json.loads accepts the body.
    body = _BLANK_LINE_RE.sub("\n", body)
    body = re.sub(r"[ \t]+", " ", body)
    body = _TRAILING_COMMA_RE.sub(r"\1", body)
    while _DOUBLE_COMMA_RE.search(body):
        body = _DOUBLE_COMMA_RE.sub(",", body)
    return json.loads(body)


def _resolve_endpoints(options: dict[str, Any], region: str) -> tuple[Optional[str], Optional[str]]:
    swiper_override = options.get("bannerSwiperUrl")
    news_override = options.get("newsDataUrl")
    if region == "cn":
        defaults = DEFAULT_ENDPOINTS_CN
    else:
        defaults = DEFAULT_ENDPOINTS_OS
    return (
        str(swiper_override) if swiper_override else defaults["banner_swiper"],
        str(news_override) if news_override else defaults["news_data"],
    )


def _build_background(options: dict[str, Any], region: str) -> Optional[HomeBackground]:
    video_path = options.get("backgroundVideoPath")
    video_url = options.get("backgroundVideoUrl")
    image_path = options.get("backgroundImagePath")
    image_url = options.get("backgroundImageUrl")

    video: Optional[str] = None
    image: Optional[str] = None

    if video_path:
        video = str(video_path)
    elif video_url and _allowed(video_url):
        video = str(video_url)
    else:
        default = DEFAULT_BG_VIDEO_CN if region == "cn" else DEFAULT_BG_VIDEO_OS
        video = default

    if image_path:
        image = str(image_path)
    elif image_url and _allowed(image_url):
        image = str(image_url)

    return HomeBackground(video_url=video, image_url=image, local_path=None)


def _build_banners(
    payload: dict[str, Any],
    region: str,
    language: str,
) -> list[HomeBanner]:
    raw_banners = _select_banner_list(payload, region, language)
    banners: list[HomeBanner] = []
    for index, item in enumerate(raw_banners):
        if not isinstance(item, dict):
            continue
        image_url = item.get("bigpic") if isinstance(item.get("bigpic"), str) else None
        if not _allowed(image_url):
            continue
        link = item.get("link") if isinstance(item.get("link"), str) else None
        if link is not None and not _allowed(link):
            link = None
        title = item.get("title") if isinstance(item.get("title"), str) else None
        if title is not None and not title.strip():
            title = None
        banners.append(HomeBanner(
            id=f"pw-banner-{region}-{language}-{index}",
            title=title,
            image_url=image_url,
            local_path=None,
            target_url=link,
        ))
    return banners


def _select_banner_list(
    payload: dict[str, Any],
    region: str,
    language: str,
) -> list[dict[str, Any]]:
    if not payload:
        return []
    # OS uses ``lb1_<lang>``; CN uses ``lb1`` (no language gating).
    if region == "cn":
        candidates = [payload.get("lb1") or [], payload.get("launcherPic") or []]
    else:
        short_lang = _short_lang(language)
        lb1_key = f"lb1_{short_lang}"
        candidates = [payload.get(lb1_key) or [], payload.get("lb1") or []]
    for candidate in candidates:
        if isinstance(candidate, list) and candidate:
            return candidate
    return []


def _short_lang(language: str) -> str:
    """Map ``en-us`` → ``en``, ``zh-cn`` → ``cn`` (and similar)."""
    if not language:
        return "en"
    primary = language.split("-", 1)[0]
    mapping = {"en": "en", "zh": "cn", "ja": "jp", "ko": "kr", "fr": "fr", "de": "de",
               "ru": "ru", "es": "es", "pt": "pt", "id": "id", "th": "th"}
    return mapping.get(primary, primary)


def _build_news_items(
    payload: dict[str, Any],
    region: str,
    language: str,
) -> list[HomeNewsItem]:
    if not payload:
        return []
    raw_blocks = _select_news_blocks(payload, region, language)
    short_lang = _short_lang(language)
    items: list[HomeNewsItem] = []
    for category_key, raw_items in raw_blocks:
        if not isinstance(raw_items, list):
            continue
        for index, item in enumerate(raw_items):
            if not isinstance(item, dict):
                continue
            title = item.get("title") if isinstance(item.get("title"), str) else ""
            title = title.strip()
            if not title:
                continue
            raw_url = item.get("url") if isinstance(item.get("url"), str) else None
            url = _absolutize_url(raw_url, region, short_lang)
            if url is not None and not _allowed(url):
                url = None
            category = _resolve_category(item, category_key, region)
            items.append(HomeNewsItem(
                id=f"pw-news-{region}-{category_key}-{index}",
                category=category,
                title=title,
                summary=None,
                image_url=None,
                target_url=url,
                published_at=_parse_date(item.get("time")),
            ))
    return items


def _absolutize_url(
    url: Optional[str],
    region: str,
    short_lang: str,
) -> Optional[str]:
    """Resolve the upstream's relative ``/article/...`` paths against the launcher's host.

    CN returns paths under ``yh.wanmei.com`` (no language segment, e.g.
    ``/news/gamenews/123.html``). OS returns paths prefixed with the
    matching language key (``/en/...`` / ``/cn/...`` / ``/jp/...``) — the
    lang segment is already present so we just prepend the host.
    Anything else with an ``http(s)://`` scheme is returned unchanged so
    the allow-list still gets a chance to reject it.
    """
    if not url:
        return None
    if url.startswith("http://") or url.startswith("https://"):
        return url
    if not url.startswith("/"):
        return url
    if region == "cn":
        return f"https://yh.wanmei.com{url}"
    return f"https://nte.perfectworld.com{url}"


def _select_news_blocks(
    payload: dict[str, Any],
    region: str,
    language: str,
) -> list[tuple[str, list[Any]]]:
    """Return [(category_key, items)] for the active region/language."""
    if region == "cn":
        # CN data is keyed by ``pc`` / ``m``; we always pick the PC buckets.
        lang_bucket = payload.get("pc")
        if not isinstance(lang_bucket, dict):
            return []
    else:
        short_lang = _short_lang(language)
        lang_bucket = payload.get(short_lang)
        if not isinstance(lang_bucket, dict):
            # Fallback to the bundled CN bucket, then to ``en`` so we still
            # surface news even when the user's language is unavailable.
            for fallback in ("cn", "en"):
                lang_bucket = payload.get(fallback)
                if isinstance(lang_bucket, dict):
                    break
            else:
                return []
    # The four categories are emitted in priority order.
    return [
        ("news", lang_bucket.get("news") or []),
        ("notice", lang_bucket.get("gamebroad") or []),
        ("event", lang_bucket.get("gameevent") or []),
        ("media", lang_bucket.get("gamenews") or []),
    ]


def _resolve_category(item: dict[str, Any], bucket_key: str, region: str) -> str:
    """Pick the normalized category from per-item or per-bucket fallback.

    CN data already carries ``channelCnName`` in Chinese (e.g. ``公告``);
    OS data carries ``channelDescription`` in the user's locale and falls
    back to the bucket name (``gamebroad`` → 公告, ``gameevent`` → 活动,
    ``gamenews`` → 资讯, ``news`` → 公告).
    """
    cn_name = item.get("channelCnName")
    if isinstance(cn_name, str) and cn_name:
        return cn_name
    description = item.get("channelDescription")
    if isinstance(description, str) and description:
        mapped = TAB_NAME_MAP.get(description.strip())
        if mapped:
            return mapped
    bucket_fallback = {
        "news": "公告",
        "notice": "公告",
        "event": "活动",
        "media": "资讯",
    }
    return bucket_fallback.get(bucket_key, "其他")


def _parse_date(value: Any) -> Optional[datetime]:
    if not isinstance(value, str) or not value:
        return None
    try:
        return datetime.strptime(value, "%Y-%m-%d").replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not (lowered.startswith("http://") or lowered.startswith("https://")):
        return False
    host = lowered.split("//", 1)[1].split("/", 1)[0]
    return any(host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)