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
| `NextJsDataProvider` | 《无限暖暖》官网回退来源 | 从 Next.js `__NEXT_DATA__` 读取官网媒体、新闻轮播和文章编号；公司级 `PaperGamesProvider` 的背景优先读取官方启动器本机 WebM 缓存 |
| `NetEaseStaticCmsProvider` | 《燕云十六声》《无限大》等网易静态站点 | NIE/Vue 静态页面、脚本、媒体资源和独立新闻 HTML |
| `LocalLauncherAssetProvider` | 必须读取安装目录的启动器 | 解析本地配置、视频、图片及版本清单，输出规范化绝对路径 |

Provider 类名与持久化 `providerId` 分离。`providerId` 一旦写入用户配置便视为稳定标识；重命名类时保留别名或执行迁移。

### 新增公司或游戏的首页匹配字典

- 首页先根据用户填写的 EXE 路径由近到远匹配完整游戏安装目录段，再检查单独填写的安装目录，最后才按 EXE 完整文件名兜底。自制直启 EXE 也遵循同一顺序。
- 新增公司 Provider 或为现有公司加入游戏时，核对该游戏的安装目录及真实启动链。可盘点本机已安装游戏，也可查官方资料、发行平台的启动配置、PCGamingWiki、公开补丁清单等资料；用户不必先下载每款游戏。`exeNames` 只登记游戏本体、正式启动器和确实承担启动链路的桥接 EXE；崩溃上报、WebView、反作弊服务、SDK、安装器、更新器、备份及诊断程序不登记为关键字。把游戏目录名和必要的中英文别名写入 `folderNames`，不把同厂商的公共启动器目录或其他游戏目录算作目标游戏安装目录。
- 在每条来源的 `matchNotes` 中注明核对日期、来源版本、保留的启动入口及尚未核实的链路；公开资料的链接写入 `matchSources`。JSON 不支持注释，使用这些字段记录说明。公开资料只列出部分启动入口时，不声称已发现全部版本或渠道。
- 同名启动器 EXE 可以出现在多个游戏的 `exeNames` 中。先按完整游戏目录识别；没有可识别目录时，重名 EXE 必须返回未匹配，不能随机选择一个 Provider。新增别名时同时更新 C#、Python 的固定匹配测试，覆盖目录优先、特殊 EXE 和同名冲突。

同厂商的不同游戏只能共享解析器，不能默认共享参数。`game_id`、`launcher_id`、`appcode`、`channel`、`sub_channel`、区域、语言、入口 URL 和 CDN key 必须放在游戏来源配置中，不能写死在 Provider 或 C# 页面里。

## 来源可信度规则

对数据源的描述必须区分“已验证的启动器来源”和“官网回退来源”：

- 米哈游、当前《鸣潮》以及当前《终末地》已有结构化启动器内容接口，可以优先接入。
- 《异环》采用混合来源：背景优先读取本地启动器资源，Banner 和资讯来自完美世界网页或 CMS。
- 《无限暖暖》的 Banner 与资讯确认来自官网 Next.js 数据；官方启动器 1.3.1 的当前首页 WebM 本机缓存路径已经实机验证，但远程下发端点仍未确认，不能描述成已验证的启动器 API。
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

### Debug 输出目录

- 后续所有 Debug 主程序产物统一使用 `E:\AI\steamhelper\bin\x64\Debug\net8.0-windows10.0.19041.0`。
- Debug 测试产物统一放在上述目录的 `Tests\<测试项目名>` 子目录。
- 不得在 `E:\AI\steamhelper\bin` 下创建 `x64` 之外的目录，也不得使用各测试项目自身的 `bin` 目录保存 Debug 产物。
- 主程序统一使用 `dotnet build SteamCN-GameLauncher.sln --configuration Debug -p:Platform=x64` 构建；测试项目必须显式设置 `OutDir` 到统一目录。

- 每个 Provider 使用固定的脱敏响应或页面样本测试，常规测试不访问实时网络。
- C# 契约测试覆盖完整响应、可选字段缺失、未知字段、空列表、部分错误、schema 不兼容和旧缓存回退。
- 每次契约变更至少增加一个“Python 输出 JSON → C# 反序列化”的兼容测试。
- 媒体测试覆盖视频失败回退静态图、哈希不匹配、取消下载、切换游戏和释放播放器。
- 链接测试必须拒绝非 HTTPS scheme，并验证相对链接已由 Python 归一化为绝对 URL。
- Provider 修复必须附带导致问题的固定样本，防止同类接口改版再次破坏解析。

