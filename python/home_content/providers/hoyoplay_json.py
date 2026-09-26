"""HoYoPlay JSON provider for miHoYo / HoYoverse launchers.

Discovery surface
-----------------
Primary endpoint — HoYoPlay's unified launcher info API. CN and global each
have their own domain; both accept ``launcher_id`` and ``language`` query
parameters and return an array of games with their launcher backgrounds.

* CN:    https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo
* OS:    https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getAllGameBasicInfo

The endpoint is part of the HoYoPlay desktop client and is not a public,
documented API. It is intentionally not described here as a stable SLA;
schemas have been observed to change without notice. The launcher id
``jGHBHlcOq1`` is for the Chinese mainland launcher and ``VYTpXlbWo8`` for
the global launcher; both have been sampled on 2026-09-14.

Fallback surface
----------------
When the primary endpoint fails (HTTP error, retcode != 0, empty result,
or game not present), this provider raises and lets the Worker's sample
provider take over. Future work: wire a per-game legacy
``<host>/mdk/launcher/api/content`` fallback when the primary endpoint is
fully down.

Per-game mapping
----------------
``providerOptions.gameBiz`` selects the game within the launcher; when it
is missing the provider returns the first available game's background.
Valid observed values: ``hyg_cn``, ``abc_cn``, ``nap_cn``, ``hkrpg_cn``,
``hk4e_cn``, ``bh3_cn`` (and their OS equivalents).

Candidate validation
--------------------
Background candidates are kept only when their URL host belongs to the
known static asset domains. Any candidate whose host does not match the
allow-list is dropped silently; the provider still returns the next
acceptable candidate or an empty ``background`` block so the client can
fall back to its static background.
"""

from __future__ import annotations

import logging
import re
from datetime import datetime, timedelta, timezone
from typing import Any, Iterable, Optional

import httpx

from ..models import (
    HomeBackground,
    HomeBanner,
    HomeContent,
    HomeContentError,
    HomeContentRequest,
    HomeNewsItem,
)
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.hoyoplay_json")


CN_BASE = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api"
OS_BASE = "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api"

LAUNCHER_ID_CN = "jGHBHlcOq1"
LAUNCHER_ID_OS = "VYTpXlbWo8"

# Candidate URLs must come from one of these static-asset domains. Any
# other host is dropped to keep background downloads on the launcher's
# CDN and to refuse unknown redirects.
ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".mihoyo.com",
    ".hoyoverse.com",
    ".yuanshen.com",
    ".bh3.com",
    ".honkaistarrail.com",
    ".zenlesszonezero.com",
    ".miyoushe.com",
)

DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (HoYoPlayJsonProvider)",
    "Accept": "application/json",
}


class HoYoPlayJsonProvider(HomeContentProvider):
    provider_id = "hoyoplay-json"

    def __init__(
        self,
        client: Optional[httpx.AsyncClient] = None,
        timeout: httpx.Timeout = DEFAULT_TIMEOUT,
    ) -> None:
        # Caller owns the lifecycle. The unit tests pass a mock-backed
        # client; the FastAPI Worker passes a long-lived client per
        # request; None means the Provider creates and disposes its own
        # short-lived client for this fetch.
        self._client_factory = client
        self._timeout = timeout

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        options = request.provider_options or {}
        region = str(options.get("region", "cn")).lower()
        launcher_id = str(options.get("launcher_id", "") or _default_launcher_id(region))
        language = str(options.get("language", request.locale or "zh-cn"))
        game_biz = str(options.get("gameBiz", "") or _game_biz_from_game_id(request.game_id))

        base = CN_BASE if region == "cn" else OS_BASE
        url = f"{base}/getAllGameBasicInfo"

        errors: list[HomeContentError] = []
        try:
            payload = await self._fetch_json(url, launcher_id=launcher_id, language=language)
        except httpx.HTTPError as error:
            logger.warning("hoyoplay primary fetch failed: %s", error)
            raise

        game_entry = _pick_game_entry(payload, game_biz)
        background = _pick_background(payload, game_biz)
        if background is None:
            errors.append(HomeContentError(
                code="hoyoplay_empty",
                message=f"HoYoPlay 未返回 {game_biz or '任何游戏'} 的背景。",
                recoverable=True,
            ))

        banners: list[HomeBanner] = []
        news: list[HomeNewsItem] = []
        upstream_game_id = str((game_entry or {}).get("game", {}).get("id", "")).strip()
        if upstream_game_id:
            try:
                content_payload = await self._fetch_json(
                    f"{base}/getGameContent",
                    launcher_id=launcher_id,
                    language=language,
                    extra_params={"game_id": upstream_game_id},
                )
                content = content_payload.get("data", {}).get("content", {})
                if isinstance(content, dict):
                    banners = _build_banners(content)
                    news = _build_news_items(content)
            except (httpx.HTTPError, ValueError) as error:
                # Background and content are independent upstream surfaces. Keep the
                # working background when the secondary content endpoint is unavailable.
                logger.warning("hoyoplay content fetch failed: %s", error)

        return HomeContent(
            background=background,
            banners=banners,
            news=news,
            update_info=None,
        )

    async def _fetch_json(
        self,
        url: str,
        *,
        launcher_id: str,
        language: str,
        extra_params: Optional[dict[str, str]] = None,
    ) -> dict[str, Any]:
        params = {"launcher_id": launcher_id, "language": language}
        params.update(extra_params or {})
        if self._client_factory is not None:
            response = await self._client_factory.get(url, params=params)
        else:
            async with httpx.AsyncClient(timeout=self._timeout, headers=DEFAULT_HEADERS) as client:
                response = await client.get(url, params=params)
        response.raise_for_status()
        data = response.json()
        if not isinstance(data, dict):
            raise ValueError("HoYoPlay 端点未返回 JSON 对象。")
        # retcode is part of the upstream envelope; treat absent or zero
        # as success. Some fixtures and proxies strip the field.
        retcode = data.get("retcode")
        if retcode is not None and retcode not in (0, "0"):
            raise ValueError(f"HoYoPlay 端点返回 retcode={retcode!r}。")
        return data


