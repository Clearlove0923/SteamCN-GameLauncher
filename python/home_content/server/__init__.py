"""FastAPI service that exposes HomeContent and screenshot-path envelopes
to the WinUI client. Diagnostics go to stderr; the final envelope JSON is
written to stdout via FastAPI's response machinery."""

from .app import create_app

__all__ = ["create_app"]