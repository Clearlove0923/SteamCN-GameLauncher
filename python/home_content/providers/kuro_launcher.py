"""Kuro Games launcher Provider for Wuthering Waves.

Discovery surface
-----------------
The official Kuro launcher serves a three-layer JSON pipeline under
``prod-alicdn-gamestarter.kurogame.com``. The Provider walks it in order:

1. ``launcher-config`` (``.../launcher/<APP>/<GAME>/index.json``) — returns
   ``functionCode.background``, the dynamic hash that selects the active
   background JSON for the current version.
2. ``wallpapers-slogan`` (``.../<APP>/<GAME>/background/<HASH>/<LANG>.json``)
   — returns ``backgroundFile`` (MP4), ``firstFrameImage`` (WebP) and
   ``slogan`` (PNG title overlay).
3. ``news-notices`` (``.../<APP>/<GAME>/information/<LANG>.json``) —
   returns ``guidance.{activity,notice,news}`` for the news list and
   ``slideshow`` for the banner carousel.

This endpoint chain is shipped inside the Kuro desktop launcher and is
not a public API. Sampled on 2026-09-14 against ``en.json``. The Kuro
launcher only ships English-language content under this URL pattern;
requesting e.g. ``zh-cn.json`` returns an empty body. Adding more
languages requires either a separate launcher build or Kuro publishing
per-language JSON files.

Fallback surface
----------------
On HTTP errors the Provider raises and the Worker's envelope handler
converts the failure into ``envelope.errors``. The FastAPI sample
provider does not cover Kuro so the Worker does not silently fall back
to anything when Kuro fails.

Per-game mapping
----------------
The constant triple below (``appId``, ``appKey``, ``gameId``) is read
from the launcher config (``KRApp.conf``). The Provider keeps the
defaults for Wuthering Waves Global (G153); Punishing Gray Raven and
other Kuro titles share the same endpoint shape but a different triple,
which callers pass via ``providerOptions``.
"""

from __future__ import annotations

import logging
from typing import Any, Optional

import httpx

from ..models import HomeBackground, HomeBanner, HomeContent, HomeContentRequest, HomeNewsItem
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.kuro_launcher")


CDN_BASE = "https://prod-alicdn-gamestarter.kurogame.com/launcher"

# Wuthering Waves Global — sampled 2026-09-14.
DEFAULT_APP_ID = "50004"
DEFAULT_APP_KEY = "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c"
DEFAULT_GAME_ID = "G153"
DEFAULT_LANGUAGE = "en"

ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".kurogame.com",
    ".kurogames.com",
    ".aki-game.net",
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
        app_id = str(options.get("appId", DEFAULT_APP_ID))
        app_key = str(options.get("appKey", DEFAULT_APP_KEY))
        game_id = str(options.get("gameId", DEFAULT_GAME_ID))
        language = str(options.get("language", DEFAULT_LANGUAGE))

        config_url = f"{CDN_BASE}/launcher/{app_id}_{app_key}/{game_id}/index.json"
        config = await self._fetch_json(config_url)

        bg_hash = str(config.get("functionCode", {}).get("background", "")).strip()
        if not bg_hash:
            raise ValueError("Kuro 启动器配置缺少 functionCode.background。")

        wallpaper_url = (
            f"{CDN_BASE}/{app_id}_{app_key}/{game_id}/background/{bg_hash}/{language}.json"
        )
        wallpaper = await self._fetch_json(wallpaper_url)

        news_url = f"{CDN_BASE}/{app_id}_{app_key}/{game_id}/information/{language}.json"
        news = await self._fetch_json(news_url)

        background = _build_background(wallpaper)
        banners = _build_banners(news)
        news_items = _build_news_items(news)

        return HomeContent(
            background=background,
            banners=banners,
            news=news_items,
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
                published_at=None,
            ))
    return items


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not (lowered.startswith("http://") or lowered.startswith("https://")):
        return False
    host = lowered.split("//", 1)[1].split("/", 1)[0]
    return any(host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)