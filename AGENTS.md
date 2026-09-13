# SteamCN-GameLauncher 开发规则

本文件适用于整个仓库。开发首页、游戏配置、背景播放、资讯展示和 Python 采集模块时必须遵守以下规则。

## 产品与技术边界

本项目使用 WinUI 3 / C# 构建桌面客户端。C# 负责窗口、页面、原生媒体播放、缓存读取、链接跳转、错误状态和用户交互；Python 负责发现、请求、解析并标准化各游戏厂商的首页内容。

Python 是统一采集入口，不是“零适配万能爬虫”。各来源的协议、参数和数据布局不同，必须通过 Provider 或声明式规则适配。不得把自动发现的候选资源未经验证直接显示给用户。

优先采用以下数据流：

```text
厂商 API / 官网 CMS ──► 服务端 Python 聚合器 ──┐
                                                ├─► 统一首页 JSON ─► C# 缓存与原生 UI
本地启动器资源 ───────► 可选本机 Python Worker ─┘
```

- Web API、HTML、JS 和官网 CMS 内容优先由服务端 Python 定时采集，客户端读取统一接口和旧缓存。
- 只有必须访问用户安装目录的内容才使用本机 Worker，例如《异环》的本地 `bg.mp4` 和 `config.json`。
- C# 的数据服务必须与传输方式解耦，允许在远程聚合接口、本机 Worker、缓存和测试数据之间替换。
- 不得要求每台客户端使用 Playwright 抓取普通网页；采集顺序应为 JSON/RSS、静态 HTML、页面内 JSON 或 JS 数据、浏览器渲染、专用 Provider。

## 统一首页模型

所有来源最终转换为同一个来源无关模型。以下结构是最低契约：

```text
HomeContent
├─ Background
│  ├─ VideoUrl
│  ├─ ImageUrl
│  └─ LocalPath
├─ Banners[]
├─ News[]
└─ UpdateInfo
```

允许为可靠播放和缓存增加可选字段，例如 `PosterUrl`、`OverlayUrl`、`MimeType`、`Md5`、`Size`、有效期和来源时间，但不得为某个厂商创建一套只能由该厂商 UI 消费的顶层模型。

- `Background` 可以同时包含视频、静态回退图、主题叠加图和本地缓存路径。
- `Banners` 至少包含稳定 ID、图片、标题和跳转链接；跳转链接可以为空。
- `News` 至少包含稳定 ID、分类、标题、链接和可空发布时间。
- `UpdateInfo` 表示当前游戏的版本或活动更新，不得与启动器自身的软件更新模型混用。
- 网络和本地响应必须使用带 `schemaVersion` 的 UTF-8 JSON envelope，并包含 `providerId`、`fetchedAt`、`content` 和结构化错误。
- JSON 使用 camelCase；时间使用 ISO 8601，并优先保存为 UTC。

详细契约维护在 `docs/HOMEPAGE_CONTENT_ARCHITECTURE.md`。模型变更必须同步修改文档、JSON Schema、C# DTO 和 Python Pydantic 模型，并增加跨语言兼容测试。

## 来源 Provider

Python Provider Registry 至少保留下列适配器。Provider 类负责来源发现和解析，共享 HTTP、缓存、重试、编码、URL 归一化和资源校验基础设施。

| Provider | 适用来源 | 已确认的数据形态 |
|---|---|---|
| `HoYoPlayJsonProvider` | 米哈游：原神、崩坏 3、崩坏：星穹铁道、绝区零等 | HoYoPlay JSON API 下发背景视频、静态图、主题图、Banner、资讯与链接 |
| `KuroLauncherProvider` | 库洛，当前已验证《鸣潮》 | 分层 CDN JSON；索引给出动态背景 hash，背景 JSON 给出 MP4、首帧和标语，资讯 JSON 给出活动、公告、新闻和轮播 |
| `HypergryphBatchProvider` | 鹰角，当前已验证《终末地》 | `batch_proxy` POST 批量返回背景图片、`video_url`、Banner、公告和侧栏内容 |
| `PerfectWorldHybridProvider` | 完美世界《异环》 | 启动器 HTML、远程 JS Banner 数据与本地启动器资源组合 |
| `NextJsDataProvider` | 当前用于《无限暖暖》官网 | 从 Next.js `__NEXT_DATA__` 读取首屏媒体、新闻轮播和文章编号 |
| `NetEaseStaticCmsProvider` | 《燕云十六声》《无限大》等网易静态站点 | NIE/Vue 静态页面、脚本、媒体资源和独立新闻 HTML |
| `LocalLauncherAssetProvider` | 必须读取安装目录的启动器 | 解析本地配置、视频、图片及版本清单，输出规范化绝对路径 |

Provider 类名与持久化 `providerId` 分离。`providerId` 一旦写入用户配置便视为稳定标识；重命名类时保留别名或执行迁移。

同厂商的不同游戏只能共享解析器，不能默认共享参数。`game_id`、`launcher_id`、`appcode`、`channel`、`sub_channel`、区域、语言、入口 URL 和 CDN key 必须放在游戏来源配置中，不能写死在 Provider 或 C# 页面里。

## 来源可信度规则

对数据源的描述必须区分“已验证的启动器来源”和“官网回退来源”：

