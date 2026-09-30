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

The provider first inspects the selected installation for the launcher's
``NTELauncher/ResFilesM/<version>/bgimgs/config.json``.  The video and
poster named by that file are returned as local media, while the pinned
network video remains a fallback.  Explicit ``backgroundVideoPath`` and
``backgroundImagePath`` options still take precedence.

### Per-game mapping

Defaults match 异环国服. ``providerOptions`` accepts:

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
from pathlib import Path
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

# SteamCN serves the Mainland China build by default. OS remains available
# only when a caller explicitly sets providerOptions.region="os".
DEFAULT_APP_CODE = "YDUTE5gscDZ229CW"  # legacy export; not used by this provider
DEFAULT_LANGUAGE = "zh-cn"
DEFAULT_REGION = "cn"

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

        local_video_path, local_image_path = _discover_local_background(request, options)
        if local_video_path or local_image_path:
            logger.info(
                "Using local NTE launcher background video=%s poster=%s",
                Path(local_video_path).name if local_video_path else "none",
                Path(local_image_path).name if local_image_path else "none",
            )
        background = _build_background(
            options,
            region,
            discovered_video_path=local_video_path,
            discovered_image_path=local_image_path,
        )
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


def _build_background(
    options: dict[str, Any],
    region: str,
    *,
    discovered_video_path: Optional[str] = None,
    discovered_image_path: Optional[str] = None,
) -> Optional[HomeBackground]:
    video_path = options.get("backgroundVideoPath") or discovered_video_path
    video_url = options.get("backgroundVideoUrl")
    image_path = options.get("backgroundImagePath") or discovered_image_path
    image_url = options.get("backgroundImageUrl")

    remote_video: Optional[str] = None
    image: Optional[str] = None

    if video_url and _allowed(video_url):
        remote_video = str(video_url)
    else:
        default = DEFAULT_BG_VIDEO_CN if region == "cn" else DEFAULT_BG_VIDEO_OS
        remote_video = default

    if image_path:
        image = str(image_path)
    elif image_url and _allowed(image_url):
        image = str(image_url)

    return HomeBackground(
        video_url=remote_video,
        image_url=image,
        local_path=str(video_path) if video_path else None,
    )


def _discover_local_background(
    request: HomeContentRequest,
    options: dict[str, Any],
) -> tuple[Optional[str], Optional[str]]:
    """Find the launcher's current background without recursively scanning the game.

    A preset may point at the launcher root, the game executable, or a nested game
    directory.  Walk only a small number of parents and probe the launcher-owned
    ``ResFilesM`` layout.  This keeps discovery deterministic and avoids treating
    unrelated videos in the installation as homepage media.
    """
    seeds: list[Path] = []
    configured_install = options.get("installDir")
    if isinstance(configured_install, str) and configured_install.strip():
        seeds.append(Path(configured_install.strip()))
    if request.install_directory:
        seeds.append(Path(request.install_directory))
    if request.executable_path:
        seeds.append(Path(request.executable_path).parent)

    checked_roots: set[str] = set()
    for seed in seeds:
        current = seed
        for _ in range(6):
            try:
                normalized = str(current.resolve(strict=False)).casefold()
            except (OSError, RuntimeError, ValueError):
                break
            if normalized not in checked_roots:
                checked_roots.add(normalized)
                discovered = _discover_background_below(current)
                if discovered != (None, None):
                    return discovered
            if current.parent == current:
                break
            current = current.parent
    return None, None


def _discover_background_below(root: Path) -> tuple[Optional[str], Optional[str]]:
    config_candidates = [root / "config.json", root / "bgimgs" / "config.json"]
    for resources_root in (root / "NTELauncher" / "ResFilesM", root / "ResFilesM"):
        try:
            version_directories = [item for item in resources_root.iterdir() if item.is_dir()]
        except (FileNotFoundError, NotADirectoryError, PermissionError, OSError):
            continue
        version_directories.sort(key=_launcher_resource_version_key, reverse=True)
        config_candidates.extend(item / "bgimgs" / "config.json" for item in version_directories)

    for config_path in config_candidates:
        result = _read_launcher_background_config(config_path)
        if result != (None, None):
            return result
    return None, None


def _launcher_resource_version_key(path: Path) -> tuple[tuple[int, ...], float, str]:
    numeric_parts = tuple(int(part) for part in re.findall(r"\d+", path.name))
    try:
        modified = path.stat().st_mtime
    except OSError:
        modified = 0.0
    return numeric_parts, modified, path.name.casefold()


def _read_launcher_background_config(config_path: Path) -> tuple[Optional[str], Optional[str]]:
    try:
        payload = json.loads(config_path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, NotADirectoryError, PermissionError, OSError, UnicodeError, json.JSONDecodeError):
        return None, None
    if not isinstance(payload, dict):
        return None, None

    background_root = config_path.parent
    video = _resolve_launcher_asset(background_root, payload.get("video"), {".mp4", ".webm"})
    image_name = payload.get("noVideoBg")
    if not isinstance(image_name, str):
        images = payload.get("imgs")
        if isinstance(images, list):
            for item in images:
                if isinstance(item, dict) and isinstance(item.get("file"), str):
                    image_name = item["file"]
                    break
    image = _resolve_launcher_asset(
        background_root,
        image_name,
        {".jpg", ".jpeg", ".png", ".webp"},
    )
    return video, image


def _resolve_launcher_asset(
    background_root: Path,
    value: Any,
    allowed_extensions: set[str],
) -> Optional[str]:
    if not isinstance(value, str) or not value.strip():
        return None
    try:
        root = background_root.resolve(strict=False)
        candidate = (root / value.strip()).resolve(strict=False)
    except (OSError, RuntimeError, ValueError):
        return None
    try:
        candidate.relative_to(root)
    except ValueError:
        return None
    if candidate.suffix.lower() not in allowed_extensions or not candidate.is_file():
        return None
    return str(candidate)


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
