# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

## 2026-09-26 20:32:08 +08:00

- 推送人员：`Violet0923`
- 目标分支：`Refactored_Version`
- 推送提交：`fix(build): 按 Python 源码指纹刷新内置 Worker`
- 实现内容：
  - `Prepare-PythonRuntime.ps1` 对 `python/pyproject.toml` 与 `python/home_content/**/*.py` 生成确定性 SHA256 指纹，并写入内置运行时；指纹变化时强制重装项目包，依赖未变化时使用 `--no-deps`，避免构建继续复制旧 Provider。
  - 构建使用 `--no-build-isolation` 复用运行时已有的 setuptools，源码更新不需要再次访问 PyPI；导入检查同时覆盖 `home_content` 与四项运行依赖。
- 验证结果：
  - 首次运行根据新指纹重装 Worker 后，第二次运行直接输出 `Embedded Python is ready`，未重复安装。
  - 清空 `PYTHONPATH` 后从 `.build/python-runtime` 导入已安装的 HoYoPlay Provider，`4162040 -> nap_cn` 断言通过，证明内置包已同步到最新源码。
- 当前限制：
  - 全新环境第一次准备运行时仍需联网下载固定 Python 归档和 wheels；后续源码改动只重装本地项目包。

## 2026-09-26 20:28:49 +08:00

- 推送人员：`Violet0923`
- 目标分支：`Refactored_Version`
- 推送提交：`fix(home): 使用 Steam AppID 标识首页游戏`
- 实现内容：
  - 首页请求的 `gameId` 改为跨设备稳定的 Steam AppID，不再发送本机随机生成的预设 GUID，使同一 Provider 能准确区分不同游戏。
  - HoYoPlay Provider 同时支持 Steam AppID 与历史业务前缀映射：`4162040` 对应绝区零 `nap_cn`，`1671200` 对应崩坏 3 `bh3_cn`；未知标识仍保留原有回退行为。
- 验证结果：
  - 内置 Python 直接断言 Steam AppID、首尾空格与未知值三种映射全部通过。
  - `dotnet build SteamCN-GameLauncher.sln --configuration Debug -p:Platform=x64` 成功，0 错误、6 个既有 CS8625 警告。
- 当前限制：
  - HoYoPlay Provider 当前只消费背景接口；其 Banner 与资讯内容仍未接入。

## 2026-09-26 20:25:18 +08:00

- 推送人员：`Violet0923`
- 目标分支：`Refactored_Version`
- 推送提交：`fix(home): 修复 Python Worker 启动与旧配置 Provider 解析`
- 实现内容：
  - 新增 `scripts/Prepare-PythonRuntime.ps1`，固定下载并校验 python-build-standalone 3.10.21 的 SHA256，在 `.build/python-runtime` 准备独立运行时，并依据 `python/pyproject.toml` 安装 Worker 所需的 FastAPI、HTTPX、Pydantic 和 Uvicorn；主项目 Build/Publish 自动检查运行时后再复制到产物，避免依赖 Git 忽略的 `bin/python-runtime` 手工目录。
  - `PythonWorkerSpawner` 拒绝 0 字节文件和 WindowsApps 的 Microsoft Store Python 执行别名，避免未安装系统 Python 时错误启动占位程序并以 9009 退出。
  - `SupportedGameRegistry` 同时维护已验证 AppID 与稳定 Provider ID；旧游戏配置没有 `HomeContentProviderId` 时，首页按 AppID 自动选择 Provider，用户显式配置仍具有最高优先级。
  - 补齐 `python/pyproject.toml` 的实际运行依赖；清理 pip 构建产生的源码目录；更新 Python 接入文档；把 Worker 测试目标框架修正为 .NET 8，并增加执行别名与 Provider 映射回归测试。
- 验证结果：
  - 远程刷新后，本地基线 `dd74038af68fb474f2f9b962fbaee1bff5140dbf` 与 `origin/Refactored_Version` 完全一致（ahead/behind `0/0`）；修复后的 Debug 产物由该最新基线和本条变更构建。
  - `dotnet build SteamCN-GameLauncher.sln --configuration Debug -p:Platform=x64` 成功，0 错误、6 个既有 CS8625 警告，产物位于规定的 `bin/x64/Debug/net8.0-windows10.0.19041.0`。
  - `PythonWorkerSpawner.Tests` 11/11 通过；`CustomNavigation.Tests` 23/23 通过。
  - 真实 Worker `/healthz` 返回 `ok`；鸣潮 `kuro-launcher` 返回背景视频、背景图、6 个 Banner、7 条资讯且 0 错误；燕云十六声 `netease-static-cms` 返回背景图、4 个 Banner、16 条资讯且 0 错误。
  - 启动新 Debug EXE 后，日志确认使用产物内 `python-runtime/python.exe`，Worker 监听 8765，旧鸣潮配置自动解析为 `kuro-launcher` 并在 UI 收到 `banners=6 news=7`。
- 当前限制：
  - 首次构建需要联网下载约 39.5 MB 的固定 Python 归档及 Python wheels；之后复用 `.build` 缓存。HoYoPlay Provider 当前只提供背景，Banner 与资讯仍待接入其对应内容接口。

## 2026-09-26 19:28:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`
- 推送提交：`fix(home-page): 无游戏 / 未适配 AppId 时隐藏资讯 + 轮播 UI`
- 实现内容：
  - `Views/Pages/LauncherHomePage.xaml.cs`：`OnNavigatedTo` 的 else 分支（`_currentGame == null`）现在把 `NewsPanel.Visibility` 设为 `Collapsed`，并清空 `_currentHomeContent` / `HomeContentPanel.SetContent(null)`，避免空 "资讯" tab header + 空轮播框架残留。`ResetBackgroundToDefaultBlack()` 在 AppId 不在 `SupportedGameRegistry` 时也做同样清理，避免从支持的鸣潮 / 终末地等切到不支持的游戏时残留上一个游戏的真实 banner + 资讯。切回适配游戏后由 `ApplyHomeAppearance` 按外观档案 `ShowHomeNews` 恢复 `Visible`。
- 验证结果：
  - Debug 构建 0 错误 / 6 警告（CS8625 全是历史 null 警告，与本次无关）。
  - 临时清空 `settings.json` 里所有非 built-in 的 `CustomManifestPreset` + 清 `CurrentCustomManifestId`，重启 launcher → 命中 `MainWindow.ShowEmptyGameLibrary()` → ContentFrame 导航到 `EmptyGameLibraryPage`（"还没有添加游戏" + 添加游戏按钮），`LauncherHomePage` 根本不会被实例化。这是更友好的"无游戏"主体验，本次修改作为双保险补齐边缘情况。
  - 代码逻辑验证：else 分支直接置 Collapsed，`ResetBackgroundToDefaultBlack` 同步清 NewsPanel + HomeContentPanel，无残留。
- 当前限制：
  - "无游戏"主体验由 `MainWindow.ShowEmptyGameLibrary()` 提供（`EmptyGameLibraryPage`），本次 commit 主要补强 `LauncherHomePage` 边缘情况和切换到不支持的 AppId 时的清理。

## 2026-09-26 19:09:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`
- 推送提交：`feat(launcher): 内嵌 Python 3.10.21 解释器与依赖，零外部 Python 依赖`
- 实现内容：
  - 下载 `cpython-3.10.21+20260924-x86_64-pc-windows-msvc-install_only.tar.gz`（39.5 MB）→ 解压到 `bin/python-runtime/`（118.6 MB / 3932 文件，包含 stdlib + DLLs + Scripts + tcl）。
  - `SteamCN-GameLauncher.csproj` 加私有 item group `<_EmbeddedPython Include="bin\python-runtime\**\*" />` + 双 `<Target>`（`CopyEmbeddedPython` AfterTargets="Build" + `CopyEmbeddedPythonToPublish` AfterTargets="Publish"）用 `<Copy>` 任务把 3932 个文件复制到 `$(OutDir)` 和 `$(PublishDir)`。故意不用 `<Content Include="python-runtime/**/*">`：WinUI3 SDK 的 PRI 生成器把每个 `<Content>` 都当语言限定符处理，触发 796+ PRI249 warning；`<None Include>` 默认不复制，二者都不稳；自定义 `<Target>` + `<Copy>` 任务最可控（`SkipUnchangedFiles=true Retries=3`，debug 增量编译秒过）。
  - `Services/Home/PythonWorkerSpawner.cs` 的 `ResolvePythonExecutable()` 在 priority 2 嵌入分支（显式 `PythonExecutablePath` → embedded `AppContext.BaseDirectory\python-runtime\python.exe` → PATH → 常见安装目录），把用户拷走整个 launcher 包就走、零外部 Python 依赖的路径坐实为默认行为；并更新顶部注释说明 csproj 用的是 `_EmbeddedPython` 私有 item group + 自定义 `<Target>` 而不是 `<Content>`。
  - `bin/python-runtime/Lib/site-packages/` 用 embedded python.exe 自带的 pip 重装 22 个包（pydantic 2.13.5 + pydantic-core 2.46.5 / fastapi 0.141.1 / uvicorn[standard] 0.53.0 / httpx 0.28.1 / starlette 1.6.0 / anyio 4.15.1 / typing-inspection 0.4.4 等），全部走 cp310-cp310-win_amd64 wheel。之前用外部 venv python 装的 pydantic_core 是 cp39 wheel，3.10.21 embedded Python 导入报 `ModuleNotFoundError: No module named 'pydantic_core._pydantic_core'`；改用 embedded python 自己的 pip 后正常。
- 验证结果：
  - Debug 构建：`dotnet build --configuration Debug -p:Platform=x64` → 0 错误 / 2 警告（旧 CS8625 null 警告，与本次无关）；`bin\python-runtime\` 完整复制（5134 文件 / 138.1 MB，含 .pdb 调试符号）。
  - Release self-contained：`dotnet publish --runtime win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=true -p:PublishTrimmed=false` → publish 目录 767.7 MB / 10098 文件，`python-runtime/` 138.1 MB / 5134 文件，`SteamCN-GameLauncher.exe` 落地。
  - 端到端：kill 所有 python.exe + port 8765 监听者 + 重启 launcher → log 显示 `[worker] using embedded python at C:\...\python-runtime\python.exe` + `[worker] spawned python worker pid=25528 port=8765 cwd=C:\code\SteamCN-GameLauncher` + uvicorn `Uvicorn running on http://127.0.0.1:8765` + `port 8765 is listening; ready for FastApiHomeContentService` + `POST /v1/home-content HTTP/1.1 200 OK` + `banners=6 news=7`（景燃pv / 景燃战斗演示 / 3.6版本pv / 库洛充值中心 / 鸣潮雷蛇联名款键鼠 / 周边-Q版手办；资讯活动 2 / 公告 3 / 资讯 2）。`Get-Process` 确认 worker 进程路径是 `python-runtime\python.exe`，不是外部 venv 或系统 Python。
  - 直接验证 embedded Python 自身：`python.exe -c "import pydantic, fastapi, uvicorn, httpx; print(pydantic.__version__, fastapi.__version__, uvicorn.__version__, httpx.__version__)"` → 输出 `2.13.5 0.141.1 0.53.0 0.28.1`，所有 wheel 标签都是 cp310-cp310-win_amd64。
- 当前限制：
  - Publish 大小 767.7 MB（含 WindowsAppSDK 自包含 + .NET runtime + 3932 个 Python 文件 + .pdb 调试符号），用户拿到的 zip 约 380 MB；后续可加 `PublishDebugSymbols=false` 去 pdb 减约 50 MB；Python `Lib/site-packages/` 还可以裁剪（httpx + anyio 在 FastAPI server 里其实只用到 starlette 间接引用）。
  - 首次 spawn 仍要等 1.5-2 秒（uvicorn 加载），启动时 banner 短暂空白。
  - 嵌入 Python 不参与 MSBuild restore，每次 Python 小版本升级需要手动重新下载 + 重新 `pip install`。
  - `[dbg-banner]` / `[dbg-news]` / `ApplyLayoutProfile` log scaffolding 仍未清理（commit `c3a8004` / `66bf828` / `b5a4ee4` 引入），下次稳定版本前需要清理。

