"""Shared contract for all source adapters."""

from abc import ABC, abstractmethod

from ..models import HomeContent, HomeContentRequest


class HomeContentProvider(ABC):
    """Discover and normalize one source without exposing source DTOs to C#."""

    provider_id: str

    @abstractmethod
    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        """Return normalized Background/Banners/News/UpdateInfo content."""


class PendingHomeContentProvider(HomeContentProvider):
    """Explicit placeholder until a source implementation and fixed samples exist."""

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        raise NotImplementedError(f"Provider '{self.provider_id}' has not been implemented")
