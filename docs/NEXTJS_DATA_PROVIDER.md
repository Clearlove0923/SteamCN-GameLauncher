# NextJsDataProvider (无限暖暖)

首页使用公司级 `PaperGamesProvider`（`providerId=papergames`）；它只复用本 Provider 的 Banner 与资讯结果，背景优先读取官方 Windows 启动器的本机 WebM 缓存。`NextJsDataProvider` / `nextjs-data` 保留为旧配置兼容入口，并继续表示纯官网来源。

`provider_id = "nextjs-data"`. Owns the 无限暖暖 (Infinity Nikki)
Next.js marketing site for OS (INFOLD PTE. LTD.) and CN (上海暖叠
网络科技有限公司 / Papergames) builds. The site is a Next.js SSR
application that ships the entire homepage payload as an
`__NEXT_DATA__` JSON envelope embedded in the HTML, plus a
paginated `/api/news` JSON endpoint for the tabbed news feed.

## Discovery surface

| Channel | Purpose                                                      |
|---------|--------------------------------------------------------------|
| `<video src=…>` in markup (OS) | Hard-coded homepage background MP4 |
| `pageData.page.pv_list[label="pc 首屏背景视频"]` (CN) | Same, but the markup only renders a `<video poster=…>` and the URL lives in the embedded config |
| `pageData.newsbanner[]` | Homepage top carousel — image only, no click-through URL  |
| `pageData.page.actBannerlist[]` | Runtime activity banners — `{label, value}` where `value` is a JSON string with `bannerimg`, `link`, `starttime`, `endtime` |
| `GET /api/news?section={0,1,2}&offset=&limit=&locale=` | Paginated news list — 0=新闻, 1=公告, 2=活动 |

官网的 `pc 首屏背景视频` 与官方 Windows 启动器首页不是同一素材。2026-10-08 对官方启动器 1.3.1 实机核对后，公司级 `PaperGamesProvider` 改为从 `%LOCALAPPDATA%\\InfinityNikki Launcher\\cache\\images` 选择修改时间最新、大小在 1 MiB 至 512 MiB 且具有 EBML/WebM 文件头的 `.webm`。找不到有效文件时返回空背景，让 C# 回退到用户的外观背景；不会再把官网视频冒充为启动器动画。远程下发端点尚未确认，因此该能力只描述为“已验证的本机启动器缓存”，不是“已验证的启动器 API”。

## Network endpoints

| Region | Homepage                                                        | News API                                                   |
|--------|------------------------------------------------------------------|------------------------------------------------------------|
| OS     | `https://infinitynikki.infoldgames.com/{locale}/home`             | `https://infinitynikki.infoldgames.com/api/news?…`         |
| CN     | `https://infinitynikki.nuanpaper.com/home`                       | `https://infinitynikki.nuanpaper.com/api/news?…`           |

`locale` defaults to `zh-TW` (OS) or `zh-CN` (CN). CN ignores the
`locale` query parameter entirely.

## Response layout

### Homepage `__NEXT_DATA__` blob

```text
__NEXT_DATA__.props.pageProps.pageData
  ├── page      # config object — pv_list, pv_list_other, actBannerlist, etc.
  ├── banner    # reserved upstream, currently always []
  └── newsbanner[]
         ├── id           # integer
         ├── title        # localized string
         ├── address      # PC image URL (CDN)
         ├── address_h5   # mobile image URL
         ├── stime        # ISO 8601 with Z suffix
         ├── etime        # ISO 8601 or null
         └── raw_data     # null in production
```

`actBannerlist` rows are `{__type: "json", label, value}` where
`value` is a JSON-encoded string with the runtime banner payload.

### `/api/news?section=N&offset=0&limit=N`

```text
{
  "data": {
    "total": <int>,
    "data": [
      {
        "id":           <int>,
        "title":        "<localized string>",
        "section":      0 | 1 | 2,
        "info":         null,
        "publish_time": "<ISO 8601 with .000Z suffix>",
        "cover":        "<CDN image URL>",
        "abstract":     "<lead paragraph>"
      },
      ...
    ]
  },
  "ret": 0,
  "msg": "ok",
  "timestamp": <epoch ms>
}
```

## Normalized output

The provider maps the upstream payload into the
`Background / Banners / News / UpdateInfo` DTOs defined in
`contracts/home-content-v1.schema.json`. Key mappings:

* **Background** — `video_url` = `<video src>` (OS) or
  `pageData.page.pv_list[label="pc 首屏背景视频"].value` (CN fallback);
  `image_url` = `<video poster>` (fallback for the OS build that
  only carries a poster frame).