## 2026-09-26 15:51:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`
- 推送提交：`feat(launcher): exe 启动自动拉起 Python Worker 子进程获取真实首页内容`
- 实现内容：
  - 新建 `Services/Home/PythonWorkerSpawner.cs`（封装 `Process.Start` + TCP 端口探测 + stdout/stderr 转发 `LogService` + 优雅 `Stop`）：`StartAsync` 先探测端口空闲 → 探测 python.exe（PATH / 常见安装目录 / `AppSettings.PythonExecutablePath`）→ spawn `-m home_content.server.main --port <port>` → 轮询 8765 listening（200ms tick，超时 `PythonWorkerStartupTimeoutSeconds` 默认 15s）→ child 退出触发 `Exited` 事件。`Stop` 先 `CloseMainWindow` → 等 `PythonWorkerShutdownTimeoutSeconds`（默认 5s）→ 强杀整棵进程树，最后统一 `Dispose`。所有失败（python 找不到 / spawn 抛 / 端口超时 / child 秒退）吞掉 log，返回 false 不阻塞 launcher。
  - 修改 `Models/AppSettings.cs`：新增 `SpawnPythonWorkerOnLaunch`（默认 `true`）、`PythonExecutablePath`（默认空 → 自动探测）、`PythonHomeContentPath`（默认空 → `<launcher-cwd>/python`）、`PythonWorkerStartupTimeoutSeconds`（15）、`PythonWorkerShutdownTimeoutSeconds`（5）。
  - 修改 `App.xaml.cs`：`OnLaunched` 启动 `MainWindow` 后立刻 fire-and-forget `TrySpawnWorkerAsync`，构造 `PythonWorkerSpawner`，订阅 `OutputReceived`/`Exited` 写 `LogService`，失败仅 log。`MainWindow.Closed` 触发 `OnMainWindowClosed` → `WorkerSpawner.Stop()` + `Dispose()` 优雅关闭子进程。新增公共静态 `App.WorkerSpawner` 方便测试和诊断。
- 验证结果：
  - 新建 `Tests/PythonWorkerSpawner.Tests/`（Exe，net10.0 → OutDir 落到 `bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\PythonWorkerSpawner.Tests\`，符合 AGENTS.md "测试项目必须显式设置 OutDir 到统一目录"）：10/10 用例通过 — `Ctor_NullSettings_Throws` / `Ctor_NullLogService_Throws` / `StartAsync_AlreadyListening_ReturnsTrue_NoSpawn`（用 `TcpListener` 起端口验证 fast-path 不 spawn，`OwnsProcess=false`） / `StartAsync_PythonNotFound_ReturnsFalse` / `StartAsync_CmdExitsImmediately_ReturnsFalse`（child 立即退出的 ANE 陷阱修复：`Exited` 事件里不 `Dispose` proc，留给 `Stop`/`Dispose` 统一释放，端口轮询的 `proc.HasExited` 不再抛 InvalidOperationException） / `Stop_NullProcess_DoesNotThrow` / `StartAsync_DisabledByFlag_ReturnsFalseQuickly` / `StartAsync_ExtractsPortFromBaseUrl` / `OutputReceived_FiresForStdout` / `Exited_FiresWhenChildExits`。
  - 端到端：杀掉 sandbox 的外部 worker，停 launcher，重启 launcher。日志显示：`spawned python worker pid=24860 port=8765` → uvicorn `Uvicorn running on http://127.0.0.1:8765` → `port 8765 is listening; ready for FastApiHomeContentService` → launcher 立刻 `POST /v1/home-content` → uvicorn 返回 200 + 5 banners（景燃pv / 景燃战斗演示 / 3.6版本pv / 库洛充值中心 / 鸣潮雷蛇联名款键鼠）+ 7 news（活动 2 / 公告 3 / 资讯 2），`[dbg-banner] BannerFlipView.SelectedIndex=0 vis=Visible`，PreviewHomeContentService 兜底永远不再触发。
- 当前限制：
  - python.exe 探测路径不包括 Microsoft Store Python Launcher（`py.exe` 启动器），仅覆盖 PATH + uv-installer 常见路径。Store 用户的 Python 会探测失败，需要手动设置 `AppSettings.PythonExecutablePath`。
  - launcher 启动时若 `SpawnPythonWorkerOnLaunch=false` 且外部无 worker，banner 显示 `BannerEmptyState` "等待 Python 首页内容"，但 launcher 自身不会弹出友好提示（已加 log，UI 提示待 SettingsPage 加开关 + 状态提示）。
  - 没有 worker 子进程崩溃自动重启机制 — child 退出后 launcher 降级到 `PreviewHomeContentService` 兜底直到重启 launcher。
  - Worker 启动慢（首次 uvicorn 加载约 1.5s）→ launcher 首页前 ~2 秒 banner 显示空状态。后续可考虑预先加载或加 skeleton placeholder。

## 2026-09-26 14:28:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`Refactored_Version`（后续同步到 `python_preview`）
- 推送提交：`feat(home-page): 默认背景改纯黑 + AppId 不在适配清单时强制走默认背景`

### 实现内容

修复启动器在未验证 AppId 上误显示测试水印图 / 残留主题底色的问题，配合四个改动：

1. **新增 `Models/Home/SupportedGameRegistry.cs`** —— 已通过端到端验证的 Steam AppId 白名单（鸣潮 3513350、绝区零 4162040、崩坏3 1671200、燕云十六声 3564740、无限暖暖 3164330、异环 4508340/4706890、终末地 4732690），提供 `IsSupported(appId)` 静态查询方法。每接入一个新游戏的真实首页内容并端到端验证通过后再追加，不要预填未验证 ID。
2. **`Views/Pages/LauncherHomePage.xaml`** —— 在 `MediaPlayerElement` 同级最底层新增 `<Border x:Name="DefaultBlackBackground" Background="Black" Grid.RowSpan="2" IsHitTestVisible="False" Visibility="Collapsed"/>`，作为未适配 AppId 时的纯黑兜底层。
3. **`Views/Pages/LauncherHomePage.xaml.cs`** —— `ShowGameAsync(game)` 在发 worker 请求前先查 `SupportedGameRegistry.IsSupported(game.AppId)`；不在清单里直接 `ResetBackgroundToDefaultBlack()` + return（不发请求、不渲染 SourceImage 兜底），新增 `ResetBackgroundToDefaultBlack()` 清空 MediaPlayerElement 和 Image 同时显示 DefaultBlackBackground；`ApplyHomeAppearance` 真实背景进入时先把 `DefaultBlackBackground` 设为 Collapsed 让视频/静态图透出来。
4. **新增 `bin\Backgrounds/default-black.png` (1920×1080 纯黑 RGB) + `SteamCN-GameLauncher.csproj` 加 CopyToOutputDirectory** —— 让 SourceImage 兜底在"已适配但 worker 没拉到数据"的情况下也走纯黑，而不是用户手动配置的测试图；同步删除 `Backgrounds\sandbox-test-bg.png`（带 SANDBOX TEST BG 水印的旧测试图，已回收站）。

### 验证结果

- **3513350（鸣潮，在清单）** —— 显示 worker 拉到的真实 PV 背景 + Banner + 真实中文新闻，行为不变。
- **99999999（未支持，临时改 manifest AppId 验证）** —— 强制纯黑背景，跳过 worker 请求，不渲染标题 / Banner / 新闻区，只有左侧导航栏。
- 用户 sandbox settings.json 的 `SelectedImage` 和 `Images` 已切到 `default-black.png`，备份在 `settings.json.bak.default-black`。
- `dotnet build SteamCN-GameLauncher.sln --configuration Debug -p:Platform=x64` 通过（2 个 `SetContent(null)` nullable warning，非阻塞）。

### 当前限制

- `SupportedGameRegistry` 是硬编码白名单，新游戏接入 Provider 验证后需要手动追加 AppId；如果用户加了新游戏但 AppId 没在清单里，会强制显示纯黑（按当前需求设计如此，不算 bug 但要写明）。
- 调试日志 `[dbg-banner]` / `[dbg-news]` / `ApplyLayoutProfile` 仍保留在 `HomeBannerAndNews.xaml.cs` + `LauncherHomePage.xaml.cs`，TODO 正式发版前移除。

## 2026-09-26 00:30:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview` + `Refactored_Version`
- 推送提交：`c3a8004` fix(home-content): 资讯项点击改 Border+Tapped 绕开 Button hit-test 异常，加 IsHitTestVisible=False 防 Banner hit area 跨界

### 实现内容

修复 issue #14 描述的"资讯轮播图下面的活动 / 公告 / 资讯条目点击无响应"问题，配合三个改动：

1. `Views/Controls/HomeBannerAndNews.xaml`：`<Grid x:Name="BannerContainer">` 加 `IsHitTestVisible="False"` —— Banner 上半区（FlipView + 5 张 banner 图）的 hit area 之前在某些条件下会跨界抢占下半区 News 区域的 click，禁用 BannerContainer 的 hit test 让 hit-test 路由正常下沉到 News 区域
2. `Views/Controls/HomeBannerAndNews.xaml` + `.cs`：news `<Button Click="NewsItem_Click">` 改成 `<Border Tag="{x:Bind}" Tapped="NewsItem_click">` —— Button 在 `Background="Transparent" BorderThickness="0"` + 高 DPI + ItemsRepeater 模板组合下，Click 事件路由不通；Border 的 Tapped 走的是冒泡路由事件，没这个坑
3. `NewsItem_Click` handler 改读 `fe?.Tag as HomeNewsDisplayItem`（替代原来的 `fe is { DataContext: ... }` pattern）—— ItemsRepeater + x:Bind 模板不自动设 DataContext，所以用 Tag 把数据塞进去

调试日志保留为 `[dbg-news]` 前缀（`LogService.Instance.AddLog` 写 `bin\x64\Debug\net8.0-windows10.0.19041.0\logs/*.log`），方便后续排查；正式发版前再移除。

`LauncherHomePage.xaml.cs` 的 `ApplyLayoutProfile` 临时加了 `[dbg-news]` 日志打印 rasterizationScale + NewsPanel 计算后尺寸位置，作为定位根因的辅助信息，会和 debug 日志一起在发布前清理掉。

### 验证结果

- worker PID 11068 真实跑 Kuro CN 端点（`prod-cn-alicdn-gamestarter.kurogame.com/launcher/10003_.../G152/information/zh-Hans.json`），`HomeContentProviderId=kuro-launcher`，`locale=zh-CN`
- UI Automation 找到 news item 1 在 `(507, 1636)`、item 2 在 `(507, 1672)`（物理坐标，窗口右下侧）
- 模拟点击 → log：
  ```
  [dbg-news] Click fired sender=Border tag=HomeNewsDisplayItem dc=
  [dbg-news] resolved url=https://www.kurobbs.com/mc/post/1539644111837351936
  ```
- URL 跳转到 kurobbs 国服论坛帖子 ✓ —— 与 Kuro CN 端点 `information/zh-Hans.json` 里 `"jumpUrl": "https://www.kurobbs.com/mc/post/1539644111837351936"` 完全一致
- Banner_Tapped 同样正常工作（之前就 OK，这次没受影响）

### 当前限制

- 调试日志 `[dbg-news]` / `[dbg-banner]` / `[dbg-news] ApplyLayoutProfile` 临时加在 `HomeBannerAndNews.xaml.cs` 和 `LauncherHomePage.xaml.cs`，正式发版前需要清掉
- 沙盒端到端验证用 UI Automation + SendInput，**实际用户用鼠标点击需要确认是否同样工作**（理论上应该没问题，因为 Border+Tapped 走标准冒泡路由，但用户实际环境 DPI / 缩放率可能不同）
- Banner 区域仍走 `Tapped` 路由事件（Grid 内），同样在某些极端 DPI 下可能也有 hit area 跨界问题；本次 fix 仅针对 news 区域
- ItemRepeater 内 Banner 子项的点击事件原本用 `Grid Tapped="Banner_Tapped"`，没改成 Border+Tapped —— 如果未来 Banner 也出现 hit-test 异常，可以套用同样模式

## 2026-09-25 23:08:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview` + `Refactored_Version`
- 推送提交：`KuroLauncherProvider` 接入 CN 国服端点 + locale→language fallback + 时间解析 + 白名单扩展

### 实现内容

- **`python/home_content/providers/kuro_launcher.py` 重写**：新增 `region` 选择机制，默认 `cn`（贴合启动器目标用户群）。`DEFAULT_CN_*` 常量（appId=`10003`、appKey=`Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5`、gameId=`G152`、CDN=`prod-cn-alicdn-gamestarter.kurogame.com`）来自 `blog.sunmkt.uk` 列出的 `KRApp.conf` 解码结果 + 2026-09-25 实测验证。`ALLOWED_HOST_SUFFIXES` 扩成 OS + CN 双套域（增加 `.aki-game.com` / `.kurobbs.com` / `.mc.kurogames.com`）。`_language_candidates(region, request)` 按 locale 前缀 / `providerOptions['language']` 显式覆盖 / region 默认 三级优先级返回候选语言列表；`_fetch_region_content` 用 `asyncio.gather(return_exceptions=True)` 并行抓 wallpaper + news，单端点 4xx 不阻断另一个端点的数据。`_parse_mmdd` 把 Kuro 资讯里的 `"MM-DD"` 时间补全当前年填到 `HomeNewsItem.published_at`。`providerOptions['appId'|'appKey'|'gameId']` 覆盖仍生效——显式提供则偏离 region 默认，否则用 region 默认。
- **`python/tests/test_kuro_launcher_provider.py` 28 项测试**：13 项原有 OS 测试 + 15 项新增 CN / locale / 时间 / fallback 测试。新增覆盖：CN fixtures 加载、`_resolve_region` 默认值 + 大小写 + 未知值降级、`_language_candidates` 在 cn/os/各 locale 下的优先级、`providerOptions['language']` 显式覆盖、CN banner 字幕中文校验（`景燃pv` 等 5 个）、CN news 中文标题 + `published_at` 解析、完整 CN fetch 路径（zh-Hans.json 命中 + huoshan CDN 命中 + `10003_.../G152` 路径）、`zh-Hans` 404 自动 fallback 到 en、单端点 4xx 部分数据可用、所有语言都 4xx 抛 `HTTPStatusError`、`_parse_mmdd` 边界（None / 空串 / `13-40` / ISO 时间）。
- **3 个 CN 脱敏样本**（`contracts/samples/kuro-cn-{launcher-config,bg-zh-Hans,info-zh-Hans}.json`）：2026-09-25 实测 CN 端点（`bg_hash` 替换为 `HASH32_CN` 占位）。info 样本保留真实中文内容（《鸣潮》3.6 版本创作激励计划 / [身赴三途]角色活动唤取 / 景燃 pv 等），bg 样本保留 `pcdownload-huoshan.aki-game.com` 真实 URL 用于白名单校验，banner 轮播图保留 `prod-alicdn-community.kurobbs.com` / `bilibili.com` 跳转链接。
- **`docs/KURO_PROVIDER.md` 全量重写**：补 region 表格、`_language_candidates` 三级优先级、CN 域名出处表、`envelope.errors.code` 错误码说明、`time` 字段映射、测试结构（28 项拆分）。"已知限制"段把"启动器只发 en.json" + "国服 CN 未独立验证"两条删掉，换成"CN `en.json` 内容为空" + "OS 不下发中文"两条更准确的边界描述。

