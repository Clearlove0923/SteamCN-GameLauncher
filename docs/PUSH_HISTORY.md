# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

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
