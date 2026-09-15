# NetEaseStaticCmsProvider (燕云十六声 / Where Winds Meet)

`provider_id = "netease-static-cms"`. Owns the 燕云十六声 (Where Winds
Meet) marketing site, which is built with NetEase's NIE static-CMS
template and server-side renders the full banner + tabbed news feed
straight into the homepage HTML.

## Discovery surface

| Channel | Purpose                                                          |
|---------|------------------------------------------------------------------|
| `#news` panel `slide-news` | Banner carousel + 4-tab news feed |
| `.slide-news .banner .swiper-slide > a > img` | Top-of-news-page image carousel |
| `.slide-news .news-wrap .news-list-N` | 4 news containers (N=0 最新 / 1 新闻 / 2 公告 / 3 活动) |
| `.slide-news .news-wrap .news-list-N .link` | Each row carries `<a class="link" href=…>` with `.date` (MM/DD), `.kind` (新闻/公告/活动), `.title`, `.desc` |

The home page itself does **not** embed a background ``<video>`` /
``<img>`` — the upstream JS bundle swaps a ``<video class="bg">`` at
runtime. We therefore fall back to the first banner image as a
static background so the C# UI has something to render immediately.

## Network endpoints

| Region | Endpoint                                                    |
|--------|-------------------------------------------------------------|
| CN     | ``https://www.yysls.cn/index.html``                         |
| HMT    | ``https://www.wherewindsmeetgame.com/hmt/index.html``       |

The two regions are not interchangeable. AGENTS.md explicitly calls
out that 燕云十六声 (CN) and 无限大 (a different NetEase title) must
not share endpoints, and we extend that to the CN/HMT split here:
``yysls.cn`` is owned by 网易 / 杭州网易雷火 and uses the NIE static
CMS, while ``wherewindsmeetgame.com`` is owned by the Sony Taiwan
co-publication and uses a different template class (`newsBanner` /
`newsList` blocks). The Provider currently only parses the CN shape
and ships an ``HMT`` default endpoint that returns an empty payload
until we map the HMT HTML.

## Response layout

### ``.slide-news`` HTML

```text
<div class="panel" id="news">
  <div class="slide-news">
    <div class="wrap">
      <div class="swiper-container banner">   <!-- carousel -->
        <div class="swiper-slide">
          <a href="https://mp.weixin.qq.com/…" title="…">
            <img class="swiper-lazy" data-src="https://nie.res.netease.com/…/x.jpg">
          </a>
        </div>
        …
      </div>
      <div class="news">
        <div class="swiper-container news-wrap">
          <div class="swiper-wrapper">
            <div class="swiper-slide">
              <div class="swiper-container news-list news-list-0">   <!-- 最新 -->
                <a class="link" href="…">
                  <div class="link-flex">
                    <div class="date">09/10</div>
                    <span class="kind">新闻</span>
                    <span class="title ellipsis">…</span>
                  </div>
                  <div class="ellipsis desc">…</div>
                </a>
              </div>
            </div>
            <div class="swiper-slide">… news-list-1 … news-list-2 … news-list-3 …</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</div>
```

* The banner image URL lives in ``data-src`` (Swiper is configured
  with lazy loading); we fall back to ``src`` if the page ever
  changes.
* Each news row is an ``<a class="link">`` with both a ``<div
  class="date">MM/DD</div>`` (only month + day, no year) and a
  ``<span class="kind">新闻/公告/活动</span>`` marker.
* ``news-list-N`` is the tab index — 0 is the mixed "最新" feed (rows
  keep their original ``.kind``), 1–3 are the dedicated
  新闻 / 公告 / 活动 tabs.

## Normalized output

The Provider maps the upstream payload into the
`Background / Banners / News / UpdateInfo` DTOs defined in
`contracts/home-content-v1.schema.json`. Key mappings:

* **Background** — `image_url` = first banner image (no
  `video_url` / `local_path`); the upstream's runtime-injected
  ``<video class="bg">`` cannot be captured server-side.
* **Banners** — one ``HomeBanner`` per ``.swiper-slide a`` inside
  ``.banner``. ``target_url`` = ``<a href>`` (only kept when the host
  is on the allow-list); ``title`` = ``title`` attribute.
* **News** — `category` derived from the tab index + per-row
  ``.kind``: tab 0 ("最新") uses `KIND_CATEGORY` (`新闻→资讯`,
  `公告→公告`, `活动→活动`); tabs 1–3 use `TAB_CATEGORY`
  (`1→资讯`, `2→公告`, `3→活动`). `target_url` = `<a href>`,
  `published_at` parsed from ``.date`` (uses the current UTC year
  because the upstream does not publish the year).