### 验证结果

- **pytest 全套**：`219 passed + 1 skipped`（前一轮 204 + 1；新增 15 项 CN 测试）。所有 13 个原有 OS 测试零回归，新 15 项全过。
- **C# 端配置无需改动**：`CustomManifestPreset.HomeContentProviderId="kuro-launcher"` + `request.locale="zh-CN"`（`LauncherHomePage.xaml.cs:138` 已硬编码）即可让 C# 端拿到中文内容，`providerOptions['region']` 默认 `cn` 自动选择国服端点。
- **沙盒端到端未跑**（沙盒里 `%TEMP%\home-content-env` venv 的依赖丢失、httpx 包不见了），需要在新 venv 起来再验证 `POST /v1/home-content` 拿到中文 envelope。

### 当前限制

- **C# 端没有 region 选项 UI**：默认 `cn` 已经满足国服 launcher 场景；若用户手动选 OS (`providerOptions['region']='os'`)，需要去 `CustomManifestPage` 增加 region 下拉（当前 7 个 Provider ID 的 ComboBox 之外）。
- **Provider 不做语言/区域自动协商**：当前用 region + locale 决定语言候选；如果未来 Kuro 增加 `en.json` 中文内容或 CN 增加 `en.json` 数据（目前 CN 的 `en.json` 确实下发但内容为空），需要更新 `_language_candidates` 让 fallback 顺序更智能（例如按 contents 数量选最丰富的语言）。
- **跨区域回退未实现**：当前 CN 端点完全 4xx 时不会自动切到 OS；如果 Kuro 临时把 CN 端点下线，UI 会拿到空 envelope。需要在 Provider 增加跨 region fallback。
- **C# 端的 `preview` fallback 仍走 PreviewHomeContentService**，对真实 Provider 失败没有 fallback 链；FastApiHomeContentService 只把 envelope.errors 透传。
- **沙盒 venv 被破坏**：本轮 pytest 在新建的 `%TEMP%\kuro-test-env` 里跑的，之前 `%TEMP%\home-content-env` 已经空了（httpx / pytest 等都消失）。需要在干净环境重新建 venv + 重新跑端到端验证才能把截图发出来。

## 2026-09-17 22:08:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`Refactored_Version`
- 推送提交：把 `python_preview` HEAD `1a106e6` 通过 fast-forward 推到 `origin/Refactored_Version`（同步 5 个新 commit：`6bcd848` / `fd6c172` / `ed62cde` / `744aa28` / `1a106e6`）

### 实现内容

- 把刚才推到 `origin/python_preview` 的全部内容同步回 `Refactored_Version`，让两条分支重新对齐。本地 `Refactored_Version` 从 `9081e95`（合并点）直接 fast-forward 到 `1a106e6`（`python_preview` HEAD），不引入新 merge commit。
- 同步过去的 5 个 commit 内容详见上一个 22:05 条目（`python_preview` 那次推送的记录），本次仅为分支对齐操作、无独立代码改动。
- 动机：`Refactored_Version` 是 C# UI / Settings / Release 主干分支，`python_preview` 长期并行演进；保持 `Refactored_Version` ≥ `python_preview` 可避免后续反向 merge 时落入"主分支缺 Provider 代码、首页回退到 preview fallback"的脆弱状态。

### 验证结果

- `git merge-base origin/Refactored_Version python_preview == 9081e95`，且 `python_preview` 历史中已包含 `9081e95`（通过 merge `6bcd848`），满足 fast-forward 条件。
- `git rev-list --left-right --count origin/Refactored_Version...python_preview` 在合并前为 `0	13`（Refactored_Version 落后 13 个 commit，其中 1 个是 9081e95 本身，12 个是后续工作）。
- Fast-forward 后 `git log --oneline origin/Refactored_Version -5` 显示 `1a106e6 / 744aa28 / ed62cde / fd6c172 / 6bcd848`，与 `python_preview` HEAD 完全一致；`git rev-list --left-right --count` 转为 `0	0`，两分支零偏差。

### 当前限制

- 当前是单向同步（`python_preview → Refactored_Version`）。`Refactored_Version` 上若有未上 `python_preview` 的独立提交（例如纯 C# UI 改动），下次反向同步时需要走真正的 merge commit 路径，会产生新的合并节点。
- 长期更稳的方案是约定"`Refactored_Version` 是被合并的目标、`python_preview` 是合并的来源"角色，或者把 `python_preview` 改成只读归档、所有 C# 工作回到 `Refactored_Version`。当前两条分支是平行的，选哪条当主线由你拍板。

## 2026-09-17 22:05:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`
- 推送提交：5 个 commit（`6bcd848` / `fd6c172` / `ed62cde` / `744aa28` + 本次 PUSH_HISTORY 更新本身）

### 实现内容

- **merge `6bcd848`**：把 `Refactored_Version` 的 `9081e95`（`feat: expand launch appearance and update options`）合并进 `python_preview`，冲突落在 `docs/PUSH_HISTORY.md` 上 —— 用独立 Python 脚本按时间戳去重 + 倒序 + 在顶部前置 merge 记录，保证 13 条旧推送记录全部保留。同步进 `python_preview` 的 C# 改动包括外观/启动设置扩展、Release 包装与 Settings UI 配套。
- **commit `fd6c172`**：补齐 `provider_registry` 与 server 的集成测试。新建 `python/tests/test_provider_registry.py`（9 项，覆盖注册表不变式：7 个稳定 ID ↔ Provider 类的双向映射、未知 ID 抛 `ValueError`、空字符串拒绝、大小写不匹配拒绝），新建 `python/tests/test_server_app.py`（13 项，用 `httpx.ASGITransport` in-process 跑 FastAPI，monkeypatch `create_provider` 注入 fake Provider）。修复 `python/home_content/server/app.py` 第 124 行硬编码的 `"HoYoPlay"` provider 名字为通用 `f"{request.provider_id} provider failed without message."`。Python 总测试数 182 → 204。
- **commit `ed62cde`**：修复 `LauncherHomePage` 白屏 —— 当 `ApplyHomeAnimation` 拿到 `null` source 时不再走无声 `StopHomeAnimation()`，改为回退显示当前 appearance profile 的静态背景图。XAML 在 `MediaPlayerElement` 同级新增 `<Image x:Name="HomeBackgroundImage" IsHitTestVisible="False" Stretch="UniformToFill" Visibility="Collapsed"/>`，code-behind 新增 `ShowStaticBackground()` 读 `_appearanceService.CurrentProfile.Current.SourceImage` + `_appearanceService.GetImagePath(...)` → `new BitmapImage(new Uri(path))`。三路分支：source null → 静态图；video play / new key → 隐藏 Image；stop / failed → 同时清空 Image。根因是 `AppearancePageBackground = Transparent` 叠加 WinUI 默认白窗口 = 纯白屏。
- **commit `744aa28`**：C# 端接通 Python Provider 链路。新建 `Services/Home/FastApiHomeContentService.cs`（envelope → result 转换，`HomeContentTransportException` 统一降级为 `transport_<ExceptionClass>` code，unwrap inner `JsonException` / `HttpRequestException`，`OperationCanceledException` 在 token 已 cancel 时 rethrow）。`Models/AppSettings.cs` 加 `HomeContentWorkerBaseUrl`（默认 `http://127.0.0.1:8765`）+ `HomeContentWorkerTimeoutSeconds`（默认 10），`CustomManifestPreset` 加 `HomeContentProviderId` 字段。`LauncherHomePage.xaml.cs` 用 `CreateHomeContentService(settings)` 工厂取代硬编码 `PreviewHomeContentService`，新增 `ResolveProviderId(game)` helper。`CustomManifestPage.xaml(.cs)` 在 BuildID/Manifest 输入框下方新增 `cmbHomeContentProvider` ComboBox，列 7 个 Provider ID + 空项。同步新建 `Tests/FastApiHomeContentService.Tests/`（24 项断言全过，自带 `FakeHomeContentTransport` 测试夹具）。

### 验证结果

- **Python pytest**：`204 passed + 1 skipped`（前一轮 182 passed + 1 skipped；新增 registry 9 + server 13 = 22 项）。所有 7 个 Provider 在 fixture 下端到端解析通过。
- **C# FastApiHomeContentService.Tests**：`24` 项断言全部通过（构造器 null / GetAsync null / Happy 透传 / IsStale 默认 false / Errors 双向 / TransportException 三种 inner / Cancellation 不吞 / request 字段透传）。
- **沙盒端到端**：本地起 uvicorn worker（`python -m home_content.server.main` PID 55124，监听 `127.0.0.1:8765`），`POST /v1/home-content` 用 `{"requestId":"e2e-1","providerId":"kuro-launcher","gameId":"00d604afe7c248f498b9682b3bf16f8c","locale":"zh-CN"}` 返回 200，5 个 banner（3.7 前瞻预告、景映角色 PV、景映实机视频、3.6 PV、kuro 充值中心）+ 4 条 news + 背景视频 URL `https://hw-pcdownload-qcloud.aki-game.net/launcher/clientUpload/0nr2n8wkbta7l7flfl.mp4`。把 `CustomManifestPresets[1]`（鸣潮）的 `HomeContentProviderId` 临时改为 `kuro-launcher`，启动 `SteamCN-GameLauncher.exe`，主页背景出现真实的 Kuro first-frame webp（蓝发女角原画，月亮 + 白花场景），标题"当前游戏 · 鸣潮"正确显示（截图 `launcher_e2e_kuro_clean.png`，验证后还原 settings + kill launcher/worker）。

### 当前限制

- **Python worker 进程启动尚未内嵌到 C# 启动流程**：`LauncherHomePage` 构造 service 时读 `settings.HomeContentWorkerBaseUrl`，但当前进程没有任何代码去 spawn uvicorn 子进程。`HomeContentE2E.Tests` 已经验证 `Process.Start` + `WaitForPort()` 的全链路，需要把那段 worker 启动代码挪进主进程（一次性 + 设置项控制）才算真正"零配置可用"。
- **Provider ID 与游戏元数据没有自动绑定**：当前 7 个 Provider 在 Python 端通过 `provider_id` 区分，C# 端 `CustomManifestPreset.HomeContentProviderId` 是自由文本 + ComboBox 提供 7 个选项；用户得自己知道"鸣潮对应 `kuro-launcher`"。后续可在 `CustomManifestService` 加 lookup 表（按 Steam AppID → Provider 推荐）。
- **未做 request 级内存缓存**：`FastApiHomeContentService.GetAsync` 每次 `ShowGameAsync` 都发新请求，切换游戏时浪费一次往返；Python worker 已覆盖远端缓存，但 C# 端没做"同一 game 30s 内复用上次 envelope"的内存层。
- **`HomeContentProviderId` 与 `HomeLaunchModeId` 正交但无联动校验**：运行时不会崩，但 UI 应该提示"该 Provider 主要适合 Steam 启动模式"之类的不匹配组合。

## 2026-09-17 22:00:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`

### 实现内容

