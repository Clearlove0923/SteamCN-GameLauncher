"""Per-game screenshot paths owned by Python rather than the WinUI pages.

The keys are stable game identifiers sent in HomeContentRequest.gameId. Values
may use placeholders that the local C# screenshot service expands from its own
Steam settings before validating and opening the directory.
"""

from types import MappingProxyType

from .models import GameScreenshotPathEnvelope, GameScreenshotPathRequest


_STEAM_SCREENSHOT_TEMPLATE = (
    r"{steamInstallPath}\userdata\{steamId}\760\remote\{appId}\screenshots"
)

# Central hardcoded registry. Add or change per-game paths here; providers must
# not duplicate these paths, and C# must not contain game-specific mappings.
SCREENSHOT_PATHS_BY_GAME_ID = MappingProxyType(
    {
        "3513350": _STEAM_SCREENSHOT_TEMPLATE.format(appId="3513350", steamInstallPath="{steamInstallPath}", steamId="{steamId}"),
        "4162040": _STEAM_SCREENSHOT_TEMPLATE.format(appId="4162040", steamInstallPath="{steamInstallPath}", steamId="{steamId}"),
    }
)


def get_screenshot_path(game_id: str) -> str | None:
    """Return the configured path template without guessing an unknown game."""

    return SCREENSHOT_PATHS_BY_GAME_ID.get(game_id)


def resolve_screenshot_path(request: GameScreenshotPathRequest) -> GameScreenshotPathEnvelope:
    """Build the separate screenshot-path response consumed by C#."""

    return GameScreenshotPathEnvelope(
        request_id=request.request_id,
        game_id=request.game_id,
        screenshot_path=get_screenshot_path(request.game_id),
    )
