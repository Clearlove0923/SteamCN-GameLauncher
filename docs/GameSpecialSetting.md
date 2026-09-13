# 游戏特殊启动设置

本文记录不能仅靠“选择游戏 EXE”完成的特殊启动方式，以及这些方式背后的原因。内容用于后续实现和人工测试，不随仓库分发第三方游戏文件、启动器文件或实验桥接 EXE。

> 最后核对日期：2026-09-13。游戏和启动器均可能更新；正式接入前应重新验证进程名、参数、目录结构和渠道差异。

## 绝区零（Zenless Zone Zero）：启用 DirectX 12

启动参数：

```text
-use-d3d12
```

## 鸣潮（Wuthering Waves）：强制使用 DirectX 11

### 背景

《鸣潮》PC 客户端可以使用 DirectX 11 或 DirectX 12。部分设备在 DX12 下可能遇到启动错误、驱动兼容性问题或崩溃，因此官方启动器曾提供“使用 DX11 启动”选项。命令行方式适用于需要由 Steam 重定向到真实游戏 EXE 的场景。

渲染参数必须传给真正的游戏进程 `Client-Win64-Shipping.exe`，而不是只传给外层启动器。

常见路径：

- 官方启动器版：`Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe`
- WeGame 版：`Client\Binaries\Win64\Client-Win64-Shipping.exe`

### 方法

DX11 参数首选：

```text
-dx11
```

也可以使用 Unreal Engine 的同义参数：

```text
-d3d11
```

二者选择一个即可，不要同时传入，也不要与 `-dx12` 或 `-d3d12` 混用。多个渲染器参数同时存在时，客户端可能因参数互斥而拒绝启动。

Steam 启动选项示例：

```text
"<Client-Win64-Shipping.exe 的完整路径>" -dx11 %command%
```

如果官方启动器当前仍显示“使用 DX11 启动”，直接使用该选项更简单。切换渲染器后可能重新编译着色器，初次进入游戏出现短时卡顿并不一定表示参数失效。

跳过库洛图标等开场展示、直接进入漂泊者选择界面的参数：

```text
-SkipSplash
```

参数可以组合，例如：

```text
"<Client-Win64-Shipping.exe 的完整路径>" -dx11 -SkipSplash %command%
```

### 实现注意事项

- 启动命令生成器不应默认替所有用户强制 DX11，应提供可选参数。
- 生成命令时应检测并拒绝互相冲突的 DX11/DX12 参数。
- 官方版与 WeGame 版目录结构不同，必须从用户选择的安装来源动态解析，不能写死盘符。
- 通过 Steam 显示“游戏中”还要求 Steam 按对应 AppID 发起启动；直接 `Process.Start` 游戏 EXE 只能启动游戏，不能保证 Steam 客户端建立 AppID 跟踪。

## 异环（Neverness to Everness）：启动器静默自动启动

跳过启动器前台显示并在后台自动启动游戏的参数：

```text
/autoplay
```

## 无限暖暖（Infinity Nikki）：跳过启动器界面并保持 Steam“游戏中”

### 背景

Windows 版《无限暖暖》不是单一 EXE 启动。当前观察到的官方启动链为：

```text
launcher.exe
  └─ <版本目录>\xstarter.exe
       └─ InfinityNikki.exe
            └─ X6Game-Win64-Shipping.exe
```

直接运行 `InfinityNikki.exe` 或 `X6Game-Win64-Shipping.exe` 通常不可作为可靠方案：游戏可能要求启动器生成的令牌，并依赖官方链路初始化 ACE 反作弊。

`xstarter.exe` 支持 `-skiplauncher`，可以跳过第一层启动器界面并继续使用官方后端启动链。但 `xstarter.exe` 在拉起游戏后会很快退出。如果 Steam 直接跟踪它，Steam 会误以为游戏已经结束，于是“游戏中”状态只短暂出现。

此外，`xstarter.exe` 和游戏可能经过 UAC 提权后进入新的进程树，Steam不一定能自动把后续 `X6Game-Win64-Shipping.exe` 继续归到最初的启动会话。

### 跳过启动器界面

基础命令：

```text
"<启动器根目录>\<当前版本>\xstarter.exe" -skiplauncher
```

