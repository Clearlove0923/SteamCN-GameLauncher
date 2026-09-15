"""LocalLauncherAssetProvider unit tests driven by sanitized fixtures.

The fixtures under ``contracts/samples/launcher-local-config-*.json``
describe synthetic launcher install roots. The provider never
touches the real filesystem during tests — we inject
``file_reader`` and ``exists_checker`` hooks that map
``pathlib.Path`` instances onto an in-memory ``{str: str | bool}``
table. That lets us assert the exact paths the provider chose without
spinning up a tempdir per test.

The fixtures themselves model three real launcher conventions:

  * Windows (NIE / launcher.exe side-by-side assets)
  * macOS (.app bundle ``Contents/Resources``)
  * Linux (``/opt/<game>/assets/`` with ``.webm`` video)

and a single canonical ``config.json`` shape shared across them.
"""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any, Callable

import pytest

from home_content.models import HomeContentRequest
from home_content.providers.local_launcher_asset import (
    DEFAULT_CONFIG_FILE_NAME,
    DEFAULT_IMAGE_FILE_NAMES,
    DEFAULT_MAX_DEPTH,
    DEFAULT_VIDEO_FILE_NAMES,
    LocalLauncherAssetProvider,
    _build_background,
    _build_update_info,
    _coerce_str_list,
    _path_to_file_url,
)

SAMPLE_ROOT = Path(__file__).resolve().parents[2] / "contracts" / "samples"


# ---------------------------------------------------------------------------
# In-memory filesystem fake
# ---------------------------------------------------------------------------


class FakeFS:
    """Mimic the small surface of pathlib.Path the provider actually uses."""

    def __init__(self) -> None:
        # Map absolute path string → file content (str) or marker ``"__file__"``.
        self._files: dict[str, str] = {}

    def add_file(self, path: str, content: str = "") -> None:
        # Always resolve so the lookup key matches the
        # ``Path.resolve()``-normalised query path on every platform.
        self._files[self._normalise(str(Path(path).resolve()))] = content

    def has_file(self, path: Path) -> bool:
        return self._normalise(str(Path(str(path)).resolve())) in self._files

    def read_file(self, path: Path) -> str:
        return self._files[self._normalise(str(Path(str(path)).resolve()))]

    @staticmethod
    def _normalise(path_str: str) -> str:
        # Normalise separators and case on Windows so tests can pass
        # ``Path("C:/Program Files/.../bg.mp4")`` interchangeably.
        # The provider always sees ``Path.resolve()``-normalised paths,
        # so on Windows ``"/opt/game"`` becomes ``"C:/opt/game"`` — we
        # accept either spelling.
        p = path_str.replace("\\", "/")
        return p.lower() if os.name == "nt" else p


import os  # placed after FakeFS so the static helper above can use it


def _provider_for(fs: FakeFS) -> LocalLauncherAssetProvider:
    """Build a Provider with the FakeFS swapped in."""
    return LocalLauncherAssetProvider(
        file_reader=fs.read_file,
        exists_checker=fs.has_file,
    )


def _request(options: dict[str, Any] | None = None) -> HomeContentRequest:
    return HomeContentRequest(
        schema_version=1,
        request_id="lla-1",
        game_id="yysls",
        provider_id="local-launcher-asset",
        locale="zh-CN",
        provider_options=options or {},
    )


def _load(name: str) -> dict[str, Any]:
    return json.loads((SAMPLE_ROOT / name).read_text(encoding="utf-8"))


# ---------------------------------------------------------------------------
# Static constants
# ---------------------------------------------------------------------------


def test_provider_id_constant() -> None:
    provider = LocalLauncherAssetProvider()
    assert provider.provider_id == "local-launcher-asset"


def test_default_constants() -> None:
    assert DEFAULT_VIDEO_FILE_NAMES == ("bg.mp4", "bg.webm")
    assert DEFAULT_IMAGE_FILE_NAMES[:2] == ("bg.jpg", "bg.png")
    assert DEFAULT_CONFIG_FILE_NAME == "config.json"
    assert DEFAULT_MAX_DEPTH == 2


# ---------------------------------------------------------------------------
# _coerce_str_list
# ---------------------------------------------------------------------------


def test_coerce_str_list_returns_default_for_none() -> None:
    assert _coerce_str_list(None, ["x"]) == ["x"]


def test_coerce_str_list_splits_comma_separated() -> None:
    assert _coerce_str_list("bg.mp4, bg.webm ,", ["x"]) == ["bg.mp4", "bg.webm"]


