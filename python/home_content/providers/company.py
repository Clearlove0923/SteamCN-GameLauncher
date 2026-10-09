"""Company-facing Providers; source adapters retain their legacy IDs."""

from __future__ import annotations

import os
from pathlib import Path

from ..game_source_registry import get_game_source_by_id
from ..models import HomeBackground
from .hoyoplay_json import HoYoPlayJsonProvider
from .hypergryph_batch import HypergryphBatchProvider
from .kuro_launcher import KuroLauncherProvider
from .netease_static_cms import NetEaseStaticCmsProvider
from .nextjs_data import NextJsDataProvider
from .perfect_world_hybrid import PerfectWorldHybridProvider


class MiHoYoProvider(HoYoPlayJsonProvider):
    provider_id = "mihoyo"

    async def fetch(self, request):
        # A company owns several games: never show the first HoYoPlay entry
        # when neither the game ID nor its source options select one.
        source = get_game_source_by_id(request.game_id)
        if source is None or source.provider_id != self.provider_id:
            raise ValueError(f"Unknown miHoYo game: {request.game_id}")
        expected_biz = source.provider_options["gameBiz"]
        supplied_biz = (request.provider_options or {}).get("gameBiz")
        if supplied_biz and supplied_biz != expected_biz:
            raise ValueError(f"miHoYo gameBiz does not match game: {request.game_id}")
        request = request.model_copy(update={
            "provider_options": {**source.provider_options, **(request.provider_options or {})},
        })
        return await super().fetch(request)


class KuroProvider(KuroLauncherProvider):
    provider_id = "kuro"


class HypergryphProvider(HypergryphBatchProvider):
    provider_id = "hypergryph"


class PerfectWorldProvider(PerfectWorldHybridProvider):
    provider_id = "perfect-world"


class PaperGamesProvider(NextJsDataProvider):
    provider_id = "papergames"

    async def fetch(self, request):
        """Combine the official launcher background with website news.

        The Next.js site's ``pc first-screen video`` is a marketing-site asset
        and is not the animation shown by the Windows launcher. The launcher
        keeps its currently selected WebM under LocalAppData; use that local
        asset when available and never present the unrelated website video as
        a launcher background.
        """

        source = get_game_source_by_id(request.game_id)
        if source is None or source.provider_id != self.provider_id:
            raise ValueError(f"Unknown Papergames game: {request.game_id}")
        request = request.model_copy(update={
            "provider_options": {**source.provider_options, **(request.provider_options or {})},
        })
        content = await super().fetch(request)
        launcher_background = _find_infinity_nikki_launcher_background(
            request.provider_options
        )
        content.background = (
            HomeBackground(local_path=str(launcher_background))
            if launcher_background is not None
            else None
        )
        return content


_WEBM_MAGIC = b"\x1a\x45\xdf\xa3"
_MIN_LAUNCHER_VIDEO_BYTES = 1 * 1024 * 1024
_MAX_LAUNCHER_VIDEO_BYTES = 512 * 1024 * 1024


def _find_infinity_nikki_launcher_background(options) -> Path | None:
    """Return the newest valid WebM cached by the official CN launcher.

    ``launcherCacheDirectory`` is an injectable per-game option used by tests
    and non-default installations. The default follows the launcher's stable
    LocalAppData cache location without embedding a user profile path.
    """

    configured = options.get("launcherCacheDirectory")
    if isinstance(configured, str) and configured.strip():
        cache_directory = Path(os.path.expandvars(configured.strip()))
    else:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            return None
        cache_directory = (
            Path(local_app_data)
            / "InfinityNikki Launcher"
            / "cache"
            / "images"
        )

    try:
        candidates = sorted(
            cache_directory.glob("*.webm"),
            key=lambda path: (path.stat().st_mtime_ns, path.name.lower()),
            reverse=True,
        )
    except OSError:
        return None

    for path in candidates:
        try:
            if path.is_symlink() or not path.is_file():
                continue
            size = path.stat().st_size
            if not _MIN_LAUNCHER_VIDEO_BYTES <= size <= _MAX_LAUNCHER_VIDEO_BYTES:
                continue
            with path.open("rb") as stream:
                if stream.read(len(_WEBM_MAGIC)) != _WEBM_MAGIC:
                    continue
            return path.resolve(strict=True)
        except OSError:
            continue
    return None


class NetEaseProvider(NetEaseStaticCmsProvider):
    provider_id = "netease"
