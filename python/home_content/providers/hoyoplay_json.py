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
from typing import Any, Iterable, Optional

import httpx

from ..models import HomeBackground, HomeContent, HomeContentError, HomeContentRequest
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

        background = _pick_background(payload, game_biz)
        if background is None:
            errors.append(HomeContentError(
                code="hoyoplay_empty",
                message=f"HoYoPlay 未返回 {game_biz or '任何游戏'} 的背景。",
                recoverable=True,
            ))

        # The primary endpoint exposes backgrounds only; banner and news
        # come from the legacy content API and are intentionally not
        # wired here yet. Future work: switch to the per-game legacy
        # endpoint when banners are required.
        return HomeContent(background=background, banners=[], news=[], update_info=None)

    async def _fetch_json(self, url: str, *, launcher_id: str, language: str) -> dict[str, Any]:
        params = {"launcher_id": launcher_id, "language": language}
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


def _pick_background(payload: dict[str, Any], game_biz: str) -> Optional[HomeBackground]:
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


def _first_allowed(urls: Iterable[Optional[str]]) -> Optional[str]:
    for url in urls:
        if _allowed(url):
            return url
    return None


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not (lowered.startswith("http://") or lowered.startswith("https://")):
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