* **Banners** — `newsbanner` rows become image banners without a
  click-through URL; `actBannerlist` rows become banners with `link`
  resolved against the allow-list. Banners without an allowed
  `image_url` are dropped so the UI does not show empty rows.
* **News** — each `/api/news?section=N` row becomes a `HomeNewsItem`
  with `category` derived from `SECTION_CATEGORY` (`0 → 资讯`,
  `1 → 公告`, `2 → 活动`), `target_url` pointing at
  `<base>/news/<id>`, `image_url` = `cover`, `published_at` parsed
  from `publish_time` (normalised to UTC).

## Allow-list

```python
ALLOWED_HOST_SUFFIXES = (
    ".infoldgames.com",       # OS marketing site + CDN
    ".papegames.com",         # CN CDN
    ".nuanpaper.com",         # CN marketing site
    ".webstatic.infoldgames.com",
    ".webstatic.papegames.com",
    ".assets.infoldgames.com",
    ".assets.papegames.com",
    ".assets.nuanpaper.com",
)
```

The CDN set is owned by the same upstream (叠纸游戏 / INFOLD / Papergames).
Subdomains are accepted by suffix match (e.g.
`https://eng.papegames.com/page` matches `.papegames.com`).

## Per-game mapping

Defaults match 无限暖暖 / 叠纸. `providerOptions` accepts:

| Key             | Default                                  | Meaning                                              |
|-----------------|------------------------------------------|------------------------------------------------------|
| `region`        | `os`                                     | `os` (INFOLD global) / `cn` (国服 Papergames)         |
| `locale`        | `zh-TW` (OS) / `zh-CN` (CN)              | URL locale segment for OS; ignored on CN              |
| `homePageUrl`   | derived from base                        | Override the homepage URL (verbatim, no suffixing)   |
| `newsApiBase`   | region default base                      | Override the news API host                           |
| `pagePath`      | `/home`                                  | Appended to `newsApiBase` when building the home URL |
| `newsLimit`     | `4`                                      | Per-section page size for `/api/news`                 |
| `launcherCacheDirectory` | `%LOCALAPPDATA%\\InfinityNikki Launcher\\cache\\images` | `PaperGamesProvider` 的本机启动器缓存覆盖路径；主要用于测试或非默认安装 |
| `contentRevision` | `launcher-cache-v1`（游戏来源配置） | 进入跨语言缓存键，用于淘汰旧的官网草坪背景缓存 |

## Verification

Sampled **2026-09-15** against both the OS and CN builds. The
sample fixtures live at `contracts/samples/infinity-nikki-*` and
are regenerated by `python/tests/_dev/sanitize_samples.py` (only
ran manually when re-sampling). The raw HTML/JSON captures kept in
`contracts/samples/_raw_infinity_nikki/` are git-ignored so they
do not bloat the repo.

| Fixture                                               | Captured                         |
|-------------------------------------------------------|----------------------------------|
| `infinity-nikki-home-os.json`                         | OS homepage `__NEXT_DATA__`      |
| `infinity-nikki-home-cn.json`                         | CN homepage `__NEXT_DATA__`      |
| `infinity-nikki-news-list-{os,cn}-s{0,1,2}.json`      | 6 list responses (2 sections × 3 regions) |
| `infinity-nikki-news-detail-{os,cn}.json`             | 2 detail responses               |

Tests run without any network access (`httpx.MockTransport`). The
total assertion count is **39** (see `python/tests/test_nextjs_data_provider.py`).

## Limitations / things to revisit

* No stable SLA — the upstream is a marketing site, not a public
  API contract. Treat the field set as best-effort.
* `newsbanner` has no click-through URL exposed. The C# UI either
  needs to attach a hard-coded destination (e.g. the in-game news
  inbox) or omit the click handler for those rows.
* `actBannerlist` rows wrap their payload as a JSON-encoded string
  inside `value`. This is an `__type: "json"` sentinel the Next.js
  builder emits; we parse it with `json.loads` and skip rows that
  fail to decode.
* The `pv_list` / `pv_list_other` config blob exists only on the
  CN build. We keep both keys in the fixture so future upstream
  additions map cleanly.
* Locale gating is intentionally simple: the OS build has multiple
  locales (`zh-TW` / `en` / `ja` / `kr`) but they all return the
  same `__NEXT_DATA__` shape. We pass `locale` through to the news
  API URL only; the provider does not localize category names.
* CN home page renders an `<video poster=…>` without `src`; the
  provider falls back to `pv_list[pc 首屏背景视频]` in that case.
  If both fail the legacy `nextjs-data` background surfaces an empty
  `HomeBackground`. The company-level `papergames` provider discards this
  website background and uses only the verified local launcher WebM.