def _pick_game_entry(payload: dict[str, Any], game_biz: str) -> Optional[dict[str, Any]]:
    games: Iterable[dict[str, Any]] = (
        payload.get("data", {}).get("game_info_list", []) or []
    )
    entry = next(
        (g for g in games if str(g.get("game", {}).get("biz", "")).lower() == game_biz.lower()),
        None,
    )
    if entry is None and games:
        # Caller did not pin a game (no game_id -> no biz mapping); use
        # the first available entry so the client still shows something.
        entry = games[0]
    return entry


def _pick_background(payload: dict[str, Any], game_biz: str) -> Optional[HomeBackground]:
    entry = _pick_game_entry(payload, game_biz)
    if entry is None:
        return None

    candidates = entry.get("backgrounds") or []
    if not candidates:
        return None

    # Prefer a background that carries a video URL. Fall back to the
    # first background with a non-empty image URL.
    video_first = next(
        (b for b in candidates if _allowed(b.get("video", {}).get("url"))),
        None,
    )
    if video_first is not None:
        video_url = video_first.get("video", {}).get("url")
        image_url = _first_allowed([
            video_first.get("background", {}).get("url"),
            (video_first.get("icon") or {}).get("url"),
        ])
        return HomeBackground(video_url=video_url, image_url=image_url, local_path=None)

    image_only = next(
        (b for b in candidates if _allowed(b.get("background", {}).get("url"))),
        None,
    )
    if image_only is None:
        return None
    return HomeBackground(
        video_url=None,
        image_url=image_only.get("background", {}).get("url"),
        local_path=None,
    )


def _build_banners(content: dict[str, Any]) -> list[HomeBanner]:
    result: list[HomeBanner] = []
    for index, row in enumerate(content.get("banners") or []):
        if not isinstance(row, dict):
            continue
        image = row.get("image") or {}
        image_url = image.get("url") if isinstance(image, dict) else None
        if not _allowed(image_url):
            continue
        target_url = image.get("link") if isinstance(image, dict) else None
        result.append(HomeBanner(
            id=f"hoyo-banner-{row.get('id') or index}",
            title=str(row.get("title") or "") or None,
            image_url=image_url,
            target_url=target_url if _allowed(target_url) else None,
        ))
    return result


def _build_news_items(content: dict[str, Any]) -> list[HomeNewsItem]:
    category_map = {
        "POST_TYPE_ACTIVITY": "活动",
        "POST_TYPE_ANNOUNCE": "公告",
        "POST_TYPE_INFO": "资讯",
    }
    result: list[HomeNewsItem] = []
    for index, row in enumerate(content.get("posts") or []):
        if not isinstance(row, dict):
            continue
        title = str(row.get("title") or "").strip()
        if not title:
            continue
        target_url = row.get("link")
        raw_type = str(row.get("type") or "").strip()
        result.append(HomeNewsItem(
            id=f"hoyo-post-{row.get('id') or index}",
            title=title,
            category=category_map.get(raw_type, raw_type or "资讯"),
            target_url=target_url if _allowed(target_url) else None,
            published_at=_parse_mmdd(row.get("date")),
        ))
    return result


def _parse_mmdd(value: Any, *, now: Optional[datetime] = None) -> Optional[datetime]:
    match = re.fullmatch(r"\s*(\d{1,2})/(\d{1,2})\s*", str(value or ""))
    if match is None:
        return None
    reference = now or datetime.now(timezone.utc)
    try:
        parsed = datetime(reference.year, int(match.group(1)), int(match.group(2)), tzinfo=timezone.utc)
    except ValueError:
        return None
    if parsed - reference > timedelta(days=45):
        parsed = parsed.replace(year=reference.year - 1)
    return parsed


def _first_allowed(urls: Iterable[Optional[str]]) -> Optional[str]:
    for url in urls:
        if _allowed(url):
            return url
    return None


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not lowered.startswith("https://"):
        return False
    host = lowered.split("//", 1)[1].split("/", 1)[0]
    return any(host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)


def _default_launcher_id(region: str) -> str:
    return LAUNCHER_ID_CN if region == "cn" else LAUNCHER_ID_OS


def _game_biz_from_game_id(game_id: str) -> str:
    """Map a stable game identifier to a HoYoPlay ``biz`` value.

    C# sends the Steam AppID. Provider callers may also use the historical
    HoYo prefix, so both forms remain supported. Unknown identifiers return
    an empty value and the provider falls back to the first available game.
    """

    normalized = (game_id or "").strip().lower()
    mapping = {
        "1671200": "bh3_cn",
        "4162040": "nap_cn",
        "bh3": "bh3_cn",
        "hk4e": "hk4e_cn",
        "hkrpg": "hkrpg_cn",
        "nap": "nap_cn",
        "hyg": "hyg_cn",
        "abc": "abc_cn",
    }
    for prefix, biz in mapping.items():
        if normalized.startswith(prefix):
            return biz
    return ""
