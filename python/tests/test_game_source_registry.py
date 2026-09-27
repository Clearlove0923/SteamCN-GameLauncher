"""Fixed, offline executable/folder matching tests."""

import unittest
import json
from importlib.resources import files

from home_content.game_source_registry import match_game_source


class GameSourceRegistryTests(unittest.TestCase):
    def test_every_game_declares_news_presentation(self) -> None:
        sources = json.loads(files("home_content").joinpath("game_sources.json").read_text(encoding="utf-8"))
        for source in sources:
            with self.subTest(game_id=source["gameId"]):
                self.assertIn("newsCategoryLabels", source)
                self.assertIn("newsCategoryOrder", source)
                order = source["newsCategoryOrder"]
                self.assertEqual(len(order), len(set(order)))
                self.assertTrue(order)
        nikki = next(row for row in sources if row["gameId"] == "infinity-nikki")
        self.assertEqual(nikki["newsCategoryLabels"]["资讯"], "新闻")
        self.assertEqual(nikki["newsCategoryOrder"], ["公告", "新闻", "活动"])

    def test_install_folder_wins_over_conflicting_executable(self) -> None:
        result = match_game_source(r"D:\Genshin Impact Game\HTGame.exe", None)
        self.assertIsNotNone(result)
        self.assertEqual(result.game_id, "genshin-impact")
        self.assertEqual(result.provider_id, "mihoyo")

    def test_nested_install_folder_and_custom_bridge_exe(self) -> None:
        result = match_game_source(
            r"E:\Games\原神\Genshin Impact Game\MySteamLaunchBridge.exe", None
        )
        self.assertIsNotNone(result)
        self.assertEqual(result.game_id, "genshin-impact")
        result = match_game_source(r"D:\Other\MySteamLaunchBridge.exe", r"D:\Games\无限暖暖")
        self.assertIsNotNone(result)
        self.assertEqual(result.game_id, "infinity-nikki")

    def test_folder_fallback_and_case_insensitive_alias(self) -> None:
        result = match_game_source(r"D:\GENSHIN IMPACT GAME\other.exe", None)
        self.assertIsNotNone(result)
        self.assertEqual(result.game_id, "genshin-impact")
        self.assertEqual(result.provider_options["gameBiz"], "hk4e_cn")

    def test_multiple_executables_and_unknown_game(self) -> None:
        for name in ("HTGame.exe", "NTEGame.exe"):
            with self.subTest(name=name):
                result = match_game_source(rf"D:\NTE\{name}", None)
                self.assertIsNotNone(result)
                self.assertEqual(result.game_id, "neverness-to-everness")
        self.assertIsNone(match_game_source(r"D:\Other\unknown.exe", r"D:\Other"))

    def test_shared_launchers_require_a_game_folder(self) -> None:
        self.assertEqual(match_game_source(r"D:\鸣潮\launcher.exe", None).game_id, "wuthering-waves")
        self.assertEqual(match_game_source(r"D:\yysls\launcher.exe", None).game_id, "where-winds-meet")
        self.assertIsNone(match_game_source(r"D:\Other\launcher.exe", None))
        self.assertEqual(match_game_source(r"D:\Other\GenshinImpact.exe", None).game_id, "genshin-impact")

    def test_helper_and_diagnostic_executables_are_not_keywords(self) -> None:
        for name in ("EpicWebHelper.exe", "crashpad_handler.exe", "ZFGameBrowser.exe",
                     "UnityCrashHandler64.exe", "WhereWindsMeetLaunchCapture.exe"):
            with self.subTest(name=name):
                self.assertIsNone(match_game_source(rf"D:\Other\{name}", None))

    def test_publicly_documented_game_entries_resolve(self) -> None:
        cases = (
            (r"D:\Other\BH3.exe", "honkai-impact-3rd"),
            (r"D:\Other\StarRail.exe", "honkai-star-rail"),
            (r"D:\Arknights Endfiled\Endfield.exe", "arknights-endfield"),
        )
        for path, game_id in cases:
            with self.subTest(path=path):
                self.assertEqual(match_game_source(path, None).game_id, game_id)

    def test_requested_games_route_by_exe_and_localized_folder(self) -> None:
        cases = (
            ("原神", "YuanShen.exe", "genshin-impact", "mihoyo", "hk4e_cn"),
            ("绝区零", "ZenlessZoneZero.exe", "zenless-zone-zero", "mihoyo", "nap_cn"),
            ("崩坏星穹铁道", "StarRail.exe", "honkai-star-rail", "mihoyo", "hkrpg_cn"),
            ("崩坏3", "BH3.exe", "honkai-impact-3rd", "mihoyo", "bh3_cn"),
            ("鸣潮", "Client-Win64-Shipping.exe", "wuthering-waves", "kuro", None),
            ("明日方舟终末地", "Endfield.exe", "arknights-endfield", "hypergryph", None),
            ("异环", "HTGame.exe", "neverness-to-everness", "perfect-world", None),
            ("无限暖暖", "InfinityNikki.exe", "infinity-nikki", "papergames", None),
            ("燕云十六声", "wwm.exe", "where-winds-meet", "netease", None),
        )
        for folder, exe, game_id, provider_id, game_biz in cases:
            with self.subTest(game_id=game_id):
                for path in (rf"D:\{folder}\{exe}", rf"D:\{folder}\unknown.exe"):
                    source = match_game_source(path, None)
                    self.assertIsNotNone(source)
                    self.assertEqual(source.game_id, game_id)
                    self.assertEqual(source.provider_id, provider_id)
                    if game_biz:
                        self.assertEqual(source.provider_options["gameBiz"], game_biz)


if __name__ == "__main__":
    unittest.main()