def test_coerce_str_list_passes_list_through() -> None:
    assert _coerce_str_list(["bg.mp4", "bg.webm"], ["x"]) == ["bg.mp4", "bg.webm"]


def test_coerce_str_list_returns_default_for_blank_input() -> None:
    assert _coerce_str_list([], ["x"]) == ["x"]
    assert _coerce_str_list("", ["x"]) == ["x"]
    assert _coerce_str_list(123, ["x"]) == ["x"]


# ---------------------------------------------------------------------------
# _path_to_file_url
# ---------------------------------------------------------------------------


def test_path_to_file_url_posix() -> None:
    if os.name == "nt":
        pytest.skip("POSIX-only assertion skipped on Windows")
    url = _path_to_file_url(Path("/opt/where-winds-meet/bg.mp4"))
    assert url == "file:///opt/where-winds-meet/bg.mp4"


def test_path_to_file_url_handles_spaces() -> None:
    url = _path_to_file_url(Path("/opt/Where Winds Meet/bg.mp4"))
    assert "Where%20Winds%20Meet" in url or "Where Winds Meet" in url


def test_path_to_file_url_uses_drive_letter_form_on_windows() -> None:
    if os.name != "nt":
        pytest.skip("Windows-only assertion skipped on POSIX")
    url = _path_to_file_url(Path("C:/Program Files/Where Winds Meet/bg.mp4"))
    assert url.startswith("file:///")
    assert "Program%20Files" in url or "Program Files" in url


# ---------------------------------------------------------------------------
# _build_background
# ---------------------------------------------------------------------------


def test_build_background_picks_first_existing_video() -> None:
    fs = FakeFS()
    fs.add_file("C:/launcher/bg.mp4", "binary")
    fs.add_file("C:/launcher/bg.jpg", "jpeg")
    bg = _build_background(
        Path("C:/launcher"),
        video_names=DEFAULT_VIDEO_FILE_NAMES,
        image_names=DEFAULT_IMAGE_FILE_NAMES,
        max_depth=1,
        exists_checker=fs.has_file,
    )
    assert bg.video_url == "file:///C:/launcher/bg.mp4"
    assert bg.image_url == "file:///C:/launcher/bg.jpg"
    assert bg.local_path == "C:/launcher/bg.mp4"


def test_build_background_falls_back_to_image_when_no_video() -> None:
    fs = FakeFS()
    install_dir = "/opt/game" if os.name != "nt" else "C:/opt/game"
    fs.add_file(f"{install_dir}/bg.png", "png")
    bg = _build_background(
        Path(install_dir),
        video_names=DEFAULT_VIDEO_FILE_NAMES,
        image_names=DEFAULT_IMAGE_FILE_NAMES,
        max_depth=1,
        exists_checker=fs.has_file,
    )
    assert bg.video_url is None
    assert bg.image_url == f"file:///{install_dir}/bg.png"
    assert bg.local_path is None


def test_build_background_returns_empty_when_nothing_exists() -> None:
    fs = FakeFS()
    bg = _build_background(
        Path("/empty"),
        video_names=DEFAULT_VIDEO_FILE_NAMES,
        image_names=DEFAULT_IMAGE_FILE_NAMES,
        max_depth=1,
        exists_checker=fs.has_file,
    )
    assert bg.video_url is None
    assert bg.image_url is None
    assert bg.local_path is None


def test_build_background_uses_subdirectory_when_max_depth_2() -> None:
    fs = FakeFS()
    install_dir = "/opt/game" if os.name != "nt" else "C:/opt/game"
    fs.add_file(f"{install_dir}/assets/bg.mp4", "binary")
    fs.add_file(f"{install_dir}/bg.jpg", "jpeg")
    bg = _build_background(
        Path(install_dir),
        video_names=DEFAULT_VIDEO_FILE_NAMES,
        image_names=DEFAULT_IMAGE_FILE_NAMES,
        max_depth=2,
        exists_checker=fs.has_file,
    )
    # Sub-directory asset wins over root-level image.
    assert bg.video_url == f"file:///{install_dir}/assets/bg.mp4"
    assert bg.image_url == f"file:///{install_dir}/bg.jpg"


