# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

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

## 2026-10-14 15:36:11 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub `master`
3. 推送提交：`docs: update Calabiyau bridge to v1.3`
4. 实现内容：
   1. 将《卡拉彼丘》特殊设置说明更新至桥接器 v1.3，并替换下载链接、附件名和 SHA-256。
   2. 说明桥接器可放在游戏安装目录内任意位置，由程序自动向上识别安装根目录。
   3. 更新 SteamCN-GameLauncher 可执行文件名及手动 Steam 启动选项示例。
5. 验证结果：GitHub 与 CNB v1.3 公开附件重新下载后的 SHA-256 均为 `D1B60016B6FA490C0348D15C538A45CA22B8A0B8880787A02215340D63717EDD`；文档版本、附件名和链接一致。
6. 当前限制：v1.3 尚未完成真实标准版、腾讯版或 Steam 客户端回归；首次使用仍需通过对应渠道官方启动器成功进入游戏一次。

## 2026-10-14 00:29:47 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`；GitHub/CNB `v3.1.9` Release；官网 `Clearlove0923/clearlove0923.github.io` `main`
3. 推送提交：`release: 发布 v3.1.9 首页动画缓存修复`
4. 实现内容：
   1. 修复厂商接口暂时不下发背景时，首页资讯、Banner 与活动数据刷新覆盖旧背景缓存并导致动画消失的问题；部分成功响应现在会保留上一次已验证的背景。
   2. 更新弹窗识别 Markdown 标题，去除标题前的 `#` 标记并使用更大号粗体文本渲染；GitHub 与 CNB Release 正文继续保留标准 Markdown 标题。
   3. 仓库开发规则补充更新弹窗与 Release 标题的差异化渲染要求，四处版本号、`version.json` 和 `CHANGELOG.md` 同步升级至 `3.1.9`。
5. 验证结果：
   1. Debug x64 主程序构建成功，0 警告、0 错误；首页缓存回归测试 25 项全部通过。
   2. Release x64 自包含构建、基于 v3.1.8 publish 的 Kachina 差分生成及 Inno Setup 编译成功；发布目录未混入 `GameTime`、`HomeCache` 等用户数据，内嵌 Python 与 HoYoPlay Provider 导入验证成功。
   3. 安装包 `SteamCN-GameLauncher-v3.1.9-win-x64-setup.exe` 为 142046956 字节，SHA-256 `c6800821613bc3f2bea8182116c7c80aba801475ec1ffa4b483713bfa25e4385`；更新包 `SteamCN-GameLauncher.Update.3.1.9.exe` 为 185724807 字节，SHA-256 `7a15b9119e735c5f93f3de24e5e5a2776c9cbcd90af6ac552a380e8ebb2517f4`；`SHA256SUMS.txt` 为 216 字节，SHA-256 `285830738220c1984fe95f1e2cacb4ed51f56c54647c70f68527a030a903bab7`。
6. 当前限制：旧版本已经清除背景元数据且本地没有剩余有效缓存时，无法凭空恢复动画，需要等待厂商重新下发背景或重新建立有效缓存；应用与安装包尚未商业代码签名。
