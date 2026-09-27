"""Company-facing Providers; source adapters retain their legacy IDs."""

from ..game_source_registry import get_game_source_by_id
from .hoyoplay_json import HoYoPlayJsonProvider
from .hypergryph_batch import HypergryphBatchProvider
from .kuro_launcher import KuroLauncherProvider
from .netease_static_cms import NetEaseStaticCmsProvider
from .nextjs_data import NextJsDataProvider
from .perfect_world_hybrid import PerfectWorldHybridProvider


class MiHoYoProvider(HoYoPlayJsonProvider):
    provider_id = "mihoyo"

    async def fetch(self, request):
        # A company owns several games: never show the first HoYoPlay entry
        # when neither the game ID nor its source options select one.
        source = get_game_source_by_id(request.game_id)
        if source is None or source.provider_id != self.provider_id:
            raise ValueError(f"Unknown miHoYo game: {request.game_id}")
        expected_biz = source.provider_options["gameBiz"]
        supplied_biz = (request.provider_options or {}).get("gameBiz")
        if supplied_biz and supplied_biz != expected_biz:
            raise ValueError(f"miHoYo gameBiz does not match game: {request.game_id}")
        request = request.model_copy(update={
            "provider_options": {**source.provider_options, **(request.provider_options or {})},
        })
        return await super().fetch(request)


class KuroProvider(KuroLauncherProvider):
    provider_id = "kuro"


class HypergryphProvider(HypergryphBatchProvider):
    provider_id = "hypergryph"


class PerfectWorldProvider(PerfectWorldHybridProvider):
    provider_id = "perfect-world"


class PaperGamesProvider(NextJsDataProvider):
    provider_id = "papergames"


class NetEaseProvider(NetEaseStaticCmsProvider):
    provider_id = "netease"