def test_build_background_prefers_mp4_over_webm_when_video_names_overridden() -> None:
    fs = FakeFS()
    install_dir = "/opt/game" if os.name != "nt" else "C:/opt/game"
    fs.add_file(f"{install_dir}/bg.webm", "webm")
    fs.add_file(f"{install_dir}/bg.mp4", "mp4")
    bg = _build_background(
        Path(install_dir),
        video_names=["bg.webm", "bg.mp4"],
        image_names=DEFAULT_IMAGE_FILE_NAMES,
        max_depth=1,
        exists_checker=fs.has_file,
    )
    assert bg.video_url == f"file:///{install_dir}/bg.webm"


# ---------------------------------------------------------------------------
# _build_update_info
# ---------------------------------------------------------------------------


def test_build_update_info_parses_config_json() -> None:
    fs = FakeFS()
    config = _load("launcher-local-config-win.json")
    install_dir = config["installDir"]
    config_entry = next(f for f in config["files"] if f["kind"] == "config")
    fs.add_file(config_entry["path"], json.dumps(config_entry["content"], ensure_ascii=False))
    info = _build_update_info(
        Path(install_dir),
        config_name=DEFAULT_CONFIG_FILE_NAME,
        file_reader=fs.read_file,
    )
    assert info is not None
    assert info.version == "2.9.0"
    assert info.title == "燕云十六声 v2.9.0"
    assert info.target_url == "https://www.yysls.cn/download/"
    assert info.published_at is not None
    assert info.published_at.year == 2026
    assert info.published_at.tzinfo is not None


def test_build_update_info_returns_none_when_missing() -> None:
    fs = FakeFS()
    install_dir = "C:/empty"
    info = _build_update_info(
        Path(install_dir),
        config_name="config.json",
        file_reader=fs.read_file,
    )
    assert info is None


def test_build_update_info_returns_none_for_invalid_json() -> None:
    fs = FakeFS()
    fs.add_file("/opt/game/config.json", "this is not json")
    info = _build_update_info(
        Path("/opt/game"),
        config_name="config.json",
        file_reader=fs.read_file,
    )
    assert info is None


def test_build_update_info_returns_none_when_disabled() -> None:
    fs = FakeFS()
    info = _build_update_info(
        Path("/opt/game"),
        config_name="",
        file_reader=fs.read_file,
    )
    assert info is None


# ---------------------------------------------------------------------------
# End-to-end fetch (Windows / macOS / Linux layouts)
# ---------------------------------------------------------------------------


def _build_fs_from_fixture(name: str) -> tuple[FakeFS, dict[str, Any]]:
    fixture = _load(name)
    fs = FakeFS()
    for entry in fixture["files"]:
        if entry["kind"] == "config":
            content = json.dumps(entry["content"], ensure_ascii=False)
        else:
            content = entry.get("content", f"<{entry['kind']} bytes={entry.get('size', 0)}>")
        # ``add_file`` calls ``Path.resolve()`` internally; on Windows
        # the bare POSIX path ``/opt/...`` resolves to ``C:\opt\...`` so
        # we feed the un-prefixed POSIX spelling and let the FakeFS
        # normalise both add and lookup paths.
        fs.add_file(entry["path"], content)
    return fs, fixture


def _winpath(url_or_path: str) -> str:
    """Prefix a leading ``/`` with ``C:/`` on Windows so a POSIX-style
    path (``"/opt/foo"``) or POSIX-style ``file://`` URL
    (``"file:///opt/foo"``) matches ``Path.resolve()`` / ``as_uri()``
    output on Windows.  No-op on POSIX."""
    if os.name != "nt":
        return url_or_path
    if url_or_path.startswith("file:///"):
        return "file:///C:/" + url_or_path[len("file:///"):]
    if url_or_path.startswith("/"):
        return "C:/" + url_or_path[1:]
    return url_or_path


@pytest.mark.asyncio
async def test_fetch_windows_layout() -> None:
    fs, fixture = _build_fs_from_fixture("launcher-local-config-win.json")
    provider = _provider_for(fs)
    result = await provider.fetch(_request({"installDir": fixture["installDir"]}))

    # ``Path.as_uri()`` percent-encodes the spaces in
    # ``Program Files`` / ``Where Winds Meet``.
    expected_video = _winpath(
        "file:///Program%20Files/Where%20Winds%20Meet/bg.mp4"
    )
    expected_image = _winpath(
        "file:///Program%20Files/Where%20Winds%20Meet/bg.jpg"
    )
    assert result.background is not None
    assert result.background.video_url == expected_video
    assert result.background.image_url == expected_image
    assert result.background.local_path == "C:/Program Files/Where Winds Meet/bg.mp4"
    assert result.banners == []
    assert result.news == []
    assert result.update_info is not None
    assert result.update_info.version == "2.9.0"