- 米哈游、当前《鸣潮》以及当前《终末地》已有结构化启动器内容接口，可以优先接入。
- 《异环》采用混合来源：背景优先读取本地启动器资源，Banner 和资讯来自完美世界网页或 CMS。
- 《无限暖暖》目前确认的是官网 Next.js 数据，不能描述成已验证的启动器 API。
- 《燕云十六声》目前确认的是网易官网静态 CMS；独立启动器的首页端点仍需通过日志、安装资源或抓包确认。
- 《无限大》目前只使用官网媒体和新闻；正式 PC 启动器发布后重新发现来源，不能因为同属网易而复用《燕云十六声》的假定接口。

接口和页面结构可能随时变化。实现和文档不得把非公开内部端点描述为具有稳定 SLA 的开放 API。新增来源前保存一份脱敏样本，并记录验证日期、区域、语言、入口和回退方案。

## C# 接口预留

新增或重构首页 UI 时必须依赖抽象服务，不得在 Page、code-behind 或 ViewModel 中直接调用厂商接口、解析网页或启动 Python 进程。

最低边界为：

```csharp
public interface IHomeContentService
{
    Task<HomeContentResult> GetAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default);
}

public interface IHomeContentTransport
{
    Task<HomeContentEnvelope> FetchAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default);
}
```

- `IHomeContentService` 负责内存缓存、磁盘缓存、请求去重、最新内容与旧缓存切换以及降级状态。
- `IHomeContentTransport` 只负责一种传输，可实现为远程 HTTP 聚合接口、本机 Python Worker 或测试替身。
- Python 可执行文件、服务 URL、模块路径、超时和来源参数必须通过配置或依赖注入提供。
- UI 尚未接入真实数据时使用实现同一接口的空服务或样本服务，不在页面中添加厂商特判。
- 切换游戏时，每个请求携带不可变的游戏、区域、语言和 Provider 配置；不得复用带可变当前配置的单例客户端，避免并发串号。

建议目录：

```text
Models/Home/                    C# 统一 DTO
Services/Home/                  内容服务、缓存、传输接口
python/home_content/            Python 聚合入口和公共设施
python/home_content/providers/  Provider 与声明式规则
contracts/                      JSON Schema、请求和响应样本
```

## 首页 UI 与媒体规则

- 背景控件放在主视图最底层，侧边栏、首页内容和启动区叠加在其上方，使背景覆盖整个窗口。
- C# 使用原生媒体能力播放 Python 返回的视频 URL 或缓存文件。Python 只发现和验证资源，不负责向 UI 输出视频帧。
- 视频循环播放并提供静态海报回退；存在主题叠加图时由客户端合成。页面卸载或切换游戏时必须取消下载并释放播放器资源。
- Banner 与资讯做成独立可复用控件。轮播默认每五秒切换，鼠标悬停时暂停，移出后恢复。
- 资讯根据规范化分类显示“活动 / 公告 / 资讯”；未知分类保留内容并进入通用分组，不得丢弃。
- Banner 和资讯链接由内容数据提供，不写死在 XAML。点击后只允许打开经过验证的 `https` URL，并交给系统默认浏览器。
- Python 缺失、网络超时、单个资源失败或接口改版时首页仍须可打开；优先显示上次成功缓存，其次显示本地静态背景和可理解的空资讯状态。

## 缓存、安全与可靠性

- 远程聚合服务建议每 10 至 30 分钟刷新，并保留最后一次成功结果。客户端按响应时间和缓存策略决定是否重新请求。
- 媒体缓存文件名使用 URL 或内容哈希，不能直接使用远程文件名。
- 下载后校验响应 MIME、文件尺寸，并在来源提供时验证 `md5` 或其他摘要；校验失败的文件不能进入正式缓存。
- 分别设置元数据请求和大文件下载的超时，所有调用必须异步、可取消且不得阻塞 UI 线程。
- 标准输出只用于本机 Worker 的最终 JSON；诊断信息写标准错误并由 C# 日志服务收集。非零退出码、无效 JSON 和 schema 不兼容必须转换为结构化错误。
- 单个 Banner、News 或背景候选失败时返回其余可用内容；只有 envelope 无效或全部来源均不可用时才判定整次失败。
- 自动发现器只能产生候选项。候选媒体必须经过类型、尺寸、宽高比、时长、来源域名和人工或规则确认，避免把 PV、头像、Cookie Banner 或过期活动误识别为首页资源。

## 测试要求

- 每个 Provider 使用固定的脱敏响应或页面样本测试，常规测试不访问实时网络。
- C# 契约测试覆盖完整响应、可选字段缺失、未知字段、空列表、部分错误、schema 不兼容和旧缓存回退。
- 每次契约变更至少增加一个“Python 输出 JSON → C# 反序列化”的兼容测试。
- 媒体测试覆盖视频失败回退静态图、哈希不匹配、取消下载、切换游戏和释放播放器。
- 链接测试必须拒绝非 HTTPS scheme，并验证相对链接已由 Python 归一化为绝对 URL。
- Provider 修复必须附带导致问题的固定样本，防止同类接口改版再次破坏解析。

## 实现原则

- 不为每个游戏复制一套首页页面。所有游戏复用统一首页和统一控件，通过配置、Provider 和 DTO 驱动差异。
- 优先使用声明式 JSONPath、CSS selector、JS 变量映射和分类映射；加密、签名、批量 POST、混合本地资源等特殊来源再使用专用 Provider。
- 不把 Provider 响应对象直接绑定到 XAML。先转换为统一 DTO，再由 ViewModel 形成展示状态。
- 不原样复制参考项目中正在迁移或无法编译验证的 API 调用。参考其分层和交互方式时，必须按本项目契约重新实现并通过本项目测试。
