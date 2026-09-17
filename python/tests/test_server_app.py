"""FastAPI server integration tests.

Exercises the ``/healthz`` and ``/v1/home-content`` endpoints end-to-end
through ASGI transport, without binding to a TCP port or relying on
uvicorn. The goals:

* the wiring ``create_provider(provider_id) -> fetch()`` actually returns a
  real envelope, not the deterministic sample stub, when a real Provider is
  reachable.
* unknown provider IDs surface as HTTP 400 (FastAPI layer translates the
  registry's ``ValueError``).
* Provider-side failures (``NotImplementedError``, ``httpx.HTTPError``,
  ``ValueError``, ``pydantic.ValidationError``) flow into the
  ``HomeContentEnvelope.errors`` channel so the WinUI client can degrade
  gracefully instead of receiving an HTTP error.
* schema envelope fields (``schema_version``, ``request_id``, ``provider_id``,
  ``fetched_at``) round-trip from request to response without rewriting.
"""

from __future__ import annotations

from typing import Any

import httpx
import pytest
from fastapi import FastAPI
from httpx import ASGITransport
from pydantic import ValidationError

from home_content.models import (
    HomeContent,
    HomeContentEnvelope,
    HomeContentRequest,
)
from home_content.providers.base import HomeContentProvider
from home_content.server.app import app as server_app
from home_content.server.app import create_app


def _request_payload(provider_id: str = "kuro-launcher") -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "requestId": "req-1",
        "gameId": "wuthering-waves",
        "providerId": provider_id,
        "locale": "en",
        "providerOptions": {},
    }


def _client(app: FastAPI) -> httpx.AsyncClient:
    """Build an httpx AsyncClient that talks to the FastAPI app in-process."""
    transport = ASGITransport(app=app)
    return httpx.AsyncClient(transport=transport, base_url="http://testserver")


@pytest.mark.asyncio
async def test_healthz_returns_ok() -> None:
    async with _client(server_app) as client:
        result = await client.get("/healthz")
    assert result.status_code == 200
    body = result.json()
    assert body["status"] == "ok"
    assert "now" in body


@pytest.mark.asyncio
async def test_home_content_routes_through_real_provider_registry(monkeypatch) -> None:
    """When a Provider returns a HomeContent, the server wraps it in an
    envelope and forwards every field through. We monkeypatch one Provider so
    the test does not touch the network."""

    captured: dict[str, Any] = {}

    class FakeKuroProvider(HomeContentProvider):
        provider_id = "kuro-launcher"

        async def fetch(self, request: HomeContentRequest) -> HomeContent:
            captured["game_id"] = request.game_id
            captured["provider_id"] = request.provider_id
            return HomeContent(
                banners=[],
            )

    monkeypatch.setattr(
        "home_content.server.app.create_provider",
        lambda _provider_id: FakeKuroProvider(),
    )

    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=_request_payload())

    assert result.status_code == 200, result.text
    body = result.json()
    assert body["schemaVersion"] == 1
    assert body["requestId"] == "req-1"
    assert body["providerId"] == "kuro-launcher"
    assert body["content"]["banners"] == []
    assert body["errors"] == []
    assert captured == {
        "game_id": "wuthering-waves",
        "provider_id": "kuro-launcher",
    }


@pytest.mark.asyncio
async def test_home_content_rejects_unknown_provider() -> None:
    async with _client(server_app) as client:
        result = await client.post(
            "/v1/home-content", json=_request_payload("not-a-real-provider")
        )
    assert result.status_code == 400, result.text
    detail = result.json()["detail"]
    assert "not-a-real-provider" in detail


@pytest.mark.asyncio
async def test_home_content_rejects_empty_provider_id() -> None:
    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=_request_payload(""))
    assert result.status_code == 400


