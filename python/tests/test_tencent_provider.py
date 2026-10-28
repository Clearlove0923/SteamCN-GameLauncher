"""Fixed offline tests for TencentProvider and 洛克王国：世界."""

from __future__ import annotations

import asyncio
import json
from importlib.resources import files
from pathlib import Path

import httpx

from home_content.models import HomeContentRequest
from home_content.providers.tencent import (
    TencentProvider,
    parse_roco_home_html,
    parse_roco_launcher_json,
)


FIXTURE = files("tests").joinpath("tencent_roco_home.html").read_text(encoding="utf-8")
LAUNCHER_FIXTURE = json.loads(
    files("tests").joinpath("tencent_roco_launcher.json").read_text(encoding="utf-8")
)


def _request(root: Path) -> HomeContentRequest:
    return HomeContentRequest(
        requestId="tencent-test",
        gameId="rock-kingdom-world",
        providerId="tencent",
        executablePath=str(root / "洛克王国：世界.exe"),
        installDirectory=str(root),
    )


def _write_mp4(path: Path) -> None:
    path.parent.mkdir(parents=True)
    path.write_bytes(b"\x00\x00\x00\x18ftypisom" + b"\x00" * (256 * 1024))


def test_fixed_homepage_sample_is_normalized_and_rejects_untrusted_links() -> None:
    banners, news = parse_roco_home_html(FIXTURE)
    assert len(banners) == 1
    assert banners[0].title == "S4 月涌狂想"
    assert banners[0].image_url.startswith("https://game.gtimg.cn/")
    assert [item.category for item in news] == ["公告", "活动", "资讯"]
    assert news[0].published_at.isoformat() == "2026-09-10T00:00:00+00:00"
    assert all(item.target_url.startswith("https://rocom.qq.com/") for item in news)
    assert len({item.id for item in news}) == len(news)


def test_current_activity_page_uses_only_verified_part1_hero() -> None:
    html = """
    <html><head><title>月涌狂想赛季来袭</title>
    <meta name="Description" content="新奇遇新精灵"></head><body>
    <img class="picture-inner" src="https://game.gtimg.cn/images/rocom/act/a20260706/web/part1/20260828/bg.jpg" alt="bg">
    <img class="picture-inner" src="https://game.gtimg.cn/images/rocom/act/a20260706/web/part2/prize.png" alt="">
    </body></html>
    """
    banners, news = parse_roco_home_html(html)
    assert len(banners) == 1
    assert banners[0].title == "月涌狂想赛季来袭"
    assert banners[0].image_url.endswith("/part1/20260828/bg.jpg")
    assert len(news) == 1
    assert news[0].category == "活动"
    assert news[0].summary == "新奇遇新精灵"


def test_official_launcher_feed_normalizes_banners_news_and_dates() -> None:
    banners, news = parse_roco_launcher_json(LAUNCHER_FIXTURE)
    assert [item.title for item in banners] == [
        "S4赛季「月涌狂想」版本更新公告",
        "S3赛季下架与继承",
    ]
    assert [item.title for item in news[:3]] == [
        "S4赛季「月涌狂想」魔法快报",
        "S4赛季「月涌狂想」版本更新公告",
        "S3赛季下架与继承",
    ]
    assert [item.category for item in news[:3]] == ["资讯", "公告", "资讯"]
    assert news[0].published_at.isoformat() == "2026-09-07T13:20:42+00:00"
    assert news[-1].target_url is None
    assert news[-1].image_url is None


def test_provider_combines_verified_local_video_with_official_site(tmp_path: Path) -> None:
    video = tmp_path / "launcher" / "tpf_ui" / "res" / "videos" / "main_ui" / "main_video.mp4"
    _write_mp4(video)

    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.host == "apps.game.qq.com"
        return httpx.Response(200, json=LAUNCHER_FIXTURE)

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
            return await TencentProvider(client=client).fetch(_request(tmp_path))

    content = asyncio.run(run())
    assert content.background.local_path == str(video.resolve())
    assert len(content.banners) == 2
    assert len(content.news) == 4


def test_provider_keeps_local_background_when_site_fails(tmp_path: Path) -> None:
    video = tmp_path / "launcher" / "tpf_ui" / "res" / "videos" / "main_ui" / "main_video.mp4"
    _write_mp4(video)

    async def run():
        async with httpx.AsyncClient(
            transport=httpx.MockTransport(lambda request: httpx.Response(503))
        ) as client:
            return await TencentProvider(client=client).fetch(_request(tmp_path))

    content = asyncio.run(run())
    assert content.background.local_path == str(video.resolve())
    assert content.banners == []
    assert content.news == []


def test_provider_falls_back_to_official_site_when_launcher_feed_fails(tmp_path: Path) -> None:
    calls: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        calls.append(request.url.host)
        if request.url.host == "apps.game.qq.com":
            return httpx.Response(503)
        return httpx.Response(200, text=FIXTURE)

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
            return await TencentProvider(client=client).fetch(_request(tmp_path))

    content = asyncio.run(run())
    assert calls == ["apps.game.qq.com", "rocom.qq.com"]
    assert len(content.banners) == 1
    assert len(content.news) == 3


def test_invalid_or_too_small_video_falls_back_to_launcher_png(tmp_path: Path) -> None:
    video = tmp_path / "launcher" / "tpf_ui" / "res" / "videos" / "main_ui" / "main_video.mp4"
    video.parent.mkdir(parents=True)
    video.write_bytes(b"not-an-mp4")
    image = tmp_path / "launcher" / "data" / "client_ui" / "web" / "assets" / "launcher-bg.png"
    image.parent.mkdir(parents=True)
    image.write_bytes(b"\x89PNG\r\n\x1a\n" + b"\x00" * 32)

    async def run():
        async with httpx.AsyncClient(
            transport=httpx.MockTransport(lambda request: httpx.Response(200, text="<html></html>"))
        ) as client:
            return await TencentProvider(client=client).fetch(_request(tmp_path))

    content = asyncio.run(run())
    assert content.background.local_path == str(image.resolve())
