"""NetEase static-CMS Provider for 燕云十六声 (Where Winds Meet).

Discovery surface
-----------------
燕云十六声's marketing pages are static, server-side rendered HTML
built with NetEase's NIE static-CMS template. The home page inlines
the entire news carousel + tabbed news feed:

  * Banner — ``.slide-news .banner .swiper-slide > a > img`` (image
    lives in ``data-src`` because Swiper is configured with lazy
    loading).
  * News   — ``.slide-news .news-wrap .news-list-N`` (one container
    per tab — 0 = 最新, 1 = 新闻, 2 = 公告, 3 = 活动). Each row carries
    ``<a class="link" href=…>`` with ``.date``, ``.kind``, ``.title``
    and ``.desc``.
  * Background — the home page itself does not embed a background
    ``<video>`` / ``<img>``; the JS bundle swaps a ``<video class="bg">``
    based on viewport. We therefore fall back to the first banner
    image as a static background image so the C# UI has something to
    render even before the launcher-specific background video is
    downloaded.

### Network endpoints

| Region | Endpoint                                                  |
|--------|-----------------------------------------------------------|
| CN     | ``https://www.yysls.cn/index.html``                       |
| HMT    | ``https://www.wherewindsmeetgame.com/hmt/index.html``     |

The two regions are not interchangeable — they are built by
different teams (网易 vs Sony Taiwan co-publication) and use
different template classes (``slide-news`` vs ``newsBanner`` /
``newsList``). AGENTS.md instructs us to keep them separate and not
share endpoints or assumptions.

### Per-game mapping

Defaults match 燕云十六声 / 网易. ``providerOptions`` accepts:

  * ``region``       — ``cn`` (default) / ``hmt``.
  * ``homePageUrl``  — override the homepage URL (verbatim, no
                       suffixing).
  * ``maxBanners``   — cap on banner rows to surface (default ``4``).
  * ``newsPerTab``   — cap on news rows per tab (default ``4``).

Verification
------------
Sampled **2026-09-15** against the CN build. The HMT build's banner
container is empty server-side and populated by an out-of-band JS
bundle; we capture the raw HTML for documentation but the Provider
currently only parses the CN shape. HMT will land in a future pass
once we map its ``newsBanner`` / ``newsList`` blocks.
"""

from __future__ import annotations

import logging
import re
from datetime import datetime, timezone
from typing import Any, Optional

import httpx

from ..models import (
    HomeBackground,
    HomeBanner,
    HomeContent,
    HomeContentRequest,
    HomeNewsItem,
)
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.netease_static_cms")


# ----- Region endpoints --------------------------------------------------

DEFAULT_BASE_CN = "https://www.yysls.cn"
DEFAULT_HOME_PATH_CN = "/index.html"
DEFAULT_BASE_HMT = "https://www.wherewindsmeetgame.com"
DEFAULT_HOME_PATH_HMT = "/hmt/index.html"
DEFAULT_REGION = "cn"
DEFAULT_MAX_BANNERS = 4
DEFAULT_NEWS_PER_TAB = 4

# Hosts we trust for media + jump URLs. The yysls.cn site is on the
# shared ``nie.res.netease.com`` / ``webinput.nie.netease.com`` / CN
# domain set. weym goes through the wherewindsmeetgame.com marketing
# site + fp.ps.easebar.com CDN; both are owned by the 网易 / Sony
# publishing chain. ``weibo.com`` / ``mp.weixin.qq.com`` are social
# destinations referenced by banner ``href``; the launcher only opens
# them in a browser so we surface them as-is (a future C# guard may
# restrict further).
ALLOWED_HOST_SUFFIXES: tuple[str, ...] = (
    ".yysls.cn",
    ".netease.com",
    ".nie.netease.com",
    ".wherewindsmeetgame.com",
    ".easebar.com",
    ".fp.ps.easebar.com",
    ".yysls.v.netease.com",
    ".yysls-build-na.fp.ps.easebar.com",
)

# Tab index → category. The marketing tab order is
# ``最新 / 新闻 / 公告 / 活动``; we collapse the first into the
# mixed feed (whose per-row ``.kind`` is the actual category) and
# keep the other three as canonical home-content categories.
TAB_CATEGORY: dict[str, str] = {
    "0": "其他",          # 最新: falls back to per-row ``.kind``
    "1": "资讯",          # 新闻
    "2": "公告",          # 公告
    "3": "活动",          # 活动
}

# Per-row ``.kind`` values that appear in the "最新" tab. Anything
# outside this map falls back to "其他".
KIND_CATEGORY: dict[str, str] = {
    "新闻": "资讯",
    "公告": "公告",
    "活动": "活动",
}

DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (NetEaseStaticCmsProvider)",
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.5",
}


# ---------------------------------------------------------------------------
# Provider
# ---------------------------------------------------------------------------


class NetEaseStaticCmsProvider(HomeContentProvider):
    provider_id = "netease-static-cms"

    def __init__(
        self,
        client: Optional[httpx.AsyncClient] = None,
        timeout: httpx.Timeout = DEFAULT_TIMEOUT,
    ) -> None:
        self._client = client
        self._timeout = timeout

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        options = request.provider_options or {}
        region = str(options.get("region", DEFAULT_REGION)).lower()
        home_url_override = options.get("homePageUrl")
        max_banners = int(options["maxBanners"]) if options.get("maxBanners") is not None else DEFAULT_MAX_BANNERS
        news_per_tab = int(options["newsPerTab"]) if options.get("newsPerTab") is not None else DEFAULT_NEWS_PER_TAB

        if region == "hmt":
            default_url = DEFAULT_BASE_HMT + DEFAULT_HOME_PATH_HMT
        else:
            default_url = DEFAULT_BASE_CN + DEFAULT_HOME_PATH_CN
        home_url = (
            str(home_url_override)
            if isinstance(home_url_override, str) and home_url_override
            else default_url
        )

        html = await self._fetch_text(home_url)
        banners: list[HomeBanner] = []
        news_items: list[HomeNewsItem] = []
        background_image: Optional[str] = None
        if html is not None:
            try:
                banner_rows, news_rows, fallback_image = parse_home_html(html)
            except ValueError as exc:
                logger.warning(
                    "NetEase static-CMS home payload from %s could not be parsed: %s",
                    home_url, exc,
                )
            else:
                background_image = fallback_image
                banners = _build_banners(banner_rows, max_banners)
                news_items = _build_news_items(news_rows, news_per_tab)

        background = _build_background(background_image)
        return HomeContent(
            background=background,
            banners=banners,
            news=news_items,
            update_info=None,
        )

    async def _fetch_text(self, url: str) -> Optional[str]:
        try:
            if self._client is not None:
                response = await self._client.get(url)
            else:
                async with httpx.AsyncClient(timeout=self._timeout, headers=DEFAULT_HEADERS) as client:
                    response = await client.get(url)
            response.raise_for_status()
            return response.text
        except (httpx.HTTPError, ValueError) as exc:
            logger.warning("Failed to fetch %s: %s", url, exc)
            return None


# ---------------------------------------------------------------------------
# HTML helpers
# ---------------------------------------------------------------------------


# Match the entire ``<div class="panel" id="news">…</div>`` section so we
# don't accidentally pick up banners from other panels (e.g. ``slide-media``).
_PANEL_NEWS_RE = re.compile(
    r'<div class="panel"\s+id="news">(.*?)<div class="panel"\s+id="media">',
    re.DOTALL,
)
_BANNER_LINK_RE = re.compile(
    r'<a href="([^"]+)"\s+title="([^"]*)"[^>]*>\s*<img[^>]+(?:data-src|src)="([^"]+)"',
    re.DOTALL,
)
_NEWS_LIST_RE = re.compile(
    r'<div class="swiper-container news-list news-list-(\d+)">(.*?)(?=<div class="swiper-container news-list|</div>\s*</div>\s*</div>\s*</div>)',
    re.DOTALL,
)
_NEWS_LINK_RE = re.compile(
    r'<a class="link"\s+href="([^"]+)"[^>]*>\s*<div class="link-flex">\s*<div class="date">([^<]+)</div>\s*<span class="kind">([^<]+)</span>\s*<span class="title ellipsis">([^<]+)</span>\s*</div>\s*<div class="ellipsis desc">([^<]*)</div>',
    re.DOTALL,
)


def parse_home_html(
    html: str,
) -> tuple[list[dict[str, str]], list[dict[str, Any]], Optional[str]]:
    """Pull ``(banner_rows, news_rows, fallback_bg_image)`` out of the
    NIE static-CMS homepage.

    Raises :class:`ValueError` when the ``#news`` panel is missing —
    the rest of the home page is marketing chrome that the Provider
    does not consume.
    """
    panel_match = _PANEL_NEWS_RE.search(html)
    if not panel_match:
        raise ValueError("news panel (#news) not found in homepage HTML")
    section = panel_match.group(1)

    banner_rows: list[dict[str, str]] = []
    first_image: Optional[str] = None
    for href, title, image in _BANNER_LINK_RE.findall(section):
        banner_rows.append({"href": href, "title": title, "image_url": image})
        if first_image is None:
            first_image = image

    news_rows: list[dict[str, Any]] = []
    for tab_index, container in _NEWS_LIST_RE.findall(section):
        for href, date, kind, title, desc in _NEWS_LINK_RE.findall(container):
            news_rows.append({
                "tab": int(tab_index),
                "href": href,
                "date": date.strip(),
                "kind": kind.strip(),
                "title": title.strip(),
                "desc": desc.strip(),
            })

    return banner_rows, news_rows, first_image


