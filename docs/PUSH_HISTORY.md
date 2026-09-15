# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

## 2026-09-15 21:05:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `NextJsDataProvider`（`python/home_content/providers/nextjs_data.py`）：叠纸《无限暖暖》（Infinity Nikki）官方营销站点的 Next.js `__NEXT_DATA__` 解析器。OS 入口 `https://infinitynikki.infoldgames.com/{locale}/home`（INFOLD PTE. LTD.），CN 入口 `https://infinitynikki.nuanpaper.com/home`（上海暖叠 / Papergames），资讯列表通过 `GET /api/news?section={0,1,2}&offset=&limit=&locale=` 拉取，每次 fetch 串行调 1 个 home + 3 个 section 端点。OS 端 `<video src=…>` 硬编码背景；CN 端 `<video poster=…>` 无 `src` 时回退到 `pageData.page.pv_list[label="pc 首屏背景视频"].value`（CDN）。`parse_home_html` 用正则 `<script id="__NEXT_DATA__" type="application/json">(.+?)</script>` 抽出整段 JSON，缺 blob / 非 JSON 都包成 `ValueError`。
- 横幅分两类：`pageData.newsbanner[]` 是首页顶部轮播（仅图片、无跳转 URL），`_build_top_banners` 按 `id` 拼出 `in-newsbanner-<id>`，图片走 allow-list 白名单，未通过直接丢弃（不渲染空 banner）；`pageData.page.actBannerlist[]` 是运行时活动横幅，结构是 `{__type: "json", label, value}`，`value` 是 JSON 字符串（含 `bannerimg` / `link` / `starttime` / `endtime`），`_build_activity_banners` 用 `json.loads` 解析，目标 URL 也走白名单；非法 JSON 跳过并 `logger.warning`。
- 资讯按 `/api/news` 三 section 拉：`section=0 → 资讯`、`section=1 → 公告`、`section=2 → 活动`（与 Tab 顺序一致）。`SECTION_CATEGORY` 显式映射；`_build_news_items` 用 `id` + region + section 拼稳定 ID，target_url = `<newsBase>/news/<id>`（绝对），空标题 / 非允许 host 的封面自动剔除。`publish_time` 是 `2026-09-10T03:00:00.000Z` 这类带毫秒的 ISO 8601，`_parse_iso` 兼容 `Z` / `+HH:MM` / 裸 datetime，归一化到 UTC；`actBannerlist` 用 `YYYY-MM-DD HH:MM:SS`，`_parse_naive` 优先 strptime 三种格式再回退 `_parse_iso`。
- `ALLOWED_HOST_SUFFIXES` 仅接受 `.infoldgames.com` / `.papegames.com` / `.nuanpaper.com` / `.webstatic.infoldgames.com` / `.webstatic.papegames.com` / `.assets.infoldgames.com` / `.assets.papegames.com` / `.assets.nuanpaper.com`；scheme 必须 http(s)；子域按 suffix 匹配（`eng.papegames.com` 也放行），前缀伪装（`evil.com/assets.infoldgames.com/x.png`）拒绝。`httpx.HTTPError` / `json.JSONDecodeError` 在 Provider 层包 `logger.warning`，网络端点 5xx 时 banner/news 自动降级为空，背景 video 缺失时 `HomeBackground` 仍然生成但不携带视频。
- 默认常量 `DEFAULT_BASE_OS=https://infinitynikki.infoldgames.com` / `DEFAULT_BASE_CN=https://infinitynikki.nuanpaper.com` / `DEFAULT_LOCALE_OS=zh-TW` / `DEFAULT_LOCALE_CN=zh-CN` / `DEFAULT_PAGE_PATH=/home` / `DEFAULT_NEWS_LIMIT=4` / `DEFAULT_REGION=os`；`providerOptions` 支持覆盖 `region` / `locale` / `homePageUrl`（完整 URL，不拼接 locale / pagePath）/ `newsApiBase` / `pagePath` / `newsLimit`。CN 调 `/api/news` 时不附带 `locale=` 参数。
- 新增脱敏样本 `contracts/samples/infinity-nikki-{home-os,home-cn,news-list-{os,cn}-s{0,1,2},news-detail-{os,cn}}.json` 共 10 个文件：基于 2026-09-15 实采响应，HTML 走 `<__NEXT_DATA__>` 正则拆出 `nextData` + `<video src=...>` + `<video poster=...>` 三段，删掉与 provider 无关的 i18n/法律/资源 URL 字段（保留 `tabName` / `newsTextSign` / `versionPvUrl` / `pv_list` / `actBannerlist` / `newsbanner` 等），保留真实 CDN 域名以便 allow-list 校验路径与生产一致，list 裁到 2 条。原始 HTML/JSON 抓在 `contracts/samples/_raw_infinity_nikki/`（`.gitignore` 已排除，不入库）。
- 新增 Provider 文档 `docs/NEXTJS_DATA_PROVIDER.md`：验证日期、采样区域、语言、4 个端点 URL 与响应示例、`__NEXT_DATA__` 与 `/api/news` 字段映射、`SECTION_CATEGORY` / `ALLOWED_HOST_SUFFIXES` / 默认常量、`providerOptions` 全表、CN 背景 pv_list 回退、newsbanner 无跳转 URL 等限制。
- 新增 `python/tests/test_nextjs_data_provider.py`：39 项 pytest，覆盖默认值 / allow-list / `_parse_iso` / `_parse_naive` / `parse_home_html`（含缺 blob、非 JSON、`<video src>` / `<video poster>` 抽取、不匹配其他 `<script>` 标签）/ `_resolve_pv_video` / `_build_background` / `_build_top_banners`（白名单拒绝）/ `_build_activity_banners`（JSON 解码失败、目标 host 拒绝）/ `_build_news_items`（空标题、非法 cover、section 映射）/ 端到端 fetch（OS 全路径、CN pv_list 回退、首页 5xx 降级、所有端点 500 全空、providerOptions.homePageUrl 覆盖、news 返回非 dict）。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `NEXTJS_DATA_PROVIDER.md`，新增 `contracts/samples/_raw_*/` 排除规则避免原始抓包入库。

