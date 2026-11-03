# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

## 2026-11-03 17:15:31 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`；覆盖 GitHub/CNB `v3.2.2` Release 附件与正文
3. 推送提交：`fix: 更新 v3.2.2 界面说明`
4. 实现内容：
   1. 首页缓存状态不再展示本机绝对目录，仅显示占用量、容量上限和保留期限；缓存错误详情改写入日志，普通界面使用面向所有用户的说明。
   2. 更新 Steam 全局配置、SteamID64、游戏配置和 Steam 启动页的提示，补充本地读取隐私说明、多账号确认逻辑，并修正把进程检测描述为已登录的不准确状态。
   3. 保持软件版本 `3.2.2` 和正式 tag `v3.2.2` 不变，在 Release“修复与优化”中追加本次 Info 文案更新说明。
5. 验证结果：Debug x64 主程序构建成功，0 错误（NuGet 漏洞数据源不可访问产生 2 个 `NU1900` 警告）；按用户要求跳过额外测试。Release x64 自包含构建、基于原 v3.2.2 publish 的 Kachina 差分及 Inno 安装包生成成功。安装包 145233200 字节、SHA-256 `e82fad699f11ee7b1b8e76eb02cf4097f4a900adbc696110690650ea18040128`；更新包 189541357 字节、SHA-256 `2366277931c79d9673d45b48a9595aa0454453d7c567be0bef4f305e15bd3b7d`。
6. 当前限制：版本号保持不变，因此已经安装 `3.2.2` 的客户端不会将本次覆盖识别为新版本；本次附件主要供后续新下载使用。应用与安装包尚未商业代码签名。

## 2026-11-03 16:42:38 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`；GitHub/CNB `v3.2.2` Release；官网 `Clearlove0923/clearlove0923.github.io` `main`
3. 推送提交：`release: 发布 v3.2.2 SteamID 自动检测`
4. 实现内容：
   1. 设置页新增 SteamID64 本地自动检测，读取 Steam `config/loginusers.vdf`；单账号自动填写保存，多账号按最近登录优先展示并要求用户确认。
   2. 账号候选展示昵称、SteamID64 和最近登录标记，保留手动填写；兼容默认路径、自定义 Steam 目录和 `steam.exe` 完整路径，并为缺失、未登录、不可读或无有效账号提供明确提示。
   3. 四处版本号、`version.json`、`CHANGELOG.md` 与正式 Release 正文同步升级为 `3.2.2`，tag 使用不带额外后缀的 `v3.2.2`。
5. 验证结果：Debug x64 主程序构建成功，0 警告、0 错误；SteamAccount 5 项、CustomNavigation 70 项、SteamAppInfo 47 项、ManifestUpdate 41 项及 UpdateService 32 项测试通过；Release x64 自包含构建、基于 v3.2.1 publish 的 Kachina 差分及 Inno 安装包生成成功。安装包 145202260 字节、SHA-256 `fce3f2abbda65f86bd267b29c005ddcc1cb06d6911117b03bb045beff0b95d9e`；更新包 189705616 字节、SHA-256 `60d1bbeea1f9dd9541909a6236d606285c984df3fe4e13eb00006fe620917cab`。
6. 当前限制：自动检测仅能读取 Steam 已写入 `loginusers.vdf` 的账号；多账号仍需用户确认，未记录账号须手动填写。NuGet 漏洞数据源在发布构建期间不可访问，但还原、构建和打包均成功；应用与安装包尚未商业代码签名。

## 2026-10-28 15:29:41 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`；GitHub/CNB `v3.2.1` Release；官网 `Clearlove0923/clearlove0923.github.io` `main`
3. 推送提交：`release: 发布 v3.2.1 Steam 信息与首页内容更新`
4. 实现内容：
   1. 发布 SteamKit 匿名直连、Steam 本机缓存与备用接口的完整版本信息选择，避免旧 BuildID/Manifest 覆盖配置；按用户反馈移除 Steam 运行时的一键更新拦截。
   2. 新增《洛克王国：世界》首页的腾讯启动器本地背景、资讯、轮播与官网回退；修复首页 Worker 启动时序和加载提示。
   3. ACF 备份期限从 30 天延长到 60 天，更新时自动清理过期备份；四处版本号、CHANGELOG、Release 正文及官网下载信息同步为 v3.2.1。
