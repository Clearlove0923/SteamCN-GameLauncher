# KuroLauncherProvider

`python/home_content/providers/kuro_launcher.py` 是库洛《鸣潮》启动器的首页 Provider。
默认指向 **Wuthering Waves 国服 (CN)**(`G152`),库洛其他游戏(战双帕弥什等)共享同一套端点形态,通过 `providerOptions` 传入不同的 `appId` / `appKey` / `gameId` / `region` 即可接入。

> 该 Provider 端点是 Kuro 桌面客户端的非公开内部接口,**不得作为稳定 SLA 的
> 开放 API 使用**。结构随时可能变化。启动器常量通过 `KRApp.conf`
> (`base64(XOR(data, 0x63))`) 解析得出。

## 验证

- **OS / Global 采样**:2026-09-14,游戏 `G153` (Wuthering Waves Global),语言 `en.json`
- **CN / 国服 采样**:2026-09-25,游戏 `G152` (Wuthering Waves 国服),语言 `zh-Hans.json`
- **OS 脱敏样本**:
  - `contracts/samples/kuro-launcher-config.json`
  - `contracts/samples/kuro-wallpapers-slogan.json`
  - `contracts/samples/kuro-news-notices.json`
- **CN 脱敏样本**:
  - `contracts/samples/kuro-cn-launcher-config.json`
  - `contracts/samples/kuro-cn-bg-zh-Hans.json`
  - `contracts/samples/kuro-cn-info-zh-Hans.json`

## 区域(Region)

启动器服务国服和国际服两套 CDN,常量、URL 路径段、语言文件命名都不同:

| Region | CDN base | appId | appKey | gameId | 主语言 | 备用语言 |
|---|---|---|---|---|---|---|
| `cn` (默认) | `prod-cn-alicdn-gamestarter.kurogame.com` | `10003` | `Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5` | `G152` | `zh-Hans` | `en` |
| `os` | `prod-alicdn-gamestarter.kurogame.com` | `50004` | `obOHXFrFanqsaIEOmuKroCcbZkQRBC7c` | `G153` | `en` | — |

`providerOptions['region']` 控制选择哪个区域;**默认 `cn`** —— 这是启动器目标受众的所在区域,
也是唯一会下发中文新闻内容的端点。未识别值降级到 `cn`。

## 语言选择

按以下优先级解析 `lang` 候选列表(从前往后尝试,首个命中即采用):

1. `providerOptions['language']` —— 调用方显式覆盖
2. `request.locale` 前缀:
   - `locale` 以 `zh` 开头 + region=cn → `["zh-Hans", "en"]`
   - `locale` 以 `en` 开头 + region=cn → `["en", "zh-Hans"]`
   - 其它 locale + region=cn → `["zh-Hans", "en"]`
   - region=os 任意 locale → `["en"]`(OS 只发 `en.json`)
3. region 默认(`cn` → `zh-Hans`;`os` → `en`)

每个语言候选尝试时,**wallpaper + news 同时并行抓取**(用 `asyncio.gather` + `return_exceptions=True`):
两个端点都 4xx 才认为该语言不可用,跳到下一个候选;
只要有一个端点 200,就采用该语言,即使另一个端点缺失也用部分数据(例如:wallpaper 4xx 但 news 200
→ 背景走空,但中文资讯仍能展示)。

## 三层端点

每个区域按顺序抓取以下三个端点:

### 1. launcher-config

```
https://<cdn>/launcher/launcher/<appId>_<appKey>/<gameId>/index.json
```

(注意 OS / CN 两套 URL 在 `/launcher/` 前缀上完全相同,只是 CDN + 段值不同。)

响应里 `functionCode.background` 是动态背景 hash,会随版本更新而变化。Provider 用它
构造下一步的 wallpapers-slogan URL。

### 2. wallpapers-slogan

```
https://<cdn>/launcher/<appId>_<appKey>/<gameId>/background/<hash>/<lang>.json
```

响应包含:

| 字段 | 用途 | HomeContent 映射 |
|---|---|---|
| `backgroundFile` | MP4 视频 URL | `HomeBackground.video_url` |
| `firstFrameImage` | 首帧 webp 海报 | `HomeBackground.image_url` |
| `slogan` | 标题叠加 PNG | **未映射**(UI 可单独消费) |
| `backgroundFileType` | 文件 MIME / 后缀提示 | **未映射** |

### 3. news-notices

```
https://<cdn>/launcher/<appId>_<appKey>/<gameId>/information/<lang>.json
```

响应结构:

```json
{
  "guidance": {
    "activity": { "title": "活动", "functionSwitch": 1, "contents": [...] },
    "notice":   { "title": "公告", "functionSwitch": 1, "contents": [...] },
    "news":     { "title": "新闻", "functionSwitch": 1, "contents": [...] }
  },
  "slideshow": [
    { "url": "banner jpg", "jumpUrl": "...", "md5": "...", "carouselNotes": "alt" }
  ]
}
```

(CN 实测三个分类标题均为中文;OS 均为英文。)

映射:

- `slideshow[]` → `HomeBanner[]`(用 `md5` 或 index 生成稳定 id)
- `guidance.activity.contents[]`、`guidance.notice.contents[]`、`guidance.news.contents[]` → `HomeNewsItem[]`,`category` 分别为 `活动` / `公告` / `资讯`
- `functionSwitch == 0` 的分类会被跳过(库洛用这个开关下线分类)
- `time: "MM-DD"` → `published_at` 用当前年补全成 ISO datetime

## 默认常量

