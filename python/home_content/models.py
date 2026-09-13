"""Pydantic mirror of contracts/home-content-v1.schema.json."""

from datetime import datetime
from typing import Any

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
    video_url: str | None = None
    image_url: str | None = None
    local_path: str | None = None


class HomeBanner(ContractModel):
    id: str
    title: str | None = None
    image_url: str | None = None
    local_path: str | None = None
    target_url: str | None = None
    starts_at: datetime | None = None
    ends_at: datetime | None = None


class HomeNewsItem(ContractModel):
    id: str
    title: str
    category: str | None = None
    summary: str | None = None
    image_url: str | None = None
    target_url: str | None = None
    published_at: datetime | None = None


class HomeUpdateInfo(ContractModel):
    version: str | None = None
    title: str | None = None
    summary: str | None = None
    target_url: str | None = None
    published_at: datetime | None = None


class HomeContentError(ContractModel):
    code: str
    message: str
    recoverable: bool = True


class HomeContent(ContractModel):
    background: HomeBackground | None = None
    banners: list[HomeBanner] = Field(default_factory=list)
    news: list[HomeNewsItem] = Field(default_factory=list)
    update_info: HomeUpdateInfo | None = None


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
    screenshot_path: str | None = None
    errors: list[HomeContentError] = Field(default_factory=list)