### 验证结果

- `pytest python/tests/test_nextjs_data_provider.py`：39 项全部通过，耗时 0.36s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项 + PerfectWorldHybridProvider 41 项 + NextJsDataProvider 39 项共 **126 项**全部通过，耗时 0.37s。
- 直接 probe 真实端点：OS 端 `https://infinitynikki.infoldgames.com/zh-TW/home` 返回 200（HTML 含 `<video src=…bc3a08b841873ed2.mp4>` + 完整 `__NEXT_DATA__`）；CN 端 `https://infinitynikki.nuanpaper.com/home` 返回 200（HTML 仅 `<video poster=…>` + `__NEXT_DATA__.pageData.page.pv_list`）；`/api/news?section={0,1,2}` 三 section 都返回 `{data: {total, data[]}, ret: 0, msg: "ok", timestamp}` envelope，`section=0` 总数 44 条、`section=1` 82 条、`section=2` 4 条（国际服），CN 端总数更大（资讯 792 条）。

### 当前限制

- 端点来自对叠纸 Next.js 营销站点的反向工程，非公开 API；首页结构、字段命名（`newsbanner` / `actBannerlist` / `pv_list`）随时可能改版。每次 rebase / 大版本后需要重新采样脱敏样本（保留 `contracts/samples/_raw_infinity_nikki/` 抓包，仅本地不入库）。
- `newsbanner` 不携带跳转 URL，C# UI 端要么绑定固定的"打开内嵌新闻 inbox"行为，要么只展示图。当前 Provider 把 `target_url=None` 透传，不伪造跳转。
- `actBannerlist.value` 是 JSON 字符串（不是对象），解析失败的行被静默丢弃并 `logger.warning`；如果上游改成对象 / 数组，需要调整 `_build_activity_banners`。
- CN home `<video>` 不带 `src`，Provider 兜底靠 `pv_list`；如果上游连 `pv_list` 都去掉，CN 端会落到空背景（`HomeBackground.video_url=None`，`image_url` 仅剩 poster）。
- 资讯跳转 URL 走 `<newsBase>/news/<id>`，但实际新闻详情页路由可能变化（例如叠纸未来改成 `/news/detail/<id>`），需要持续验证。
- CN 调 `/api/news` 不携带 `locale=` 参数；如果 CN 上游未来按语言分发，需要在 Provider 里加 `news_locale` 逻辑。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=nextjs-data` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-15 20:45:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `PerfectWorldHybridProvider`（`python/home_content/providers/perfect_world_hybrid.py`）：完美世界《异环》（Neverness to Everness）的本地启动器资源 + 远程 JS 数据混合 Provider。每次 fetch 并发拉两个端点：Global 从 `https://www.perfectworld.com/public/commonData/gamesData/gameSwiper/nte-gameSwiper.js`（横幅 swiper）和 `https://nte.perfectworld.com/include/newsData20260112.js`（资讯）；国服从 `https://static.games.wanmei.com/public/commonData/gamesData/gameSwiper/yh-gameSwiper.js` 和 `https://yh.wanmei.com/include/newsData20260112.js`。两个端点返回 `var NAME = {...};` 形式的 JS 对象字面量，`extract_js_payload` 剥包装、压双逗号、去除尾部逗号后用 `json.loads` 解析。
- 背景优先用 `providerOptions.backgroundVideoPath` / `backgroundImagePath`（本地启动器 `bg.mp4` / `bg.jpg`），其次 `backgroundVideoUrl` / `backgroundImageUrl`（CDN 兜底，allow-list 校验），最次走 `DEFAULT_BG_VIDEO_OS`（`ntevmg.perfectworld.com/webops/nte/nte_bgvideo_20260418.mp4`）/ `DEFAULT_BG_VIDEO_CN`（`yhvmg.wmupd.com/webops/yh/yh_bgvideo_20260418.mp4`），从启动器主页 HTML 提取并固化。
- 横幅来自 `lb1_<lang>`（OS，按语言取 `lb1_en` / `lb1_cn` / `lb1_jp` …）或 `lb1`（CN，无语言门控），缺失时按 `lb1` 兜底。资讯按 `pc.*`（CN，无语言门控）或 `<short_lang>.*`（OS）选 bucket，每个 bucket 内按 `news → gamebroad → gameevent → gamenews` 顺序产出；OS 的 `channelDescription` 经 `TAB_NAME_MAP` 映射到中文分类（`Notices/Mitteilungen/...` → 公告，`News/Nachrichten/...` → 资讯，`Events/Événements/...` → 活动），CN 直接用 `channelCnName`。
- 资讯 URL 是相对路径（OS `/en/article/news/...` / CN `/news/...`），`_absoluize_url` 按区域加 host：OS → `https://nte.perfectworld.com{url}`、CN → `https://yh.wanmei.com{url}`。跳转 URL 非 https 或 host 不在 allow-list 时置空（不抛错）。
- `ALLOWED_HOST_SUFFIXES` 仅接受 `.perfectworld.com` / `.wanmei.com` / `.wmupd.com` / `.games.wanmei.com` / `.static.pwsdk.com`；scheme 必须 http(s)；白名单外的 host 视为无效并丢弃。`start_ts` / `time`（`YYYY-MM-DD`）转 UTC `datetime`；`JSONDecodeError` / `ValueError` 在 Provider 层包装为 `ValueError`，与 Kuro / HoYo 行为一致；网络端点 503 时不抛错，返回空 banner/news 并交给 Worker 写 `envelope.errors`。
- 默认常量 `DEFAULT_APP_CODE=YDUTE5gscDZ229CW`、`DEFAULT_LANGUAGE=en-us`、`DEFAULT_REGION=os`；`providerOptions` 支持覆盖 `region` / `language` / `installDir` / `backgroundVideoPath` / `backgroundImagePath` / `backgroundVideoUrl` / `backgroundImageUrl` / `bannerSwiperUrl` / `newsDataUrl`。
- 新增脱敏样本 `contracts/samples/nte-{game-swiper-intl,game-swiper-cn,news-data-intl,news-data-cn}.json`：基于 2026-09-14 真实 JS 端点响应，经 `_extract_js_payload` 同款清洗逻辑后保存，URL 保留真实域名以便 allow-list 校验路径与生产一致；md5 缩短、列表裁剪到 2/3 条。
- 新增 Provider 文档 `docs/PERFECT_WORLD_HYBRID_PROVIDER.md`：验证日期、采样游戏 / 语言、4 个端点 URL 与响应示例、字段映射、tab 名称映射、默认常量、白名单、本地启动器资源入口、回退方案与已知限制（`appCode` 反编译来源 / JS 端点私有格式 / CN 不按语言分发 / 资讯 URL 相对路径 / MIME MD5 校验在 C# 端）。
- 新增 `python/tests/test_perfect_world_hybrid_provider.py`：41 项 pytest，覆盖 JS 提取（var 包装 / 裸对象 / 双逗号 / 尾逗号）、allow-list、白名单过滤、tab 映射、本地 vs 网络背景、相对 URL URL 绝对化、providerOptions 覆盖、HTTP 5xx / 非 JSON 响应不抛错、本地 backgroundVideoPath 优先于网络 URL。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `PERFECT_WORLD_HYBRID_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_perfect_world_hybrid_provider.py`：41 项全部通过，耗时 0.30s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项 + PerfectWorldHybridProvider 41 项共 87 项全部通过，耗时 0.32s。
- 直接 probe 真实端点（Global `nte-gameSwiper.js` + `newsData20260112.js`、CN `yh-gameSwiper.js` + `newsData20260112.js`）确认 4 个 URL 都返回 JS 包装的 JSON；Global `nte_bgvideo_20260418.mp4` 与 CN `yh_bgvideo_20260418.mp4` 都是 200 OK 的 MP4；`nte.perfectworld.com/cn/main.html` 内嵌 `<video src=...>` 与 `newsData20260112.js` 一致。

