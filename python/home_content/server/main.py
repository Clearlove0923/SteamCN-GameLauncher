"""Console entry point for the home-content worker.

The WinUI launcher process spawns this script as a child process. All
configuration (host, port, log level) comes from environment variables
that the C# side injects; nothing is hard-coded here.
"""

from __future__ import annotations

import os
import sys

import uvicorn


def main() -> int:
    host = os.environ.get("HOMECONTENT_HOST", "127.0.0.1")
    port = int(os.environ.get("HOMECONTENT_PORT", "8765"))
    log_level = os.environ.get("HOMECONTENT_LOG_LEVEL", "info")

    # stderr / stdout buffering: keep stderr line-buffered for live logs,
    # stdout fully buffered so the JSON envelope stays coherent.
    sys.stderr.reconfigure(line_buffering=True)
    sys.stdout.reconfigure(line_buffering=True)

    uvicorn.run(
        "home_content.server.app:app",
        host=host,
        port=port,
        log_level=log_level,
        # The C# parent owns the lifecycle; never have uvicorn spawn a
        # reload watcher or a second worker.
        reload=False,
        workers=1,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())