- 新建 `Services/Home/FastApiHomeContentService.cs`：包装 `HttpHomeContentTransport`（已实现），把 `HomeContentEnvelope` 转成 `IHomeContentService.GetAsync` 契约的 `HomeContentResult`。Happy path 透传 `envelope.Content`（无 clone，引用相等），`envelope.Errors` 非空时透传为 `HomeContentResult.Errors`，空时保持 null（不浪费内存）。`HomeContentTransportException` 异常统一降级为"空 `HomeContent` + 一个 `transport_<ExceptionClass>` code 的 `HomeContentError`（`Recoverable=true`）"，unwrap inner 异常类型（`JsonException`、`HttpRequestException`）让 code 更可识别。`OperationCanceledException` 在 token 已 cancel 时**不吞**，rethrow 让 UI 取消语义生效。构造器 `null transport` 抛 `ArgumentNullException`，`GetAsync(null request)` 同样抛 `ArgumentNullException`。
- 改 `Models/AppSettings.cs`：新增 `HomeContentWorkerBaseUrl`（默认 `"http://127.0.0.1:8765"` 与 `HomeContentE2E.Tests` 约定一致）、`HomeContentWorkerTimeoutSeconds`（默认 10）。`CustomManifestPreset` 新增 `HomeContentProviderId` 字段（默认 `""` 表示走 preview fallback），同步 `Clone()`。
- 改 `Views/Pages/LauncherHomePage.xaml.cs`：`_homeContentService` 从硬编码 `new PreviewHomeContentService()` 改为 `CreateHomeContentService(settings)` 工厂方法 —— 当 `HomeContentWorkerBaseUrl` 非空时构造 `FastApiHomeContentService(new HttpHomeContentTransport(...))`，否则 fallback `PreviewHomeContentService`。`ShowGameAsync` 的 `HomeContentRequest.ProviderId` 从 `_currentGame?.HomeContentProviderId`（空时 fallback `"preview"`）取，不再硬编码。新增私有 `ResolveProviderId(game)` 帮助方法处理 trim + 空判断。
- 改 `Views/Pages/CustomManifestPage.xaml` + `.xaml.cs`：在 BuildID/Manifest 输入框下方新增"首页内容 Provider（可选）" `ComboBox` `cmbHomeContentProvider`，列出 7 个 Provider ID + "（空，使用 preview 示例数据）" 选项；新增私有 helper `SelectComboBoxItemByTag(combo, tag)`（未知/legacy 值 fallback 首项）和 `GetComboBoxItemTag(combo)`；`ApplyPresetToUI` 同步填充 ComboBox，`BuildPresetFromUI` 读取 `Tag` 写回 `HomeContentProviderId`。
- 新建 `Tests/FastApiHomeContentService.Tests/` 项目（独立 csproj，`OutDir=bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\FastApiHomeContentService.Tests\`，符合 AGENTS.md "测试项目必须显式设置 OutDir 到统一目录" 规则）：`Compile Include` 链接 `Models/Home/HomeContentContracts.cs` + `Models/Home/GameScreenshotPathContracts.cs` + `Services/Home/{IHomeContentService,IHomeContentTransport,HomeContentJson,HttpHomeContentTransport,FastApiHomeContentService}.cs`，自带 `FakeHomeContentTransport : IHomeContentTransport` 测试夹具。覆盖 24 项检查：构造器 null / GetAsync null / Happy path Content 透传 / IsStale 默认 false / Errors 空时 null / Errors 非空时透传 / TransportException 无 inner + JsonException inner + HttpRequestException inner 三种 code / message 包含原始异常 / Cancellation token 两种场景不吞 OCE / request 字段 GameId/ProviderId/Locale 透传。

### 验证结果

- `pytest python/tests`：204 passed + 1 skipped（无变化，前次基线）。
- `dotnet build Tests/FastApiHomeContentService.Tests/FastApiHomeContentService.Tests.csproj -c Debug`：0 错误 0 警告，1.01s。
- `bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\FastApiHomeContentService.Tests\FastApiHomeContentService.Tests.exe`：`All 24 checks passed.`
- `dotnet build SteamCN-GameLauncher.sln --configuration Debug -p:Platform=x64`：0 错误 0 警告，2.19s 增量编译。

### 当前限制

- **Python worker 进程启动尚未实现**：`LauncherHomePage` 构造 service 时读 `settings.HomeContentWorkerBaseUrl`，但当前进程没有任何代码去 spawn uvicorn 子进程。沙盒里 Python venv 已装 uvicorn 0.39.0，但未启动监听 8765，所以 `FastApiHomeContentService` 实际从未被调到 —— 启动器启动后 `OnNavigatedTo -> ShowGameAsync` 的请求仍然走 fallback 的 `PreviewHomeContentService`。`HomeContentE2E.Tests` 已经验证 transport + Python worker 全链路（用 `Process.Start` + `WaitForPort()`），可直接挪到主进程。
- **Provider ID 与游戏元数据没有绑定**：当前 7 个 Provider 在 Python 端通过 `provider_id` 区分，但 C# 端 `CustomManifestPreset.HomeContentProviderId` 是自由文本，UI ComboBox 只列了 7 个选项；用户得自己知道"鸣潮对应 `kuro-launcher`"这种映射。如果后续要支持"根据 AppID 自动推荐 Provider"，需要在 `CustomManifestService` 加 lookup 表。
- **未做 request 级缓存**：`FastApiHomeContentService.GetAsync` 每次 `ShowGameAsync` 都发新请求，切换游戏时会浪费一次往返；Python worker 已经覆盖了网络侧缓存，但 C# 端没做"同一 game 30 秒内复用上次 envelope"的内存缓存。
- **HomeContentProviderId 与 LaunchMode 独立**：`HomeLaunchModeId`（steam-cn / direct-cn / steam-international）和 `HomeContentProviderId`（kuro-launcher / hoyoplay-json 等）正交，但 UI 上没有联动校验 —— 用户可能给"国服直接启动"的游戏配上 `steam-launcher` 这种语义不匹配的 Provider。运行时不会崩，但 UI 应该提示"该 Provider 主要适合 Steam 启动模式"。

## 2026-09-17 21:42:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`

### 实现内容

- 修复 `LauncherHomePage` 视频不可用时显示白屏的问题：Python Provider 还未接入 C# 端时，`_currentHomeContent?.Background` 为 null，`ResolveVideoSource` 返回 null，原本 `ApplyHomeAnimation` 直接 `StopHomeAnimation()` 返回，UI 仅剩 `AppearancePageBackground = Transparent`，叠加 WinUI 默认窗口白底，用户看到的就是纯白。
- 新增 `Views/Pages/LauncherHomePage.xaml` 控件 `<Image x:Name="HomeBackgroundImage" IsHitTestVisible="False" Stretch="UniformToFill" Visibility="Collapsed"/>`，放在 `MediaPlayerElement` 同一 Grid 层、用 Visibility 互斥切换；Image 与视频不同时显示。
- 新增 `Views/Pages/LauncherHomePage.xaml.cs` 方法 `ShowStaticBackground()`：从 `_appearanceService.CurrentProfile.Current.SourceImage` 读取用户当前选中的背景图片名（来源 `BackgroundOptions.SourceImage`，路径前缀为 `ImageDirectory = Path.Combine(AppContext.BaseDirectory, "Backgrounds")`），调用 `_appearanceService.GetImagePath(SourceImage)` 拿到绝对路径，构造 `BitmapImage(new Uri(path, UriKind.Absolute))`，设置 `HomeBackgroundImage.Source` 并 Visibility=Visible。`SourceImage` 为空时直接 Collapse Image（保持原"白屏"行为但至少不再假装能播）；`GetImagePath` 抛 `IOException`（如文件名无效、文件不存在）或 `Uri` 构造失败时也 Collapse + 写一条 log，绝不让 UI 崩。
- `ApplyHomeAnimation` 三个分支同步改造：
  - `source is null`：从 `StopHomeAnimation()` 改为 `ShowStaticBackground()`。
  - `key 相同跳过重设 Source` 的分支：显示视频前先 `HomeBackgroundImage.Visibility = Collapsed`、`Source = null`（避免残留 Image 覆盖视频首帧）。
  - `try` 块新设 Source：同样先清 Image 再显示视频，保证"视频 OR 静态图"二选一。
- `StopHomeAnimation()` 增加两行：把 `HomeBackgroundImage` 也 Collapse 并清空 Source，确保视频切回 / 切走时 Image 不残留。
- 文件顶部新增 `using Microsoft.UI.Xaml.Media.Imaging;`（用 `BitmapImage`）。

### 验证结果

- `dotnet build SteamCN-GameLauncher.sln --configuration Debug -p:Platform=x64`：0 错误 0 警告，增量编译 3.06s（之前 15.51s 全量）。
- 修复编译过程中遇到旧测试启动的 `SteamCN-GameLauncher.exe`（PID 35016）锁住输出文件问题，已用 `Stop-Process -Force` 终止后重编。

### 当前限制

- 此改动只解决"视频不可用时显示白屏"，不解决"视频可用时白屏"——后者需要 C# 端真正接入 Python Provider（把 `HttpHomeContentTransport` 包成 `IHomeContentService`、给 `CustomManifestPreset` 加 `ProviderId` 字段、`LauncherHomePage._homeContentService` 替换硬编码的 `PreviewHomeContentService`）。
- 当前 `SourceImage` 仅支持本地 Backgrounds 目录中的图片名；不接受 HTTPS / 网络 URL（与视频策略相反）。如未来需要把"应用内默认背景"也纳入兜底（比如新装没选背景图时显示 logo），需要单独的"默认 fallback 资源"通道。
- `ShowStaticBackground()` 没有应用 `BackgroundOptions.Opacity` / `CropX` / `CropY` / `CropScale` 等裁切参数——目前是"原图直接拉伸到 UniformToFill"。AGENTS.md 设计是 `BackgroundCropRenderer` 在 `SaveCropAsync` 时已经裁好图，所以运行时不需要再裁；但如果 `SourceImage` 是导入未裁切的原图（如直接走 `ImportAsync` 跳过裁切），可能会有视觉问题。本次保持简单，未引入运行时裁切。

## 2026-09-17 21:32:00 +08:00

- 推送人员：`wonderful-oss`
- 目标分支：`python_preview`

### 实现内容

- 补齐 `python/home_content/provider_registry.py` 的回归覆盖：`python/tests/test_provider_registry.py` 新增 9 项 pytest，固定 7 个 Provider 的注册表（`hoyoplay-json` / `kuro-launcher` / `hypergryph-batch` / `perfect-world-hybrid` / `nextjs-data` / `netease-static-cms` / `local-launcher-asset`），覆盖：ID 与类的映射、Provider ID 全局唯一且非空非空白、未知 ID / 空字符串 / 大小写错误都抛 `ValueError`、`create_provider` 返回类型匹配、所有注册类都继承自 `HomeContentProvider` 基类。
- 补齐 `python/home_content/server/app.py` 的整链路集成测试：`python/tests/test_server_app.py` 新增 13 项 pytest，用 `httpx.ASGITransport(app=server_app.app)` 在进程内驱动 FastAPI（不绑端口、不依赖 uvicorn），覆盖：`/healthz` GET 返 `{"status":"ok"}`、`/v1/home-content` 命中真实 Provider 时 `requestId` / `providerId` 透传至 envelope 且 `errors=[]`、未知 providerId 返 HTTP 400、空 providerId 同样 400、Provider 抛 `NotImplementedError` 时回退到 `sample_provider.build_sample_envelope`、Provider 抛 `httpx.HTTPError` / `httpx.HTTPStatusError` / `ValueError` / `pydantic.ValidationError` 时返 HTTP 200 + envelope 中 `content.background=null`、`errors[0]` 带 `provider_<ExceptionClass>` code 且 `recoverable=true`、`create_app()` 每次调用都返回新实例、`requestId` 缺失时 pydantic 校验返 422。
- 修 `python/home_content/server/app.py` 的 Provider 失败默认错误消息：原先硬编码 `"HoYoPlay Provider failed without message."`，generic 化为 `f"{request.provider_id} provider failed without message."`，避免 Kuro / Hypergryph / NetEase / PerfectWorld 等 Provider 失败时报错信息误导。同步新增的 `test_home_content_surfaces_provider_failures_as_structured_errors` 用断言 `"HoYoPlay" not in error["message"]` 锁定。

### 验证结果

- `pytest python/tests/test_provider_registry.py python/tests/test_server_app.py -v`：22 项全部通过，耗时 0.37s。
- `pytest python/tests`：204 passed + 1 skipped（之前为 182 passed + 1 skipped，新增 22 项；LocalLauncherAssetProvider 的 POSIX-only 跳过未变）。
- `pytest python/tests` 整次运行 0.78s，无 deprecation warning、无 collection error。

### 当前限制

- 真实 Provider 仍未接入 C# 端 `LauncherHomePage`：`PreviewHomeContentService` 仍是硬编码默认，`HttpHomeContentTransport` 仍无人调用，UI 点击"鸣潮"等仍走示例数据，背景动画不会跑。后续需要 C# 端用 `HttpHomeContentTransport` 包一层 `IHomeContentService` 替掉 `PreviewHomeContentService`，并给 `CustomManifestPreset` 加 `ProviderId` 字段。
- 本次只新增了 server / registry 测试，未启动实际 uvicorn 进程拉真实厂商数据验证端到端；`HomeContentE2E.Tests`（C# 端 12 项）按 AGENTS.md 规则需迁至 `E:\AI\steamhelper\bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\HomeContentE2E.Tests\`，本沙盒缺 E: 盘未执行该步骤。
- `app.py` 的 Provider 派发已基于 `request.provider_id` 工作，但没有任何 Provider 默认 fallback 策略：当 Provider 抛错时返回空 `HomeContent` + structured error，UI 端是否需要"自动尝试下一个 Provider"未知尚未与 C# 端约定。