### 当前限制

- `appCode` / 启动器常量与 JS 端点 URL 都来自对 NTE 启动器资源与官网 HTML 的反向分析，不公开。完美世界改版或启动器升级后端点路径可能漂移，需要重新采样。
- JS 端点用的是私有 JS 对象字面量（带尾逗号、双逗号、空行分隔），`extract_js_payload` 已处理这些。如果未来版式换了（用了别的写法），需要更新正则。
- 横幅 swiper 的 `bigpic` / `link` / `mlink` 域名都在 allow-list，但实际 launcher 还会下发 `discord.gg` / `x.com` / `youtube.com` / `pwgam.es` 等第三方跳转域名（`mlink` 或 `link` 直接指向），Provider 自动剔除这些跳转。
- CN 端 `pc.*` 不分语言；OS 端 `lang.*` 在目标语言无响应时按 `cn` → `en` 兜底，避免空 news。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=perfect-world-hybrid` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- 启动器本地资源（`bg.mp4` / `bg.jpg`）没有自动 `installDir` 探测，由 C# 侧先 `LocalLauncherAssetProvider` 探测路径再喂给本 Provider。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-14 23:20:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `HypergryphBatchProvider`（`python/home_content/providers/hypergryph_batch.py`）：鹰角《明日方舟：终末地》及同厂商游戏的单端点批量协议 `POST https://launcher.gryphline.com/api/proxy/web/batch_proxy`（Global）/ `https://launcher.hypergryph.com/api/proxy/web/batch_proxy`（CN）。Provider 在一次 POST 里并发请求 `get_main_bg_image` / `get_banner` / `get_announcement` 三种 `kind`，统一从 `proxy_rsps[]` 中提取对应 `*_rsp`。背景优先 `video_url`，降级 `url`；Banner 来自 `banners[]`，id 字段做稳定 ID；News 来自 `tabs[].announcements[]`，英文 tabName 映射到中文分类（`Notices→公告`、`Events→活动`、`News→资讯`），其他保留原文。`start_ts`（毫秒）转 UTC `datetime`。
- 候选 URL 通过 host 白名单（`.hg-cdn.com` / `.hycdn.cn` / `.gryphline.com` / `.hypergryph.com` / `.skport.com` / `.skland.com`），scheme 必须为 `http(s)`，白名单外的 banner / jump_url 直接丢弃，背景无候选时 `HomeBackground=None`。`json.JSONDecodeError` 在 Provider 层被包装为 `ValueError`，与 Kuro / HoYo 行为一致。
- 默认常量 `DEFAULT_APP_CODE=YDUTE5gscDZ229CW`、`DEFAULT_CHANNEL=6`、`DEFAULT_SUB_CHANNEL=6`、`DEFAULT_LANGUAGE=en-us`、`DEFAULT_REGION=os`；`providerOptions` 支持覆盖 `region` / `baseUrl` / `appCode` / `channel` / `subChannel` / `language`。Global 与 CN 入口通过 `region=cn` 切换（自动取 `launcher.hypergryph.com/api` 与 `appCode=6LL0KJuqHBVz33WK / channel=1 / subChannel=1 / language=zh-cn`）。
- 新增脱敏样本 `contracts/samples/hypergryph-batch-envelope.json`：基于 2026-09-14 真实响应（Global/en-us/终末地），合并三个 `kind` 的 `proxy_rsps[]`，md5 缩短到 8 位，banner / announcement 列表裁剪到 3 / 2 条；保留真实 CDN / 官方域名以便 allow-list 校验路径与生产一致。
- 新增 Provider 文档 `docs/HYPERGRYPH_BATCH_PROVIDER.md`：记录验证日期、采样游戏 / 语言、batch_proxy 单端点协议、字段映射、tabName → 中文分类映射、默认常量、白名单、回退方案与已知限制（`appCode` 反编译来源 / `data_version` 当前为空 / 未映射 `url_config` `sidebar` `single_ent`）。
- 新增 `python/tests/test_hypergryph_batch_provider.py`：23 项 pytest，覆盖 allow-list、白名单过滤、`start_ts` 转 UTC、`providerOptions` 覆盖、空 envelope、缺 `proxy_rsps` envelope、非 JSON 响应、HTTP 5xx 上抛。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `HYPERGRYPH_BATCH_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_hypergryph_batch_provider.py`：23 项全部通过，耗时 0.21s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项共 46 项全部通过，耗时 0.22s。
- 直接 `python -m home_content.providers.hypergryph_batch`（probe 脚本）调真实接口：Global 端点返回 `proxy_rsps[0..2]`，其中 `get_main_bg_image_rsp.main_bg_image` 携带 MP4 + PNG，`get_banner_rsp.banners` 含 9 条 banner（CDN + 森空岛跳转），`get_announcement_rsp.tabs` 含 Notices / Events / News 三类。CN 端点（`launcher.hypergryph.com`，`zh-cn`）返回结构一致，CDN host 为 `hg-utils-public.hycdn.cn`。

