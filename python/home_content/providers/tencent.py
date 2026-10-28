"""Tencent company Provider for Mainland China launcher and website content.

The first supported game is 洛克王国：世界.  Its official PC launcher keeps
the current home animation in a fixed ``launcher/tpf_ui`` path, while public
news and carousel cards are rendered by the official ``rocom.qq.com`` site.
The local lookup is deliberately narrow: it never scans game movies and it
only accepts validated MP4/PNG files from the verified launcher layout.
"""

from __future__ import annotations

import hashlib
import logging
import re
from datetime import datetime, timedelta, timezone
from html.parser import HTMLParser
from pathlib import Path
from typing import Any, Optional
from urllib.parse import urljoin, urlparse

import httpx

from ..game_source_registry import get_game_source_by_id
from ..models import HomeBackground, HomeBanner, HomeContent, HomeContentRequest, HomeNewsItem
from .base import HomeContentProvider

logger = logging.getLogger("home_content.providers.tencent")

DEFAULT_HOME_URL = "https://rocom.qq.com/"
DEFAULT_LAUNCHER_CONTENT_URL = (
    "https://apps.game.qq.com/cmc/cross"
    "?serviceId=467&source=pc_launcher&limit=50&filter=tag"
    "&tagids=139426,137617,137629,139435&logic=or"
)
DEFAULT_TIMEOUT = httpx.Timeout(connect=5.0, read=10.0, write=5.0, pool=5.0)
DEFAULT_HEADERS = {
    "User-Agent": "SteamCN-GameLauncher/1.0 (TencentProvider)",
    "Accept": "text/html,application/xhtml+xml",
    "Accept-Language": "zh-CN,zh;q=0.9",
}

_MIN_VIDEO_BYTES = 256 * 1024
_MAX_VIDEO_BYTES = 512 * 1024 * 1024
_MAX_PARENT_LEVELS = 8
_VIDEO_PATHS = (
    Path("launcher/tpf_ui/res/videos/main_ui/main_video.mp4"),
    Path("launcher/tpf_ui/res/videos/main_ui/main_video_temp.mp4"),
    Path("launcher/tpf_ui/res/videos/login/login_video.mp4"),
)
_IMAGE_PATHS = (
    Path("launcher/data/client_ui/web/assets/launcher-bg.png"),
    Path("launcher/data/client_ui/web/assets/launcher-bk.png"),
    Path("Win64/NRC/Binaries/Win64/SplashScreen.png"),
)
_DATE_RE = re.compile(r"(?<!\d)(20\d{2})[-/.年](\d{1,2})[-/.月](\d{1,2})日?")
_NEWS_HINT_RE = re.compile(r"(?:news|notice|article|info|公告|资讯|活动|更新)", re.I)
_BANNER_HINT_RE = re.compile(r"(?:banner|swiper|slide|carousel|\bkv\b)", re.I)
_HERO_IMAGE_RE = re.compile(r"/act/.+/part1/.+/(?:bg|background)\.(?:jpe?g|png|webp)(?:\?|$)", re.I)
_GENERIC_TITLES = {"首页", "官网", "详情", "更多", "more", "点击查看", "立即前往"}
_LAUNCHER_BANNER_TAG_ID = 137617
_LAUNCHER_NEWS_TAG_ID = 137629
_LAUNCHER_NOTICE_TAG_ID = 139435
_CHINA_TIMEZONE = timezone(timedelta(hours=8))