| 常量 | 值 | 含义 |
|---|---|---|
| `DEFAULT_REGION` | `cn` | 默认区域 |
| `DEFAULT_CN_APP_ID` | `10003` | 国服应用 ID |
| `DEFAULT_CN_APP_KEY` | `Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5` | 国服 appKey |
| `DEFAULT_CN_GAME_ID` | `G152` | Wuthering 鸣潮 (国服) |
| `DEFAULT_LANGUAGE_CN` | `zh-Hans` | CN 主语言 |
| `DEFAULT_APP_ID` | `50004` | OS 应用 ID(向后兼容) |
| `DEFAULT_APP_KEY` | `obOHXFrFanqsaIEOmuKroCcbZkQRBC7c` | OS appKey(向后兼容) |
| `DEFAULT_GAME_ID` | `G153` | Wuthering 鸣潮 (Global,向后兼容) |
| `DEFAULT_LANGUAGE` | `en` | `DEFAULT_LANGUAGE_OS` 的别名(向后兼容) |

通过 `HomeContentRequest.providerOptions` 可覆盖 region + 三项端点常量 + language:

```json
{
  "providerOptions": {
    "region": "cn",
    "appId": "10003",
    "appKey": "Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5",
    "gameId": "G152",
    "language": "zh-Hans"
  }
}
```

`region` 默认 `cn`;其他字段缺省时各自使用 region 的默认。
也就是说 `providerOptions` 只覆盖"想要偏离 region 默认"的字段。

## 候选资源验证(白名单)

`ALLOWED_HOST_SUFFIXES` 同时覆盖两套区域:

```
.kurogame.com      .kurogames.com
.aki-game.net      .aki-game.com
.kurobbs.com       .mc.kurogames.com
```

各域名实际出处的 CN 实测样本:

| 域名 | 用途 |
|---|---|
| `prod-cn-alicdn-gamestarter.kurogame.com` | CN 启动器配置 / JSON |
| `prod-alicdn-community.kurobbs.com` | CN banner 轮播图 |
| `mc.kurogames.com` | CN 官方公告详情页跳转链接 |
| `www.kurobbs.com` | CN 库洛社区帖子跳转 |
| `pcdownload-huoshan.aki-game.com` | CN 视频 / 首帧 / 资源 CDN |
| `www.bilibili.com` | CN 视频跳转(B站官方号) |

URL 协议必须是 `http(s)`。白名单外的候选视为无效并丢弃;若背景全部候选都被丢弃,
`HomeBackground` 返回 `None`,Worker 把错误写到 `envelope.errors`,
C# 客户端降级到上次成功缓存或静态背景。

## 回退方案

- **语言层面**:每种语言尝试都失败时,跳到下一个候选;全部失败抛 `httpx.HTTPStatusError`
- **端点层面**:wallpaper 或 news 任一 4xx 时仍采用另一端的 200 数据(部分内容可用)
- **整体层面**:HTTP 异常 / ValueError 被 FastAPI 路由层转为 `envelope.errors`,HTTP 状态码仍 200。
  C# 客户端通过 `envelope.errors` 决定是否降级。
- **`envelope.errors.code` 形如 `provider_ConnectError` / `provider_ValueError`**

## 测试

- **Provider 单元测试**:`python/tests/test_kuro_launcher_provider.py`(28 项 pytest,0.3s)
  - 13 项原有 OS / `_allowed` / `_build_*` 覆盖
  - 5 项新增 CN 常量 + region 解析 + locale→language
  - 4 项新增 CN 内容校验(中文标题 / 时间解析 / 中文 banner 副标题 / 完整 fetch)
  - 3 项新增 fetch 边界(全部 4xx、单端点 4xx、zh-Hans→en 回落)
  - 1 项 MM-DD 时间解析
  - 2 项跨语言 fallback 校验
- **HTTP 端到端**:`Tests/HomeContentE2E.Tests/`(dotnet)— 调 `/v1/home-content` 时用 `providerId=kuro-launcher`

## 已知限制

- **`slogan` PNG 标题叠加图未映射**到 `HomeContent`,UI 需要时由 C# 端单独请求。
- **CN `en.json` 内容为空**:CN 端点确实下发 `en.json`,但 contents 数组全空,`slideshow` 也为空。
  Provider 会按 locale→language 顺序自动绕开,优先 `zh-Hans`。
- **OS 不下发中文**:OS 端点 `/information/zh-Hans.json` 返回 404 NoSuchKey。
  若调用方强行 `region=os` + `language=zh-Hans`,Provider 会抛 `HTTPStatusError 404`,
  由 FastAPI 包成 `envelope.errors`。没有 OS 中文兜底。
- **`activity` 分类常为 `functionSwitch=0`**(当前版本没有活动),Provider 自动跳过,
  但调用方如果需要活动内容,需要明确开关。
- 库洛所有 CDN 域名都在白名单;新增域名(如未来上的 `cdn.example.invalid`)需要
  更新 `ALLOWED_HOST_SUFFIXES`。
- MIME / MD5 / 尺寸校验在 Provider 中未做;按 AGENTS.md 要求,文件级校验在
  C# 端 `HttpHomeContentTransport` 拉取后的缓存层完成(待办)。
- **当前 CN 端点的 banner 图片 MD5 / jp(e)g 路径包含日期戳**(`...20260908.jpg`),
  这些都是 CDN 真实资源;Provider 不做改写也不缓存图片本体,只是把它们放进
  `HomeBanner.image_url`。媒体缓存按 AGENTS.md 规则走 URL+内容 hash 文件名。
- **区域常量直接硬编码**:国服 / OS 的 `appId` / `appKey` / `gameId` 是从官方启动器
  `KRApp.conf` 解出来的常量,任何 Kuro 端改 appKey 都要求同步更新代码 + 重采样样本;
  没有运行时发现机制。