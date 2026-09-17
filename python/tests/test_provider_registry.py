"""Provider registry unit tests.

The registry maps stable ``provider_id`` strings to their adapter classes so
that the FastAPI server and the WinUI client can reference a Provider without
importing its concrete class. These tests pin the registry shape:

* every real Provider exposes a non-empty ``provider_id``
* the registry contains exactly the seven shipped Providers
* ``create_provider`` returns a usable instance
* unknown IDs raise ``ValueError`` (FastAPI surfaces this as HTTP 400)
* adding a new Provider requires updating the registry — the explicit tuple
  here is the single source of truth that protects against a Provider landing
  without being wired through to the worker process.
"""

from __future__ import annotations

import pytest

from home_content.provider_registry import PROVIDER_TYPES, create_provider
from home_content.providers import (
    HoYoPlayJsonProvider,
    HypergryphBatchProvider,
    KuroLauncherProvider,
    LocalLauncherAssetProvider,
    NetEaseStaticCmsProvider,
    NextJsDataProvider,
    PerfectWorldHybridProvider,
)
from home_content.providers.base import HomeContentProvider

EXPECTED_PROVIDERS: dict[str, type[HomeContentProvider]] = {
    "hoyoplay-json": HoYoPlayJsonProvider,
    "kuro-launcher": KuroLauncherProvider,
    "hypergryph-batch": HypergryphBatchProvider,
    "perfect-world-hybrid": PerfectWorldHybridProvider,
    "nextjs-data": NextJsDataProvider,
    "netease-static-cms": NetEaseStaticCmsProvider,
    "local-launcher-asset": LocalLauncherAssetProvider,
}


def test_registry_contains_all_expected_providers() -> None:
    """The seven shipped Providers must all be reachable by their stable ID."""
    assert set(PROVIDER_TYPES.keys()) == set(EXPECTED_PROVIDERS.keys())


def test_registry_ids_match_provider_classes() -> None:
    """Each ID in the registry maps to the exact class we expect."""
    for provider_id, provider_cls in EXPECTED_PROVIDERS.items():
        assert PROVIDER_TYPES[provider_id] is provider_cls, (
            f"{provider_id} registered as {PROVIDER_TYPES[provider_id].__name__}, "
            f"expected {provider_cls.__name__}"
        )


def test_provider_classes_expose_distinct_ids() -> None:
    """Two Providers must not share the same ``provider_id`` — collision would
    silently break routing in :func:`create_provider`."""
    ids = [provider_cls.provider_id for provider_cls in EXPECTED_PROVIDERS.values()]
    assert len(ids) == len(set(ids)), f"duplicate provider_id in {ids}"


def test_provider_ids_are_non_empty_strings() -> None:
    """``provider_id`` is the routing key; an empty value would silently fall
    back to the wrong Provider."""
    for provider_cls in EXPECTED_PROVIDERS.values():
        assert isinstance(provider_cls.provider_id, str)
        assert provider_cls.provider_id, f"{provider_cls.__name__} has empty provider_id"
        assert provider_cls.provider_id == provider_cls.provider_id.strip(), (
            f"{provider_cls.__name__} provider_id has surrounding whitespace"
        )


def test_create_provider_returns_usable_instance() -> None:
    """``create_provider`` must return an instance that can be invoked via
    ``fetch``; we don't actually hit the network here — Kuro's fetch would
    need real CDN responses — but we can verify construction + abstract type."""
    for provider_id, provider_cls in EXPECTED_PROVIDERS.items():
        instance = create_provider(provider_id)
        assert isinstance(instance, provider_cls), (
            f"create_provider({provider_id!r}) returned {type(instance).__name__}, "
            f"expected {provider_cls.__name__}"
        )
        assert isinstance(instance, HomeContentProvider)
        assert instance.provider_id == provider_id


def test_create_provider_rejects_unknown_id() -> None:
    """An ID outside the registry must raise ``ValueError`` so the FastAPI
    layer can map it to HTTP 400 instead of silently substituting another
    Provider."""
    with pytest.raises(ValueError) as exc_info:
        create_provider("not-a-real-provider")
    assert "not-a-real-provider" in str(exc_info.value)
    assert "Unknown providerId" in str(exc_info.value)


def test_create_provider_empty_string_raises() -> None:
    """Empty string is not a registered ID and must be rejected."""
    with pytest.raises(ValueError):
        create_provider("")


def test_create_provider_is_case_sensitive() -> None:
    """Provider IDs are stable identifiers; case differences must not silently
    match. This guards against accidental lowercase typos like ``"Kuro-Launcher"``
    resolving to ``"kuro-launcher"``."""
    with pytest.raises(ValueError):
        create_provider("Kuro-Launcher")
    with pytest.raises(ValueError):
        create_provider("KURO-LAUNCHER")


def test_registry_classes_subclass_base() -> None:
    """Every registered class must inherit :class:`HomeContentProvider` so the
    server can treat them uniformly."""
    for provider_cls in EXPECTED_PROVIDERS.values():
        assert issubclass(provider_cls, HomeContentProvider), (
            f"{provider_cls.__name__} does not inherit HomeContentProvider"
        )