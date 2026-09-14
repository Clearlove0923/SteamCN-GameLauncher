"""Pydantic mirror of contracts/home-content-v1.schema.json."""

from __future__ import annotations

from datetime import datetime
from typing import Any, Optional

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class ContractModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="allow")


class HomeContentRequest(ContractModel):
    schema_version: int = 1
    request_id: str
    game_id: str
    provider_id: str
    locale: str = "zh-CN"
    provider_options: dict[str, Any] = Field(default_factory=dict)


class HomeBackground(ContractModel):
    video_url: Optional[str] = None
    image_url: Optional[str] = None
    local_path: Optional[str] = None


class HomeBanner(ContractModel):
    id: str
    title: Optional[str] = None
    image_url: Optional[str] = None
    local_path: Optional[str] = None
    target_url: Optional[str] = None
    starts_at: Optional[datetime] = None
    ends_at: Optional[datetime] = None


class HomeNewsItem(ContractModel):
    id: str
    title: str
    category: Optional[str] = None
    summary: Optional[str] = None
    image_url: Optional[str] = None
    target_url: Optional[str] = None
    published_at: Optional[datetime] = None


class HomeUpdateInfo(ContractModel):
    version: Optional[str] = None
    title: Optional[str] = None
    summary: Optional[str] = None
    target_url: Optional[str] = None
    published_at: Optional[datetime] = None


class HomeContentError(ContractModel):
    code: str
    message: str
    recoverable: bool = True


class HomeContent(ContractModel):
    background: Optional[HomeBackground] = None
    banners: list[HomeBanner] = Field(default_factory=list)
    news: list[HomeNewsItem] = Field(default_factory=list)
    update_info: Optional[HomeUpdateInfo] = None


class HomeContentEnvelope(ContractModel):
    schema_version: int = 1
    request_id: str
    provider_id: str
    fetched_at: datetime
    content: HomeContent
    errors: list[HomeContentError] = Field(default_factory=list)


class GameScreenshotPathRequest(ContractModel):
    schema_version: int = 1
    request_id: str
    game_id: str


class GameScreenshotPathEnvelope(ContractModel):
    schema_version: int = 1
    request_id: str
    game_id: str
    screenshot_path: Optional[str] = None
    errors: list[HomeContentError] = Field(default_factory=list)