`providerOptions` also caps `maxBanners` (default 4) and
`newsPerTab` (default 4) — pass `maxBanners=0` to disable banners
altogether without losing the news feed.

## Allow-list

```python
ALLOWED_HOST_SUFFIXES = (
    ".yysls.cn",                   # CN marketing site
    ".netease.com",                # shared NetEase domain
    ".nie.netease.com",            # CN image CDN
    ".wherewindsmeetgame.com",     # HMT marketing site
    ".easebar.com",                # HMT CDN
    ".fp.ps.easebar.com",          # HMT CDN (subdomain)
    ".yysls.v.netease.com",        # music CDN
    ".yysls-build-na.fp.ps.easebar.com",  # HMT asset CDN
)
```

Subdomains are accepted by suffix match. Banner ``href`` URLs that
land on third-party social destinations (`mp.weixin.qq.com`,
`mp.weixin.qq.com`, `space.bilibili.com`, …) are silently dropped so
the UI does not surface them as jump targets. A future revision
should expose them through a separate ``social_links`` channel if the
launcher wants to deep-link into them.

## Per-game mapping

Defaults match 燕云十六声 / 网易. `providerOptions` accepts:

| Key             | Default                  | Meaning                                              |
|-----------------|--------------------------|------------------------------------------------------|
| `region`        | `cn`                     | `cn` (网易 / NIE static CMS) / `hmt` (Sony HMT, placeholder) |
| `homePageUrl`   | derived from region base | Override the homepage URL (verbatim, no suffixing)   |
| `maxBanners`    | `4`                      | Cap on banner rows to surface                        |
| `newsPerTab`    | `4`                      | Cap on news rows per tab                             |

## Verification

Sampled **2026-09-15** against the CN build. The fixture at
`contracts/samples/yysls-cn.json` carries the parsed banners + news
rows plus the trimmed HTML for round-trip tests. The raw captures
live in `contracts/samples/_raw_yysls/` (git-ignored, only kept
locally to regenerate fixtures).

| Fixture          | Captured                                            |
|------------------|-----------------------------------------------------|
| `yysls-cn.json`  | CN homepage ``#news`` panel — 5 banners, 40 news rows |

Tests run without any network access (`httpx.MockTransport`). The
total assertion count is **29** (see `python/tests/test_netease_static_cms_provider.py`).

## Limitations / things to revisit

* No stable SLA — the marketing site is regenerated by NetEase's CMS
  whenever a new event goes live. Treat the field set as best-effort
  and re-sample if the launcher suddenly stops surfacing new
  banners / news.
* Background video cannot be captured server-side because the
  upstream JS bundle swaps a ``<video class="bg">`` at runtime. The
  Provider surfaces the first banner image as a static fallback so the
  C# UI has *something* to render immediately, but the dynamic
  video background will only appear once the JS runs in the browser.
  Pair this Provider with `LocalLauncherAssetProvider` once the
  local launcher hands the real background video over.
* `HMT` region (`region="hmt"`) currently only emits the empty
  default payload — the ``wherewindsmeetgame.com`` build uses a
  different template class (`newsBanner` / `newsList` blocks) with
  the banner container filled in by an out-of-band JS bundle. We
  capture the raw HTML for documentation but do not parse it yet.
  AGENTS.md instructs us to keep the two regions separate because
  they share neither endpoint nor markup.
* `date` is month + day only — the year is assumed to be the
  current UTC year. The launcher refresh loop runs every 10–30
  minutes so any cross-year drift self-corrects within an hour.
* Banner ``href`` URLs that land on `weibo.com`, `mp.weixin.qq.com`,
  etc. are silently dropped (the allow-list rejects them). A future
  C# guard may want to expose them as in-app social shortcuts
  instead of discarding them outright.
* The ``#news`` panel is the only piece of the homepage we consume;
  the launcher/boss/weapon/media/job panels are intentionally
  ignored (the launcher-side widgets already cover that surface).
* Provider in FastAPI 入口 `provider_registry` 中尚未注册;
  `/v1/home-content` 用 `providerId=netease-static-cms` 仍走 sample
  envelope. Registration is a future iteration's work.
* MIME / MD5 / size validation is not performed; per AGENTS.md the
  C# `HttpHomeContentTransport` cache layer owns that (todo).