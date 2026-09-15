# LocalLauncherAssetProvider (本地启动器资源)

`provider_id = "local-launcher-asset"`. The local-only counterpart to
every network source adapter. Walks a single launcher install root
and surfaces the canonical ``bg.mp4`` / ``bg.jpg`` / ``config.json``
files the C# UI knows how to render.

## Discovery surface

| Channel | Purpose                                                                 |
|---------|-------------------------------------------------------------------------|
| `<installDir>/bg.mp4` (preferred) | Background video shipped with the launcher |
| `<installDir>/bg.jpg` (fallback)  | Static background image when no video is present |
| `<installDir>/config.json` (optional) | Launcher manifest — surfaced as `HomeUpdateInfo` |

The provider never makes HTTP requests and never writes to disk.
The launcher install root must be supplied by the C# side via
`providerOptions.installDir` — the launcher self-detection lives in
C# so this Python worker does not have to guess registry / Steam
library paths on Windows.

## Response layout

```text
HomeContent
├── background
│   ├── video_url    = file:///C:/Program%20Files/Where%20Winds%20Meet/bg.mp4
│   ├── image_url    = file:///C:/Program%20Files/Where%20Winds%20Meet/bg.jpg
│   └── local_path   = C:/Program Files/Where Winds Meet/bg.mp4
├── banners         = []
├── news            = []
└── update_info
    ├── version     = "2.9.0"
    ├── title       = "燕云十六声 v2.9.0"
    ├── summary     = "「芳草意留人」版本更新"
    ├── target_url  = "https://www.yysls.cn/download/"
    └── published_at = 2026-09-10T03:00:00+08:00
```

* `video_url` is wrapped as a `file://` URI so the C# side can pass
  it to its native media control with the same code path used for
  HTTP sources.
* `image_url` is populated when no video is found, otherwise it is
  skipped (the launcher almost always ships both).
* `local_path` is the raw absolute filesystem path (`Path.as_posix()`)
  for code paths that prefer filesystem paths over URIs.
* `banners` and `news` are intentionally empty — launchers do not
  expose a banner / news feed at the launcher install root; that
  surface comes from the network-backed Providers.

## Asset probing

The provider walks the install root to a bounded depth and picks the
first existing file from each ordered candidate list:

| Channel   | Default candidates (in order)                                  |
|-----------|----------------------------------------------------------------|
| Video     | `["bg.mp4", "bg.webm"]`                                       |
| Image     | `["bg.jpg", "bg.png", "background.jpg", "background.png"]`     |
| Config    | `["config.json"]`                                              |

The probe depth defaults to **2** so it reaches assets that live in
the conventional `assets/`, `video/`, or `media/` subdirectory
without ever recursing into a stray `.git/`, `logs/`, or `cache/`
folder. Pass `maxDepth=1` to restrict the search to the install
root only.

## Per-game mapping

Defaults match the layout observed across the launchers we already
integrate (完美世界异环 / 库洛鸣潮 / 鹰角终末地 / 叠纸无限暖暖).
`providerOptions` accepts:

| Key              | Default                                                                | Meaning                                              |
|------------------|------------------------------------------------------------------------|------------------------------------------------------|
| `installDir`     | **required**                                                           | Absolute path to the launcher install root           |
| `videoFileNames` | `["bg.mp4", "bg.webm"]`                                                | Ordered list of background video candidates          |
| `imageFileNames` | `["bg.jpg", "bg.png", "background.jpg", "background.png"]`              | Ordered list of background image candidates          |
| `configFileName` | `"config.json"`                                                        | Basename of the launcher config JSON. Set to `""` to disable. |
| `maxDepth`       | `2`                                                                    | How deep the provider looks (`1` = root only)         |

Both `videoFileNames` and `imageFileNames` also accept a single
comma-separated string for convenience
(`"bg.mp4,bg.webm"`).

## Cross-platform behaviour

* On Windows the absolute path is rendered as `C:/Program Files/...`
  and `Path.as_uri()` percent-encodes the spaces, so the surfaced
  URL is `file:///C:/Program%20Files/Where%20Winds%20Meet/bg.mp4`.
  `local_path` keeps the un-encoded form for filesystem consumers.
* On POSIX the absolute path is rendered as `/opt/...` and the URL
  is `file:///opt/...` (no percent-encoding of `/` or `:`).
* When `Path("C:/foo").resolve()` resolves a POSIX-style install
  directory (`/opt/...`) on Windows, the FakeFS used by tests
  automatically prefixes the lookup key with `C:/` so add and
  lookup paths stay in sync.

## Verification

Sampled **2026-09-15** against a synthetic launcher layout mirroring
完美世界 / 库洛 / 鹰角 / 叠纸 launchers. There is no production
deployment yet — the Provider is exercised by `pytest` with
`file_reader` / `exists_checker` injection so no real filesystem
access happens during tests. The fixtures at
`contracts/samples/launcher-local-config-{win,mac,linux}.json`
describe the three launcher layouts the test suite walks through.

| Fixture                                | Captured                                  |
|----------------------------------------|-------------------------------------------|
| `launcher-local-config-win.json`       | Windows install (`C:/Program Files/...`)  |
| `launcher-local-config-mac.json`       | macOS `.app` bundle (`/Applications/...`) |
| `launcher-local-config-linux.json`     | Linux `assets/` layout (`/opt/...`)       |

Tests run without any network access (no `httpx`) and without touching
the real filesystem. The total assertion count is **28** (see
`python/tests/test_local_launcher_asset_provider.py`).

## Limitations / things to revisit

* The provider does **not** discover launcher install paths by
  itself — the C# side is responsible for registry / Steam library
  / `~/Applications` resolution and feeds the resulting absolute
  path through `providerOptions.installDir`. This keeps the Python
  worker free of platform-specific shell-out code and avoids
  accidentally scanning the user's filesystem.
* `banners` / `news` are always empty. Launchers rarely (if ever)
  ship a banner carousel or news feed as part of the install root;
  those surfaces come from the network-backed Providers. If a
  launcher ever does ship local assets (e.g. a promotional banner
  bundled with the installer), the next iteration can extend
  `LocalLauncherAssetProvider` to surface them without breaking
  the existing fetch contract.
* `config.json` parsing is intentionally narrow: only `version`,
  `title`, `summary`, `downloadUrl`, `publishedAt` are read. Adding
  more fields requires updating both the test fixture and the C#
  DTO contract.
* `maxDepth=2` is the default so the provider reaches `assets/`,
  `video/`, and `media/` subdirectories — the conventional locations
  for Linux launchers and macOS `.app` bundles. Launchers that
  scatter assets deeper than two levels (rare in practice) need
  the override.
* The probe never recurses into ``.git/``, ``logs/``, ``cache/`` or
  any other "ignore me" directory; only the canonical
  ``assets/`` / ``video/`` / ``media/`` names are considered. If a
  launcher ships assets in a non-standard subdirectory, pass it via
  ``providerOptions`` or open a follow-up to add it to the default
  list.
* Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册;
  `/v1/home-content` 用 `providerId=local-launcher-asset` 仍走
  sample envelope. Registration is a future iteration's work.
* MIME / MD5 / size validation is not performed; per AGENTS.md the
  C# `HttpHomeContentTransport` cache layer owns that (todo).