5. 验证结果：Debug x64 构建成功，0 警告、0 错误；C# 测试 SteamAppInfo 47 项、ManifestUpdate 41 项、CustomNavigation 70 项通过；Python 31 项与 35 个子测试通过；Release x64 自包含构建、基于 v3.2.0 publish 的 Kachina 差分及 Inno 安装包生成成功。安装包 145219920 字节、SHA-256 96611dca3801f9b94e137a16dcf954f8265ebac66158db9053cebc90d6424d5b；更新包 189748807 字节、SHA-256 61103a6de2aa35e7ee6c322efccbae325d43f0636759683b41ad6eb00e000e50。
6. 当前限制：Steam 直连依赖网络与公开信息范围，备用接口和本机缓存可能滞后；腾讯内部内容接口不承诺稳定 SLA；尚未重新完成真实游戏 UI 与移动网络下官网完整下载回归。应用与安装包未商业代码签名。

## 2026-10-28 15:00:08 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub `master_preview`
3. 推送提交：`feat: 完善 Steam 信息获取与首页内容，延长 ACF 备份期限`
4. 实现内容：
   1. 新增 SteamKit 匿名直连公开产品信息，统一获取 BuildID、Depot、Manifest 和启动信息；失败时明确标注第三方备用来源。
   2. 一键更新读取本机 Steam appinfo 缓存并比较完整版本快照，阻止冲突版本覆盖和 Steam 运行时写入，修复接口滞后仍提示更新成功的问题。
   3. ACF 备份期限延长至 60 天，每次成功更新（含内容未变化）自动清理当前游戏的过期备份，每个 AppID 最多保留两份。
   4. 新增腾讯《洛克王国：世界》首页 Provider、安装入口匹配、本地启动器背景与资讯轮播适配，以及官网回退。
   5. 修复首页早于 Python Worker 启动任务初始化时无法连接的问题，区分内容加载中和空资讯提示；补充固定样本、回归测试、来源说明和第三方许可文件。
5. 验证结果：Debug x64 主程序构建成功，0 警告、0 错误；SteamAppInfo 47 项、ManifestUpdate 41 项、CustomNavigation 70 项通过；Python 相关测试 31 项与 35 个子测试通过。本会话实测 Steam 直连异环返回 BuildID 26236512 与 Manifest 5084594620537324499。
6. 当前限制：本次只推送代码，不修改版本号、不发布 Release。Steam 直连依赖网络与匿名可访问范围，回退来源可能滞后；未实现 SteamDB 网页自动抓取。腾讯内部内容接口不承诺稳定 SLA；本次未重新进行完整游戏实机 UI 回归。

## 2026-10-14 23:26:36 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`；GitHub/CNB `v3.2.0` Release；官网 `Clearlove0923/clearlove0923.github.io` `main`
3. 推送提交：`merge: 同步 master 文档并完成 v3.2.0 分支发布`
4. 实现内容：
   1. 修复手动删除首页内容缓存后，厂商接口暂时未下发背景时无法重新恢复首页动画的问题。
   2. 将最后一次验证成功的背景来源保存到独立 `HomeState` 目录，按游戏、Provider、区域、语言和来源参数隔离；普通首页缓存清理与软件更新均保留该状态。
   3. 四处版本号、`version.json` 与 `CHANGELOG.md` 同步升级至 `3.2.0`，并补充独立状态目录的架构、打包和仓库规则。
5. 验证结果：
   1. Debug x64 主程序构建成功，0 警告、0 错误；首页缓存回归测试 31 项全部通过。
   2. Release x64 自包含构建、基于 v3.1.9 publish 的 Kachina 差分生成及 Inno Setup 编译成功；发布目录未混入 `HomeState`、`HomeCache`、`GameTime` 等用户数据，内嵌 Python 与 FFmpeg 导入验证成功，且未包含尚未提交的腾讯 Provider。
   3. 安装包 `SteamCN-GameLauncher-v3.2.0-win-x64-setup.exe` 为 142021686 字节，SHA-256 `30ea2eb33e21f479b32299b0edd470e71334602d952b75308d42ad311f526d9e`；更新包 `SteamCN-GameLauncher.Update.3.2.0.exe` 为 185847371 字节，SHA-256 `d65a2b085589f4cdb1ccc76e852e85fe0eca640051f613cfac7c26ac05defa79`。
6. 当前限制：厂商当前未下发背景且用户在升级前已同时删除旧背景元数据与媒体文件时，无法凭空恢复动画；应用与安装包尚未商业代码签名。
