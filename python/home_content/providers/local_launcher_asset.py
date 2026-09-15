"""LocalLauncherAssetProvider — 本地启动器资源探测。

Discovery surface
-----------------
This provider is the local-only counterpart to every network source
adapter. It is **not** a generic filesystem scanner — it walks a single
launcher install root and surfaces a normalised set of canonical
asset paths the C# UI knows how to render:

  * ``<installDir>/bg.mp4`` (preferred) — background video the launcher
    ships. The C# side feeds the URL straight into its native media
    control, so the value is wrapped as a ``file://`` URI rather than
    an absolute path. ``local_path`` is populated alongside it for
    consumers that prefer raw filesystem paths.
  * ``<installDir>/bg.jpg`` (fallback) — static background image when
    the launcher ships no video. The provider tries a small list of
    common names so 完美世界 / 库洛 / 鹰角 / 叠纸 launchers all work
    without per-game overrides.
  * ``<installDir>/config.json`` (optional) — a tiny JSON manifest
    that may carry the launcher's current ``version``, ``downloadUrl``
    and ``announcementTitle``. We surface those into ``HomeUpdateInfo``
    so the UI can show a "newer version available" prompt without
    hitting a separate endpoint.

The provider never makes HTTP requests and never writes to disk. The
launcher install root must be supplied by the C# side (the launcher
self-detection lives in C# so this Python worker does not have to
guess registry / Steam library paths on Windows).

### Per-game mapping

Defaults match the layout observed across the launchers we already
integrate (完美世界异环 / 库洛鸣潮 / 鹰角终末地 / 叠纸无限暖暖).
``providerOptions`` accepts:

  * ``installDir``        — absolute path to the launcher install root
                            (e.g. ``C:/Program Files/Where Winds Meet``).
                            Required.
  * ``videoFileNames``    — ordered list of background video candidates
                            (default ``["bg.mp4", "bg.webm"]``). The
                            provider picks the first one that exists.
  * ``imageFileNames``    — ordered list of background image candidates
                            (default ``["bg.jpg", "bg.png",
                            "background.jpg", "background.png"]``).
  * ``configFileName``    — basename of the launcher config JSON
                            (default ``"config.json"``). Set to
                            ``""`` to disable config-driven
                            ``HomeUpdateInfo``.
  * ``maxDepth``          — how deep the provider is allowed to look
                            for background assets (default ``1``).
                            ``1`` = inspect the install root only;
                            ``2`` = also try immediate subdirectories
                            named ``video/`` or ``assets/``.

Verification
------------
Sampled 2026-09-15 against a synthetic launcher layout mirroring
perfect-world / kurogame / hypergryph / papergames launchers. There
is no production deployment yet — the Provider is exercised by
``pytest`` with the ``os`` and ``pathlib`` modules patched so no
real filesystem access happens during tests.
"""

from __future__ import annotations

import json
import logging
import os
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any, Iterable, Optional
from urllib.parse import quote

from ..models import (
    HomeBackground,
    HomeContent,
    HomeContentRequest,
    HomeUpdateInfo,
)
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.local_launcher_asset")


# ----- Defaults -----------------------------------------------------------

DEFAULT_VIDEO_FILE_NAMES: tuple[str, ...] = ("bg.mp4", "bg.webm")
DEFAULT_IMAGE_FILE_NAMES: tuple[str, ...] = (
    "bg.jpg",
    "bg.png",
    "background.jpg",
    "background.png",
)
DEFAULT_CONFIG_FILE_NAME = "config.json"
DEFAULT_MAX_DEPTH = 2  # allow ``assets/`` / ``video/`` / ``media/`` subdirectories

DEFAULT_TIMEOUT_SECONDS = 1.0  # read-only; keep it tight


# ---------------------------------------------------------------------------
# Provider
# ---------------------------------------------------------------------------


