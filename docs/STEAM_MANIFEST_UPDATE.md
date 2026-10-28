# Steam 配置更新的数据来源与版本检查

一键更新通过 `SteamUpdateInfoResolver` 比较本机缓存与在线产品信息。在线服务 `FreshSteamAppInfoService` 优先通过 SteamKit2 3.4.0 匿名连接 Steam 网络并查询 PICS access token 和 product info；失败才调用第三方 api.steamcmd.net。普通自动获取入口也采用该服务。

每次查询创建独立会话，连接和请求共用 25 秒超时，可取消；连接中断最多重连一次，结束后断开。不要求账号密码，不读取真实硬件标识，采用进程内临时标识。SteamKit2 是社区协议库，并非 Valve 官方 SDK。只查询匿名可访问的公开信息。

页面显示来源和查询时间（接收时间，并非发布时间）。第三方与缓存来源不保证实时，直连也只代表 Steam 本次提供的数据，不承诺网络可用性或跨区域零延迟。全部版本和启动字段来自同一快照。

1. 本机解析支持 appinfo 39、40、41（魔数尾字节 0x27、0x28、0x29），包括 41 的字符串表。只读指定 AppID，不导出访问令牌、账号或完整缓存。格式参考 [SteamAppInfo 格式说明](https://github.com/xPaw/SteamAppInfo/blob/master/README.md)。
2. 每个候选必须具有有效 BuildID、游戏名、安装目录、启动项和完整的适用 Depot Manifest。候选与现有 ACF 的 AppID、已安装版本及非零 TargetBuildID 比较，冲突时排除该候选，不拼接其他来源的字段。
3. 剩余候选优先使用较大 BuildID，版本相同时优先在线查询。这是防止自动回退的保守策略，不把 BuildID 当作 SemVer；涉及分支切换或官方回退时，需要先人工核对，不能强行覆盖。
4. 本机缓存缺失、损坏或格式未知时尝试远程来源；网络失败时仍可使用完整且与本机状态一致的缓存。两者都不可用或冲突时不写入。
5. 查询结束后与写入前均检查 ACF 和 Steam 运行状态。写入要求 Steam 已完全退出，以减少 Steam 内存状态覆盖磁盘配置的风险；该检查不能阻止外部程序随后重新启动 Steam。
6. 缓存和远程接口都可能过期，检查通过只表示没有已知冲突，不能保证实时最新。未修改手工生成配置入口的行为。

## 异环回归样本

2026-10-28 本机核对 AppID 4706890、public 分支：ACF 已安装 BuildID 为 26092471，TargetBuildID 为 26236512。远程接口仍返回旧 BuildID 和 Manifest 3646058931896810596；本机缓存返回 BuildID 26236512、Depot 4706891 的 Manifest 5084594620537324499。解析和候选选择必须保留后一组完整数据。

脱敏 ACF 位于 `Tests/ManifestUpdate.Tests/Fixtures/nte-update-pending.acf`；二进制格式固定样本由 `CacheTests.cs` 中的脱敏元数据生成，覆盖三种格式、来源冲突、缺字段、损坏缓存、取消和断网回退。常规测试不访问网络或真实 Steam 目录。
