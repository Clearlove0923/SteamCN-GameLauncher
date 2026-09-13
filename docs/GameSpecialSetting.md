# 游戏特殊启动设置
## 绝区零（Zenless Zone Zero）：
启用 DirectX 12
启动参数：
```text
-use-d3d12
```
## 鸣潮（Wuthering Waves）：
- 强制使用 DirectX 11
### 背景
《鸣潮》PC 客户端可以使用 DirectX 11 或 DirectX 12。部分设备在 DX12 下可能遇到启动错误、驱动兼容性问题或崩溃，因此官方启动器曾提供“使用 DX11 启动”选项。但是官方启动器勾选之后通过Steam启动国服并不会使用DX11，需要加启动参数
常见路径：
- 官方启动器版：`Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe`
- WeGame 版：`Client\Binaries\Win64\Client-Win64-Shipping.exe`
### 方法
```text
-d3d11
```
Steam 启动选项示例：
```text
"<Client-Win64-Shipping.exe 的完整路径>" -dx11 %command%
```
- 跳过库洛图标等开场展示、直接进入漂泊者选择界面的参数：
```text
-SkipSplash
```

## 异环（Neverness to Everness）：
- 启动器静默自动启动，跳过启动器前台显示并在后台自动启动游戏的参数：
```text
/autoplay
```
## 无限暖暖（Infinity Nikki）：
- 跳过启动器界面并保持 Steam“游戏中”
### 背景
Windows 版《无限暖暖》不是单一 EXE 启动。当前观察到的官方启动链为：
```text
launcher.exe
  └─ <版本目录>\xstarter.exe
       └─ InfinityNikki.exe
            └─ X6Game-Win64-Shipping.exe
```
直接运行 `InfinityNikki.exe` 或 `X6Game-Win64-Shipping.exe` 通常不可作为可靠方案：游戏可能要求启动器生成的令牌，并依赖官方链路初始化 ACE 反作弊。
`xstarter.exe` 支持 `-skiplauncher`，可以跳过第一层启动器界面并继续使用官方后端启动链。
但 `xstarter.exe` 在拉起游戏后会很快退出。如果 Steam 直接跟踪它，Steam 会误以为游戏已经结束，于是“游戏中”状态只短暂出现。
因此需要一个桥接保活进程让Steam一直处于游戏中状态
### 跳过启动器界面
基础命令：
```text
"<启动器根目录>\<当前版本>\xstarter.exe" -skiplauncher
```
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
2. 动态发现最高版本的 `xstarter.exe`。
3. 使用 `-skiplauncher`、启动器根 Working Directory 和需要的 UAC 提权启动官方后端。
5. 不等待短命的 `xstarter.exe`，改为在限定时间内轮询新的 `X6Game-Win64-Shipping.exe`。
6. 发现游戏进程后，桥接器保持运行。
7. 游戏进程消失后等待数秒防抖，确认没有重启或切换进程，再让桥接器退出。
8. 启动失败、UAC取消或等待超时时写入本地日志，并结束 Steam会话。
若提供《无限暖暖》的零配置预设，可以允许用户把桥接 EXE 放入安装目录后直接由 Steam导入。自动发现应按以下顺序进行，避免低效且风险较高的全盘扫描：
1. 桥接 EXE 所在目录。
2. 安装目录的有限层级父目录和相邻启动器目录。
3. 《无限暖暖》对应的卸载注册表项。
4. 官方启动器常见安装位置。
5. 自动发现失败时要求用户明确选择启动器根目录，不猜测任意 `xstarter.exe`。
## 参考
- [Wuthering Waves - PCGamingWiki](https://www.pcgamingwiki.com/wiki/Wuthering_Waves)
- [Infinity Nikki Steam 启动配置](https://steamdb.info/app/3164330/config/)
