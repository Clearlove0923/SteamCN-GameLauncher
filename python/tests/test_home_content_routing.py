"""Offline Worker route tests; no live vendor API calls."""

import asyncio
import importlib
import unittest
from unittest.mock import patch

import httpx

from home_content.models import HomeContent


class HomeContentRoutingTests(unittest.TestCase):
    def test_selected_executable_overrides_stale_provider(self) -> None:
        server = importlib.import_module("home_content.server.app")
        captured = []

        class FakeProvider:
            async def fetch(self, request):
                captured.append(request)
                return HomeContent()

        async def request():
            async with httpx.AsyncClient(transport=httpx.ASGITransport(app=server.app),
                                         base_url="http://test") as client:
                return await client.post("/v1/home-content", json={
                    "requestId": "route-1", "gameId": "wrong-steam-id",
                    "providerId": "kuro-launcher",
                    "executablePath": r"D:\Genshin Impact Game\YuanShen.exe",
                    "installDirectory": r"D:\Genshin Impact Game",
                    "providerOptions": {"gameBiz": "wrong_game"},
                })

        with patch.object(server, "create_provider", return_value=FakeProvider()) as create:
            response = asyncio.run(request())
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["providerId"], "hoyoplay-json")
        create.assert_called_once_with("hoyoplay-json")
        self.assertEqual(captured[0].game_id, "genshin-impact")
        self.assertEqual(captured[0].provider_options["gameBiz"], "hk4e_cn")

    def test_unknown_paths_return_structured_error(self) -> None:
        server = importlib.import_module("home_content.server.app")

        async def request():
            async with httpx.AsyncClient(transport=httpx.ASGITransport(app=server.app),
                                         base_url="http://test") as client:
                return await client.post("/v1/home-content", json={
                    "requestId": "route-2", "gameId": "unrecognized",
                    "providerId": "auto", "executablePath": r"D:\Other\unknown.exe",
                })

        response = asyncio.run(request())
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["errors"][0]["code"], "unsupported_game")


if __name__ == "__main__":
    unittest.main()
