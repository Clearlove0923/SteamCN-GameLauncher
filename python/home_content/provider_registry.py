"""Stable provider IDs mapped to replaceable adapter classes."""

from .providers import (
    HoYoPlayJsonProvider,
    HypergryphBatchProvider,
    KuroLauncherProvider,
    LocalLauncherAssetProvider,
    NetEaseStaticCmsProvider,
    NextJsDataProvider,
    PerfectWorldHybridProvider,
)
from .providers.base import HomeContentProvider


PROVIDER_TYPES: dict[str, type[HomeContentProvider]] = {
    provider.provider_id: provider
    for provider in (
        HoYoPlayJsonProvider,
        KuroLauncherProvider,
        HypergryphBatchProvider,
        PerfectWorldHybridProvider,
        NextJsDataProvider,
        NetEaseStaticCmsProvider,
        LocalLauncherAssetProvider,
    )
}


def create_provider(provider_id: str) -> HomeContentProvider:
    """Resolve a stable ID without coupling callers to a provider class name."""

    try:
        return PROVIDER_TYPES[provider_id]()
    except KeyError as error:
        raise ValueError(f"Unknown providerId: {provider_id}") from error