## 推送功能记录

- 仓库统一使用 `docs/PUSH_HISTORY.md` 记录每次推送实现的功能，不得为不同推送另外创建记录文件或记录目录。
- 每次向远程分支推送前，必须在该文件顶部追加一条记录，并与对应代码提交一同提交和推送。
- 每条记录至少包含带时区的时间戳、推送人员、目标分支、实现内容和验证结果；存在尚未完成的接口、运行限制或已知问题时必须一并写明。
- 推送人员使用当前仓库 `git config user.name` 的值；不得在记录中写入邮箱、用户路径、令牌、Cookie 或其他敏感信息。
- 功能记录描述最终实现，不记录已放弃的中间方案。仅修改文档时也要记录该次推送的文档范围，不得事后集中补写多次推送记录。

## 实现原则

- 不为每个游戏复制一套首页页面。所有游戏复用统一首页和统一控件，通过配置、Provider 和 DTO 驱动差异。
- 优先使用声明式 JSONPath、CSS selector、JS 变量映射和分类映射；加密、签名、批量 POST、混合本地资源等特殊来源再使用专用 Provider。
- 不把 Provider 响应对象直接绑定到 XAML。先转换为统一 DTO，再由 ViewModel 形成展示状态。
- 不原样复制参考项目中正在迁移或无法编译验证的 API 调用。参考其分层和交互方式时，必须按本项目契约重新实现并通过本项目测试。

## 启动器自动更新（Kachina）

