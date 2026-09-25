"""Kuro Games launcher Provider for Wuthering Waves.

Discovery surface
----------------
The official Kuro launcher serves a three-layer JSON pipeline under either
``prod-alicdn-gamestarter.kurogame.com`` (Global / OS) or
``prod-cn-alicdn-gamestarter.kurogame.com`` (Mainland China / CN). The
Provider walks it in order:

1. ``launcher-config`` (``.../launcher/<APP>_<APPKEY>/<GAME>/index.json``)
   — returns ``functionCode.background``, the dynamic hash that selects the
   active background JSON for the current version.
2. ``wallpapers-slogan`` (``.../<APP>_<APPKEY>/<GAME>/background/<HASH>/<LANG>.json``)
   — returns ``backgroundFile`` (MP4), ``firstFrameImage`` (WebP) and
   ``slogan`` (PNG title overlay).
3. ``news-notices`` (``.../<APP>_<APPKEY>/<GAME>/information/<LANG>.json``)
   — returns ``guidance.{activity,notice,news}`` for the news list and
   ``slideshow`` for the banner carousel.

Region differences
------------------
The CN launcher (gameId ``G152``, appId ``10003``, appKey
``Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5``) ships ``zh-Hans.json`` and a
companion empty ``en.json``; the OS launcher (gameId ``G153``, appId
``50004``) only ships ``en.json``. The CN region is the default because
the launcher primarily serves 国服 (Mainland China) users. Callers pick
the region with ``providerOptions['region']`` (``'cn'`` | ``'os'``).

This endpoint chain is shipped inside the Kuro desktop launcher and is
not a public API. Sampled 2026-09-14 (OS, en.json) and 2026-09-25
(CN, zh-Hans.json). URL constants were verified against the published
``KRApp.conf`` (base64(XOR(data, 0x63))) on both launchers.

Fallback surface
----------------
The Provider tries the locale-derived language first, then ``en``. Both
wallpaper and news are fetched in parallel; if one language variant
returns 404 the Provider falls through to the next. On HTTP errors the
Provider raises and the Worker's envelope handler converts the failure
into ``envelope.errors``.

Per-game mapping
----------------
The constant triple below (``appId``, ``appKey``, ``gameId``) differs
between CN and OS; other Kuro titles (战双帕弥什 etc.) share the same
endpoint shape with different triples, which callers pass via
``providerOptions`` (``region``, ``appId``, ``appKey``, ``gameId``).
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime
from typing import Any, Optional

import httpx

from ..models import HomeBackground, HomeBanner, HomeContent, HomeContentRequest, HomeNewsItem
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.kuro_launcher")


# ---------------------------------------------------------------------------
# Region constants
# ---------------------------------------------------------------------------

# Global / OS launcher — sampled 2026-09-14 against ``en.json``.
CDN_BASE_OS = "https://prod-alicdn-gamestarter.kurogame.com/launcher"
DEFAULT_APP_ID = "50004"
DEFAULT_APP_KEY = "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c"
DEFAULT_GAME_ID = "G153"

# Mainland China / CN launcher — sampled 2026-09-25 against ``zh-Hans.json``.
# Verified via ``prod-cn-alicdn-gamestarter.kurogame.com/launcher/launcher/
# 10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/G152/index.json``.
CDN_BASE_CN = "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher"
DEFAULT_CN_APP_ID = "10003"
DEFAULT_CN_APP_KEY = "Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5"
DEFAULT_CN_GAME_ID = "G152"

# Default region: CN. The launcher's target audience is 国服 users, and
# the CN endpoint is the only one that ships Chinese news content.
DEFAULT_REGION = "cn"
DEFAULT_LANGUAGE_CN = "zh-Hans"
DEFAULT_LANGUAGE_OS = "en"
# Backwards-compatible alias — pre-region code imported this name.
DEFAULT_LANGUAGE = DEFAULT_LANGUAGE_OS

# Per-region constants. ``primary_language`` is the language Kuro ships
# on that region's CDN; the Provider still tries ``en`` as a fallback in
# case the primary language file ever disappears.
_REGION_CONSTANTS: dict[str, dict[str, str]] = {
    "cn": {
        "cdn_base": CDN_BASE_CN,
        "app_id": DEFAULT_CN_APP_ID,
        "app_key": DEFAULT_CN_APP_KEY,
        "game_id": DEFAULT_CN_GAME_ID,
        "primary_language": DEFAULT_LANGUAGE_CN,  # "zh-Hans"
        "fallback_language": DEFAULT_LANGUAGE_OS,  # "en"
    },
    "os": {
        "cdn_base": CDN_BASE_OS,
        "app_id": DEFAULT_APP_ID,
        "app_key": DEFAULT_APP_KEY,
        "game_id": DEFAULT_GAME_ID,
        "primary_language": DEFAULT_LANGUAGE_OS,  # "en"
        "fallback_language": DEFAULT_LANGUAGE_CN,  # "zh-Hans" (theoretically)
    },
}

# Allow-list covers both regions. Kuro CN background CDN lives under
# ``.aki-game.com`` (huoshan variant), banner artwork under
# ``.kurobbs.com``, and news detail pages under ``mc.kurogames.com``.
ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".kurogame.com",
    ".kurogames.com",
    ".aki-game.net",
    ".aki-game.com",
    ".kurobbs.com",
    ".mc.kurogames.com",
)

DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (KuroLauncherProvider)",
    "Accept": "application/json",
}


class KuroLauncherProvider(HomeContentProvider):
    provider_id = "kuro-launcher"

    def __init__(
        self,
        client: Optional[httpx.AsyncClient] = None,
        timeout: httpx.Timeout = DEFAULT_TIMEOUT,
    ) -> None:
        self._client = client
        self._timeout = timeout

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        options = request.provider_options or {}
        region = _resolve_region(options)
        constants = _REGION_CONSTANTS[region]
        # providerOptions still override the per-region defaults so
        # callers can re-target the Provider at non-default endpoints
        # (e.g. a CN kiosk install with custom appKey).
        app_id = str(options.get("appId", constants["app_id"]))
        app_key = str(options.get("appKey", constants["app_key"]))
        game_id = str(options.get("gameId", constants["game_id"]))
        app_segment = f"{app_id}_{app_key}"
        cdn_base = constants["cdn_base"]

        # Step 1: launcher-config (region-specific). Always uses the
        # region's primary language for the *config* file as well, but
        # the config endpoint itself does not embed any per-language
        # payload — the path component is cosmetic.
        config_url = f"{cdn_base}/launcher/{app_segment}/{game_id}/index.json"
        config = await self._fetch_json(config_url)
        bg_hash = str(config.get("functionCode", {}).get("background", "")).strip()
        if not bg_hash:
            raise ValueError(
                f"Kuro {region.upper()} 启动器配置缺少 functionCode.background。"
            )

        # Step 2 + 3: wallpaper + news. Build an ordered language
        # candidate list, then fetch each pair in parallel; advance to
        # the next language when the current one 404s on both endpoints.
        candidates = _language_candidates(region, request)
        wallpaper, news, used_lang = await self._fetch_region_content(
            cdn_base=cdn_base,
            app_segment=app_segment,
            game_id=game_id,
            bg_hash=bg_hash,
            candidates=candidates,
        )

        if wallpaper is None and news is None:
            raise ValueError(
                f"Kuro {region.upper()} 端点对 {_format_candidates(candidates)} 均无响应。"
            )

        logger.info(
            "kuro[%s] used language=%s (candidates=%s)",
            region, used_lang, candidates,
        )

        return HomeContent(
            background=_build_background(wallpaper or {}),
            banners=_build_banners(news or {}),
            news=_build_news_items(news or {}),
            update_info=None,
        )

    async def _fetch_json(self, url: str) -> dict[str, Any]:
        if self._client is not None:
            response = await self._client.get(url)
        else:
            async with httpx.AsyncClient(timeout=self._timeout, headers=DEFAULT_HEADERS) as client:
                response = await client.get(url)
        response.raise_for_status()
        data = response.json()
        if not isinstance(data, dict):
            raise ValueError(f"Kuro 端点未返回 JSON 对象:{url}")
        return data

    async def _fetch_region_content(
        self,
        *,
        cdn_base: str,
        app_segment: str,
        game_id: str,
        bg_hash: str,
        candidates: list[str],
    ) -> tuple[Optional[dict[str, Any]], Optional[dict[str, Any]], str]:
        """Try each language; return first pair where at least one of
        (wallpaper, news) succeeds. Both endpoints are fetched in
        parallel inside each language attempt to keep latency low.
        """
        last_err: Optional[Exception] = None
        for lang in candidates:
            wallpaper_url = (
                f"{cdn_base}/{app_segment}/{game_id}/background/{bg_hash}/{lang}.json"
            )
            news_url = (
                f"{cdn_base}/{app_segment}/{game_id}/information/{lang}.json"
            )

            wallpaper_result, news_result = await asyncio.gather(
                self._fetch_json(wallpaper_url),
                self._fetch_json(news_url),
                return_exceptions=True,
            )

            wallpaper = wallpaper_result if not isinstance(wallpaper_result, Exception) else None
            news = news_result if not isinstance(news_result, Exception) else None

            if wallpaper is None and news is None:
                # Both endpoints 404'd for this language. Record and
                # try the next candidate.
                last_err = (
                    wallpaper_result if isinstance(wallpaper_result, Exception)
                    else news_result
                )
                logger.debug(
                    "kuro language %r failed: %s / %s", lang, wallpaper_result, news_result,
                )
                continue

            # At least one succeeded; accept partial data.
            return wallpaper, news, lang

        # All candidates failed.
        raise last_err if last_err is not None else ValueError(
            "Kuro 端点所有候选语言均无响应。"
        )


# ---------------------------------------------------------------------------
# Region / language resolution
# ---------------------------------------------------------------------------


def _resolve_region(options: dict[str, Any]) -> str:
    """Pick the launch region. Unknown values fall back to DEFAULT_REGION."""
    region = str(options.get("region", DEFAULT_REGION)).strip().lower()
    if region not in _REGION_CONSTANTS:
        logger.warning(
            "kuro unknown region %r, falling back to %r", region, DEFAULT_REGION,
        )
        return DEFAULT_REGION
    return region


def _language_candidates(
    region: str, request: HomeContentRequest
) -> list[str]:
    """Ordered list of language tokens to try on the given region.

    Precedence: explicit ``providerOptions['language']`` > ``request.locale``
    > region default. The first language is always the one Kuro ships
    on that region; the second is the cross-region fallback.
    """
    options = request.provider_options or {}
    if "language" in options:
        return [str(options["language"])]

    locale = (request.locale or "").strip().lower()
    if region == "cn":
        if locale.startswith("en"):
            return [DEFAULT_LANGUAGE_OS, DEFAULT_LANGUAGE_CN]
        return [DEFAULT_LANGUAGE_CN, DEFAULT_LANGUAGE_OS]
    # OS region ships en.json only.
    return [DEFAULT_LANGUAGE_OS]


def _format_candidates(candidates: list[str]) -> str:
    return " / ".join(repr(c) for c in candidates)


# ---------------------------------------------------------------------------
# Parsing helpers
# ---------------------------------------------------------------------------


def _build_background(payload: dict[str, Any]) -> Optional[HomeBackground]:
    video_url = payload.get("backgroundFile")
    first_frame = payload.get("firstFrameImage")
    # Prefer the video. Drop candidates whose host is not on the allow-list.
    if _allowed(video_url):
        return HomeBackground(
            video_url=video_url,
            image_url=first_frame if _allowed(first_frame) else None,
            local_path=None,
        )
    if _allowed(first_frame):
        return HomeBackground(video_url=None, image_url=first_frame, local_path=None)
    return None


def _build_banners(payload: dict[str, Any]) -> list[HomeBanner]:
    raw = payload.get("slideshow") or []
    banners: list[HomeBanner] = []
    for index, item in enumerate(raw):
        if not isinstance(item, dict):
            continue
        image_url = item.get("url")
        if not _allowed(image_url):
            continue
        banners.append(HomeBanner(
            id=_banner_id(item, index),
            title=str(item.get("carouselNotes") or "") or None,
            image_url=image_url,
            local_path=None,
            target_url=item.get("jumpUrl") if isinstance(item.get("jumpUrl"), str) else None,
        ))
    return banners


def _banner_id(item: dict[str, Any], index: int) -> str:
    md5 = item.get("md5")
    if isinstance(md5, str) and md5:
        return f"kuro-slide-{md5}"
    return f"kuro-slide-{index}"


def _build_news_items(payload: dict[str, Any]) -> list[HomeNewsItem]:
    guidance = payload.get("guidance")
    if not isinstance(guidance, dict):
        return []
    items: list[HomeNewsItem] = []
    for category, raw_category_key in (
        ("活动", "activity"),
        ("公告", "notice"),
        ("资讯", "news"),
    ):
        bucket = guidance.get(raw_category_key)
        if not isinstance(bucket, dict):
            continue
        if bucket.get("functionSwitch") in (0, "0"):
            continue
        contents = bucket.get("contents") or []
        if not isinstance(contents, list):
            continue
        for index, item in enumerate(contents):
            if not isinstance(item, dict):
                continue
            title = str(item.get("content") or "").strip()
            if not title:
                continue
            items.append(HomeNewsItem(
                id=f"kuro-{raw_category_key}-{index}",
                category=category,
                title=title,
                summary=None,
                image_url=None,
                target_url=item.get("jumpUrl") if isinstance(item.get("jumpUrl"), str) else None,
                published_at=_parse_mmdd(item.get("time")),
            ))
    return items


def _parse_mmdd(value: Any) -> Optional[datetime]:
    """Kuro news entries carry ``time: "MM-DD"`` (no year). Stamp the
    current year so the C# UI can sort consistently. Returns None when
    the value is missing or malformed.
    """
    if not isinstance(value, str):
        return None
    text = value.strip()
    if len(text) != 5 or text[2] != "-":
        return None
    try:
        month = int(text[0:2])
        day = int(text[3:5])
        return datetime(datetime.now().year, month, day)
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