@pytest.mark.asyncio
async def test_fetch_macos_layout() -> None:
    fs, fixture = _build_fs_from_fixture("launcher-local-config-mac.json")
    provider = _provider_for(fs)
    result = await provider.fetch(_request({"installDir": fixture["installDir"]}))

    expected_video = _winpath(
        "file:///Applications/Where%20Winds%20Meet.app/Contents/Resources/bg.mp4"
    )
    assert result.background.video_url == expected_video
    # macOS bundle has no static image.
    assert result.background.image_url is None
    assert result.update_info is not None


@pytest.mark.asyncio
async def test_fetch_linux_layout_uses_assets_subdir() -> None:
    fs, fixture = _build_fs_from_fixture("launcher-local-config-linux.json")
    provider = _provider_for(fs)
    result = await provider.fetch(_request({"installDir": fixture["installDir"]}))

    # ``bg.webm`` lives under ``assets/``; ``maxDepth=2`` lets the
    # provider reach it.
    expected_video = _winpath(
        "file:///opt/where-winds-meet/assets/bg.webm"
    )
    expected_image = _winpath(
        "file:///opt/where-winds-meet/assets/bg.png"
    )
    assert result.background.video_url == expected_video
    assert result.background.image_url == expected_image


@pytest.mark.asyncio
async def test_fetch_without_install_dir_degrades_to_empty() -> None:
    provider = LocalLauncherAssetProvider()
    result = await provider.fetch(_request())
    assert result.background is not None
    assert result.background.video_url is None
    assert result.update_info is None


@pytest.mark.asyncio
async def test_fetch_with_empty_install_dir_degrades_to_empty() -> None:
    provider = LocalLauncherAssetProvider()
    result = await provider.fetch(_request({"installDir": "   "}))
    assert result.background.video_url is None
    assert result.update_info is None


@pytest.mark.asyncio
async def test_fetch_respects_video_and_image_overrides() -> None:
    fs = FakeFS()
    fs.add_file("C:/opt/game/launcher-bg.mp4", "video")
    fs.add_file("C:/opt/game/poster.png", "image")
    provider = _provider_for(fs)
    result = await provider.fetch(_request({
        "installDir": "C:/opt/game",
        "videoFileNames": ["launcher-bg.mp4"],
        "imageFileNames": ["poster.png"],
    }))
    assert result.background.video_url == "file:///C:/opt/game/launcher-bg.mp4"
    assert result.background.image_url == "file:///C:/opt/game/poster.png"


@pytest.mark.asyncio
async def test_fetch_respects_config_file_name_override() -> None:
    fs = FakeFS()
    fs.add_file("/opt/game/launcher.json", json.dumps({
        "version": "9.9.9",
        "title": "Custom config",
    }, ensure_ascii=False))
    provider = _provider_for(fs)
    result = await provider.fetch(_request({
        "installDir": "C:/opt/game",
        "configFileName": "launcher.json",
    }))
    assert result.update_info is not None
    assert result.update_info.version == "9.9.9"
    assert result.update_info.title == "Custom config"


@pytest.mark.asyncio
async def test_fetch_disabling_config_returns_no_update_info() -> None:
    fs, fixture = _build_fs_from_fixture("launcher-local-config-win.json")
    provider = _provider_for(fs)
    result = await provider.fetch(_request({
        "installDir": fixture["installDir"],
        "configFileName": "",
    }))
    assert result.update_info is None
    # Background is still surfaced even when config is disabled.
    assert result.background.video_url is not None


@pytest.mark.asyncio
async def test_fetch_max_depth_1_skips_assets_subdir() -> None:
    fs, fixture = _build_fs_from_fixture("launcher-local-config-linux.json")
    provider = _provider_for(fs)
    result = await provider.fetch(_request({
        "installDir": fixture["installDir"],
        "maxDepth": 1,  # restricted; ``assets/`` is unreachable.
    }))
    # Linux fixture only has assets/, so with depth=1 we find nothing.
    assert result.background.video_url is None
    assert result.background.image_url is None
    # Config still wins because it lives at the install root.
    assert result.update_info is not None


def test_path_to_file_url_resolves_relative_components() -> None:
    # ``Path("a/../b/c.mp4").resolve()`` normalises ``..`` segments.
    url = _path_to_file_url(Path("/opt/game/extra/../bg.mp4"))
    assert "/opt/game/bg.mp4" in url