## 2026-09-17 21:16:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 通过 `git merge --no-ff Refactored_Version` 把 `Refactored_Version` 的提交 `9081e95 feat: expand launch appearance and update options` 同步到 `python_preview`。
- 同步内容覆盖 33 个 C# 文件（UI / Models / Services / Tests / 项目配置）：启动方式三态持久化、国服 EXE 直启、外观"统一/独立"双模式、首页动画与资讯栏开关、产品名"Steam国服游戏启动器"统一、GitHub Releases 检查升级、Release DTO / 选择策略 / 版本比较器 + 独立测试项目等。
- 唯一冲突文件 `docs/PUSH_HISTORY.md`：python_preview HEAD（`2293a84`）与 `9081e95` 各自在顶部追加了一条推送条目。按时间倒序保留两侧记录并拼接，旧记录以 merge-base `6a8bc96` 为准保持不变。

### 验证结果

- `git merge --no-ff Refactored_Version`：33 个 C# 文件自动合并成功，仅 `docs/PUSH_HISTORY.md` 触发内容冲突。
- 冲突解析脚本读取 `HEAD` / `Refactored_Version` / `6a8bc96` 三个版本，按时间戳排序去重，输出新文件保留所有记录。
- 当前 `python_preview` HEAD 已包含本次合并 commit（待推送）。

### 当前限制

- 合并后 `python_preview` 与 `Refactored_Version` 分支历史出现分叉（如需保持线性历史可改用 cherry-pick + 后续 fixup，但会失去合并节点）。
- 本次未自动推送，需手动 `git push origin python_preview` 完成同步。
- 合并后 `python_preview` 上的 C# 项目同时拥有"启动方式持久化 / GitHub Releases 更新"等 Refactored 特性，可立即在本地编译验证。

## 2026-09-17 00:29:06 +08:00

- 推送人员：`Violet0923`
- 目标分支：`Refactored_Version`

### 实现内容

- 为每个游戏增加持久化的首页启动方式，可在“国服”“Steam 玩国服”“Steam 玩国际服”之间切换；旧配置默认迁移为“Steam 玩国服”。
- 增加国服真实 EXE 直接启动服务，并区分 Steam 国服重定向与 Steam 国际服原生启动检查；游戏配置页根据启动方式显示适用字段和操作。
- 外观设置增加“所有界面统一设置”和“每个界面分别设置”两种背景模式；独立模式覆盖首页、游戏配置、截图、外观和设置页，首次启用时深拷贝统一设置，此后分别保存背景选择、卡片不透明度、图片不透明度、遮罩强度和显示方式。
- 首页增加动画与资讯栏显示开关；动画仅接受本地文件或 HTTPS 视频，关闭、缺失或播放失败时自动回退到当前页面背景图片，并在页面卸载时停止媒体资源。
- 将中文产品显示名称统一调整为“Steam国服游戏启动器”，同步窗口、设置页、包元数据、安装向导、卸载列表、开始菜单、桌面快捷方式及中文安装包文件名；EXE、安装目录、AppId 和配置目录保持不变以兼容覆盖安装。
- 将线上更新检查由仓库 `version.json` 改为 GitHub Releases REST API：正式渠道检查最新正式 Release，测试渠道包含 prerelease 并排除 draft；使用 Release tag 比较版本、正文作为更新说明、Release 页面作为下载入口。
- 保留本地 Debug `version.json` 以测试强制更新和开放时间；手动检查网络失败时显示明确错误，不再误报“当前已是最新版本”。
- 增加 GitHub Release DTO、Release 选择策略、稳定版/预发布版本比较器及独立回归测试项目。

### 验证结果

- 已获取并确认远程 `origin/Refactored_Version` 与提交前本地 HEAD 无分歧。
- 主程序 Debug x64 构建通过，0 个错误；NuGet 漏洞索引因当前网络不可用产生 1 个 `NU1900` 警告。
- `BackgroundCrop.Tests`：7 项检查全部通过。
- `CustomNavigation.Tests`：20 项检查全部通过。
- `HomeContentContract.Tests`：7 项检查全部通过。
- `ManifestUpdate.Tests`：14 项检查全部通过。
- `SteamAppInfo.Tests`：41 项检查全部通过。
- `UpdateService.Tests`：10 项检查全部通过。
- 当前 GitHub 最新正式 Release 已核对为 `v2.6.1`，与本地版本一致。
- `git diff --check`：通过。

### 当前限制

- 首页仍由 `PreviewHomeContentService` 提供示例内容，尚无真实视频地址时会按设计显示所选背景图片；厂商动画与资讯仍需后续 Python Provider 接入。
- “Steam 玩国际服”依赖用户现有 Steam 安装和游戏配置，若 Steam 启动选项仍重定向到国服，需要用户先清除或调整该启动选项。
- 通过国服真实 EXE 直接启动不会产生 Steam“游戏中”状态。
- GitHub Release 不提供 `forceUpdate` 和 `availableAfter` 字段，线上 Release 当前均按普通更新处理；这两个行为仅由本地 Debug `version.json` 提供测试。
- 本次更新安装器配置与发布脚本，但没有生成或上传新的 Release 安装包；新名称将在下一次正式打包时生效。

## 2026-09-15 21:45:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `LocalLauncherAssetProvider`（`python/home_content/providers/local_launcher_asset.py`）：本地启动器资源探测。**完全本地、零网络请求**——`fetch()` 只读 `providerOptions.installDir` 下的 `bg.mp4` / `bg.webm` / `bg.jpg` / `bg.png` / `config.json` 等文件。`video_url` 输出 `file://` URI（Windows 用 `as_uri()` 自动处理 `C:/` 与空格转义），同时 `local_path` 给绝对路径；`config.json` 中 `version` / `title` / `summary` / `downloadUrl` / `publishedAt` 顺手填到 `HomeUpdateInfo`。`banners` 和 `news` 永远为空（启动器本地不带 Banner / 资讯流，那部分是网络 Provider 的活）。
- 探测顺序：`videoFileNames` 默认 `["bg.mp4", "bg.webm"]`、`imageFileNames` 默认 `["bg.jpg", "bg.png", "background.jpg", "background.png"]`、`configFileName` 默认 `"config.json"`。默认 `maxDepth=2` 会看 `assets/` / `video/` / `media/` 子目录（Linux launcher 常用），不会扫 `.git/` / `logs/` 这类目录。三个 file name 列表都可以用 `providerOptions` 覆盖，也接受逗号分隔字符串。`_candidate_paths` 用 `Path.resolve()` 兼容 Windows / POSIX 路径。
- `_path_to_file_url` 处理 `Path("C:/Program Files/...").resolve()` 在 Windows 上需要 percent-encode 空格的情况。如果 `as_uri()` 因含 `#` 等字符抛 `ValueError`，回退到手动 `file:///<encoded>` 拼接，启动器如果装在带 `#` 的目录也不会让 Worker 崩。
- 完全本地不依赖 `httpx`。构造器支持 `file_reader` / `exists_checker` 注入钩子，pytest 用 `FakeFS` 模拟文件系统的 3 种布局（Windows / macOS `.app` bundle / Linux `assets/`），不读真实文件。
- `providerOptions` 支持：
  - `installDir`（必需，绝对路径）—C# 侧探测 Steam 库 / 注册表 / `/Applications` 后传入
  - `videoFileNames` / `imageFileNames`（list 或逗号字符串）
  - `configFileName`（设 `""` 关闭）
  - `maxDepth`（限制搜索深度）
- `FakeFS._normalise` 在 Windows 上自动把 POSIX 路径归一化为 `C:/...`，并对查询路径也走 `Path.resolve()`，保证 add / lookup 一致。`_winpath()` 测试 helper 把 `file:///opt/...` 在 Windows 上前缀 `C:/`，让 URL 期望值跨平台统一。
- 新增脱敏样本 `contracts/samples/launcher-local-config-{win,mac,linux}.json`：模拟三种 launcher 布局，每个 fixture 包含 `installDir` + `files`（含 `bg.mp4` / `config.json` 等） + 共享的 config.json 内容（含 `version=2.9.0`、`publishedAt=2026-09-10T03:00:00+08:00` 等）。
- 新增 Provider 文档 `docs/LOCAL_LAUNCHER_ASSET_PROVIDER.md`：验证日期、3 种布局的资源映射、`file://` URI 在 Windows / POSIX 上的差异、候选文件顺序、`maxDepth` 默认 2 的依据、`providerOptions` 全表、跨平台路径归一化、限制（不自己探测 installDir、banners/news 为空、config.json 字段白名单、maxDepth=2）。
- 在 `README.md` 新增「**启动器资源探测**」段落：解释首页内容的两层数据源（网络 Provider + 本地启动器资源），列出 6 个网络 Provider 文档链接，加 `LocalLauncherAssetProvider` C# 端配合示例（先拉网络版，再用本地 launcher 资源覆盖背景视频）。
- 新增 `python/tests/test_local_launcher_asset_provider.py`：28 项 pytest，覆盖默认值 / `_coerce_str_list` 各种输入 / `_path_to_file_url` POSIX + Windows + 空格 / `_build_background`（video 优先 / 仅图兜底 / 全空 / 子目录 / 自定义 file names）/ `_build_update_info`（解析 / 缺失 / 无效 JSON / 关闭）/ 端到端 fetch（Windows / macOS / Linux / 缺 installDir / override video+image / override configFileName / 关闭 config / maxDepth=1 不扫子目录）。Provider 通过 `file_reader` / `exists_checker` 注入，测试用 `FakeFS` 模拟，**完全不入网、不读真实文件**。
- `.gitignore` 在 `docs/` 白名单里追加 `LOCAL_LAUNCHER_ASSET_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_local_launcher_asset_provider.py`：27 项通过 + 1 项 POSIX-only 跳过，耗时 0.25s。
- `pytest python/tests/`：HoYoPlayProvider 10 + KuroLauncherProvider 13 + HypergryphBatchProvider 23 + PerfectWorldHybridProvider 41 + NextJsDataProvider 39 + NetEaseStaticCmsProvider 29 + LocalLauncherAssetProvider 27(+1 skip) = **182 passed, 1 skipped** 在 0.40s 内完成。

### 当前限制

