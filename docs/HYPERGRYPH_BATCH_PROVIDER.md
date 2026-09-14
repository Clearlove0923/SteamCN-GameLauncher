# HypergryphBatchProvider

`python/home_content/providers/hypergryph_batch.py` 是鹰角《明日方舟:终末地》(Endfield)
以及同厂商其他游戏的首页 Provider。当前默认指向 **Endfield Global**,通过
`providerOptions` 可切换到 CN 或指定不同的 `appCode` / `channel` / `subChannel`。

> 该 Provider 端点是 Hypergryph 桌面客户端(以及森空岛 / 网页门户)内部的非公开接口,
> **不得作为稳定 SLA 的开放 API 使用**。结构、域名和 `appCode` 随时可能变化。
> `appCode` 来自启动器打包的资源反编译结果(见
> [`daydreamer-json/ak-endfield-api-archive`](https://github.com/daydreamer-json/ak-endfield-api-archive)
> 的 `config.ts`,base64 编码的 `base` 字段),不在官方公开文档里。

## 验证

- **采样日期**:2026-09-14
- **采样游戏**:Arknights: Endfield(Global + CN)
- **采样语言**:`en-us`(Global)/ `zh-cn`(CN)
- **脱敏样本**:`contracts/samples/hypergryph-batch-envelope.json`(合并 envelope,
  md5 缩短到 8 位,banner/announcement 列表裁剪到 3/2 条)
- **采样脚本**:见 git 历史(一次性脚本,不入仓)

## 单端点批量协议

鹰角用一个 batch 端点下发首页所需的全部数据,Provider 在一次 POST 里并发请求
三种 `kind`,在 `proxy_rsps[]` 中分别收到对应 `*_rsp`。

### 端点

```
POST https://launcher.gryphline.com/api/proxy/web/batch_proxy     (Global)
POST https://launcher.hypergryph.com/api/proxy/web/batch_proxy     (CN)
Content-Type: application/json
```

### 请求

```json
{
  "proxy_reqs": [
    {
      "kind": "get_main_bg_image",
      "get_main_bg_image_req": {
        "appcode": "YDUTE5gscDZ229CW",
        "channel": "6",
        "sub_channel": "6",
        "language": "en-us",
        "platform": "Windows",
        "source": "launcher"
      }
    },
    {
      "kind": "get_banner",
      "get_banner_req": { ... 同上字段 ... }
    },
    {
      "kind": "get_announcement",
      "get_announcement_req": { ... 同上字段 ... }
    }
  ]
}
```

### 响应

```json
{
  "proxy_rsps": [
    { "kind": "get_main_bg_image", "get_main_bg_image_rsp": { ... } },
    { "kind": "get_banner",        "get_banner_rsp":          { ... } },
    { "kind": "get_announcement",  "get_announcement_rsp":    { ... } }
  ]
}
```

注意:服务端为每种 `kind` 返回一个独立的 `proxy_rsps[]` 元素,**不**是把所有
`*_rsp` 塞到 `proxy_rsps[0]`。`_extract_responses` 统一处理两种布局(单元素 + 多元素)。

## 字段映射

| 上游字段 | HomeContent 字段 | 备注 |
|---|---|---|
| `get_main_bg_image_rsp.main_bg_image.video_url` | `HomeBackground.video_url` | 优先使用 |
| `get_main_bg_image_rsp.main_bg_image.url` | `HomeBackground.image_url` | 视频缺失时回退 |
| `get_banner_rsp.banners[].url` | `HomeBanner.image_url` | 不在白名单的丢弃 |
| `get_banner_rsp.banners[].jump_url` | `HomeBanner.target_url` | 非 https / 非白名单 host 时置空 |
| `get_banner_rsp.banners[].id` | `HomeBanner.id` | 形如 `hg-banner-{id}` |
| `get_announcement_rsp.tabs[].tabName` | `HomeNewsItem.category` | 见下表映射 |
| `get_announcement_rsp.tabs[].announcements[].content` | `HomeNewsItem.title` | 空标题丢弃 |
| `get_announcement_rsp.tabs[].announcements[].jump_url` | `HomeNewsItem.target_url` | 同上规则 |
| `get_announcement_rsp.tabs[].announcements[].start_ts` | `HomeNewsItem.published_at` | ms → UTC datetime |
| `get_announcement_rsp.tabs[].announcements[].id` | `HomeNewsItem.id` | 形如 `hg-news-{id}` |

### Tab 名称 → 规范化分类

| 英文 tabName | HomeNewsItem.category |
|---|---|
| `Notices` | `公告` |
| `Events` | `活动` |
| `News` | `资讯` |
| 其他 | 保留原文(UI 落到通用分组) |

鹰角的启动器目前只在 CN 端下发中文 tabName,在 Global 端下发英文 tabName;
Provider 同时支持两种形式。

## 默认常量

| 常量 | 值 | 含义 |
|---|---|---|
| `DEFAULT_BASE_OS` | `https://launcher.gryphline.com/api` | Global API base |
| `DEFAULT_BASE_CN` | `https://launcher.hypergryph.com/api` | CN API base |
| `DEFAULT_APP_CODE` | `YDUTE5gscDZ229CW` | Endfield Global 的 appCode |
| `DEFAULT_CHANNEL` | `6` | osWinRel |
| `DEFAULT_SUB_CHANNEL` | `6` | osWinRel |
| `DEFAULT_LANGUAGE` | `en-us` | Global 默认语言 |
| `DEFAULT_REGION` | `os` | 默认区域(影响 baseUrl) |

通过 `HomeContentRequest.providerOptions` 可覆盖全部字段:

```json
{
  "providerOptions": {
    "region": "cn",
    "baseUrl": "https://launcher.hypergryph.com/api",
    "appCode": "6LL0KJuqHBVz33WK",
    "channel": "1",
    "subChannel": "1",
    "language": "zh-cn"
  }
}
```

鹰角其他游戏(如《明日方舟》本体)接入同一组端点形态,只需替换 `appCode` /
`channel` / `subChannel`,Provider 不需要改代码。

## 候选资源验证(白名单)

`ALLOWED_HOST_SUFFIXES` 仅接受以下 host 后缀:

```
.hg-cdn.com     .hycdn.cn       .gryphline.com
.hypergryph.com  .skport.com     .skland.com
```

- `.hg-cdn.com`:Global CDN(`gl-utils-public.hg-cdn.com`)
- `.hycdn.cn`:CN CDN(`hg-utils-public.hycdn.cn`)
- `.gryphline.com` / `.hypergryph.com`:鹰角官方域名,涵盖新闻落地页
- `.skport.com` / `.skland.com`:森空岛文章/活动页(Global / CN 各一)

URL scheme 必须是 `http://` 或 `https://`,其它协议直接拒绝。白名单外的 host
视为无效并丢弃;若背景全部候选都被丢弃,`HomeBackground` 返回 `None`,Worker
把错误写到 `envelope.errors`,C# 客户端降级到上次成功缓存或静态背景。

> Banner / 资讯里出现的 `x.com` / `tiktok.com` / `discord.gg` /
> `store.steampowered.com` 等第三方域名**不在白名单**,会被 Provider 自动剔除。
> 这些外部链接当前不会进入首页 UI(侧边栏 sidebar 也没有透传)。

## 回退方案

当前 Provider 不抛未实现,但**没有** sample envelope 兜底。当 batch_proxy 失败时,
异常被 FastAPI 路由层转换为 `envelope.errors`,HTTP 状态码仍为 200。C# 客户端
通过 `envelope.errors` 决定是否降级。

`envelope.errors.code` 形如 `provider_ConnectError` / `provider_ValueError`,
便于上层做更精细的处理。

## 测试

- **Provider 单元测试**:`python/tests/test_hypergryph_batch_provider.py`(23 项 pytest,< 0.3s)
  - 覆盖 allow-list、白名单过滤、tab 映射、`start_ts` 转 UTC、providerOptions 覆盖、
    空 envelope、非 JSON 响应、HTTP 5xx 上抛
- **HTTP 端到端**:`Tests/HomeContentE2E.Tests/`(dotnet)— 调 `/v1/home-content` 时用
  `providerId=hypergryph-batch`(未在 sample 服务中注册,需要在 FastAPI 入口扩展)

## 已知限制

- **`appCode` 反编译得到**,不公开。如鹰角发版时更换字符串,Provider 默认值会失效,
  需要重新采样。
- **`data_version` 字段始终为空**,Provider 不参与版本协商;若鹰角后续引入版本化协议,
  需要扩展 `_extract_responses` 并新增缓存键策略。
- **`get_url_config`、`get_sidebar`、`get_single_ent` 未映射**到 HomeContent:
  `get_url_config` 是充值/问卷链接(无 UI 落点);`get_sidebar` 是社交媒体入口(布局
  不属于首页);`get_single_ent` 当前为空(没有版本按钮)。
- **响应 `data_version` 字段当前始终为空**,Provider 不做版本协商。
- **Global 各语言**(`de-de`、`es-mx` 等)在 `defaultSettings.launcherWebLang` 中
  定义,但 Provider 默认 `en-us`;其他语言通过 `providerOptions.language` 切换。
- **CN 仅 `zh-cn`**(`defaultSettings.launcherWebLangCN`),其他语言通过
  `providerOptions.language` 切换时由 Provider 透传给上游。
- 所有 CDN 域名都在白名单;新增域名(如未来上的新 CDN)需要更新 `ALLOWED_HOST_SUFFIXES`。
- MIME / MD5 / 尺寸校验在 Provider 中未做;按 AGENTS.md 要求,文件级校验在
  C# 端 `HttpHomeContentTransport` 拉取后的缓存层完成(待办)。