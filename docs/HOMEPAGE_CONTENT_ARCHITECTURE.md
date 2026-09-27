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

`executablePath` 来自预设中用户填写的 EXE，可能是游戏本体，也可能是自制的直启桥接程序；`installDirectory` 来自预设的安装目录。Python 首先按 EXE 路径中由近到远的完整游戏目录段匹配，再按填写的安装目录由近到远匹配，最后按 EXE 完整文件名兜底；都未命中时返回 `unsupported_game`，不会根据 Steam AppID 猜测。匹配后 Python 从 `python/home_content/game_sources.json` 选择 Provider 及国服参数，忽略请求中可能过期的 `providerId` 和 `providerOptions`。映射表加载一次，使用字典查找，不扫描磁盘。`E:\Games\原神\Genshin Impact Game\YuanShen.exe` 优先命中最近的 `Genshin Impact Game`；自制 EXE 位于该目录内时也按原神路由。`cacheFolderName` 优先采用路径中识别出的游戏文件夹名称，其次是用户填写的安装目录末段，最后是稳定游戏标识。请求路径仅传给本机 Worker，不写入日志。

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

首页按公司选择 Provider，再按共享来源字典中的 `gameId` 和游戏参数选择该公司的具体游戏。旧版来源形态标识仍可用于读取已保存的配置；新增来源使用公司标识：

| Python 类 | providerId |
|---|---|
| `MiHoYoProvider` | `mihoyo`；旧 `hoyoplay-json` |
| `KuroProvider` | `kuro`；旧 `kuro-launcher` |
| `HypergryphProvider` | `hypergryph`；旧 `hypergryph-batch` |
| `PerfectWorldProvider` | `perfect-world`；旧 `perfect-world-hybrid` |
| `PaperGamesProvider` | `papergames`；旧 `nextjs-data` |
| `NetEaseProvider` | `netease`；旧 `netease-static-cms` |
| `LocalLauncherAssetProvider` | `local-launcher-asset` |

### 国服默认来源

SteamCN 内置适配游戏统一使用中国大陆来源。EXE 别名、安装目录别名、Provider 和厂商参数集中维护在
`python/home_content/game_sources.json`，由 Python 包装入 Worker，C# 内嵌同一文件用于即时判断是否显示首页：

| 游戏 | Provider | 默认来源 |
|---|---|---|
| 鸣潮 | `kuro` | 国服 GameStarter CDN，`G152` / `zh-Hans` |
| 原神、绝区零、崩坏：星穹铁道、崩坏 3 | `mihoyo` | 米哈游中国大陆 HoYoPlay，各游戏分别使用 `hk4e_cn`、`nap_cn`、`hkrpg_cn`、`bh3_cn` |
| 燕云十六声 | `netease` | `yysls.cn` 国服官网 CMS |
| 无限暖暖 | `papergames` | `infinitynikki.nuanpaper.com` 国服站点与资讯接口 |
| 异环 | `perfect-world` | `yh.wanmei.com`、`games.wanmei.com` 和 `wmupd.com` 国服来源 |
| 明日方舟：终末地 | `hypergryph` | 鹰角国服 launcher，`appCode=6LL0KJuqHBVz33WK`、渠道 `1`、`zh-cn` |

新增内置游戏时配置必须包含 `region=cn`；启动器加载配置时会验证这一约束。国际服来源不能作为 SteamCN 内置游戏的隐式回退。默认区域策略变更时必须同步递增 C# 首页缓存键版本，避免旧的国际服元数据在有效期内继续显示。

匹配名称以用户填写的 EXE 路径和安装目录中的完整游戏目录段为优先，EXE 文件名仅作兜底。字典包含上述游戏的常见中文安装目录名；《无限暖暖》属于叠纸来源，当前仍使用已验证的官网 Next.js 数据。匹配到米哈游游戏后，`mihoyo` 再以该游戏的 `gameBiz` 选择专属首页内容；缺少有效游戏选择时返回错误，避免误显示旗下另一款游戏。目录名中的冒号在 Windows 路径中不可用，例如《崩坏：星穹铁道》使用 `崩坏星穹铁道` 作为目录别名。未匹配的路径不会借用其他游戏的 Provider。

