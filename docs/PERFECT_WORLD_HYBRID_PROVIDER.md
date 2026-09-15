# PerfectWorldHybridProvider

`python/home_content/providers/perfect_world_hybrid.py` 是完美世界《异环》(Neverness to Everness)启动器的首页 Provider。背景优先读本地启动器资源,其余来自完美世界官网的 JS 数据端点。

> 该 Provider 同时调用两个**已知的、但**非公开 API(完美世界 / 鹰角 / 库洛 / 米哈游 / 网易 / 叠纸的首页内容端点均如此)。**不得作为稳定 SLA 的开放 API 使用**。端点 URL 在新版启动器或官网改版后可能漂移。

## 验证

- **采样日期**:2026-09-14
- **采样游戏**:Neverness to Everness(Global `nte.perfectworld.com` + 国服 `yh.wanmei.com`)
- **采样语言**:`en-us`(Global)/ `zh-cn`(国服)
- **脱敏样本**(4 个):
  - `contracts/samples/nte-game-swiper-intl.json` — Global 横幅 swiper 数据
  - `contracts/samples/nte-news-data-intl.json` — Global 资讯数据
  - `contracts/samples/nte-game-swiper-cn.json` — 国服横幅 swiper 数据
  - `contracts/samples/nte-news-data-cn.json` — 国服资讯数据

每个文件对应一个上游 JS 端点的 body 截断版(去掉了 `var NAME = ` 包装与重复逗号)。URL 域名保留以便 allow-list 校验路径与生产一致。

## 数据源(2 个端点,每次 fetch 调一次 POST / 一次 GET)

| 来源 | OS 端点                                                                              | CN 端点                                                                              |
|------|---------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------|
| 横幅 swiper | `https://www.perfectworld.com/public/commonData/gamesData/gameSwiper/nte-gameSwiper.js` | `https://static.games.wanmei.com/public/commonData/gamesData/gameSwiper/yh-gameSwiper.js` |
| 资讯 data    | `https://nte.perfectworld.com/include/newsData20260112.js`                              | `https://yh.wanmei.com/include/newsData20260112.js`                                      |

两个端点都返回 `var NAME = {...};` 形式的 JS 对象字面量(`extract_js_payload` 会剥掉包装、压缩重复逗号、去除尾部逗号后用 `json.loads` 解析)。

### 横幅 swiper 响应

```jsonc
// OS: lang-keyed buckets
{
  "lb1_en": [{ "title": "...", "viceTitle": "...", "bigpic": "https://...",
              "viewpic": "...", "link": "https://...", "mlink": "https://..." }, ...],
  "lb1_cn": [...],
  "lb1_jp": [...]
}

// CN: single-language buckets
{
  "lb1": [...],
  "launcherPic": [...]
}
```

### 资讯 data 响应

```jsonc
// OS: per-language buckets
{
  "en": {
    "news":       [{ "title": "...", "url": "/en/article/news/.../1.html",
                     "time": "2026-09-15", "channelDescription": "News",
                     "channelName": "gamenews" }, ...],
    "gamenews":   [...],   // game news / update notes
    "gamebroad":  [...],   // account action notices
    "gameevent":  [...]    // events
  },
  "cn": { ... },
  "jp": { ... }
}

// CN: device-keyed buckets
{
  "pc": { "news": [...], "gamenews": [...], "gamebroad": [...], "gameevent": [...] },
  "m":  { ... }
}
```

每条资讯:`{title, url, time, channelDescription/channelCnName, channelName}`。

## 字段映射

| 上游字段                                       | HomeContent 字段         | 备注 |
|------------------------------------------------|--------------------------|------|
| `get_main_bg_image` 视频 URL(默认 `ntevmg.perfectworld.com` / `yhvmg.wmupd.com`) | `HomeBackground.video_url` | 优先于 `providerOptions.backgroundVideoPath` / `backgroundVideoUrl` |
| `main_bg_image.url` PNG / WebP                | `HomeBackground.image_url` | 视频缺失时回退 |
| `lb1_<lang>`/`lb1` banners[].bigpic           | `HomeBanner.image_url`   | 不在白名单的丢弃 |
| `lb1_<lang>`/`lb1` banners[].link             | `HomeBanner.target_url`   | 非 https / 非白名单 host 时置空 |
| `lb1_<lang>`/`lb1` banners[].title            | `HomeBanner.title`       | 空串归一为 `None` |
| `tabs[].announcements[].content`              | `HomeNewsItem.title`     | 空标题丢弃 |
| `tabs[].announcements[].url`                  | `HomeNewsItem.target_url` | 相对路径按区域加 host,见 `_absolutize_url` |
| `tabs[].announcements[].time`                 | `HomeNewsItem.published_at` | `YYYY-MM-DD` → UTC `datetime` |

### Tab 名 → 规范化分类

CN 端 `channelCnName` 已经是中文(`公告` / `新闻` / `活动`),直接透传。OS 端 `channelDescription` 是本地化字符串,通过 `TAB_NAME_MAP` 映射:

| channelDescription | category |
|--------------------|----------|
| Notices / Mitteilungen / Annonces / Avisos / 공지 / お知らせ | 公告 |
| News / Nachrichten / Actualités / Noticias / Berita / ข่าวสาร / Notícias / Новости | 资讯 |
| Events / Événements / Eventos / 이벤트 / イベント / События | 活动 |
| 未知                                 | 通用 bucket 兜底 |

