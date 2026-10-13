# 推送记录

本文件按时间倒序记录每次推送实现的功能。每次推送前在现有记录上方追加新条目。

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

## 2026-10-11 11:54:13 +08:00

1. 推送人员：`Violet0923`
2. 目标分支：GitHub/CNB `master_preview`、`master`；GitHub/CNB `v3.1.8` Release；官网 `Clearlove0923/clearlove0923.github.io` `main`
3. 推送提交：`docs: 记录 v3.1.8 正式发布验证`
4. 实现内容：
   1. GitHub 与 CNB 的 `master_preview`、`master` 和 `v3.1.8` 标签已同步到提交 `6680052`，两端 Release 均使用审定的中文有序序号发布说明。
   2. GitHub 与 CNB Release 均上传相同的 Inno Setup 安装包、Kachina 更新包和 `SHA256SUMS.txt`；CNB Release 已设为 latest，GitHub Release 已设为正式 latest 版本。
   3. 官网 `version.json` 与 `index.html` 已同步 `v3.1.8`、发布日期 `2026-10-11` 和版本化 GitHub 下载直链，官网提交为 `737c849`。
5. 验证结果：
   1. 从 GitHub 与 CNB 匿名公开地址分别重新下载三份附件，两端安装包 SHA-256 均为 `be3b3654410919887624bbe2db7761df7af70bc56ecd54d3dfbe760bcc8f21f0`，更新包均为 `a2a71d62fae61f3a4fe1cac59b6411e678334fd598c4ac5332a3fe9bd903f62e`，校验文件均为 `f5f37e44a9bf2be3cb1abd632d0b2bc96810c044ea214e954b8548787ab4fd7`。
   2. 公开官网已返回 `v3.1.8`、`2026-10-11` 和正确的版本化下载直链，首页兜底文案与 `version.json` 一致。
6. 当前限制：移动网络下的官网访问与下载体验仍需人工实测；应用与安装包尚未商业代码签名。

## 2026-10-11 11:08:55 +08:00

- 推送人员：`Violet0923`
- 目标分支：GitHub `master_preview`、`master`
- 推送提交：`merge: 同步 master 文档并完成 v3.1.8 分支发布`
- 实现内容：
  - 在 v3.1.8 发布提交上合并远端 `master` 构建期间新增的 4 个文档提交，保留《卡拉彼丘》桥接器 v1.1 下载、校验值、使用步骤及 Release tag 命名规则。
  - 合并仅涉及 `AGENTS.md`、`docs/GameSpecialSetting.md` 与 `docs/PUSH_HISTORY.md`，未改变已经构建和验证的 v3.1.8 产品代码与正式产物。
- 验证结果：逐提交检查远端新增内容并人工解决推送历史的时间顺序冲突；`git diff --check` 无空白错误，发布提交和远端文档历史均保留。
- 当前限制：应用与安装包尚未商业代码签名。

## 2026-10-11 11:07:18 +08:00

- 推送人员：`Violet0923`
- 目标分支：GitHub/CNB `master_preview`、`master`；GitHub/CNB `v3.1.8` Release；官网 `Clearlove0923/clearlove0923.github.io` `main`
- 推送提交：`release: 发布 v3.1.8 多 Depot 与卡拉彼丘首页支持`
- 实现内容：
  - 自定义 Manifest 从单 Depot 扩展为有序 Depot 集合；AppID 自动获取与一键更新会共同写入全部具有 public Manifest 的兼容 Depot，旧版单 Depot 预设自动迁移，手动修改主 Depot 或 Manifest 时明确回退为单 Depot。
  - ACF 生成拆分为独立构建器并校验空集合、无效 ID 与重复 Depot；页面新增最终写入列表，文档与固定测试同步更新。
  - 新增创梦天地 `idreamsky` Provider 与《卡拉彼丘》国服匹配配置，仅读取已验证的启动器 `CalabiYau\\ImageCache` 本地动画或海报并校验格式、大小和路径范围。
  - 修复仅包含 `LocalPath` 的首页背景缺少稳定视频身份，导致本地动画异步预处理结果被丢弃的问题；仓库规则新增发布说明必须使用有序序号的格式约束。
  - 四处版本号、`version.json` 与 `CHANGELOG.md` 同步升级为 `3.1.8`，发布说明采用用户审定的中文序号格式。
- 验证结果：
  - Debug x64 主程序构建成功，0 警告、0 错误；10 个纯 C# 回归项目共 267 项通过，C#→Python Worker 实时端到端 12 项通过。
  - Python Provider 离线测试 253 项与 32 个子测试通过，1 项按条件跳过；测试使用独立 Python 3.10 临时环境，未写入正式运行时。
  - Release x64 自包含构建、基于最终 v3.1.7 publish 的 Kachina 差分生成及 Inno Setup 编译成功；发布目录内嵌 `idreamsky` Provider 导入验证成功，主程序隐藏启动 6 秒未提前退出。
  - 安装包 `SteamCN-GameLauncher-v3.1.8-win-x64-setup.exe` 为 142049988 字节，SHA-256 `be3b3654410919887624bbe2db7761df7af70bc56ecd54d3dfbe760bcc8f21f0`；更新包 `SteamCN-GameLauncher.Update.3.1.8.exe` 为 185840860 字节，SHA-256 `a2a71d62fae61f3a4fe1cac59b6411e678334fd598c4ac5332a3fe9bd903f62e`；`SHA256SUMS.txt` 为 216 字节，SHA-256 `f5f37e44a9bf2be3cb1abd632d0b2bc96810c044ea214e954b8548787ab4fd7`。
- 当前限制：自动 Depot 集合来自公开元数据，不能证明用户账号 Package 对全部候选均有授权；私有分支与特殊安装方式仍需人工确认。《卡拉彼丘》当前只提供本机启动器缓存背景，不包含远程 Banner 与资讯。应用与安装包尚未商业代码签名。