必须满足以下条件：

- `<当前版本>` 不能写死，应枚举启动器根目录下形如 `x.y.z` 或 `x.y.z.w` 的目录，并选择实际包含 `xstarter.exe` 的最高版本。
- Working Directory 必须是启动器根目录，而不是版本目录。
- 当前链路通常需要 `runas` 提权，因此仍可能出现 UAC；“跳过启动器”指不显示启动器主界面，不代表完全无提示或绕过反作弊。
- 不应读取、复制或持久化启动器令牌，也不应修改游戏和反作弊文件。

Steam为 Linux/Steam Deck 登记过直接运行 `InfinityNikki.exe -SkipLauncherTokenCheck_SD` 的启动项，但 `_SD` 参数不是当前 Windows 正式启动项，不应未经实测用于 Windows 或国服渠道。

### 保持 Steam“游戏中”

解决 `xstarter.exe` 提前退出的方法是让 Steam直接启动一个独立的保活桥接进程：

```text
Steam（按 AppID 启动）
  └─ SteamLaunchBridge.exe
       ├─ 启动 xstarter.exe -skiplauncher
       ├─ 等待 X6Game-Win64-Shipping.exe 出现
       └─ 游戏退出后才结束自身
```

桥接进程本身不伪造 Steam 在线状态。它只是作为 Steam直接启动且生命周期稳定的进程存在，使 Steam可以自然地保持对应 AppID 的运行状态和时长统计。

推荐流程：

1. Steam直接启动桥接 EXE，而不是直接启动 `xstarter.exe`。
2. 桥接器启动前记录已有的目标游戏 PID；若游戏已经运行，拒绝错误附加到旧会话。
3. 动态发现最高版本的 `xstarter.exe`。
4. 使用 `-skiplauncher`、启动器根 Working Directory 和需要的 UAC 提权启动官方后端。
5. 不等待短命的 `xstarter.exe`，改为在限定时间内轮询新的 `X6Game-Win64-Shipping.exe`。
6. 发现游戏进程后，桥接器保持运行。
7. 游戏进程消失后等待数秒防抖；确认没有重启或切换进程，再让桥接器退出。
8. 启动失败、UAC取消或等待超时时写入本地日志，并结束 Steam会话。

桥接器应采用通用配置，而不是把《无限暖暖》写死在程序中。最低配置项包括：

```text
启动程序发现规则
启动参数
工作目录
是否提权
目标进程名列表
启动等待超时
轮询间隔
退出防抖时间
已运行进程处理策略
日志路径
```

若提供《无限暖暖》的零配置预设，可以允许用户把桥接 EXE 放入安装目录后直接由 Steam导入。自动发现应按以下顺序进行，避免低效且风险较高的全盘扫描：

1. 桥接 EXE 所在目录。
2. 安装目录的有限层级父目录和相邻启动器目录。
3. 《无限暖暖》对应的卸载注册表项。
4. 官方启动器常见安装位置。
5. 自动发现失败时要求用户明确选择启动器根目录，不猜测任意 `xstarter.exe`。

Steam作为非 Steam 游戏直接导入零配置桥接器时，桥接器应把“无启动参数”解释为使用自动发现预设。否则桥接器会因缺少参数立即退出，表现就是 Steam只在极短时间内显示“游戏中”。

### 能力边界

- 保活桥接可以解决 Steam运行状态过早结束的问题，但不能单独保证 Steam Overlay、成就或商店接口注入到提权后的真实游戏进程。
- 进程名、版本目录和反作弊行为可能随更新改变，必须保留日志和超时，不能无限等待。
- 自动测试只能验证发现规则和进程保活逻辑；当前方案仍需在安装了游戏、Steam和 ACE 的真实环境中验证完整“游戏中”生命周期。
- 仓库只维护方法、配置契约和必要源码时，不能提交或分发官方 `launcher.exe`、`xstarter.exe`、游戏本体、反作弊组件或账号令牌。

## 参考

- [Wuthering Waves - PCGamingWiki](https://www.pcgamingwiki.com/wiki/Wuthering_Waves)
- [Infinity Nikki Steam 启动配置](https://steamdb.info/app/3164330/config/)
