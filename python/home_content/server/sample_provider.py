"""Sample provider payload used by the FastAPI skeleton.

The real adapters live in :mod:`home_content.providers` and will replace
this stub once their endpoints are wired and validated. This sample keeps
the e2e flow working without depending on any external vendor.
"""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Optional

from ..models import (
    HomeBackground,
    HomeBanner,
    HomeContent,
    HomeContentEnvelope,
    HomeContentRequest,
    HomeNewsItem,
    HomeUpdateInfo,
)


def build_sample_envelope(request: HomeContentRequest) -> HomeContentEnvelope:
    """Build a deterministic HomeContentEnvelope for the given request.

    The Python Worker is meant to be replaced by the real provider registry.
    Until each adapter ships its fixed sample, this stub keeps the e2e
    contract test green and lets the WinUI client render something.
    """

    game_id = request.game_id
    now = datetime.now(timezone.utc)

    background = HomeBackground(
        video_url="https://cdn.example.invalid/home/bg.mp4",
        image_url="https://cdn.example.invalid/home/bg-poster.webp",
        local_path=None,
    )

    banners = [
        HomeBanner(
            id=f"{game_id}-banner-1",
            title="示例 Banner · 接入后由厂商数据替换",
            image_url="https://cdn.example.invalid/home/banner-1.webp",
            target_url="https://example.invalid/activity/1",
        ),
        HomeBanner(
            id=f"{game_id}-banner-2",
            title="背景动画将由 Python Worker 注入",
            image_url="https://cdn.example.invalid/home/banner-2.webp",
            target_url="https://example.invalid/activity/2",
        ),
    ]

    news = [
        HomeNewsItem(
            id=f"{game_id}-news-activity",
            title="活动内容等待接入",
            category="活动",
            target_url="https://example.invalid/news/activity",
            published_at=now,
        ),
        HomeNewsItem(
            id=f"{game_id}-news-announcement",
            title="公告内容等待接入",
            category="公告",
            target_url="https://example.invalid/news/announcement",
            published_at=now,
        ),
        HomeNewsItem(
            id=f"{game_id}-news-information",
            title="资讯内容等待接入",
            category="资讯",
            target_url="https://example.invalid/news/information",
            published_at=now,
        ),
    ]

    update_info = HomeUpdateInfo(
        version="0.0.0-sample",
        title="示例数据,由 sample_provider 提供",
        summary="Provider 接入完成后,此字段将由真实厂商数据替换。",
        target_url=None,
        published_at=now,
    )

    content = HomeContent(
        background=background,
        banners=banners,
        news=news,
        update_info=update_info,
    )

    return HomeContentEnvelope(
        schema_version=1,
        request_id=request.request_id,
        provider_id=request.provider_id,
        fetched_at=now,
        content=content,
        errors=[],
    )


def build_sample_screenshot_path(request_id: str, game_id: str) -> dict:
    """Placeholder for the screenshot-path envelope.

    The real implementation belongs to the screenshot_paths module; this
    stub returns a deterministic path so the C# side can be wired today.
    """

    return {
        "schemaVersion": 1,
        "requestId": request_id,
        "gameId": game_id,
        "screenshotPath": f"C:/Steam/userdata/screenshots/{game_id}",
        "errors": [],
    }