### 当前限制

- `appCode` / `base` URL 来自 [`daydreamer-json/ak-endfield-api-archive`](https://github.com/daydreamer-json/ak-endfield-api-archive) 的 `config.ts` 反编译（base64 编码），鹰角不公开字符串。发版后可能更换，需要重新采样。
- `data_version` 字段当前始终为空字符串，Provider 不做版本协商；若鹰角后续引入版本化协议，需要扩展 `_extract_responses` 并新增缓存键策略。
- `get_url_config`、`get_sidebar`、`get_single_ent` 未映射到 `HomeContent`：前者是充值/问卷链接（无 UI 落点）；`sidebar` 是社交媒体入口（不属于首页布局）；`single_ent` 当前响应为空（没有版本按钮）。
- Global 多语言（`de-de`/`es-mx`/`fr-fr`/`ja-jp`/`ko-kr`/`zh-tw` 等）通过 `defaultSettings.launcherWebLang` 在 `launcherWeb.ts` 中定义，但 Provider 默认 `en-us`；其他语言通过 `providerOptions.language` 切换。
- CN 仅 `zh-cn`（`defaultSettings.launcherWebLangCN`）；其他语言通过 `providerOptions.language` 切换时由 Provider 透传给上游，未做独立验证。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=hypergryph-batch` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-14 22:30:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `KuroLauncherProvider`（`python/home_content/providers/kuro_launcher.py`）：库洛鸣潮启动器的三层 CDN JSON 抓取，按顺序拉 `launcher-config`（`functionCode.background` 动态 hash）、`wallpapers-slogan`（MP4 + 首帧 webp + 标语 png）、`news-notices`（`guidance.{activity,notice,news}` 三个分类 + `slideshow` 轮播）。背景优先 `videoFile`，降级 `firstFrameImage`，候选 URL 通过 CDN 域名白名单（`.kurogame.com` / `.kurogames.com` / `.aki-game.net`）。Banner 来自 `slideshow[]`，News 来自 `guidance` 三个分类，跳过 `functionSwitch == 0` 的分类。
- `providerOptions` 支持覆盖 `appId` / `appKey` / `gameId` / `language`；默认常量（`50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c` / `G153` / `en`）来自 `KRApp.conf`（`base64(XOR(data, 0x63))`）解码。
- 新增脱敏样本 `contracts/samples/kuro-{launcher-config,wallpapers-slogan,news-notices}.json`：2026-09-14 真实响应的样本，`guidance` 三个分类真实样本包含 notice + news，activity 的 `functionSwitch=0` 用于验证跳过逻辑。
- 新增 Provider 文档 `docs/KURO_PROVIDER.md`：记录验证日期、采样游戏、语言、三层端点、字段映射、默认常量、白名单、回退方案与已知限制。
- 新增 `python/tests/test_kuro_launcher_provider.py`：13 项 pytest，覆盖白名单过滤、视频优先、biz 默认值、三层端点串联、`functionCode.background` 缺失抛错、HTTP 错误传播、`providerOptions` URL 覆盖。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 路由不同 URL 到不同 fixture，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `KURO_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_kuro_launcher_provider.py`：13 项全部通过，耗时 0.16s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项共 23 项全部通过。
- 直接 `python -m home_content.providers.kuro_launcher` 调真实接口，envelope 携带 `hw-pcdownload-qcloud.aki-game.net` MP4 视频 + WebP 海报 + 5 个 banner + 多个 notice/news 项。

### 当前限制

- Kuro 启动器当前只下发 `en.json`；请求 `zh-cn.json` 会拿到空响应。增加语言需等 Kuro 发布对应文件或本地 PlayerAgent 注入。
- 国服 / CN 入口未独立验证：`G153` 当前是 Global 端点，国服可能用不同的 `appId` / `appKey` / `gameId` / CDN。
- `slogan` PNG 标题叠加图未映射到 `HomeContent`，UI 需要时由 C# 端单独请求。
- `activity` 分类常为 `functionSwitch=0`（当前版本没有活动），Provider 自动跳过。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-14 21:30:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `HoYoPlayJsonProvider`（`python/home_content/providers/hoyoplay_json.py`）：主端点 `https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo`（CN）/ `sg-hyp-api.hoyoverse.com`（OS），按 `providerOptions.launcher_id` + `region` 选 launcher，按 `gameBiz` 或 `game_id` 前缀（`hk4e/hkrpg/bh3/nap/hyg/abc`）选游戏，未匹配时退回 launcher 列表的第一个游戏。响应仅取背景：优先有 `video.url` 的候选，降级到 `background.url`，候选 URL 必须通过 CDN 域名白名单（`*.mihoyo.com`、`*.hoyoverse.com`、`*.yuanshen.com`、`*.bh3.com`、`*.honkaistarrail.com`、`*.zenlesszonezero.com`、`*.miyoushe.com`）。
- `models.py` 把 Pydantic 类型注解统一改写为 `Optional[X]`：`from __future__ import annotations` + `Optional[str/Optional[datetime/...] = None`，避免 Pydantic 2.x 在 Python 3.10 之前的运行时报 `str | None` 类型错。
- `server/app.py` 把 Provider 抛出的 `httpx.HTTPError` / `ValueError` / `ValidationError` 统一转换为 `HomeContentEnvelope.errors`，HTTP 状态码仍为 200；客户端通过 `errors` 决定降级策略，`NotImplementedError` 仍走 sample envelope。
- 新增脱敏样本 `contracts/samples/hoyoplay-cn-launcher-info.json`：基于 2026-09-14 真实响应（CN/zh-cn/launcher_id=`jGHBHlcOq1`），URL 中 32 字符 hex hash 替换为 `HASH32`，保留每个 biz 一条 background 用于回归。
- 新增 Provider 文档 `docs/HOYOPLAY_PROVIDER.md`：记录验证日期、区域、语言、入口、launcher_id、biz 映射、白名单策略、回退方案与已知限制。
- 新增 `python/tests/test_hoyoplay_provider.py`：10 项 pytest，覆盖白名单过滤、视频优先、biz 过滤、未知 game 退回、retcode != 0、空响应。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `HOYOPLAY_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_hoyoplay_provider.py`：10 项全部通过，耗时 0.16s。
- HTTP 端到端 `HomeContentE2E.Tests`：12 项仍全部通过（Provider 不再退回 sample，envelope 来自真实 `hyp-api`）。
- `dotnet build HomeContentE2E.Tests.csproj -c Debug -p:Platform=x64`：0 个警告、0 个错误。
- 直接 `python -m home_content.server.main` 启动 Worker 后 `Invoke-RestMethod` 调 `/v1/home-content`，返回的 envelope 真实携带 `launcher-webstatic.mihoyo.com` 的 webm 视频和 webp 海报 URL。
- 5 个游戏（`nap_cn`/`hk4e_cn`/`hkrpg_cn`/`bh3_cn`/未知 `game_id`）的真实接口抽样表现符合预期。

### 当前限制

- 当前 Provider 仅返回背景，未补齐 banner / news（主端点不提供）；老的 `<host>/mdk/launcher/api/content` 端点按游戏单独抓样本尚未启动。
- URL 域名白名单手工维护，新增 CDN 域名需更新 `ALLOWED_HOST_SUFFIXES`。
- MIME / MD5 / 尺寸校验在 Provider 中未做；按 AGENTS.md 要求，应在 C# 端 `HttpHomeContentTransport` 拉取后由缓存层负责（待办）。
- OS 端点仅按社区维护的 launcher_id 接入，未独立采样；国际服玩家需要单独验证一次。

## 2026-09-14 21:14:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 新增 Python 端 FastAPI 服务骨架：`python/home_content/server/app.py` 暴露 `GET /healthz`、`POST /v1/home-content` 和 `POST /v1/screenshot-paths`；`main.py` 提供 uvicorn 启动入口；`sample_provider.py` 在真实 Provider 落地前提供确定性示例 envelope，便于 C# 端先行联调。
- 扩展 `pyproject.toml`：加入 `fastapi`、`uvicorn[standard]`、`httpx[http2]` 三个运行时依赖；同时把 `requires-python` 调整为 `>=3.10`，并将 Pydantic 类型注解统一改写为 `Optional[X]` 形式以兼容 3.10 与 3.11，避免 PEP 604 在 3.9 之前的运行时错误。
- 新增 C# 端 `Services/Home/HttpHomeContentTransport.cs`：实现 `IHomeContentTransport`，通过 `HttpClient` 调用 Python Worker 的 `/v1/home-content`，使用既有的 `HomeContentJson` 反序列化 envelope，并在网络错误与 schema 错误时统一抛出 `HomeContentTransportException`，便于上层降级策略使用。
- 新增端到端回归测试 `Tests/HomeContentE2E.Tests`：测试程序自包含——若 `127.0.0.1:8765` 不可达则通过 `HOMECONTENT_VENV`（默认 `%TEMP%\home-content-env`）与 `HOMECONTENT_CWD`（默认仓库 `python/` 根）自动 spawn Python Worker，跑完自动清理。覆盖 schemaVersion、providerId/requestId 回传、background video/poster URL、banners/news 必填字段、未知 providerId 必须返回 400。
- `HttpHomeContentTransport` 兼容 .NET 8/10：`Accept` 头改用 `MediaTypeWithQualityHeaderValue`，避免 .NET 10 SDK 收紧的 `MediaTypeHeaderValue` 签名差异。

### 验证结果

- `HomeContentE2E.Tests`：12 项检查全部通过；测试过程中 Python Worker 子进程成功启动、关闭，无残留进程。
- Python 模块导入（`home_content.server.app`、`provider_registry`、`models`）在临时 venv 下成功。
- `dotnet build HomeContentE2E.Tests.csproj -c Debug -p:Platform=x64`：0 个警告、0 个错误。
- `git diff --check`：通过。

### 当前限制

- 真实 Provider 仍是 `PendingHomeContentProvider`，所有 adapter 落到 sample envelope；接入厂商数据时只需实现各自的 `fetch` 方法，FastAPI 路由层不再改动。
- e2e 测试当前以 `net10.0` 作为本地构建目标（环境只装了 .NET 10 SDK），主项目仍保持 `net8.0-windows10.0.19041.0`；装好 .NET 8 后应改回 `net8.0`。
- Debug 产物落主项目 `bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\HomeContentE2E.Tests\`，未按 AGENTS.md 迁移到 E 盘目录（当前环境无 E: 盘）。

## 2026-09-14 20:29:21 +08:00

- 推送人员：`Violet0923`
- 目标分支：`Refactored_Version`

### 实现内容

- 重构首页布局，使侧边栏、资讯区域和启动区域适配当前固定窗口尺寸。
- 将首页 Banner 与资讯拆分为可复用的 `HomeBannerAndNews` 控件，通过统一 `HomeContent` 模型接收数据。
- 支持动态资讯分类、分类切换、超过可视数量后的滚轮浏览，以及 Banner 自动轮播和悬停暂停。
- 增加 `PreviewHomeContentService`，在 Python 后端接入前通过同一服务边界提供预览数据。
- 将侧边栏改为深灰色，游戏列表入口改为手柄图标；游戏列表浮层显示可执行文件图标，并增加进入游戏配置的添加按钮。
- 调整游戏列表浮层的指针命中范围和关闭延时，减少鼠标从侧边栏移入列表时的闪烁。
- 首页开始游戏按钮接入 Steam 启动流程，并将开始游戏与齿轮入口组合为紧凑的统一按钮区域。
- 增加每个游戏的特殊启动参数字段，生成启动命令时将参数插入真实 EXE 与 `%command%` 之间。
- 重构游戏配置页面：合并 Steam 标识、BuildID 和 Manifest，将常用操作统一放入游戏配置卡片，并把特殊启动参数拆分为独立卡片。
- 将操作名称调整为“一键更新 Steam 配置”“打开游戏启动器”和“保存配置”，三个操作在同一行展示。
- 离开游戏配置页面或切换游戏配置时检测未保存修改，支持保存、不保存或取消切换。
- 将原 Steam 配置导航项的悬停提示改为“游戏配置”。
- 更新 Python 后端对接说明、Debug 输出目录和推送记录规则。

### 验证结果

- 主程序 Debug x64 构建通过，0 个警告、0 个错误。
- `CustomNavigation.Tests`：16 项检查全部通过。
- `HomeContentContract.Tests`：7 项检查全部通过。
- `ManifestUpdate.Tests`：14 项检查全部通过。
- `SteamAppInfo.Tests`：41 项检查全部通过。
- `BackgroundCrop.Tests`：7 项检查全部通过。
- `git diff --check`：通过。

### 当前限制

- 首页当前由 `PreviewHomeContentService` 提供预览内容，厂商数据仍需由后续 Python Provider 实现并通过既有服务接口接入。
- 未保存提示属于 WinUI 交互流程，当前通过编译和配置持久化测试验证，尚未加入自动化 UI 点击测试。
