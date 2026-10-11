"""iDreamSky launcher assets for the Mainland China build of Calabiyau.

The current launcher stores its selected home animation in
``CalabiYau/ImageCache``.  This provider only probes that verified directory
relative to the executable/install paths supplied by the client; it never
recursively scans the game or exposes in-game cutscenes as launcher content.
"""

from __future__ import annotations

from pathlib import Path
from typing import Iterable

from ..models import HomeBackground, HomeContent, HomeContentRequest
from .base import HomeContentProvider


_MIN_VIDEO_BYTES = 256 * 1024
_MAX_VIDEO_BYTES = 512 * 1024 * 1024
_MAX_PARENT_LEVELS = 7


class IDreamSkyProvider(HomeContentProvider):
    provider_id = "idreamsky"

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        cache_directory = _find_image_cache(request)
        video = _newest_valid(cache_directory, ".mp4", _is_mp4) if cache_directory else None
        image = _newest_valid(cache_directory, ".jpg", _is_jpeg) if cache_directory else None
        if image is None and cache_directory is not None:
            image = _newest_valid(cache_directory, ".png", _is_png)
        return HomeContent(
            background=HomeBackground(
                local_path=str(video or image) if video or image else None,
                image_url=None,
                video_url=None,
            ) if video or image else None,
            banners=[],
            news=[],
            update_info=None,
        )


def _find_image_cache(request: HomeContentRequest) -> Path | None:
    for seed in _path_seeds(request):
        current = seed.parent if seed.suffix else seed
        for _ in range(_MAX_PARENT_LEVELS):
            for candidate in (current / "ImageCache", current / "CalabiYau" / "ImageCache"):
                try:
                    if candidate.is_dir():
                        return candidate.resolve(strict=True)
                except OSError:
                    pass
            parent = current.parent
            if parent == current:
                break
            current = parent
    return None


def _path_seeds(request: HomeContentRequest) -> Iterable[Path]:
    for value in (request.executable_path, request.install_directory):
        if isinstance(value, str) and value.strip():
            yield Path(value.strip())


def _newest_valid(directory: Path, suffix: str, validator) -> Path | None:
    try:
        candidates = sorted(
            directory.glob(f"*{suffix}"),
            key=lambda path: (path.stat().st_mtime_ns, path.name.casefold()),
            reverse=True,
        )
    except OSError:
        return None
    for path in candidates:
        try:
            if path.is_symlink() or not path.is_file():
                continue
            size = path.stat().st_size
            if suffix == ".mp4" and not _MIN_VIDEO_BYTES <= size <= _MAX_VIDEO_BYTES:
                continue
            with path.open("rb") as stream:
                header = stream.read(16)
            if validator(header):
                return path.resolve(strict=True)
        except OSError:
            continue
    return None


def _is_mp4(header: bytes) -> bool:
    return len(header) >= 12 and header[4:8] == b"ftyp"


def _is_jpeg(header: bytes) -> bool:
    return header.startswith(b"\xff\xd8\xff")


def _is_png(header: bytes) -> bool:
    return header.startswith(b"\x89PNG\r\n\x1a\n")


__all__ = ["IDreamSkyProvider"]
