"""FastAPI application factory.

Exposes the unified HomeContent envelope to the WinUI client. The WinUI
side consumes this through :class:`IHomeContentTransport` over HTTP, with
the endpoint URL injected via configuration (no hard-coded addresses in
the C# layer).

This module intentionally avoids reading or interpreting source-specific
DTOs. All vendor parsing happens inside the Provider classes registered
in :mod:`home_content.provider_registry`.
"""

from __future__ import annotations

import logging
import sys
from datetime import datetime, timezone
from typing import Any

import httpx
from fastapi import FastAPI, HTTPException, status
from pydantic import ValidationError

from ..models import (
    GameScreenshotPathEnvelope,
    GameScreenshotPathRequest,
    HomeContent,
    HomeContentEnvelope,
    HomeContentError,
    HomeContentRequest,
)
from ..provider_registry import create_provider
from .sample_provider import build_sample_envelope, build_sample_screenshot_path

# Diagnostics MUST go to stderr per the unified Python contract; stdout is
# reserved for the JSON envelope returned by FastAPI's response handler.
logger = logging.getLogger("home_content.server")
if not logger.handlers:
    handler = logging.StreamHandler(stream=sys.stderr)
    handler.setFormatter(
        logging.Formatter("%(asctime)s %(levelname)s %(name)s %(message)s")
    )
    logger.addHandler(handler)
    logger.setLevel(logging.INFO)


def create_app() -> FastAPI:
    app = FastAPI(
        title="SteamCN Home Content Worker",
        version="0.1.0",
        description=(
            "Provides unified HomeContent / GameScreenshotPath envelopes to "
            "the WinUI client. Source-specific parsing lives in "
            "home_content.providers; this service only routes requests."
        ),
    )

    @app.get("/healthz", tags=["meta"])
    def healthz() -> dict[str, Any]:
        """Liveness probe used by the WinUI client before scheduling a fetch."""
        return {"status": "ok", "now": datetime.now(timezone.utc).isoformat()}

    @app.post(
        "/v1/home-content",
        response_model=HomeContentEnvelope,
        tags=["home-content"],
    )
    async def fetch_home_content(request: HomeContentRequest) -> HomeContentEnvelope:
        try:
            provider = create_provider(request.provider_id)
        except ValueError as error:
            logger.warning("unknown providerId=%s", request.provider_id)
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=str(error),
            ) from error

        logger.info(
            "home-content request game_id=%s provider_id=%s request_id=%s",
            request.game_id,
            request.provider_id,
            request.request_id,
        )

        # Real adapters raise NotImplementedError until their fixed samples
        # ship; fall back to the deterministic sample so the WinUI client
        # can be wired today and the envelope stays schema-valid.
        try:
            content = await provider.fetch(request)
            return HomeContentEnvelope(
                schema_version=1,
                request_id=request.request_id,
                provider_id=request.provider_id,
                fetched_at=datetime.now(timezone.utc),
                content=content,
                errors=[],
            )
        except NotImplementedError:
            logger.info(
                "provider %s not implemented, returning sample envelope",
                request.provider_id,
            )
            return build_sample_envelope(request)
        except (httpx.HTTPError, ValueError, ValidationError) as error:
            # Provider fetches the upstream launcher API; on failure we
            # surface the error in the envelope so the WinUI client can
            # decide whether to keep the cached background or fall back
            # to the static placeholder. Schema validity is preserved.
            logger.warning(
                "provider %s failed (%s): %s",
                request.provider_id,
                type(error).__name__,
                error,
            )
            return HomeContentEnvelope(
                schema_version=1,
                request_id=request.request_id,
                provider_id=request.provider_id,
                fetched_at=datetime.now(timezone.utc),
                content=HomeContent(),
                errors=[
                    HomeContentError(
                        code=f"provider_{type(error).__name__}",
                        message=str(error)
                        or f"{request.provider_id} provider failed without message.",
                        recoverable=True,
                    )
                ],
            )

    @app.post(
        "/v1/screenshot-paths",
        response_model=GameScreenshotPathEnvelope,
        tags=["screenshot-paths"],
    )
    def fetch_screenshot_path(
        request: GameScreenshotPathRequest,
    ) -> GameScreenshotPathEnvelope:
        logger.info(
            "screenshot-path request game_id=%s request_id=%s",
            request.game_id,
            request.request_id,
        )
        # TODO: route to the real screenshot_paths registry once available.
        sample = build_sample_screenshot_path(request.request_id, request.game_id)
        return GameScreenshotPathEnvelope.model_validate(sample)

    return app


# Module-level app instance so `uvicorn home_content.server.app:app` works.
app = create_app()