- Provider 不自己探测 installDir——registry / Steam 库 / `~/Applications` 的探测逻辑在 C# 侧完成。这避免 Python 端做平台特定的 shell 调用，但意味着任何使用 `LocalLauncherAssetProvider` 的 C# 流程必须先有 launcher 路径发现代码（已存在 / 计划实现）。
- `banners` / `news` 永远为空。启动器本地几乎不带 Banner / 资讯流，这两个字段是网络 Provider 的活。如果某个 launcher 把营销资源打进安装目录，可以扩展 `_candidate_paths` 把 banner/news 也覆盖，但目前默认不支持。
- `config.json` 解析只读 `version` / `title` / `summary` / `downloadUrl` / `publishedAt` 五个字段；`publishedAt` 兼容 `Z` 与 `+HH:MM` 后缀，缺字段时整个 `update_info` 退化为 None。增加字段要同步改 Provider DTO（C# 端）+ 文档 + fixture。
- `maxDepth=2` 默认足够覆盖 `assets/` / `video/` / `media/` 子目录，不会扫 `.git/` / `logs/` / `cache/`。启动器如果资源在更深位置（例如嵌套 `assets/video/`），要传 `maxDepth>=3`。
- 探测白名单子目录只包含 `assets` / `video` / `media`。如果某个 launcher 把资源放进 `data/` / `resource/` / `bg/` 等其他目录，需要在 C# 端用 `providerOptions` 传 `videoFileNames` 直接命中文件，而不是依赖子目录猜测。
- `_path_to_file_url` 在 `Path.as_uri()` 因路径含 `#` 等抛 `ValueError` 时回退到手动拼接，**不会**抛错。但 percent-encoding 规则与 `as_uri()` 不完全一致——带特殊字符的安装目录可能产生 URL 解析差异（极少见）。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=local-launcher-asset` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。本地资源 C# 端直接读文件 + 自己校验即可。

## 2026-09-15 21:25:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `NetEaseStaticCmsProvider`（`python/home_content/providers/netease_static_cms.py`）：网易《燕云十六声》（Where Winds Meet）官方营销站点的 NIE 静态 CMS 解析器。CN 入口 `https://www.yysls.cn/index.html`（杭州网易雷火 / NIE static-CMS 模板），HMT 入口 `https://www.wherewindsmeetgame.com/hmt/index.html`（Sony 台湾繁体联运 / Sony 营销模板，结构不同暂未解析）。营销站点在 HTML 中直接 server-side render 整个首页：`#news` panel 下包含 banner swiper + 4 个 `.news-list-N` 资讯列表容器。
- Banner 从 `.slide-news .banner .swiper-slide > a` 解析；图片走 `data-src`（Swiper lazy load）否则 `src`，标题取 `title` 属性，跳转 URL 是 `<a href>`。背景没有硬编码——`<video class="bg">` 是上游 JS bundle 运行时注入——Provider 把第一张 banner 图作为 `HomeBackground.image_url` 兜底，让 C# UI 在 launcher 自带背景视频未下载完成前有静态底图。
- 资讯从 `.slide-news .news-wrap .news-list-N` 解析；N=0 "最新" 用 `KIND_CATEGORY`（`新闻→资讯`、`公告→公告`、`活动→活动`），N=1/2/3 用 `TAB_CATEGORY`（`资讯/公告/活动`）。每行 `<a class="link" href>` 携带 `.date`（`MM/DD` 月日格式）/ `.kind` / `.title` / `.desc`，target_url 用 `<a href>`，允许 host 白名单过滤。`MM/DD` 用当前 UTC 年补齐（上游不发年份，启动器每 10–30 分钟刷新一次，跨年偏差自动收敛）。
- `ALLOWED_HOST_SUFFIXES` 仅接受 `.yysls.cn` / `.netease.com` / `.nie.netease.com` / `.wherewindsmeetgame.com` / `.easebar.com` / `.fp.ps.easebar.com` / `.yysls.v.netease.com` / `.yysls-build-na.fp.ps.easebar.com`；scheme 必须 http(s)。第三方跳转（`mp.weixin.qq.com` / `space.bilibili.com` / `weibo.com` 等社交域名）默认丢弃，不显示在 UI 上。`providerOptions` 支持 `maxBanners=0` 关掉 banner（仍保留 news 列表），`newsPerTab=0` 关掉对应 tab 的资讯。
- `parse_home_html` 用两个正则：`<div class="panel" id="news">(.*?)<div class="panel" id="media">` 抓整段 news panel；`_BANNER_LINK_RE` / `_NEWS_LIST_RE`（按 tab 分组）+ `_NEWS_LINK_RE`（按 row）解出原始行。缺 panel 时包 `ValueError`，HTTP 5xx 时降级为空 envelope。
- 默认常量 `DEFAULT_REGION=cn`、`DEFAULT_BASE_CN=https://www.yysls.cn`、`DEFAULT_HOME_PATH_CN=/index.html`、`DEFAULT_BASE_HMT=https://www.wherewindsmeetgame.com`、`DEFAULT_MAX_BANNERS=4`、`DEFAULT_NEWS_PER_TAB=4`；`providerOptions` 支持 `region` / `homePageUrl`（完整 URL，不拼接 path）/ `maxBanners` / `newsPerTab`。HMT 区域暂只占位不解析，等下次把 `newsBanner` / `newsList` 模板结构画完再补。
- 新增脱敏样本 `contracts/samples/yysls-cn.json`：基于 2026-09-15 真实响应（`www.yysls.cn/index.html`，57KB），用两个正则抽出 5 张 banner + 40 条 news（4 个 tab × 10 条），`trimmedHtml` 是剥掉 `<style>` / `<script>` / `<link>` 后保留 `.slide-news` 结构的精简 HTML，供 round-trip 测试用；原始抓包走 `contracts/samples/_raw_yysls/`（`.gitignore` 已排除，不入库）。
- 新增 Provider 文档 `docs/NETEASE_STATIC_CMS_PROVIDER.md`：验证日期、采样区域、2 个端点 URL、`#news` panel HTML 模板与字段映射、`TAB_CATEGORY` / `KIND_CATEGORY` / 默认常量、白名单、`MM/DD` 用当前年的回退方案、HMT 模板未实现说明、社交跳转丢弃说明。
- 新增 `python/tests/test_netease_static_cms_provider.py`：29 项 pytest，覆盖默认值 / allow-list / `TAB_CATEGORY` & `KIND_CATEGORY` 映射 / `_parse_mmdd_date`（含用当前年回退）/ `parse_home_html`（news panel 缺失、round-trip、banner 抽取）/ `_build_background` / `_build_banners`（`maxBanners` 上限、host 白名单、跳转丢弃）/ `_build_news_items`（per-tab 上限、4 类覆盖、空标题、host 白名单）/ 端到端 fetch（CN 默认路径、HMT 端点、homePageUrl 覆盖、maxBanners=0、HTTP 5xx、panel 缺失）。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `NETEASE_STATIC_CMS_PROVIDER.md`（`contracts/samples/_raw_*/` 已在上一轮覆盖，连带本次的 `_raw_yysls/` 一起不入库）。

### 验证结果

- `pytest python/tests/test_netease_static_cms_provider.py`：29 项全部通过，耗时 0.20s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项 + PerfectWorldHybridProvider 41 项 + NextJsDataProvider 39 项 + NetEaseStaticCmsProvider 29 项共 **155 项**全部通过，耗时 0.33s。
- 直接 probe 真实端点：`https://www.yysls.cn/index.html` 返回 200（57KB HTML，banner swiper 4+ 张图、news-list-{0,1,2,3} 共 40 条 news row，`<video class="bg">` 在 markup 中是空 src 由 JS 注入）；`https://www.wherewindsmeetgame.com/hmt/index.html` 返回 200（36KB HTML，结构不同用 `newsBanner` / `newsList` 模板，banner 容器空需 JS 注入）。

### 当前限制

- 端点来自对网易 NIE 静态 CMS 的反向工程，非公开 API。`#news` panel 结构（`slide-news` / `news-list-N` / `link` class 名）随时可能改版。每次 rebase / 大版本后需要重新采样脱敏样本（保留 `contracts/samples/_raw_yysls/` 抓包，仅本地不入库）。
- 背景视频不在 HTML 里，是 `<video class="bg">` 由上游 JS bundle 在 viewport 上注入。Provider 只暴露第一张 banner 图作为静态兜底；后续可让 `LocalLauncherAssetProvider` 在 launcher 安装目录探测真实 bg.mp4 覆盖回来。
- 资讯 `.date` 只有 `MM/DD`，缺年份。Provider 用当前 UTC 年补齐——春节 / 跨年边界的资讯可能短暂标错年份，但启动器每 10–30 分钟刷新，跨年后一小时左右自愈。
- HMT（`wherewindsmeetgame.com`）区域目前只占位 `DEFAULT_BASE_HMT`，模板 `newsBanner` / `newsList` 解析未实现。AGENTS.md 要求 CN / HMT 不共用 endpoint / 假设，所以等下一次把繁体模板结构画清楚再单独补 `parse_hmt_home_html`，不与 NIE static-CMS 混用。
- Banner `href` 中带社交跳转（`mp.weixin.qq.com` / `weibo.com` / `space.bilibili.com` 等）一律被白名单丢弃，不进 UI。如果 launcher 想做"分享到微博"快捷入口，需要单独建一个 `social_links` channel，不在当前 Provider 里。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=netease-static-cms` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-15 21:05:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `NextJsDataProvider`（`python/home_content/providers/nextjs_data.py`）：叠纸《无限暖暖》（Infinity Nikki）官方营销站点的 Next.js `__NEXT_DATA__` 解析器。OS 入口 `https://infinitynikki.infoldgames.com/{locale}/home`（INFOLD PTE. LTD.），CN 入口 `https://infinitynikki.nuanpaper.com/home`（上海暖叠 / Papergames），资讯列表通过 `GET /api/news?section={0,1,2}&offset=&limit=&locale=` 拉取，每次 fetch 串行调 1 个 home + 3 个 section 端点。OS 端 `<video src=…>` 硬编码背景；CN 端 `<video poster=…>` 无 `src` 时回退到 `pageData.page.pv_list[label="pc 首屏背景视频"].value`（CDN）。`parse_home_html` 用正则 `<script id="__NEXT_DATA__" type="application/json">(.+?)</script>` 抽出整段 JSON，缺 blob / 非 JSON 都包成 `ValueError`。
- 横幅分两类：`pageData.newsbanner[]` 是首页顶部轮播（仅图片、无跳转 URL），`_build_top_banners` 按 `id` 拼出 `in-newsbanner-<id>`，图片走 allow-list 白名单，未通过直接丢弃（不渲染空 banner）；`pageData.page.actBannerlist[]` 是运行时活动横幅，结构是 `{__type: "json", label, value}`，`value` 是 JSON 字符串（含 `bannerimg` / `link` / `starttime` / `endtime`），`_build_activity_banners` 用 `json.loads` 解析，目标 URL 也走白名单；非法 JSON 跳过并 `logger.warning`。
- 资讯按 `/api/news` 三 section 拉：`section=0 → 资讯`、`section=1 → 公告`、`section=2 → 活动`（与 Tab 顺序一致）。`SECTION_CATEGORY` 显式映射；`_build_news_items` 用 `id` + region + section 拼稳定 ID，target_url = `<newsBase>/news/<id>`（绝对），空标题 / 非允许 host 的封面自动剔除。`publish_time` 是 `2026-09-10T03:00:00.000Z` 这类带毫秒的 ISO 8601，`_parse_iso` 兼容 `Z` / `+HH:MM` / 裸 datetime，归一化到 UTC；`actBannerlist` 用 `YYYY-MM-DD HH:MM:SS`，`_parse_naive` 优先 strptime 三种格式再回退 `_parse_iso`。
- `ALLOWED_HOST_SUFFIXES` 仅接受 `.infoldgames.com` / `.papegames.com` / `.nuanpaper.com` / `.webstatic.infoldgames.com` / `.webstatic.papegames.com` / `.assets.infoldgames.com` / `.assets.papegames.com` / `.assets.nuanpaper.com`；scheme 必须 http(s)；子域按 suffix 匹配（`eng.papegames.com` 也放行），前缀伪装（`evil.com/assets.infoldgames.com/x.png`）拒绝。`httpx.HTTPError` / `json.JSONDecodeError` 在 Provider 层包 `logger.warning`，网络端点 5xx 时 banner/news 自动降级为空，背景 video 缺失时 `HomeBackground` 仍然生成但不携带视频。
- 默认常量 `DEFAULT_BASE_OS=https://infinitynikki.infoldgames.com` / `DEFAULT_BASE_CN=https://infinitynikki.nuanpaper.com` / `DEFAULT_LOCALE_OS=zh-TW` / `DEFAULT_LOCALE_CN=zh-CN` / `DEFAULT_PAGE_PATH=/home` / `DEFAULT_NEWS_LIMIT=4` / `DEFAULT_REGION=os`；`providerOptions` 支持覆盖 `region` / `locale` / `homePageUrl`（完整 URL，不拼接 locale / pagePath）/ `newsApiBase` / `pagePath` / `newsLimit`。CN 调 `/api/news` 时不附带 `locale=` 参数。
- 新增脱敏样本 `contracts/samples/infinity-nikki-{home-os,home-cn,news-list-{os,cn}-s{0,1,2},news-detail-{os,cn}}.json` 共 10 个文件：基于 2026-09-15 实采响应，HTML 走 `<__NEXT_DATA__>` 正则拆出 `nextData` + `<video src=...>` + `<video poster=...>` 三段，删掉与 provider 无关的 i18n/法律/资源 URL 字段（保留 `tabName` / `newsTextSign` / `versionPvUrl` / `pv_list` / `actBannerlist` / `newsbanner` 等），保留真实 CDN 域名以便 allow-list 校验路径与生产一致，list 裁到 2 条。原始 HTML/JSON 抓在 `contracts/samples/_raw_infinity_nikki/`（`.gitignore` 已排除，不入库）。
- 新增 Provider 文档 `docs/NEXTJS_DATA_PROVIDER.md`：验证日期、采样区域、语言、4 个端点 URL 与响应示例、`__NEXT_DATA__` 与 `/api/news` 字段映射、`SECTION_CATEGORY` / `ALLOWED_HOST_SUFFIXES` / 默认常量、`providerOptions` 全表、CN 背景 pv_list 回退、newsbanner 无跳转 URL 等限制。
- 新增 `python/tests/test_nextjs_data_provider.py`：39 项 pytest，覆盖默认值 / allow-list / `_parse_iso` / `_parse_naive` / `parse_home_html`（含缺 blob、非 JSON、`<video src>` / `<video poster>` 抽取、不匹配其他 `<script>` 标签）/ `_resolve_pv_video` / `_build_background` / `_build_top_banners`（白名单拒绝）/ `_build_activity_banners`（JSON 解码失败、目标 host 拒绝）/ `_build_news_items`（空标题、非法 cover、section 映射）/ 端到端 fetch（OS 全路径、CN pv_list 回退、首页 5xx 降级、所有端点 500 全空、providerOptions.homePageUrl 覆盖、news 返回非 dict）。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `NEXTJS_DATA_PROVIDER.md`，新增 `contracts/samples/_raw_*/` 排除规则避免原始抓包入库。

### 验证结果