class TencentProvider(HomeContentProvider):
    """Select a Tencent game and combine its verified local and web sources."""

    provider_id = "tencent"

    def __init__(
        self,
        client: Optional[httpx.AsyncClient] = None,
        timeout: httpx.Timeout = DEFAULT_TIMEOUT,
    ) -> None:
        self._client = client
        self._timeout = timeout

    async def fetch(self, request: HomeContentRequest) -> HomeContent:
        source = get_game_source_by_id(request.game_id)
        if source is None or source.provider_id != self.provider_id:
            raise ValueError(f"Unknown Tencent game: {request.game_id}")
        options = {**source.provider_options, **(request.provider_options or {})}
        request = request.model_copy(update={"provider_options": options})

        background = _find_roco_launcher_background(request)
        launcher_url = str(options.get("launcherContentUrl") or DEFAULT_LAUNCHER_CONTENT_URL)
        launcher_payload = await self._fetch_json(launcher_url)
        banners, news = parse_roco_launcher_json(launcher_payload)

        # The CMC feed is the source used by the verified official launcher.
        # Keep the marketing page parser only as a conservative fallback when
        # the feed is unavailable or its contract changes.
        if not banners or not news:
            home_url = str(options.get("homePageUrl") or DEFAULT_HOME_URL)
            html = await self._fetch_text(home_url)
            fallback_banners, fallback_news = parse_roco_home_html(html or "", home_url)
            if not banners:
                banners = fallback_banners
            if not news:
                news = fallback_news
        return HomeContent(
            background=background,
            banners=banners,
            news=news,
            update_info=None,
        )

    async def _fetch_json(self, url: str) -> Any | None:
        try:
            if self._client is not None:
                response = await self._client.get(url)
            else:
                async with httpx.AsyncClient(
                    timeout=self._timeout,
                    headers={**DEFAULT_HEADERS, "Accept": "application/json"},
                    follow_redirects=True,
                ) as client:
                    response = await client.get(url)
            response.raise_for_status()
            return response.json()
        except (httpx.HTTPError, ValueError) as exc:
            logger.warning("Failed to fetch Tencent launcher content %s: %s", url, exc)
            return None

    async def _fetch_text(self, url: str) -> Optional[str]:
        try:
            if self._client is not None:
                response = await self._client.get(url)
            else:
                async with httpx.AsyncClient(
                    timeout=self._timeout, headers=DEFAULT_HEADERS, follow_redirects=True
                ) as client:
                    response = await client.get(url)
            response.raise_for_status()
            return response.text
        except (httpx.HTTPError, ValueError) as exc:
            # Local launcher animation remains usable when the marketing site
            # is unavailable or changes shape.
            logger.warning("Failed to fetch Tencent home page %s: %s", url, exc)
            return None


def _find_roco_launcher_background(request: HomeContentRequest) -> HomeBackground | None:
    roots: list[Path] = []
    for value in (request.executable_path, request.install_directory):
        if not isinstance(value, str) or not value.strip():
            continue
        seed = Path(value.strip())
        current = seed.parent if seed.suffix else seed
        for _ in range(_MAX_PARENT_LEVELS):
            if current not in roots:
                roots.append(current)
            if current.parent == current:
                break
            current = current.parent

    for root in roots:
        for relative in _VIDEO_PATHS:
            path = _validated_file(root, relative, _is_mp4, _MIN_VIDEO_BYTES, _MAX_VIDEO_BYTES)
            if path is not None:
                return HomeBackground(local_path=str(path))
    for root in roots:
        for relative in _IMAGE_PATHS:
            path = _validated_file(root, relative, _is_png, 8, 64 * 1024 * 1024)
            if path is not None:
                return HomeBackground(local_path=str(path))
    return None


def _validated_file(root: Path, relative: Path, validator, minimum: int, maximum: int) -> Path | None:
    path = root / relative
    try:
        if path.is_symlink() or not path.is_file():
            return None
        resolved_root = root.resolve(strict=True)
        resolved_path = path.resolve(strict=True)
        if not resolved_path.is_relative_to(resolved_root):
            return None
        size = path.stat().st_size
        if not minimum <= size <= maximum:
            return None
        with path.open("rb") as stream:
            if not validator(stream.read(16)):
                return None
        return resolved_path
    except OSError:
        return None


def _is_mp4(header: bytes) -> bool:
    return len(header) >= 12 and header[4:8] == b"ftyp"


def _is_png(header: bytes) -> bool:
    return header.startswith(b"\x89PNG\r\n\x1a\n")


def parse_roco_launcher_json(payload: Any) -> tuple[list[HomeBanner], list[HomeNewsItem]]:
    """Normalize the Tencent CMC response used by the official PC launcher."""

    if not isinstance(payload, dict) or payload.get("status") != 0:
        return [], []
    data = payload.get("data")
    rows = data.get("items") if isinstance(data, dict) else None
    if not isinstance(rows, list):
        return [], []

    banners: list[HomeBanner] = []
    news: list[HomeNewsItem] = []
    seen_banners: set[str] = set()
    seen_news: set[str] = set()
    for row in rows:
        if not isinstance(row, dict):
            continue
        title = str(row.get("sTitle") or "").strip()
        target = _official_url(
            row.get("sRedirectURL") or row.get("sUrl"),
            DEFAULT_HOME_URL,
            allow_media=False,
        )
        image = _launcher_image_url(row)
        tag_ids, tag_text = _launcher_tags(row)
        identity = str(row.get("iNewsId") or row.get("iId") or target or title).strip()
        if not identity:
            continue

        is_banner = _LAUNCHER_BANNER_TAG_ID in tag_ids or "轮播banner" in tag_text.casefold()
        if is_banner and image and identity not in seen_banners:
            seen_banners.add(identity)
            banners.append(HomeBanner(
                id=f"roco-banner-{identity}",
                title=_clean_launcher_title(title) or None,
                image_url=image,
                target_url=target,
            ))

        is_news = (
            _LAUNCHER_NEWS_TAG_ID in tag_ids
            or _LAUNCHER_NOTICE_TAG_ID in tag_ids
            or "launcher-资讯" in tag_text
            or "launcher-公告" in tag_text
        )
        if not is_news or not title or identity in seen_news:
            continue
        seen_news.add(identity)
        news.append(HomeNewsItem(
            id=f"roco-news-{identity}",
            title=_clean_launcher_title(title),
            category=_launcher_category(title, tag_ids, tag_text),
            image_url=image,
            target_url=target,
            published_at=_launcher_published_at(row),
        ))
    return banners, news


