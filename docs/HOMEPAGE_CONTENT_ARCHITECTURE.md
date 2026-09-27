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
  "gameId": "wuthering-waves",
  "executablePath": "D:\\Games\\Wuthering Waves Game\\Client\\Binaries\\Win64\\Client-Win64-Shipping.exe",
  "installDirectory": "D:\\Games\\Wuthering Waves Game",
  "cacheFolderName": "Wuthering Waves Game",
  "providerId": "auto",
  "locale": "zh-CN",
  "providerOptions": {}
}
```

`executablePath` 来自预设的真实游戏 EXE，`installDirectory` 来自预设的安装目录。Python 首先按 EXE 文件名匹配，然后按路径中的完整目录段匹配；两者都未命中时返回 `unsupported_game`，不会根据 Steam AppID 猜测。匹配后 Python 从 `python/home_content/game_sources.json` 选择 Provider 及国服参数，忽略请求中可能过期的 `providerId` 和 `providerOptions`。映射表加载一次，使用字典查找，不扫描磁盘。原神示例匹配 `YuanShen.exe` 或 `Genshin Impact Game`；异环允许 `HTGame.exe` 或 `NTEGame.exe`。`cacheFolderName` 优先采用路径中识别出的游戏文件夹名称，其次是用户填写的安装目录末段，最后是稳定游戏标识。请求路径仅传给本机 Worker，不写入日志。

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
      "localPath": null,
      "variants": [
        { "id": "animation-a", "videoUrl": "https://example.invalid/background.mp4", "imageUrl": "https://example.invalid/background.webp", "localPath": null }
      ]
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
- `localPath`：Python 从本地启动器找到，或 C# 媒体缓存保存的绝对路径，可为空。缓存视频后仍保留原始 `videoUrl` 作为播放身份，避免播放中的动画因 HTTPS 地址变成本地路径而重启。
- `variants`：可选的动画候选列表；每项有稳定 `id`、`videoUrl`、可选海报 `imageUrl` 和缓存 `localPath`。客户端每次启动为同一游戏选择一个候选，本次运行中保持不变；不含视频的静态图不进入此列表。
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

### 国服默认来源

SteamCN 内置适配游戏统一使用中国大陆来源。EXE 别名、安装目录别名、Provider 和厂商参数集中维护在
`python/home_content/game_sources.json`，由 Python 包装入 Worker，C# 内嵌同一文件用于即时判断是否显示首页：

| 游戏 | Provider | 默认来源 |
|---|---|---|
| 鸣潮 | `kuro-launcher` | 国服 GameStarter CDN，`G152` / `zh-Hans` |
| 绝区零、崩坏 3 | `hoyoplay-json` | 米哈游中国大陆 HoYoPlay，`*_cn` game biz |
| 燕云十六声 | `netease-static-cms` | `yysls.cn` 国服官网 CMS |
| 无限暖暖 | `nextjs-data` | `infinitynikki.nuanpaper.com` 国服站点与资讯接口 |
| 异环 | `perfect-world-hybrid` | `yh.wanmei.com`、`games.wanmei.com` 和 `wmupd.com` 国服来源 |
| 明日方舟：终末地 | `hypergryph-batch` | 鹰角国服 launcher，`appCode=6LL0KJuqHBVz33WK`、渠道 `1`、`zh-cn` |

新增内置游戏时配置必须包含 `region=cn`；启动器加载配置时会验证这一约束。国际服来源不能作为 SteamCN 内置游戏的隐式回退。默认区域策略变更时必须同步递增 C# 首页缓存键版本，避免旧的国际服元数据在有效期内继续显示。

资讯分类按各游戏返回的数据动态显示，不预设每个游戏都有固定的三个标签。`newsCategoryLabels` 可按游戏来源覆盖展示标签；鸣潮的 `news` 分组显示为“新闻”，其他游戏仍按各自来源显示“资讯”或其他分类。分类按钮切换对应列表，条目和轮播图仅打开数据提供的 HTTPS 链接。

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

## 本地缓存

客户端通过 `CachedHomeContentService` 装饰真实 `IHomeContentService`。页面和 Provider
不直接读写缓存。首页 JSON 元数据和媒体优先存放在软件安装目录；目录不可写时整体回退到用户目录：

```text
<软件目录>\HomeCache
└─ <游戏文件夹名称>
   ├─ metadata\<providerId>\<gameId>\*.json
   └─ media
      ├─ images\<URL-SHA256>.<ext>
      └─ videos\<URL-SHA256>.<ext>

回退：%LOCALAPPDATA%\SteamCN-GameLauncher\Cache\Home
```

- 同一请求在内存中复用 30 秒；JSON 元数据默认 20 分钟内视为新鲜，不请求 Worker。
- 元数据过期后仍立即返回并标记 `IsStale=true`，随后在后台刷新。刷新失败不得覆盖最后一次成功内容。
- 每个请求键保留当前和上一份成功 JSON；超过保留期的元数据按最近访问时间清理。
- 媒体文件在每个稳定 gameId 子目录中按 URL 哈希去重。下载使用临时文件，完成 MIME、大小和文件签名校验后原子发布。
- 图片单文件默认上限 20 MB，视频单文件默认上限 300 MB；媒体总容量默认 1 GB，默认最长保留 30 天。
- `HomeContent` 磁盘副本保留远程 URL。返回 UI 前由媒体缓存把已有文件映射成 `file:` URI 或 Banner `localPath`，因此清理媒体后仍可回退远端资源。
- 缓存容量、保留天数和手动清理入口位于设置页；清理首页缓存不得影响用户导入的 `Backgrounds` 图片。

### 动画解码与循环

- C# 对软件缓存中的 WebM 动画后台生成 H.264 MP4 副本，使用 CRF 18 保留较高画质；首次仍可播放原文件，转换成功后在窗口的双播放器间渐变切换。转换文件与原文件位于同一游戏缓存目录，缓存清理会一并管理。转换仍属有损编码，不能保证逐像素一致。
- 转换使用内置 Python 运行时中 `imageio-ffmpeg==0.6.0` 附带的独立 FFmpeg 可执行文件，低优先级运行，失败或取消时保留原文件播放。转换完成前的首次播放仍可能受设备 VP9 解码性能影响。
- 对本地 MP4 使用双项 `MediaPlaybackList` 预读并循环，避免单个媒体源在末尾反复 seek；远程视频和未转换的 WebM 继续使用原生循环。
- 随运行时分发的 FFmpeg 构建启用了 GPL 组件（含 libx264）。分发安装包时需保留 FFmpeg 的许可与对应源码获取信息；上游说明见 [FFmpeg legal](https://ffmpeg.org/legal.html)、[imageio-ffmpeg](https://pypi.org/project/imageio-ffmpeg/) 和 [Gyan 构建](https://www.gyan.dev/ffmpeg/builds/)。
- 并发的相同请求只允许一个真实刷新；页面通过 `IHomeContentRefreshSource` 接收后台刷新和媒体落盘结果。