- `pytest python/tests/test_nextjs_data_provider.py`：39 项全部通过，耗时 0.36s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项 + PerfectWorldHybridProvider 41 项 + NextJsDataProvider 39 项共 **126 项**全部通过，耗时 0.37s。
- 直接 probe 真实端点：OS 端 `https://infinitynikki.infoldgames.com/zh-TW/home` 返回 200（HTML 含 `<video src=…bc3a08b841873ed2.mp4>` + 完整 `__NEXT_DATA__`）；CN 端 `https://infinitynikki.nuanpaper.com/home` 返回 200（HTML 仅 `<video poster=…>` + `__NEXT_DATA__.pageData.page.pv_list`）；`/api/news?section={0,1,2}` 三 section 都返回 `{data: {total, data[]}, ret: 0, msg: "ok", timestamp}` envelope，`section=0` 总数 44 条、`section=1` 82 条、`section=2` 4 条（国际服），CN 端总数更大（资讯 792 条）。

### 当前限制

- 端点来自对叠纸 Next.js 营销站点的反向工程，非公开 API；首页结构、字段命名（`newsbanner` / `actBannerlist` / `pv_list`）随时可能改版。每次 rebase / 大版本后需要重新采样脱敏样本（保留 `contracts/samples/_raw_infinity_nikki/` 抓包，仅本地不入库）。
- `newsbanner` 不携带跳转 URL，C# UI 端要么绑定固定的"打开内嵌新闻 inbox"行为，要么只展示图。当前 Provider 把 `target_url=None` 透传，不伪造跳转。
- `actBannerlist.value` 是 JSON 字符串（不是对象），解析失败的行被静默丢弃并 `logger.warning`；如果上游改成对象 / 数组，需要调整 `_build_activity_banners`。
- CN home `<video>` 不带 `src`，Provider 兜底靠 `pv_list`；如果上游连 `pv_list` 都去掉，CN 端会落到空背景（`HomeBackground.video_url=None`，`image_url` 仅剩 poster）。
- 资讯跳转 URL 走 `<newsBase>/news/<id>`，但实际新闻详情页路由可能变化（例如叠纸未来改成 `/news/detail/<id>`），需要持续验证。
- CN 调 `/api/news` 不携带 `locale=` 参数；如果 CN 上游未来按语言分发，需要在 Provider 里加 `news_locale` 逻辑。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=nextjs-data` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-15 20:45:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `PerfectWorldHybridProvider`（`python/home_content/providers/perfect_world_hybrid.py`）：完美世界《异环》（Neverness to Everness）的本地启动器资源 + 远程 JS 数据混合 Provider。每次 fetch 并发拉两个端点：Global 从 `https://www.perfectworld.com/public/commonData/gamesData/gameSwiper/nte-gameSwiper.js`（横幅 swiper）和 `https://nte.perfectworld.com/include/newsData20260112.js`（资讯）；国服从 `https://static.games.wanmei.com/public/commonData/gamesData/gameSwiper/yh-gameSwiper.js` 和 `https://yh.wanmei.com/include/newsData20260112.js`。两个端点返回 `var NAME = {...};` 形式的 JS 对象字面量，`extract_js_payload` 剥包装、压双逗号、去除尾部逗号后用 `json.loads` 解析。
- 背景优先用 `providerOptions.backgroundVideoPath` / `backgroundImagePath`（本地启动器 `bg.mp4` / `bg.jpg`），其次 `backgroundVideoUrl` / `backgroundImageUrl`（CDN 兜底，allow-list 校验），最次走 `DEFAULT_BG_VIDEO_OS`（`ntevmg.perfectworld.com/webops/nte/nte_bgvideo_20260418.mp4`）/ `DEFAULT_BG_VIDEO_CN`（`yhvmg.wmupd.com/webops/yh/yh_bgvideo_20260418.mp4`），从启动器主页 HTML 提取并固化。
- 横幅来自 `lb1_<lang>`（OS，按语言取 `lb1_en` / `lb1_cn` / `lb1_jp` …）或 `lb1`（CN，无语言门控），缺失时按 `lb1` 兜底。资讯按 `pc.*`（CN，无语言门控）或 `<short_lang>.*`（OS）选 bucket，每个 bucket 内按 `news → gamebroad → gameevent → gamenews` 顺序产出；OS 的 `channelDescription` 经 `TAB_NAME_MAP` 映射到中文分类（`Notices/Mitteilungen/...` → 公告，`News/Nachrichten/...` → 资讯，`Events/Événements/...` → 活动），CN 直接用 `channelCnName`。
- 资讯 URL 是相对路径（OS `/en/article/news/...` / CN `/news/...`），`_absoluize_url` 按区域加 host：OS → `https://nte.perfectworld.com{url}`、CN → `https://yh.wanmei.com{url}`。跳转 URL 非 https 或 host 不在 allow-list 时置空（不抛错）。
- `ALLOWED_HOST_SUFFIXES` 仅接受 `.perfectworld.com` / `.wanmei.com` / `.wmupd.com` / `.games.wanmei.com` / `.static.pwsdk.com`；scheme 必须 http(s)；白名单外的 host 视为无效并丢弃。`start_ts` / `time`（`YYYY-MM-DD`）转 UTC `datetime`；`JSONDecodeError` / `ValueError` 在 Provider 层包装为 `ValueError`，与 Kuro / HoYo 行为一致；网络端点 503 时不抛错，返回空 banner/news 并交给 Worker 写 `envelope.errors`。
- 默认常量 `DEFAULT_APP_CODE=YDUTE5gscDZ229CW`、`DEFAULT_LANGUAGE=en-us`、`DEFAULT_REGION=os`；`providerOptions` 支持覆盖 `region` / `language` / `installDir` / `backgroundVideoPath` / `backgroundImagePath` / `backgroundVideoUrl` / `backgroundImageUrl` / `bannerSwiperUrl` / `newsDataUrl`。
- 新增脱敏样本 `contracts/samples/nte-{game-swiper-intl,game-swiper-cn,news-data-intl,news-data-cn}.json`：基于 2026-09-14 真实 JS 端点响应，经 `_extract_js_payload` 同款清洗逻辑后保存，URL 保留真实域名以便 allow-list 校验路径与生产一致；md5 缩短、列表裁剪到 2/3 条。
- 新增 Provider 文档 `docs/PERFECT_WORLD_HYBRID_PROVIDER.md`：验证日期、采样游戏 / 语言、4 个端点 URL 与响应示例、字段映射、tab 名称映射、默认常量、白名单、本地启动器资源入口、回退方案与已知限制（`appCode` 反编译来源 / JS 端点私有格式 / CN 不按语言分发 / 资讯 URL 相对路径 / MIME MD5 校验在 C# 端）。
- 新增 `python/tests/test_perfect_world_hybrid_provider.py`：41 项 pytest，覆盖 JS 提取（var 包装 / 裸对象 / 双逗号 / 尾逗号）、allow-list、白名单过滤、tab 映射、本地 vs 网络背景、相对 URL URL 绝对化、providerOptions 覆盖、HTTP 5xx / 非 JSON 响应不抛错、本地 backgroundVideoPath 优先于网络 URL。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `PERFECT_WORLD_HYBRID_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_perfect_world_hybrid_provider.py`：41 项全部通过，耗时 0.30s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项 + PerfectWorldHybridProvider 41 项共 87 项全部通过，耗时 0.32s。
- 直接 probe 真实端点（Global `nte-gameSwiper.js` + `newsData20260112.js`、CN `yh-gameSwiper.js` + `newsData20260112.js`）确认 4 个 URL 都返回 JS 包装的 JSON；Global `nte_bgvideo_20260418.mp4` 与 CN `yh_bgvideo_20260418.mp4` 都是 200 OK 的 MP4；`nte.perfectworld.com/cn/main.html` 内嵌 `<video src=...>` 与 `newsData20260112.js` 一致。

### 当前限制

- `appCode` / 启动器常量与 JS 端点 URL 都来自对 NTE 启动器资源与官网 HTML 的反向分析，不公开。完美世界改版或启动器升级后端点路径可能漂移，需要重新采样。
- JS 端点用的是私有 JS 对象字面量（带尾逗号、双逗号、空行分隔），`extract_js_payload` 已处理这些。如果未来版式换了（用了别的写法），需要更新正则。
- 横幅 swiper 的 `bigpic` / `link` / `mlink` 域名都在 allow-list，但实际 launcher 还会下发 `discord.gg` / `x.com` / `youtube.com` / `pwgam.es` 等第三方跳转域名（`mlink` 或 `link` 直接指向），Provider 自动剔除这些跳转。
- CN 端 `pc.*` 不分语言；OS 端 `lang.*` 在目标语言无响应时按 `cn` → `en` 兜底，避免空 news。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=perfect-world-hybrid` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- 启动器本地资源（`bg.mp4` / `bg.jpg`）没有自动 `installDir` 探测，由 C# 侧先 `LocalLauncherAssetProvider` 探测路径再喂给本 Provider。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-14 23:20:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `HypergryphBatchProvider`（`python/home_content/providers/hypergryph_batch.py`）：鹰角《明日方舟：终末地》及同厂商游戏的单端点批量协议 `POST https://launcher.gryphline.com/api/proxy/web/batch_proxy`（Global）/ `https://launcher.hypergryph.com/api/proxy/web/batch_proxy`（CN）。Provider 在一次 POST 里并发请求 `get_main_bg_image` / `get_banner` / `get_announcement` 三种 `kind`，统一从 `proxy_rsps[]` 中提取对应 `*_rsp`。背景优先 `video_url`，降级 `url`；Banner 来自 `banners[]`，id 字段做稳定 ID；News 来自 `tabs[].announcements[]`，英文 tabName 映射到中文分类（`Notices→公告`、`Events→活动`、`News→资讯`），其他保留原文。`start_ts`（毫秒）转 UTC `datetime`。
- 候选 URL 通过 host 白名单（`.hg-cdn.com` / `.hycdn.cn` / `.gryphline.com` / `.hypergryph.com` / `.skport.com` / `.skland.com`），scheme 必须为 `http(s)`，白名单外的 banner / jump_url 直接丢弃，背景无候选时 `HomeBackground=None`。`json.JSONDecodeError` 在 Provider 层被包装为 `ValueError`，与 Kuro / HoYo 行为一致。
- 默认常量 `DEFAULT_APP_CODE=YDUTE5gscDZ229CW`、`DEFAULT_CHANNEL=6`、`DEFAULT_SUB_CHANNEL=6`、`DEFAULT_LANGUAGE=en-us`、`DEFAULT_REGION=os`；`providerOptions` 支持覆盖 `region` / `baseUrl` / `appCode` / `channel` / `subChannel` / `language`。Global 与 CN 入口通过 `region=cn` 切换（自动取 `launcher.hypergryph.com/api` 与 `appCode=6LL0KJuqHBVz33WK / channel=1 / subChannel=1 / language=zh-cn`）。
- 新增脱敏样本 `contracts/samples/hypergryph-batch-envelope.json`：基于 2026-09-14 真实响应（Global/en-us/终末地），合并三个 `kind` 的 `proxy_rsps[]`，md5 缩短到 8 位，banner / announcement 列表裁剪到 3 / 2 条；保留真实 CDN / 官方域名以便 allow-list 校验路径与生产一致。
- 新增 Provider 文档 `docs/HYPERGRYPH_BATCH_PROVIDER.md`：记录验证日期、采样游戏 / 语言、batch_proxy 单端点协议、字段映射、tabName → 中文分类映射、默认常量、白名单、回退方案与已知限制（`appCode` 反编译来源 / `data_version` 当前为空 / 未映射 `url_config` `sidebar` `single_ent`）。
- 新增 `python/tests/test_hypergryph_batch_provider.py`：23 项 pytest，覆盖 allow-list、白名单过滤、`start_ts` 转 UTC、`providerOptions` 覆盖、空 envelope、缺 `proxy_rsps` envelope、非 JSON 响应、HTTP 5xx 上抛。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `HYPERGRYPH_BATCH_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_hypergryph_batch_provider.py`：23 项全部通过，耗时 0.21s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项 + HypergryphBatchProvider 23 项共 46 项全部通过，耗时 0.22s。
- 直接 `python -m home_content.providers.hypergryph_batch`（probe 脚本）调真实接口：Global 端点返回 `proxy_rsps[0..2]`，其中 `get_main_bg_image_rsp.main_bg_image` 携带 MP4 + PNG，`get_banner_rsp.banners` 含 9 条 banner（CDN + 森空岛跳转），`get_announcement_rsp.tabs` 含 Notices / Events / News 三类。CN 端点（`launcher.hypergryph.com`，`zh-cn`）返回结构一致，CDN host 为 `hg-utils-public.hycdn.cn`。

### 当前限制

