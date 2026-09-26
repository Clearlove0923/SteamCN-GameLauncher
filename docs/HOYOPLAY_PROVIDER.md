# HoYoPlay Provider

`python/home_content/providers/hoyoplay_json.py` 是米哈游 / HoYoverse 启动器的首页 Provider,
服务原神、崩坏3、星穹铁道、绝区零、未定事件簿、云·原神。

> 该 Provider 的端点是 HoYoPlay 桌面客户端的非公开内部接口,**不得作为
> 稳定 SLA 的开放 API 使用**。结构随时可能变化。

## 验证

- **采样日期**:2026-09-14
- **采样区域**:CN(国服)
- **采样语言**:zh-cn
- **采样 launcher**:`jGHBHlcOq1`
- **脱敏样本**:`contracts/samples/hoyoplay-cn-launcher-info.json`
  (URL 中的 32 字符 hex hash 全部替换为 `HASH32`)
- **内容样本**:`contracts/samples/hoyoplay-cn-zzz-content.json`
  (2026-09-26 验证绝区零 `getGameContent`;保留字段结构并替换资源 URL 和文章编号)

## 主端点

```
https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id=<LAUNCHER_ID>&language=<LANG>
```

OS 端点使用 `https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getAllGameBasicInfo`。

### Launcher ID

| 区域 | Launcher ID | 用途 |
|---|---|---|
| CN | `jGHBHlcOq1` | 中国大陆启动器(2026-09-14 验证) |
| OS | `VYTpXlbWo8` | 国际启动器(由社区维护,未单独验证) |

Launcher ID 通过 `HomeContentRequest.providerOptions.launcher_id` 传入,缺省时按
`providerOptions.region` 取 `cn` 或 `os` 的默认值。

### 游戏 biz 映射

`providerOptions.gameBiz` 直接传入;缺省时根据 `request.gameId` 前缀推断:

| 前缀 | biz | 适用游戏 |
|---|---|---|
| `bh3` | `bh3_cn` | 崩坏3 |
| `hk4e` | `hk4e_cn` | 原神 |
| `hkrpg` | `hkrpg_cn` | 崩坏:星穹铁道 |
| `nap` | `nap_cn` | 绝区零 |
| `hyg` | `hyg_cn` | 云·原神 |
| `abc` | `abc_cn` | 未定事件簿 |

未匹配时退回 launcher 列表中的**第一个游戏**(目前是云·原神,只有静态图)。

## 响应结构(已观察)

```json
{
  "retcode": 0,
  "message": "OK",
  "data": {
    "game_info_list": [
      {
        "game": { "id": "x", "biz": "nap_cn" },
        "backgrounds": [
          {
            "id": "...",
            "background": { "url": "https://launcher-webstatic.mihoyo.com/.../bg.webp" },
            "icon": { "url": "...", "hover_url": "...", "md5": "..." },
            "video": { "url": "https://launcher-webstatic.mihoyo.com/.../bg.webm" },
            "theme": { "url": "..." }
          }
        ]
      }
    ]
  }
}
```

每个游戏通常返回 1–4 个 background 候选项。Provider 的选择策略:

1. **首选有 `video.url` 的候选** — 背景动画由 C# 端 `MediaPlayer` 循环播放
2. **降级到第一个有 `background.url` 的候选** — 静态海报,UI 用作视频失败回退
3. **候选 URL 域名不在白名单时丢弃** — 白名单见下节

## 候选资源验证(白名单)

`ALLOWED_HOST_SUFFIXES` 在 `hoyoplay_json.py` 顶部声明,仅接受以下 CDN 域名:

```
.mihoyo.com        .hoyoverse.com      .yuanshen.com
.bh3.com           .honkaistarrail.com .zenlesszonezero.com
.miyoushe.com
```

URL 协议必须是 `http(s)`。白名单外的候选视为无效并丢弃;若所有候选都被丢弃,
Provider 返回 `background=None`,Worker 把错误写到 `envelope.errors`,
C# 客户端降级到静态背景或上次成功缓存。

> ⚠️ AGENTS.md 要求"下载后校验响应 MIME、文件尺寸、md5"。该 Provider 只做
> URL 域名白名单验证;**文件下载和 MIME / MD5 校验由 C# 端
> `HttpHomeContentTransport` 触发后的缓存层负责**(后续工作)。

## 回退方案

当主端点失败时,Provider **不抛未实现**;当前实现在以下情况把错误写进
`envelope.errors`,让客户端降级:

| 异常 | envelope errors code |
|---|---|
| `httpx.HTTPError`(连接、超时、5xx) | `provider_ConnectError` / `provider_TimeoutException` / ... |
| `ValueError`(retcode != 0、空响应) | `provider_ValueError` |
| `ValidationError`(Provider 输出的内容无法通过 schema) | `provider_ValidationError` |

FastAPI 路由层将所有上述异常转换成 `HomeContentEnvelope { content: 空, errors: [...] }` 返回,
HTTP 状态码仍是 200。C# 客户端检查 `Errors` 决定是否使用上次缓存。

背景成功后,Provider 从选中的 `game_info_list[].game.id` 调用同一 HoYoPlay 服务的
`getGameContent` 端点。`data.content.banners` 转换为轮播图，`posts` 的
`POST_TYPE_ACTIVITY / POST_TYPE_ANNOUNCE / POST_TYPE_INFO` 分别转换为“活动 / 公告 / 资讯”。
内容端点失败时保留已经取得的背景并返回空内容，避免单一来源失败拖垮整个首页。

## 测试

- **Provider 单元测试**:`python/tests/test_hoyoplay_provider.py`(pytest,14 项)
- **HTTP 端到端**:`Tests/HomeContentE2E.Tests/`(dotnet,12 项)— 调 `/v1/home-content`
  并断言 schemaVersion / providerId / background 字段

## 已知限制

- `updateInfo` 尚未接入
- 当前内容端点已使用绝区零国服实测；其他 HoYo 游戏继续沿用相同结构，但新增区域或游戏时仍需保存脱敏样本验证
- URL 域名白名单是手工维护;新增 CDN 域名需要更新 `ALLOWED_HOST_SUFFIXES`
- OS 端点未单独采样,需要使用国际服启动器再做一次样本验证
