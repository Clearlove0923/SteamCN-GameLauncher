# 统一首页内容架构

## 数据流

```text
WinUI Page / ViewModel
        │
        ▼
IHomeContentService ──────► 本地缓存
        │
        ▼
IHomeContentTransport
        │ UTF-8 JSON
        ▼
Python Provider Registry
        │
        ├─ HoYoPlayJsonProvider
        ├─ KuroLauncherProvider
        ├─ HypergryphBatchProvider
        ├─ PerfectWorldHybridProvider
        ├─ NextJsDataProvider
        ├─ NetEaseStaticCmsProvider
        └─ LocalLauncherAssetProvider
```

Python 将不同厂商和本地启动器来源转换为同一个 `HomeContent`。C# 不感知实际 Provider 类型，只读取统一响应。

`HomeContent` 顶层固定为 `Background`、`Banners[]`、`News[]` 和 `UpdateInfo`。游戏截图目录不属于首页采集模型，由 Python 的独立截图路径注册表提供。

## 请求契约

```json
{
  "schemaVersion": 1,
  "requestId": "9dc1809421354f1c90bd02b512d5d7c4",
  "gameId": "custom-manifest-id",
  "providerId": "kuro-launcher",
  "locale": "zh-CN",
  "providerOptions": {}
}
```

`providerOptions` 是传给 Python Provider 的来源参数。C# 只负责保存和转交，不应解释其中的厂商字段。敏感信息不得直接写入该对象或日志。

## 响应契约

```json
{
  "schemaVersion": 1,
  "requestId": "9dc1809421354f1c90bd02b512d5d7c4",
  "providerId": "kuro-launcher",
  "fetchedAt": "2026-09-12T12:00:00Z",
  "content": {
    "background": {
      "videoUrl": "https://example.invalid/background.mp4",
      "imageUrl": "https://example.invalid/background.webp",
      "localPath": null
    },
    "banners": [
      {
        "id": "banner-1",
        "title": "活动标题",
        "imageUrl": "https://example.invalid/banner.webp",
        "localPath": null,
        "targetUrl": "https://example.invalid/news/1",
        "startsAt": null,
        "endsAt": null
      }
    ],
    "news": [
      {
        "id": "news-1",
        "category": "活动",
        "title": "资讯标题",
        "summary": null,
        "imageUrl": null,
        "targetUrl": "https://example.invalid/news/1",
        "publishedAt": "2026-09-12T08:00:00Z"
      }
    ],
    "updateInfo": {
      "version": "1.0.0",
      "title": "版本更新",
      "summary": "更新内容摘要",
      "targetUrl": "https://example.invalid/update",
      "publishedAt": "2026-09-12T08:00:00Z"
    }
  },
  "errors": []
}
```

## 字段规则

### Background

- `videoUrl`：远程动画或视频地址，可为空。
- `imageUrl`：远程静态背景地址，可为空，并作为视频无法播放时的回退资源。
- `localPath`：Python 从本地启动器找到或缓存到本地的绝对路径，可为空。
- 三个字段允许同时存在。显示优先级由 C# 服务层统一决定，不由页面分别判断。

### Banners

- `id` 在同一 Provider 内保持稳定，用于轮播定位和缓存更新。
- `imageUrl` 与 `localPath` 至少有一个可用。
- `targetUrl` 为空时 Banner 仍可展示，但不可点击。
- 时间字段统一使用 ISO 8601 UTC；没有明确时间时返回 `null`。

### News

- `id`、`title` 必填。
- `category`、`summary`、`imageUrl`、`targetUrl`、`publishedAt` 可为空。
- Provider 返回来源顺序；需要统一排序时由 Python 聚合层完成。

### UpdateInfo

- 整体可为空。
- 用于首页当前版本活动或更新入口，不代表 SteamCN-GameLauncher 自身的软件更新通知。

## Provider 标识

Provider 类名与配置标识分离。首批稳定标识建议为：

| Python 类 | providerId |
|---|---|
| `HoYoPlayJsonProvider` | `hoyoplay-json` |
| `KuroLauncherProvider` | `kuro-launcher` |
| `HypergryphBatchProvider` | `hypergryph-batch` |
| `PerfectWorldHybridProvider` | `perfect-world-hybrid` |
| `NextJsDataProvider` | `nextjs-data` |
| `NetEaseStaticCmsProvider` | `netease-static-cms` |
| `LocalLauncherAssetProvider` | `local-launcher-asset` |

`providerId` 一旦进入用户配置便视为持久化标识。重命名 Python 类时不得直接更改已有标识，需要提供迁移或别名。

## 游戏截图路径契约

截图路径与厂商首页 Provider 解耦。C# 使用 `IGameScreenshotPathService` 按稳定 `gameId` 查询，Python 在 `python/home_content/screenshot_paths.py` 集中维护固定映射。

```json
{
  "schemaVersion": 1,
  "requestId": "screenshot-request-1",
  "gameId": "4162040",
  "screenshotPath": "{steamInstallPath}\\userdata\\{steamId}\\760\\remote\\4162040\\screenshots",
  "errors": []
}
```

- Python 可以写死每个已适配游戏的目录或路径模板，但映射只能集中在截图路径注册表中。
- `{steamInstallPath}` 和 `{steamId}` 由 C# 使用本机设置展开；Python 后端不得猜测用户机器上的绝对目录。
- C# 展开模板后必须规范化并验证路径，再交给截图页面读取。
- 未配置的 `gameId` 返回 `null`，不得猜测其他游戏的路径。

## C# 接入顺序

1. 建立 `Models/Home` DTO，并按 camelCase JSON 契约反序列化。
2. 首页 ViewModel 依赖 `IHomeContentService`，先使用空实现完成 UI。
3. 加入缓存实现，确保没有 Python 时首页仍可用。
4. 实现 `IHomeContentTransport`，处理 HTTP 或本机 Worker 生命周期和 JSON 错误。
5. 接入 Python Provider 注册表与第一个 Provider。
6. 用契约样本执行 Python 输出到 C# 反序列化的兼容测试。