- `appCode` / `base` URL 来自 [`daydreamer-json/ak-endfield-api-archive`](https://github.com/daydreamer-json/ak-endfield-api-archive) 的 `config.ts` 反编译（base64 编码），鹰角不公开字符串。发版后可能更换，需要重新采样。
- `data_version` 字段当前始终为空字符串，Provider 不做版本协商；若鹰角后续引入版本化协议，需要扩展 `_extract_responses` 并新增缓存键策略。
- `get_url_config`、`get_sidebar`、`get_single_ent` 未映射到 `HomeContent`：前者是充值/问卷链接（无 UI 落点）；`sidebar` 是社交媒体入口（不属于首页布局）；`single_ent` 当前响应为空（没有版本按钮）。
- Global 多语言（`de-de`/`es-mx`/`fr-fr`/`ja-jp`/`ko-kr`/`zh-tw` 等）通过 `defaultSettings.launcherWebLang` 在 `launcherWeb.ts` 中定义，但 Provider 默认 `en-us`；其他语言通过 `providerOptions.language` 切换。
- CN 仅 `zh-cn`（`defaultSettings.launcherWebLangCN`）；其他语言通过 `providerOptions.language` 切换时由 Provider 透传给上游，未做独立验证。
- Provider 在 FastAPI 入口的 `provider_registry` 中尚未注册；`/v1/home-content` 用 `providerId=hypergryph-batch` 仍走 sample envelope。注册逻辑作为下一个迭代的样本扩展项。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-14 22:30:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `KuroLauncherProvider`（`python/home_content/providers/kuro_launcher.py`）：库洛鸣潮启动器的三层 CDN JSON 抓取，按顺序拉 `launcher-config`（`functionCode.background` 动态 hash）、`wallpapers-slogan`（MP4 + 首帧 webp + 标语 png）、`news-notices`（`guidance.{activity,notice,news}` 三个分类 + `slideshow` 轮播）。背景优先 `videoFile`，降级 `firstFrameImage`，候选 URL 通过 CDN 域名白名单（`.kurogame.com` / `.kurogames.com` / `.aki-game.net`）。Banner 来自 `slideshow[]`，News 来自 `guidance` 三个分类，跳过 `functionSwitch == 0` 的分类。
- `providerOptions` 支持覆盖 `appId` / `appKey` / `gameId` / `language`；默认常量（`50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c` / `G153` / `en`）来自 `KRApp.conf`（`base64(XOR(data, 0x63))`）解码。
- 新增脱敏样本 `contracts/samples/kuro-{launcher-config,wallpapers-slogan,news-notices}.json`：2026-09-14 真实响应的样本，`guidance` 三个分类真实样本包含 notice + news，activity 的 `functionSwitch=0` 用于验证跳过逻辑。
- 新增 Provider 文档 `docs/KURO_PROVIDER.md`：记录验证日期、采样游戏、语言、三层端点、字段映射、默认常量、白名单、回退方案与已知限制。
- 新增 `python/tests/test_kuro_launcher_provider.py`：13 项 pytest，覆盖白名单过滤、视频优先、biz 默认值、三层端点串联、`functionCode.background` 缺失抛错、HTTP 错误传播、`providerOptions` URL 覆盖。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 路由不同 URL 到不同 fixture，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `KURO_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_kuro_launcher_provider.py`：13 项全部通过，耗时 0.16s。
- `pytest python/tests/`：HoYoPlayProvider 10 项 + KuroLauncherProvider 13 项共 23 项全部通过。
- 直接 `python -m home_content.providers.kuro_launcher` 调真实接口，envelope 携带 `hw-pcdownload-qcloud.aki-game.net` MP4 视频 + WebP 海报 + 5 个 banner + 多个 notice/news 项。

### 当前限制

- Kuro 启动器当前只下发 `en.json`；请求 `zh-cn.json` 会拿到空响应。增加语言需等 Kuro 发布对应文件或本地 PlayerAgent 注入。
- 国服 / CN 入口未独立验证：`G153` 当前是 Global 端点，国服可能用不同的 `appId` / `appKey` / `gameId` / CDN。
- `slogan` PNG 标题叠加图未映射到 `HomeContent`，UI 需要时由 C# 端单独请求。
- `activity` 分类常为 `functionSwitch=0`（当前版本没有活动），Provider 自动跳过。
- MIME / MD5 / 尺寸校验未做；按 AGENTS.md 要求由 C# 端 `HttpHomeContentTransport` 拉取后的缓存层负责（待办）。

## 2026-09-14 21:30:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 实现真实 Provider `HoYoPlayJsonProvider`（`python/home_content/providers/hoyoplay_json.py`）：主端点 `https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo`（CN）/ `sg-hyp-api.hoyoverse.com`（OS），按 `providerOptions.launcher_id` + `region` 选 launcher，按 `gameBiz` 或 `game_id` 前缀（`hk4e/hkrpg/bh3/nap/hyg/abc`）选游戏，未匹配时退回 launcher 列表的第一个游戏。响应仅取背景：优先有 `video.url` 的候选，降级到 `background.url`，候选 URL 必须通过 CDN 域名白名单（`*.mihoyo.com`、`*.hoyoverse.com`、`*.yuanshen.com`、`*.bh3.com`、`*.honkaistarrail.com`、`*.zenlesszonezero.com`、`*.miyoushe.com`）。
- `models.py` 把 Pydantic 类型注解统一改写为 `Optional[X]`：`from __future__ import annotations` + `Optional[str/Optional[datetime/...] = None`，避免 Pydantic 2.x 在 Python 3.10 之前的运行时报 `str | None` 类型错。
- `server/app.py` 把 Provider 抛出的 `httpx.HTTPError` / `ValueError` / `ValidationError` 统一转换为 `HomeContentEnvelope.errors`，HTTP 状态码仍为 200；客户端通过 `errors` 决定降级策略，`NotImplementedError` 仍走 sample envelope。
- 新增脱敏样本 `contracts/samples/hoyoplay-cn-launcher-info.json`：基于 2026-09-14 真实响应（CN/zh-cn/launcher_id=`jGHBHlcOq1`），URL 中 32 字符 hex hash 替换为 `HASH32`，保留每个 biz 一条 background 用于回归。
- 新增 Provider 文档 `docs/HOYOPLAY_PROVIDER.md`：记录验证日期、区域、语言、入口、launcher_id、biz 映射、白名单策略、回退方案与已知限制。
- 新增 `python/tests/test_hoyoplay_provider.py`：10 项 pytest，覆盖白名单过滤、视频优先、biz 过滤、未知 game 退回、retcode != 0、空响应。Provider 支持 `httpx.AsyncClient` 注入，测试用 `MockTransport` 喂入脱敏样本，不访问真实网络。
- `.gitignore` 在 `docs/` 白名单里追加 `HOYOPLAY_PROVIDER.md`。

### 验证结果

- `pytest python/tests/test_hoyoplay_provider.py`：10 项全部通过，耗时 0.16s。
- HTTP 端到端 `HomeContentE2E.Tests`：12 项仍全部通过（Provider 不再退回 sample，envelope 来自真实 `hyp-api`）。
- `dotnet build HomeContentE2E.Tests.csproj -c Debug -p:Platform=x64`：0 个警告、0 个错误。
- 直接 `python -m home_content.server.main` 启动 Worker 后 `Invoke-RestMethod` 调 `/v1/home-content`，返回的 envelope 真实携带 `launcher-webstatic.mihoyo.com` 的 webm 视频和 webp 海报 URL。
- 5 个游戏（`nap_cn`/`hk4e_cn`/`hkrpg_cn`/`bh3_cn`/未知 `game_id`）的真实接口抽样表现符合预期。

### 当前限制

- 当前 Provider 仅返回背景，未补齐 banner / news（主端点不提供）；老的 `<host>/mdk/launcher/api/content` 端点按游戏单独抓样本尚未启动。
- URL 域名白名单手工维护，新增 CDN 域名需更新 `ALLOWED_HOST_SUFFIXES`。
- MIME / MD5 / 尺寸校验在 Provider 中未做；按 AGENTS.md 要求，应在 C# 端 `HttpHomeContentTransport` 拉取后由缓存层负责（待办）。
- OS 端点仅按社区维护的 launcher_id 接入，未独立采样；国际服玩家需要单独验证一次。

## 2026-09-14 21:14:00 +08:00

- 推送人员：`Violet0923`
- 目标分支：`python_preview`

### 实现内容

- 新增 Python 端 FastAPI 服务骨架：`python/home_content/server/app.py` 暴露 `GET /healthz`、`POST /v1/home-content` 和 `POST /v1/screenshot-paths`；`main.py` 提供 uvicorn 启动入口；`sample_provider.py` 在真实 Provider 落地前提供确定性示例 envelope，便于 C# 端先行联调。
- 扩展 `pyproject.toml`：加入 `fastapi`、`uvicorn[standard]`、`httpx[http2]` 三个运行时依赖；同时把 `requires-python` 调整为 `>=3.10`，并将 Pydantic 类型注解统一改写为 `Optional[X]` 形式以兼容 3.10 与 3.11，避免 PEP 604 在 3.9 之前的运行时错误。
- 新增 C# 端 `Services/Home/HttpHomeContentTransport.cs`：实现 `IHomeContentTransport`，通过 `HttpClient` 调用 Python Worker 的 `/v1/home-content`，使用既有的 `HomeContentJson` 反序列化 envelope，并在网络错误与 schema 错误时统一抛出 `HomeContentTransportException`，便于上层降级策略使用。
- 新增端到端回归测试 `Tests/HomeContentE2E.Tests`：测试程序自包含——若 `127.0.0.1:8765` 不可达则通过 `HOMECONTENT_VENV`（默认 `%TEMP%\home-content-env`）与 `HOMECONTENT_CWD`（默认仓库 `python/` 根）自动 spawn Python Worker，跑完自动清理。覆盖 schemaVersion、providerId/requestId 回传、background video/poster URL、banners/news 必填字段、未知 providerId 必须返回 400。
- `HttpHomeContentTransport` 兼容 .NET 8/10：`Accept` 头改用 `MediaTypeWithQualityHeaderValue`，避免 .NET 10 SDK 收紧的 `MediaTypeHeaderValue` 签名差异。

### 验证结果

- `HomeContentE2E.Tests`：12 项检查全部通过；测试过程中 Python Worker 子进程成功启动、关闭，无残留进程。
- Python 模块导入（`home_content.server.app`、`provider_registry`、`models`）在临时 venv 下成功。
- `dotnet build HomeContentE2E.Tests.csproj -c Debug -p:Platform=x64`：0 个警告、0 个错误。
- `git diff --check`：通过。

### 当前限制

- 真实 Provider 仍是 `PendingHomeContentProvider`，所有 adapter 落到 sample envelope；接入厂商数据时只需实现各自的 `fetch` 方法，FastAPI 路由层不再改动。
- e2e 测试当前以 `net10.0` 作为本地构建目标（环境只装了 .NET 10 SDK），主项目仍保持 `net8.0-windows10.0.19041.0`；装好 .NET 8 后应改回 `net8.0`。
- Debug 产物落主项目 `bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\HomeContentE2E.Tests\`，未按 AGENTS.md 迁移到 E 盘目录（当前环境无 E: 盘）。

## 2026-09-14 20:29:21 +08:00

- 推送人员：`Violet0923`
- 目标分支：`Refactored_Version`

### 实现内容

- 重构首页布局，使侧边栏、资讯区域和启动区域适配当前固定窗口尺寸。
- 将首页 Banner 与资讯拆分为可复用的 `HomeBannerAndNews` 控件，通过统一 `HomeContent` 模型接收数据。
- 支持动态资讯分类、分类切换、超过可视数量后的滚轮浏览，以及 Banner 自动轮播和悬停暂停。
- 增加 `PreviewHomeContentService`，在 Python 后端接入前通过同一服务边界提供预览数据。
- 将侧边栏改为深灰色，游戏列表入口改为手柄图标；游戏列表浮层显示可执行文件图标，并增加进入游戏配置的添加按钮。
- 调整游戏列表浮层的指针命中范围和关闭延时，减少鼠标从侧边栏移入列表时的闪烁。
- 首页开始游戏按钮接入 Steam 启动流程，并将开始游戏与齿轮入口组合为紧凑的统一按钮区域。
- 增加每个游戏的特殊启动参数字段，生成启动命令时将参数插入真实 EXE 与 `%command%` 之间。
- 重构游戏配置页面：合并 Steam 标识、BuildID 和 Manifest，将常用操作统一放入游戏配置卡片，并把特殊启动参数拆分为独立卡片。
- 将操作名称调整为“一键更新 Steam 配置”“打开游戏启动器”和“保存配置”，三个操作在同一行展示。
- 离开游戏配置页面或切换游戏配置时检测未保存修改，支持保存、不保存或取消切换。
- 将原 Steam 配置导航项的悬停提示改为“游戏配置”。
- 更新 Python 后端对接说明、Debug 输出目录和推送记录规则。

### 验证结果

- 主程序 Debug x64 构建通过，0 个警告、0 个错误。
- `CustomNavigation.Tests`：16 项检查全部通过。
- `HomeContentContract.Tests`：7 项检查全部通过。
- `ManifestUpdate.Tests`：14 项检查全部通过。
- `SteamAppInfo.Tests`：41 项检查全部通过。
- `BackgroundCrop.Tests`：7 项检查全部通过。
- `git diff --check`：通过。

### 当前限制

- 首页当前由 `PreviewHomeContentService` 提供预览内容，厂商数据仍需由后续 Python Provider 实现并通过既有服务接口接入。
- 未保存提示属于 WinUI 交互流程，当前通过编译和配置持久化测试验证，尚未加入自动化 UI 点击测试。
