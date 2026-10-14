# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

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

## 2026-10-13 20:27:08 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub `master`
3. 推送提交：`docs: update Calabiyau bridge to v1.2`
4. 实现内容：
   1. 将《卡拉彼丘》特殊设置说明更新至桥接器 v1.2，并替换下载链接、附件名和 SHA-256。
   2. 说明 v1.2 为 Windows x64 原生单文件，可直接复制运行，无需安装 .NET、重命名或创建配置。
   3. 更新 SteamCN-GameLauncher 可执行文件名及手动 Steam 启动选项示例。
5. 验证结果：文档中的版本、附件名、下载标签和 SHA-256 与 GitHub、CNB v1.2 公开发布附件一致；两端附件下载校验通过。
6. 当前限制：v1.2 尚未重新完成实机回归；首次使用仍需通过对应渠道官方启动器成功进入游戏一次。

## 2026-10-11 12:25:39 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`
3. 推送提交：`docs: 限制推送记录为最近五次`
4. 实现内容：
   1. 仓库规则新增推送历史保留上限：每次追加本次记录后，`docs/PUSH_HISTORY.md` 仅保留按带时区时间戳倒序排列的最近 5 次推送记录。
   2. 明确同一次推送只计一条，禁止通过拆分文件、另建归档或移动旧记录规避清理要求。
   3. 本次已按新规则删除第 6 次及更早的历史记录。
5. 验证结果：`docs/PUSH_HISTORY.md` 共保留 5 个时间戳记录区块；`git diff --check` 无空白错误。
6. 当前限制：无。