class LocalLauncherAssetProvider(HomeContentProvider):
    """Surface the canonical local assets shipped by a launcher."""

    provider_id = "local-launcher-asset"

    def __init__(
        self,
        *,
        file_reader: Optional[Any] = None,
        exists_checker: Optional[Any] = None,
    ) -> None:
        """Build a LocalLauncherAssetProvider.

        ``file_reader`` and ``exists_checker`` are hooks used by tests
        to swap the real ``Path.read_text`` / ``Path.is_file`` with a
        in-memory fixture without touching the actual filesystem.
        Both default to the matching ``pathlib.Path`` methods.
        """
        self._file_reader = file_reader or _default_file_reader
        self._exists_checker = exists_checker or _default_exists_checker

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        options = request.provider_options or {}
        install_dir = options.get("installDir")
        if not isinstance(install_dir, str) or not install_dir.strip():
            logger.warning(
                "LocalLauncherAssetProvider requires providerOptions.installDir; got %r",
                install_dir,
            )
            return HomeContent(
                background=HomeBackground(video_url=None, image_url=None, local_path=None),
                banners=[],
                news=[],
                update_info=None,
            )

        video_names = _coerce_str_list(options.get("videoFileNames"), DEFAULT_VIDEO_FILE_NAMES)
        image_names = _coerce_str_list(options.get("imageFileNames"), DEFAULT_IMAGE_FILE_NAMES)
        config_name = options.get("configFileName", DEFAULT_CONFIG_FILE_NAME)
        if isinstance(config_name, str):
            config_name = config_name.strip() or None
        else:
            config_name = None
        max_depth = int(options.get("maxDepth") or DEFAULT_MAX_DEPTH)

        root = Path(install_dir)
        background = _build_background(
            root,
            video_names=video_names,
            image_names=image_names,
            max_depth=max_depth,
            exists_checker=self._exists_checker,
        )
        update_info = _build_update_info(
            root,
            config_name=config_name,
            file_reader=self._file_reader,
        )

        return HomeContent(
            background=background,
            banners=[],
            news=[],
            update_info=update_info,
        )


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _coerce_str_list(value: Any, default: Iterable[str]) -> list[str]:
    if value is None:
        return list(default)
    if isinstance(value, str):
        # Allow the caller to pass a single comma-separated string.
        return [item.strip() for item in value.split(",") if item.strip()] or list(default)
    if isinstance(value, (list, tuple)):
        out = [str(item).strip() for item in value if str(item).strip()]
        return out or list(default)
    return list(default)


def _default_file_reader(path: Path) -> str:
    # read_text without an encoding argument opens in the platform
    # default encoding; the launcher config files we care about are
    # always UTF-8 so be explicit.
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def _default_exists_checker(path: Path) -> bool:
    return path.is_file()


def _find_first_existing(
    candidates: Iterable[Path],
    exists_checker: Any,
) -> Optional[Path]:
    for candidate in candidates:
        try:
            if exists_checker(candidate):
                return candidate
        except OSError:
            continue
    return None


def _candidate_paths(root: Path, names: Iterable[str], max_depth: int) -> Iterable[Path]:
    """Walk the install root to a bounded depth, yielding ``root/name``
    plus the common ``assets/`` and ``video/`` subdirectory variants.

    The probe is intentionally cheap: we never recurse more than
    ``max_depth`` levels deep and we only consider a fixed set of
    well-known subdirectory names so a stray ``.git`` or ``logs/``
    folder does not pollute the search.
    """
    seen: set[Path] = set()
    subdirs = [Path("")]
    if max_depth >= 2:
        subdirs.extend(Path(name) for name in ("assets", "video", "media"))
    for subdir in subdirs:
        for name in names:
            candidate = root / subdir / name
            if candidate in seen:
                continue
            seen.add(candidate)
            yield candidate