def _launcher_tags(row: dict[str, Any]) -> tuple[set[int], str]:
    ids: set[int] = set()
    text_parts = [str(row.get("sTagInfo") or "")]
    for key in ("sTagids", "sTagIds"):
        value = row.get(key)
        if isinstance(value, str):
            for part in value.split(","):
                if part.strip().isdigit():
                    ids.add(int(part.strip()))
        elif isinstance(value, list):
            ids.update(int(part) for part in value if isinstance(part, int))
    tag_rows = row.get("sTagInfoList")
    if isinstance(tag_rows, list):
        for tag in tag_rows:
            if not isinstance(tag, dict):
                continue
            tag_id = tag.get("id")
            if isinstance(tag_id, int):
                ids.add(tag_id)
            text_parts.append(str(tag.get("name") or ""))
    for match in re.finditer(r"(?<!\d)(\d{5,})(?=\||,|$)", text_parts[0]):
        ids.add(int(match.group(1)))
    return ids, " ".join(text_parts)


def _launcher_image_url(row: dict[str, Any]) -> str | None:
    candidates: list[Any] = [row.get("sIMG"), row.get("sIMGNew"), row.get("sImgBig")]
    covers = row.get("sCoverList")
    if isinstance(covers, list):
        candidates.extend(
            cover.get("url") for cover in covers if isinstance(cover, dict)
        )
    for candidate in candidates:
        url = _official_url(candidate, "https://static.gametalk.qq.com/", allow_media=True)
        if url:
            return url
    return None


def _clean_launcher_title(title: str) -> str:
    return re.sub(r"-(?:wegame)?(?:助手页|启动器)$", "", title, flags=re.I).strip()


def _launcher_category(title: str, tag_ids: set[int], tag_text: str) -> str:
    value = f"{title} {tag_text}".casefold()
    if "活动" in title or "activity" in value or "event" in value:
        return "活动"
    if "公告" in title or (
        _LAUNCHER_NOTICE_TAG_ID in tag_ids and _LAUNCHER_NEWS_TAG_ID not in tag_ids
    ):
        return "公告"
    return "资讯"


def _launcher_published_at(row: dict[str, Any]) -> datetime | None:
    for key in ("sIdxTime", "sCreated", "sTargetIdxTime"):
        value = row.get(key)
        if not isinstance(value, str) or not value.strip():
            continue
        try:
            local_time = datetime.strptime(value.strip(), "%Y-%m-%d %H:%M:%S")
            return local_time.replace(tzinfo=_CHINA_TIMEZONE).astimezone(timezone.utc)
        except ValueError:
            continue
    return None


class _RocoHomeParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.links: list[dict[str, Any]] = []
        self._link: dict[str, Any] | None = None
        self._in_title = False
        self.title_parts: list[str] = []
        self.description = ""
        self.hero_image: str | None = None

    def handle_starttag(self, tag: str, attrs) -> None:
        values = {key.lower(): value or "" for key, value in attrs}
        lowered = tag.lower()
        if lowered == "title":
            self._in_title = True
        elif lowered == "meta":
            key = (values.get("name") or values.get("property") or "").casefold()
            if key in {"description", "og:description"} and not self.description:
                self.description = values.get("content", "").strip()
        elif lowered == "a" and self._link is None:
            self._link = {
                "href": values.get("href", ""),
                "class": values.get("class", ""),
                "title": values.get("title", ""),
                "text": [],
                "image": None,
                "image_alt": "",
            }
        elif lowered == "img":
            source = values.get("data-src") or values.get("src")
            if self._link is not None:
                self._link["image"] = source
                self._link["image_alt"] = values.get("alt", "")
                self._link["class"] += " " + values.get("class", "")
            elif self.hero_image is None and _HERO_IMAGE_RE.search(source):
                self.hero_image = source

    def handle_data(self, data: str) -> None:
        if self._link is not None:
            self._link["text"].append(data)
        if self._in_title:
            self.title_parts.append(data)

    def handle_endtag(self, tag: str) -> None:
        lowered = tag.lower()
        if lowered == "title":
            self._in_title = False
        elif lowered == "a" and self._link is not None:
            self.links.append(self._link)
            self._link = None


