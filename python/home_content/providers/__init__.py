"""Source-specific adapters exposed through the provider registry."""

from .hoyoplay_json import HoYoPlayJsonProvider
from .hypergryph_batch import HypergryphBatchProvider
from .kuro_launcher import KuroLauncherProvider
from .local_launcher_asset import LocalLauncherAssetProvider
from .netease_static_cms import NetEaseStaticCmsProvider
from .nextjs_data import NextJsDataProvider
from .perfect_world_hybrid import PerfectWorldHybridProvider
from .company import (
    MiHoYoProvider,
    KuroProvider,
    HypergryphProvider,
    PerfectWorldProvider,
    PaperGamesProvider,
    NetEaseProvider,
)

__all__ = [
    "MiHoYoProvider",
    "KuroProvider",
    "HypergryphProvider",
    "PerfectWorldProvider",
    "PaperGamesProvider",
    "NetEaseProvider",
    "HoYoPlayJsonProvider",
    "KuroLauncherProvider",
    "HypergryphBatchProvider",
    "PerfectWorldHybridProvider",
    "NextJsDataProvider",
    "NetEaseStaticCmsProvider",
    "LocalLauncherAssetProvider",
]