@pytest.mark.asyncio
async def test_home_content_falls_back_to_sample_when_not_implemented(
    monkeypatch,
) -> None:
    """A Provider raising :class:`NotImplementedError` (the contract for
    placeholders) should not break the envelope contract — the server must
    return a deterministic sample so the WinUI client always has something
    to render."""

    class PlaceholderProvider(HomeContentProvider):
        provider_id = "kuro-launcher"

        async def fetch(self, request: HomeContentRequest) -> HomeContent:
            raise NotImplementedError("Provider 'kuro-launcher' has not been implemented")

    monkeypatch.setattr(
        "home_content.server.app.create_provider",
        lambda _provider_id: PlaceholderProvider(),
    )

    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=_request_payload())

    assert result.status_code == 200
    body = result.json()
    assert body["providerId"] == "kuro-launcher"
    # Sample provider fills background/banners/news/update_info
    assert body["content"]["background"]["videoUrl"] == "https://cdn.example.invalid/home/bg.mp4"
    assert len(body["content"]["banners"]) == 2
    assert len(body["content"]["news"]) == 3
    assert body["errors"] == []


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "exc",
    [
        httpx.ConnectError("upstream unreachable"),
        httpx.HTTPStatusError(
            "503 Service Unavailable",
            request=httpx.Request("GET", "https://example.invalid/"),
            response=httpx.Response(503),
        ),
        ValueError("malformed upstream response"),
    ],
)
async def test_home_content_surfaces_provider_failures_as_structured_errors(
    monkeypatch,
    exc: Exception,
) -> None:
    """Upstream or parsing failures must round-trip as ``HomeContentEnvelope.errors``
    so the WinUI client can keep the cached background or fall back to a
    static placeholder. The HTTP response stays 200 (envelope valid) and the
    body keeps a (possibly empty) ``content``."""

    class FailingProvider(HomeContentProvider):
        provider_id = "kuro-launcher"

        async def fetch(self, request: HomeContentRequest) -> HomeContent:
            raise exc

    monkeypatch.setattr(
        "home_content.server.app.create_provider",
        lambda _provider_id: FailingProvider(),
    )

    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=_request_payload())

    assert result.status_code == 200
    body = result.json()
    assert body["content"]["background"] is None
    assert body["content"]["banners"] == []
    assert len(body["errors"]) == 1
    error = body["errors"][0]
    assert error["code"].startswith("provider_")
    assert error["recoverable"] is True
    # Generic error message no longer hardcodes the HoYoPlay vendor name
    assert "HoYoPlay" not in error["message"]


@pytest.mark.asyncio
async def test_home_content_surfaces_validation_error_as_structured_error(
    monkeypatch,
) -> None:
    """``pydantic.ValidationError`` must also flow into ``errors``."""

    class InvalidProvider(HomeContentProvider):
        provider_id = "kuro-launcher"

        async def fetch(self, request: HomeContentRequest) -> HomeContent:
            raise ValidationError.from_exception_data(
                "FakeModel",
                [{"type": "missing", "loc": ("field",), "input": {}, "msg": "field required"}],
            )

    monkeypatch.setattr(
        "home_content.server.app.create_provider",
        lambda _provider_id: InvalidProvider(),
    )

    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=_request_payload())

    assert result.status_code == 200
    body = result.json()
    assert len(body["errors"]) == 1
    assert body["errors"][0]["code"] == "provider_ValidationError"
    assert body["errors"][0]["recoverable"] is True


@pytest.mark.asyncio
async def test_request_id_round_trips_to_envelope(monkeypatch) -> None:
    """``requestId`` must round-trip so the WinUI client can correlate the
    request and response (and so failed requests show up in the right log
    entry)."""

    class StubProvider(HomeContentProvider):
        provider_id = "kuro-launcher"

        async def fetch(self, request: HomeContentRequest) -> HomeContent:
            return HomeContent()

    monkeypatch.setattr(
        "home_content.server.app.create_provider",
        lambda _provider_id: StubProvider(),
    )

    payload = _request_payload()
    payload["requestId"] = "custom-request-id-42"

    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=payload)

    assert result.status_code == 200
    assert result.json()["requestId"] == "custom-request-id-42"


@pytest.mark.asyncio
async def test_create_app_returns_fresh_app_per_call() -> None:
    """``create_app`` must be safe to call multiple times — useful for tests
    that want isolated apps and avoids hidden module-level state."""
    a = create_app()
    b = create_app()
    assert a is not b
    assert a.title == b.title


@pytest.mark.asyncio
async def test_healthz_is_accessible_without_request_body() -> None:
    """Health probes must not require any body — they are GET-only and the
    WinUI client polls before scheduling a fetch."""
    async with _client(server_app) as client:
        result = await client.get("/healthz")
    assert result.status_code == 200


@pytest.mark.asyncio
async def test_home_content_request_id_required() -> None:
    """``requestId`` is part of the contract — pydantic raises 422 when missing,
    not 400."""
    payload = _request_payload()
    payload.pop("requestId")

    async with _client(server_app) as client:
        result = await client.post("/v1/home-content", json=payload)

    assert result.status_code == 422