`exeNames` 只记录游戏本体、正式启动器或实际承担启动链路的桥接 EXE；不包含崩溃上报、WebView、反作弊服务、SDK、安装器、更新器、备份和诊断程序。来源可以是本机安装链路，也可以是官方资料、发行平台启动配置或公开补丁清单。`matchNotes` 和 `matchSources` 记录版本、证据和未核实范围。多个游戏可使用同名启动器，目录匹配仍优先，脱离已识别目录的同名 EXE 不触发 Provider 选择。新增公司或游戏时按 `AGENTS.md` 的“新增公司或游戏的首页匹配字典”规则补充来源和两端离线测试。

资讯分类按各游戏返回的数据动态显示，不预设每个游戏都有固定的三个标签。每条游戏来源都配置 `newsCategoryLabels` 和 `newsCategoryOrder`，由该游戏已核对的官网或启动器来源决定名称和顺序；无限暖暖配置为“公告 / 新闻 / 活动”，其中来源的“资讯”显示为“新闻”。只显示本次确有内容的分类，未知分类追加显示；空内容时不造默认“资讯”标签。轮播图保持原始宽高比填满资讯栏，比例不一致时裁切边缘。分类按钮切换对应列表，条目和轮播图仅打开数据提供的 HTTPS 链接。

所有公司和游戏的本机采集共用一个 Python Worker。启动时若配置端口已被其他进程占用，本次会在空闲回环端口启动自己的 Worker，不复用可能过期的旧进程；页面使用本次 Worker 的实际端口。主程序正常退出时结束其进程树，Windows Job 对象也在主程序异常退出后结束该 Worker。用户自行启动的外部服务不属于本程序，不会被结束。

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

- C# 对软件缓存中的 WebM 动画后台生成 H.264 MP4 副本，使用 CRF 18 保留较高画质；首次仍可播放原文件。切换游戏时暂停旧动画并保留当前帧，新动画在第二个 `MediaPlayerElement` 中准备，打开并短暂预热后保持完全不透明，只淡出已暂停的旧画面，再释放旧播放器。没有新动画时显示游戏静态背景；未适配或没有可用背景时显示外观设置中的首页背景图片。转换文件与原文件位于同一游戏缓存目录，缓存清理会一并管理。转换仍属有损编码，不能保证逐像素一致。
- 转换使用内置 Python 运行时中 `imageio-ffmpeg==0.6.0` 附带的独立 FFmpeg 可执行文件，低优先级运行，失败或取消时保留原文件播放。转换完成前的首次播放仍可能受设备 VP9 解码性能影响。
- 本地 MP4 继续使用两个相同播放项预读循环，其余视频使用播放器原生循环。窗口隐藏到托盘前暂停播放器，重新显示后再继续播放。**遗留事项（2026-09-27）：**原神与绝区零切换时，新动画刚露出仍会出现肉眼可见的快速整体跳动；托盘恢复没有这个现象，OBS 和手机录制均无法可靠捕捉。去掉交叉淡入、只替换单个播放器的媒体源、播放原始 WebM、分别淡入新视频或淡出旧视频、提取静态帧承接并提前释放旧播放器均未消除跳动。静态帧实验没有收益，未保留在正式实现中。白屏已经消除；跳动原因与修复待后续处理。
- 随运行时分发的 FFmpeg 构建启用了 GPL 组件（含 libx264）。分发安装包时需保留 FFmpeg 的许可与对应源码获取信息；上游说明见 [FFmpeg legal](https://ffmpeg.org/legal.html)、[imageio-ffmpeg](https://pypi.org/project/imageio-ffmpeg/) 和 [Gyan 构建](https://www.gyan.dev/ffmpeg/builds/)。
- 并发的相同请求只允许一个真实刷新；页面通过 `IHomeContentRefreshSource` 接收后台刷新和媒体落盘结果。
