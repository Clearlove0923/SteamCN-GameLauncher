# KuroLauncherProvider

`python/home_content/providers/kuro_launcher.py` 是库洛《鸣潮》启动器的首页 Provider。
当前默认指向 **Wuthering Waves Global**(`G153`),库洛其他游戏(战双帕弥什等)共享同一套端点形态,通过 `providerOptions` 传入不同的 `appId` / `appKey` / `gameId` 即可接入。

> 该 Provider 端点是 Kuro 桌面客户端的非公开内部接口,**不得作为稳定 SLA 的
> 开放 API 使用**。结构随时可能变化。启动器常量通过 `KRApp.conf`
> (`base64(XOR(data, 0x63))`) 解析得出。

## 验证

- **采样日期**:2026-09-14
- **采样游戏**:Wuthering Waves Global(`G153`)
- **采样语言**:`en.json`
- **脱敏样本**:
  - `contracts/samples/kuro-launcher-config.json`
  - `contracts/samples/kuro-wallpapers-slogan.json`
  - `contracts/samples/kuro-news-notices.json`

## 三层端点

Provider 按顺序抓取以下三个端点:

### 1. launcher-config

```
https://prod-alicdn-gamestarter.kurogame.com/launcher/launcher/<appId>_<appKey>/<gameId>/index.json
```

响应里 `functionCode.background` 是动态背景 hash,会随版本更新而变化。Provider 用它
构造下一步的 wallpapers-slogan URL。

### 2. wallpapers-slogan

```
https://prod-alicdn-gamestarter.kurogame.com/launcher/<appId>_<appKey>/<gameId>/background/<hash>/<lang>.json
```

响应包含:

| 字段 | 用途 | HomeContent 映射 |
|---|---|---|
| `backgroundFile` | MP4 视频 URL | `HomeBackground.video_url` |
| `firstFrameImage` | 首帧 webp 海报 | `HomeBackground.image_url` |
| `slogan` | 标题叠加 PNG | **未映射**(UI 可单独消费) |

### 3. news-notices

```
https://prod-alicdn-gamestarter.kurogame.com/launcher/<appId>_<appKey>/<gameId>/information/<lang>.json
```

响应结构:

```json
{
  "guidance": {
    "activity": { "title": "Event", "functionSwitch": 0, "contents": [] },
    "notice":   { "title": "Notice", "functionSwitch": 1, "contents": [...] },
    "news":     { "title": "News", "functionSwitch": 1, "contents": [...] }
  },
  "slideshow": [
    { "url": "banner jpg", "jumpUrl": "...", "md5": "...", "carouselNotes": "alt" }
  ]
}
```

映射:

- `slideshow[]` → `HomeBanner[]`(用 `md5` 或 index 生成稳定 id)
- `guidance.activity.contents[]`、`guidance.notice.contents[]`、`guidance.news.contents[]` → `HomeNewsItem[]`,`category` 分别为 `活动` / `公告` / `资讯`
- `functionSwitch == 0` 的分类会被跳过(库洛用这个开关下线分类)

## 默认常量

| 常量 | 值 | 含义 |
|---|---|---|
| `DEFAULT_APP_ID` | `50004` | 鸣潮在 Kuro 系统内的应用 ID |
| `DEFAULT_APP_KEY` | `obOHXFrFanqsaIEOmuKroCcbZkQRBC7c` | 鸣潮在 Kuro 系统内的 appKey |
| `DEFAULT_GAME_ID` | `G153` | Wuthering Waves |
| `DEFAULT_LANGUAGE` | `en` | 启动器当前只下发 `en.json` |

通过 `HomeContentRequest.providerOptions` 可覆盖全部四项:

```json
{
  "providerOptions": {
    "appId": "50015",
    "appKey": "<PGR_KEY>",
    "gameId": "G143",
    "language": "en"
  }
}
```

## 候选资源验证(白名单)

`ALLOWED_HOST_SUFFIXES` 仅接受以下 CDN:

```
.kurogame.com     .kurogames.com     .aki-game.net
```

URL 协议必须是 `http(s)`。白名单外的候选视为无效并丢弃;若背景全部候选都被丢弃,
`HomeBackground` 返回 `None`,Worker 把错误写到 `envelope.errors`,
C# 客户端降级到上次成功缓存或静态背景。

## 回退方案

当前 Provider 不抛未实现,但**没有** sample envelope 兜底。当三层任意一层失败时,
异常被 FastAPI 路由层转换为 `envelope.errors`,HTTP 状态码仍为 200。C# 客户端
通过 `envelope.errors` 决定是否降级。

`envelope.errors.code` 形如 `provider_ConnectError` / `provider_ValueError`,
便于上层做更精细的处理。

## 测试

- **Provider 单元测试**:`python/tests/test_kuro_launcher_provider.py`(13 项 pytest,0.16s)
- **HTTP 端到端**:`Tests/HomeContentE2E.Tests/`(dotnet)— 调 `/v1/home-content` 时用 `providerId=kuro-launcher`

## 已知限制

- **启动器当前只下发 `en.json`**;请求 `zh-cn.json` 会拿到空响应。增加语言需要
  等 Kuro 发布对应文件,或本地 PlayerAgent 解析后注入。
- **国服 / CN 入口未独立验证**。`G153` 当前是 Global 端点;国服可能用同一组常量
  但不同 CDN,或者用不同的 `appId` / `appKey` / `gameId`。接入前需要重新采样。
- **`slogan` PNG 标题叠加图未映射**到 `HomeContent`,UI 需要时由 C# 端单独请求。
- **`activity` 分类常为 `functionSwitch=0`**(当前版本没有活动),Provider 自动跳过,
  但调用方如果需要活动内容,需要明确开关。
- 库洛所有 CDN 域名都在白名单;新增域名(如未来上的 `cdn.example.invalid`)需要
  更新 `ALLOWED_HOST_SUFFIXES`。
- MIME / MD5 / 尺寸校验在 Provider 中未做;按 AGENTS.md 要求,文件级校验在
  C# 端 `HttpHomeContentTransport` 拉取后的缓存层完成(待办)。