v3.0.0 起使用 [Kachina Installer](https://github.com/YuehaiTeam/kachina-installer) 作为独立更新器。`UpdateService` 通过 GitHub Releases API 做启动期轻量检测并把版本号、Release 正文和下载页交给 UI；只有用户确认后，`KachinaUpdateService` 才启动同目录的 `SteamCN-GameLauncher.update.exe`。禁止强制更新、锁定导航或在用户未确认时下载安装。

### 版本号格式

- `AppInfo.Version`、`SteamCN-GameLauncher.csproj` `<Version>`、`version.json`、`Package.appxmanifest` Identity.Version 四处一致。
- 一律使用 **SemVer 格式**（如 `3.0.0`，**不带 `v` 前缀**）。Kachina 构建元数据使用该版本号。
- 显示版本号时由代码拼接：`AppInfo.FullVersion = $"v{Version} ({Channel})"`。
- Git tag 仍带 `v` 前缀（`v3.0.0`），用于 GitHub Release 标识。

### Kachina 关键标识

| 字段 | 值 | 来源 |
|---|---|---|
| 更新器文件 | `SteamCN-GameLauncher.update.exe` | `KachinaUpdateService.UpdaterFileName` |
| Kachina 资源 ID | `Clearlove0923/SteamCN-GameLauncher` | `scripts/Publish-Release.ps1` |
| CNB 仓库 | `SteamCN-GameLauncher/SteamCN-GameLauncher` | `UpdateSourcePolicy.CnbRepositoryPath` |
| 默认来源 | `cnb` | `packaging/kachina.config.json` 第一项 |
| 备用来源 | `github` | 同一配置第二项 |
| 安装路径 | `%LocalAppData%\Programs\SteamCN-GameLauncher` | Inno Setup `DefaultDirName` |

- `regName`、主程序名、更新器名和安装路径发布后保持稳定。
- 安装路径禁止无迁移方案地写回 `C:\Program Files`；Kachina 配置使用 `prefer-user`，仅在目录不可写时申请 UAC。
- `Backgrounds`、`backups`、`GameTime`、`HomeCache`、`logs` 必须列入 `ignoreFolderPath`，更新时不得覆盖或删除。

### 发布产物

`scripts/Publish-Release.ps1` 一次发布产出两套：

| 渠道 | 工具 | 产物 | 用途 |
|---|---|---|---|
| **Inno Setup** | `ISCC.exe` | `SteamCN-GameLauncher-v3.0.0-win-x64-setup.exe` | 官网和首次安装继续使用的主安装包 |
| **Kachina** | 固定版本 `kachina-builder` | `SteamCN-GameLauncher.update.exe`（嵌入 Inno 安装内容）+ `SteamCN-GameLauncher.Update.3.0.0.exe` | CNB 与 GitHub Release 上传同一份在线更新包 |

`scripts/Publish-Release.ps1` 固定并校验 Kachina builder 版本与 SHA-256。发布时必须把完全相同的 `SteamCN-GameLauncher.Update.<version>.exe` 上传到 CNB 与 GitHub 的 `v<version>` Release；文件名、tag 和配置模板必须一致。可通过 `-KachinaPreviousPublishDirectory` 提供一个或多个旧版 publish 目录生成二进制差分。
release tag使用v3.1.7 v3.1.6这种tag，不要加上英文字段


### 自动更新触发流程

1. 启动后 `UpdateService.CheckUpdateAsync()` 调用 GitHub Releases API，比较当前版本与最新 Release。
2. 有更新时弹出非强制 `ContentDialog`，标题显示目标版本，正文以可滚动区域展示 Release 中的新增功能、修复和说明；用户可选择“查看更新”或“稍后”。
3. 设置页默认选择 CNB，也允许切换 GitHub。点击“立即更新”后执行 `SteamCN-GameLauncher.update.exe -I --source <cnb|github>`。
4. 更新器成功启动后主程序真正退出而不是缩到托盘，由 Kachina 完成差异下载、替换与重新启动。
5. 更新器缺失或启动失败时显示原因，并允许打开所选来源的 Release 页面手动下载。

Kachina 的多来源配置按顺序将 CNB 放在第一项、GitHub 放在第二项。当前 `--source` 选择单一来源，不得在文案中声称运行中的 Kachina 会自动跨源重试；CNB 异常时由用户切换 GitHub 备用源。

## 版本更新官网同步

软件每次发布新版本后，必须同步官网下载网址，否则用户从官网下载到的是旧版本。官网是一个独立仓库，不在本仓库内发布。

- 官网仓库：https://github.com/Clearlove0923/clearlove0923.github.io。
- 官网下载区由 `version.json` 单一配置驱动，HTML 里的链接只是兜底地址；**不得直接改 HTML 而不改 `version.json`**。
- 安装包的分发主渠道是CNB和GitHub release，官网只做展示与跳转。

### 网址的两种形态

| 附件命名 | 官网下载网址 | 是否随版本变化 |
|---|---|---|
| 带版本号 | `.../releases/download/v2.6.3/SteamCN-GameLauncher-v2.6.3-win-x64-setup.exe` | 每次发版必变，版本号在网址中出现两处（路径段与文件名段） |
| 固定名 `SteamCN-GameLauncher-Setup.exe` | `.../releases/latest/download/SteamCN-GameLauncher-Setup.exe` | 不随版本变化，`latest` 自动指向最新 Release |

- 带版本号的网址**禁止手工拼改**，两处版本号漏改任意一处即 404，必须由脚本按模板生成。
- 采用固定名时，固定名附件必须存在于**最新** Release 中，否则 `latest` 直链 404；需要保留版本化归档名时，同一 Release 可同时上传两份附件。

### 同步范围

一次版本更新需要同步下列内容，且彼此一致：

| 位置 | 内容 |
|---|---|
| 官网 `version.json` | `version`（新版本号）、`date`（发布日期）、`windows.url`（该版本的 Release 直链） |
| 官网 `index.html` | 下载按钮的兜底 `href` |
| 本仓库 `CHANGELOG.md` | 顶部新增该版本条目 |
| 本仓库版本号 | `version.json` / csproj / AppInfo / appxmanifest 四处一致，tag 带 `v` 前缀 |

同步 `index.html` 兜底地址不能省略：国内访客可能无法访问 `api.github.com`，此时页面会回落到写死地址，未同步就会拿到旧版。

### 同步命令

在官网目录执行：

```bash
cd /f/my-site

# 附件仍带版本号：按 urlPattern 模板自动生成该版本直链（推荐）
bash update-release.sh v2.6.3 --auto

# 附件已改为固定名：切到 latest 永久直链，只需执行一次
bash update-release.sh v2.6.3 --fixed

# 推送上线
bash deploy.sh "更新到 v2.6.3"
```

### 同步后验证

- 官网下载区显示的版本号、日期与 Release 的 tag、发布日期一致。
- 复制主下载按钮地址，确认版本号正确或为 `latest/download/SteamCN-GameLauncher-Setup.exe`。
- 在官网与 Release 各点一次完整下载，确认可下载且文件哈希与本次构建产物一致。
- 用移动网络访问官网再验证一次，确认国内网络下按钮不失效。
- 同步错误时使用 `bash rollback.sh` 回退，不得用 force push 改写历史。

### 禁止事项

- 禁止把安装包提交进官网仓库；官网仓库已忽略 `downloads/` 与 `*.exe`。
- 禁止在固定名附件尚未上传时切换到 `--fixed`，会直接导致官网下载 404。
- 禁止只改官网版本号而不改下载直链，会造成版本与文件不一致。