def parse_roco_home_html(html: str, base_url: str = DEFAULT_HOME_URL) -> tuple[list[HomeBanner], list[HomeNewsItem]]:
    """Extract conservative carousel/news candidates from official HTML.

    The Tencent marketing page is not a documented API.  Candidates require
    an official HTTPS URL plus semantic class/path hints; generic navigation
    links and untrusted images are intentionally ignored.
    """

    parser = _RocoHomeParser()
    parser.feed(html)
    banners: list[HomeBanner] = []
    news: list[HomeNewsItem] = []
    seen_banners: set[str] = set()
    seen_news: set[str] = set()

    for row in parser.links:
        target = _official_url(row["href"], base_url, allow_media=False)
        image = _official_url(row.get("image"), base_url, allow_media=True)
        text = " ".join(" ".join(row["text"]).split())
        title = (row.get("title") or text or row.get("image_alt") or "").strip()
        hints = " ".join((row.get("class", ""), row.get("href", ""), row.get("image") or ""))

        is_banner = bool(target and image and _BANNER_HINT_RE.search(hints))
        if is_banner and target not in seen_banners:
            seen_banners.add(target)
            banners.append(HomeBanner(
                id=_stable_id("banner", target),
                title=title or None,
                image_url=image,
                target_url=target,
            ))

        if is_banner or not target or not title or title.casefold() in _GENERIC_TITLES:
            continue
        if not (_NEWS_HINT_RE.search(hints) or _NEWS_HINT_RE.search(title)):
            continue
        key = target.casefold()
        if key in seen_news:
            continue
        seen_news.add(key)
        news.append(HomeNewsItem(
            id=_stable_id("news", target),
            title=title,
            category=_category(title, hints),
            image_url=image,
            target_url=target,
            published_at=_published_at(text),
        ))

    # The official site currently renders a seasonal activity page, not a
    # conventional news list. Use only its verified part-1 hero as the current
    # activity and ignore decorative prizes, QR codes and character cards.
    page_title = " ".join(" ".join(parser.title_parts).split())
    hero_image = _official_url(parser.hero_image, base_url, allow_media=True)
    home_target = _official_url(base_url, base_url, allow_media=False)
    if hero_image and home_target and not banners:
        banners.append(HomeBanner(
            id=_stable_id("banner", hero_image),
            title=page_title or "洛克王国：世界",
            image_url=hero_image,
            target_url=home_target,
        ))
    if hero_image and home_target and page_title and not news:
        news.append(HomeNewsItem(
            id=_stable_id("news", home_target + "#current-activity"),
            title=page_title,
            category="活动",
            summary=parser.description or None,
            image_url=hero_image,
            target_url=home_target,
        ))
    return banners, news


def _official_url(value: Any, base_url: str, *, allow_media: bool) -> str | None:
    if not isinstance(value, str) or not value.strip() or value.startswith(("#", "javascript:")):
        return None
    url = urljoin(base_url, value.strip())
    parsed = urlparse(url)
    if parsed.scheme.lower() != "https" or not parsed.hostname:
        return None
    host = parsed.hostname.casefold()
    allowed = (
        host == "rocom.qq.com"
        or host.endswith(".rocom.qq.com")
        or (not allow_media and (host == "mall.qq.com" or host.endswith(".wegame.com.cn")))
        or (
            allow_media
            and (
                host == "game.gtimg.cn"
                or host.endswith(".gtimg.cn")
                or host == "static.gametalk.qq.com"
            )
        )
    )
    return url if allowed else None


def _category(title: str, hints: str) -> str:
    value = f"{title} {hints}".casefold()
    if "公告" in value or "notice" in value or "更新" in value:
        return "公告"
    if "活动" in value or "activity" in value or "event" in value:
        return "活动"
    return "资讯"


def _published_at(text: str) -> datetime | None:
    match = _DATE_RE.search(text)
    if not match:
        return None
    try:
        return datetime(*(int(part) for part in match.groups()), tzinfo=timezone.utc)
    except ValueError:
        return None


def _stable_id(prefix: str, value: str) -> str:
    digest = hashlib.sha256(value.encode("utf-8")).hexdigest()[:16]
    return f"roco-{prefix}-{digest}"


__all__ = ["TencentProvider", "parse_roco_home_html", "parse_roco_launcher_json"]