通用 bucket 兜底:`news` → 公告、`gamebroad` → 公告、`gameevent` → 活动、`gamenews` → 资讯。

## 默认常量

| 常量 | 值 | 含义 |
|------|-----|------|
| `DEFAULT_APP_CODE` | `YDUTE5gscDZ229CW` | Endfield Global 的启动器 appCode |
| `DEFAULT_LANGUAGE` | `en-us` | 默认语言 |
| `DEFAULT_REGION` | `os` | 默认区域(影响 banner swiper / news data 的端点 + 背景 URL) |
| `DEFAULT_BG_VIDEO_OS` | `https://ntevmg.perfectworld.com/webops/nte/nte_bgvideo_20260418.mp4` | Global 启动器首页背景视频 |
| `DEFAULT_BG_VIDEO_CN` | `https://yhvmg.wmupd.com/webops/yh/yh_bgvideo_20260418.mp4` | 国服启动器首页背景视频 |

通过 `HomeContentRequest.providerOptions` 可覆盖:

```json
{
  "region": "cn",
  "language": "zh-cn",
  "installDir": "C:/Games/NTE",
  "backgroundVideoPath": "C:/Games/NTE/Client/bg.mp4",
  "backgroundImagePath": "C:/Games/NTE/Client/bg.jpg",
  "bannerSwiperUrl": "https://custom.example.test/swiper.js",
  "newsDataUrl": "https://custom.example.test/news.js"
}
```

## 候选资源验证(白名单)

`ALLOWED_HOST_SUFFIXES` 仅接受以下 host 后缀:

```
.perfectworld.com     .wanmei.com         .wmupd.com
.games.wanmei.com     .static.pwsdk.com
```

URL scheme 必须是 `http://` 或 `https://`,其它协议直接拒绝。白名单外的 host 视为无效并丢弃;若背景全部候选都被丢弃,`HomeBackground` 返回 `None`,Worker 把错误写到 `envelope.errors`,C# 客户端降级到上次成功缓存或静态背景。

## 本地启动器资源

NTE 启动器(QtQuick + CEF)位于 `%LocalAppData%\NTEGame`,游戏本体在 `Neverness To Everness\Client\WindowsNoEditor\HT\`。当前 Provider 不读取安装目录,但 `providerOptions.backgroundVideoPath` / `backgroundImagePath` 暴露了 `bg.mp4` / `bg.jpg` 的本地路径入口,集成方可在 C# 端先用 `LocalLauncherAssetProvider` 探测路径,再喂给本 Provider。

> 没有自动探测 `installDir` 是因为 AGENTS.md 要求 Provider 与传输解耦 — C# 侧负责发现本地资源,Python 侧只消费 URL/路径。

## 回退方案

- 网络两端点 503 / 解析失败 → 返回空 banner / 空 news,`HomeBackground` 仍走默认 URL;`envelope.errors` 记录失败。
- 上游 JS 体损坏(`extract_js_payload` 抛 `ValueError`) → 同上,Worker 写入 `envelope.errors`。
- URL 字段非 https 或不在白名单 → Provider 静默丢弃,**不**抛错。

`envelope.errors.code` 形如 `provider_ConnectError` / `provider_ValueError`,便于上层做更精细的处理。

## 测试

- **Provider 单元测试**:`python/tests/test_perfect_world_hybrid_provider.py`(41 项 pytest,< 0.4s)
  - 覆盖 JS 提取、allow-list、tab 映射、本地 vs 网络背景、COS URL 相对路径绝对化、providerOptions 覆盖、HTTP 5xx / 非 JSON 响应不抛错
- **HTTP 端到端**:`Tests/HomeContentE2E.Tests/`(dotnet)— 调 `/v1/home-content` 时用 `providerId=perfect-world-hybrid`(未在 sample 服务中注册,需要在 FastAPI 入口扩展)

## 已知限制

- **`appCode` / 启动器常量** 反编译自 NTE 启动器资源(见 AGENTS.md),不公开。发版后可能漂移,需要重新采样。
- **JS 端点格式** 是完美世界官网的私有数据形态(JS 对象字面量,带 trailing comma),`extract_js_payload` 处理了双逗号、尾随逗号、空行分隔;但如果未来版式变了,需要更新正则。
- **CN 资讯** 来自 `pc.*` bucket,不区分语言。**OS 资讯** 在 `lang.*` bucket,目标语言不在响应里时会兜底到 `cn` 再兜底到 `en`,不会爆掉。
- **横幅 swiper** OS 端按 `lb1_<lang>` 选择,CN 端取 `lb1`(不按语言)。当 `lb1_<lang>` 缺失时 Provider 会按 `lb1` 兜底(空数组不会触发)。
- **`start_ts` 没有时间字段**:OS 资讯的 `time` 是 `YYYY-MM-DD` 字符串,精度到天。CN 同样。提供 UTC `datetime` 转换。
- 所有 CDN / 官方域名都在白名单;新增域名(如未来上的新 CDN)需要更新 `ALLOWED_HOST_SUFFIXES`。
- MIME / MD5 / 尺寸校验在 Provider 中未做;按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责(待办)。