def _build_background(
    root: Path,
    *,
    video_names: Iterable[str],
    image_names: Iterable[str],
    max_depth: int,
    exists_checker: Any,
) -> HomeBackground:
    """Pick the first existing background video / image under ``root``."""
    video_path: Optional[Path] = None
    image_path: Optional[Path] = None

    video_candidates = list(_candidate_paths(root, video_names, max_depth))
    video_path = _find_first_existing(video_candidates, exists_checker)

    # Skip the video's own directory for the image probe so we don't
    # match the ``bg.jpg`` poster of an ``assets/`` subdir as the
    # video fallback.
    image_roots = [root, *(root / sub for sub in ("assets", "video", "media") if max_depth >= 2)]
    image_candidates: list[Path] = []
    for image_root in image_roots:
        image_candidates.extend(image_root / name for name in image_names)
    image_candidates = list(dict.fromkeys(image_candidates))  # dedupe preserving order
    if video_path is not None:
        image_path = _find_first_existing(
            [c for c in image_candidates if c != video_path],
            exists_checker,
        )
    else:
        image_path = _find_first_existing(image_candidates, exists_checker)

    return HomeBackground(
        video_url=_path_to_file_url(video_path) if video_path else None,
        image_url=_path_to_file_url(image_path) if image_path else None,
        local_path=video_path.as_posix() if video_path else None,
    )


def _path_to_file_url(path: Path) -> str:
    """Convert an absolute ``Path`` to a ``file://`` URI.

    On Windows the URI uses the ``/C:/...`` drive-letter form so
    third-party media decoders that only accept POSIX file URLs still
    accept it. POSIX paths go through ``as_uri`` so we always pick
    the platform-correct spelling.
    """
    absolute = path.resolve()
    try:
        return absolute.as_uri()
    except ValueError:
        # ``PureWindowsPath.as_uri`` raises on Python 3.10 when the
        # path contains characters that need percent-encoding (e.g.
        # ``#``). Fall back to the manual spelling so we never crash
        # the Worker just because the launcher happens to install
        # itself under a directory with a hash mark in the name.
        is_windows = isinstance(absolute, PureWindowsPath)
        path_str = str(absolute)
        if is_windows:
            # ``C:\foo\bar`` → ``/C:/foo/bar``
            drive, _, tail = path_str.partition(":")
            head = "/" + drive + ":/" if drive else "/"
            tail = tail.lstrip("/\\")
            path_str = head + tail
        quoted = quote(path_str, safe="/:@!$&'()*+,;=-._~")
        return f"file:///{quoted.lstrip('/')}"


def _build_update_info(
    root: Path,
    *,
    config_name: Optional[str],
    file_reader: Any,
) -> Optional[HomeUpdateInfo]:
    """Parse the launcher's ``config.json`` if it exists."""
    if not config_name:
        return None
    config_path = root / config_name
    try:
        raw = file_reader(config_path)
    except (OSError, FileNotFoundError, KeyError):
        # ``KeyError`` covers the in-memory FakeFS used by tests when
        # the config file does not exist; ``OSError`` /
        # ``FileNotFoundError`` cover the real filesystem path.
        return None
    except (ValueError, json.JSONDecodeError) as exc:
        logger.warning(
            "LocalLauncherAssetProvider: %s is not valid JSON (%s)", config_path, exc
        )
        return None
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as exc:
        logger.warning(
            "LocalLauncherAssetProvider: %s is not valid JSON (%s)", config_path, exc
        )
        return None
    if not isinstance(payload, dict):
        return None
    version = payload.get("version") if isinstance(payload.get("version"), str) else None
    title = payload.get("title") if isinstance(payload.get("title"), str) else None
    summary = payload.get("summary") if isinstance(payload.get("summary"), str) else None
    download_url = (
        payload.get("downloadUrl")
        if isinstance(payload.get("downloadUrl"), str)
        else None
    )
    published_at = _parse_iso8601(payload.get("publishedAt"))
    if not any([version, title, summary, download_url, published_at]):
        return None
    return HomeUpdateInfo(
        version=version,
        title=title,
        summary=summary,
        target_url=download_url,
        published_at=published_at,
    )


def _parse_iso8601(value: Any) -> Optional[datetime]:
    if not isinstance(value, str) or not value.strip():
        return None
    raw = value.strip()
    candidate = raw.replace("Z", "+00:00") if raw.endswith("Z") else raw
    try:
        parsed = datetime.fromisoformat(candidate)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


__all__ = [
    "LocalLauncherAssetProvider",
    "_path_to_file_url",
    "_build_background",
    "_build_update_info",
]