"""Stable provider IDs mapped to replaceable adapter classes."""

from .providers import (
    HoYoPlayJsonProvider,
    HypergryphBatchProvider,
    KuroLauncherProvider,
    LocalLauncherAssetProvider,
    NetEaseStaticCmsProvider,
    NextJsDataProvider,
    PerfectWorldHybridProvider,
    MiHoYoProvider,
    KuroProvider,
    HypergryphProvider,
    PerfectWorldProvider,
    PaperGamesProvider,
    NetEaseProvider,
)
from .providers.base import HomeContentProvider


PROVIDER_TYPES: dict[str, type[HomeContentProvider]] = {
    provider.provider_id: provider
    for provider in (
        MiHoYoProvider,
        KuroProvider,
        HypergryphProvider,
        PerfectWorldProvider,
        PaperGamesProvider,
        NetEaseProvider,
        LocalLauncherAssetProvider,
    )
}

# Persisted IDs from older builds remain callable. New game-source entries use
# company IDs, while callers holding an old preset can still fetch its content.
LEGACY_PROVIDER_TYPES: dict[str, type[HomeContentProvider]] = {
    provider.provider_id: provider
    for provider in (
        HoYoPlayJsonProvider,
        KuroLauncherProvider,
        HypergryphBatchProvider,
        PerfectWorldHybridProvider,
        NextJsDataProvider,
        NetEaseStaticCmsProvider,
    )
}


def create_provider(provider_id: str) -> HomeContentProvider:
    """Resolve a stable ID without coupling callers to a provider class name."""

    try:
        provider_type = PROVIDER_TYPES.get(provider_id) or LEGACY_PROVIDER_TYPES[provider_id]
        return provider_type()
    except KeyError as error:
        raise ValueError(f"Unknown providerId: {provider_id}") from error
