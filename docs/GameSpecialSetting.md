# 游戏特殊启动设置
## 绝区零（Zenless Zone Zero）：
启用 DirectX 12
启动参数：
```text
-use-d3d12
```
## 鸣潮（Wuthering Waves）：
- 强制使用 DirectX 11
```text
-d3d11
```
- 跳过库洛图标等开场展示、直接进入漂泊者选择界面的参数：
```text
-SkipSplash
```
## 异环（Neverness to Everness）：
- 启动器静默自动启动，跳过启动器前台显示并在后台自动启动游戏：
```text
/autoplay
```
## 燕云十六声（Where Winds Meet）：
### 使用方法
1. 更新官方启动器和游戏本体
2. 将 `WhereWindsMeetDirect.exe` 复制到《燕云十六声》安装根目录，即与 `yysls_medium`、`Win32` 同级的目录。不要覆盖 `launcher.exe` 或 `yysls.exe`。
3. 双击桥接 EXE

### 可选参数（在终端、快捷方式目标或相应启动配置中追加）：
只跟踪已启动或随后由官方启动器启动的游戏，不主动直启本体。未发现游戏时默认等待最多 180 秒。
```text
--attach-only
```
设置 --attach-only 等待游戏出现的最长时间，范围 1–1800 秒。
```text
--startup-timeout-seconds N 
```
游戏进程消失后的延时退出时间，范围 0–300 秒，默认 10 秒。
```text
--exit-delay-seconds N
```
"路径"显式指定安装根目录；默认从桥接 EXE 所在目录向上查找。
```text
--install-root 
```
直启时指定本体目录；默认优先 Win64r，再尝试 Win64rh。
```text
--variant Win64r/ --variant Win64rh 
```
只输出识别到的安装目录、工作目录和将使用的启动命令，不启动游戏。
```text
--dry-run
```
出错时不显示弹窗；诊断信息仍写入日志。
```text
--no-dialog
```
### 实现原理
- 桥接程序从自身目录或 `--install-root` 指定目录向上寻找同时含有 `yysls_medium` 和 `Win32\deploy` 的安装根目录。它只识别该根目录下 `yysls_medium\Engine\Binaries\Win64r\yysls.exe` 或 `Win64rh\yysls.exe`。
- 若游戏未运行且未设置 `--attach-only`，使用游戏 EXE 所在目录作为工作目录，以管理员权限尝试启动本体，参数为 `--launch-type=launcher`。该参数来自捕获的一次官方启动器启动命令，并非网易承诺长期兼容的公开接口。
- 每 500 毫秒检查一次 `yysls.exe`，并核实进程完整路径属于当前安装目录；同名但来自其他目录的进程不会被当作目标。已经运行的游戏不会被重复启动。
- 检测到目标游戏退出后，等待设定的延时时间再结束桥接进程。
### 游戏更新后何时需要维护
普通资源或本体版本更新不一定影响桥接程序；它不绑定 `yysls.exe` 的版本号或文件哈希。
但以下变化需要重新检查，必要时修改并重新发布桥接 EXE：
1. 安装根目录结构、`yysls.exe` 名称或 `Win64r` / `Win64rh` 路径变化：更新路径发现和进程匹配规则。
2. 官方启动器改用新的命令行参数、工作目录、环境变量、登录令牌或反作弊初始化流程：重新捕获官方启动命令；直启可能不再可用，此时优先使用 `--attach-only` 配合官方启动器。
3. 游戏启动时出现短暂的中间进程、多个本体进程或进程名变化：重新验证进程监控与退出防抖逻辑。
4. 游戏更新清理了安装目录中的非官方文件：检查桥接 EXE 是否仍在原位，必要时从保留的构建文件重新复制；不要覆盖官方文件。
5. 游戏 EXE 的图标更新：当前桥接 EXE 内嵌的是制作时提取的图标，不会自动同步，需要重新提取图标并构建。
每次大版本更新后，应先让官方启动器完成更新并确认其能正常启动游戏，再测试桥接 EXE 的直启或附着、游戏退出后的延时退出，以及（如使用）Steam 跟踪状态。

## 无限暖暖（Infinity Nikki）：
### 使用方法
- 将InfinityNikkiLaunchBridge.exe放在游戏安装目录下作为可执行文件去运行
### 跳过启动器界面
基础命令：
```text
-skiplauncher
```
### 实现原理
Windows 版《无限暖暖》不是单一 EXE 启动。当前观察到的官方启动链为：
```text
launcher.exe
  └─ <版本目录>\xstarter.exe
       └─ InfinityNikki.exe
            └─ X6Game-Win64-Shipping.exe
```
直接运行 `InfinityNikki.exe` 或 `X6Game-Win64-Shipping.exe` 通常不可作为可靠方案：游戏可能要求启动器生成的令牌，并依赖官方链路初始化 ACE 反作弊。
`xstarter.exe` 支持 `-skiplauncher`，可以跳过第一层启动器界面并继续使用官方后端启动链。
解决 `xstarter.exe` 提前退出的方法是让 Steam直接启动一个独立的保活桥接进程：
```text
Steam（按 AppID 启动）
  └─ SteamLaunchBridge.exe
       ├─ 启动 xstarter.exe -skiplauncher
       ├─ 等待 X6Game-Win64-Shipping.exe 出现
       └─ 游戏退出后才结束自身
```
桥接进程本身不伪造 Steam 在线状态。它只是作为 Steam直接启动且生命周期稳定的进程存在，使 Steam可以自然地保持对应 AppID 的运行状态和时长统计。



## 参考
- [Wuthering Waves - PCGamingWiki](https://www.pcgamingwiki.com/wiki/Wuthering_Waves)
- [Infinity Nikki Steam 启动配置](https://steamdb.info/app/3164330/config/)
