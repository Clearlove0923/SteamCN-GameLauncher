"""Fixed, offline executable/folder matching tests."""

import unittest

from home_content.game_source_registry import match_game_source


class GameSourceRegistryTests(unittest.TestCase):
    def test_executable_wins_over_folder(self) -> None:
        result = match_game_source(r"D:\Genshin Impact Game\HTGame.exe", None)
        self.assertIsNotNone(result)
        self.assertEqual(result.game_id, "neverness-to-everness")
        self.assertEqual(result.provider_id, "perfect-world-hybrid")

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


if __name__ == "__main__":
    unittest.main()