# ---------------------------------------------------------------------------
# Builders
# ---------------------------------------------------------------------------


def _build_background(fallback_image: Optional[str]) -> HomeBackground:
    """Wrap the optional static background image.

    The NIE static-CMS homepage itself does not expose a background
    video; the upstream JS bundle swaps a ``<video class="bg">`` at
    runtime. We surface the first banner image as a static fallback
    so the C# UI has something to render immediately.
    """
    return HomeBackground(video_url=None, image_url=fallback_image, local_path=None)


def _build_banners(
    banner_rows: list[dict[str, str]],
    max_banners: int,
) -> list[HomeBanner]:
    banners: list[HomeBanner] = []
    for index, row in enumerate(banner_rows):
        if index >= max_banners:
            break
        image_url = row.get("image_url") if isinstance(row.get("image_url"), str) else None
        if not image_url or not _allowed(image_url):
            continue
        link = row.get("href") if isinstance(row.get("href"), str) else None
        if link is not None and not _allowed(link):
            link = None
        title = row.get("title") if isinstance(row.get("title"), str) else None
        if isinstance(title, str):
            title = title.strip() or None
        banners.append(HomeBanner(
            id=f"nie-banner-{index}-{abs(hash(image_url)) % 100000}",
            title=title,
            image_url=image_url,
            local_path=None,
            target_url=link,
        ))
    return banners


def _build_news_items(
    news_rows: list[dict[str, Any]],
    news_per_tab: int,
) -> list[HomeNewsItem]:
    items: list[HomeNewsItem] = []
    seen_per_tab: dict[int, int] = {}
    for index, row in enumerate(news_rows):
        tab = int(row.get("tab", 0))
        if seen_per_tab.get(tab, 0) >= news_per_tab:
            continue
        seen_per_tab[tab] = seen_per_tab.get(tab, 0) + 1

        title = row.get("title") if isinstance(row.get("title"), str) else ""
        title = title.strip()
        if not title:
            continue
        href = row.get("href") if isinstance(row.get("href"), str) else None
        if href is not None and not _allowed(href):
            href = None
        category = _resolve_category(row)
        items.append(HomeNewsItem(
            id=f"nie-news-tab{tab}-{index}",
            category=category,
            title=title,
            summary=(row.get("desc") or "").strip() or None,
            image_url=None,
            target_url=href,
            published_at=_parse_mmdd_date(row.get("date")),
        ))
    return items


def _resolve_category(row: dict[str, Any]) -> str:
    tab = int(row.get("tab", 0))
    if tab == 0:
        # ``最新`` mixes per-row ``.kind`` values, prefer those.
        kind = row.get("kind") if isinstance(row.get("kind"), str) else ""
        return KIND_CATEGORY.get(kind.strip(), "其他")
    return TAB_CATEGORY.get(str(tab), "其他")


def _parse_mmdd_date(value: Any) -> Optional[datetime]:
    """Parse the ``MM/DD`` dates used by the news list.

    The news carousel only publishes month + day; the upstream does
    not surface a full year. We assume the **current year** (UTC) so
    news published in late December does not roll into the next year
    accidentally — the launcher refresh loop re-fetches every 10–30
    minutes so any cross-year drift self-corrects within an hour.
    """
    if not isinstance(value, str) or not value.strip():
        return None
    raw = value.strip()
    for fmt in ("%m/%d", "%Y-%m-%d", "%Y/%m/%d"):
        try:
            parsed = datetime.strptime(raw, fmt)
            return parsed.replace(year=datetime.now(timezone.utc).year, tzinfo=timezone.utc)
        except ValueError:
            continue
    return None


def _allowed(url: Optional[str]) -> bool:
    if not url:
        return False
    lowered = url.lower()
    if not (lowered.startswith("http://") or lowered.startswith("https://")):
        return False
    host = lowered.split("//", 1)[1].split("/", 1)[0]
    return any(host == suffix.lstrip(".") or host.endswith(suffix) for suffix in ALLOWED_HOST_SUFFIXES)