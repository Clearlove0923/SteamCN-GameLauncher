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
- 使用高清材质包：
```text
-krqlv=uhd
```
## 异环（Neverness to Everness）：
- 需要使用桥接器实现绕过官方启动器启动游戏本体
```text
桥接器仓库，仓库有多个游戏的桥接器，认准对应游戏的的安装包
https://github.com/Clearlove0923/GameLaunchBridges
使用方法：
推荐将游戏相关文件放到一个目录下（启动器文件+游戏本体文件）
将桥接器放到游戏本体的安装目录下作为启动游戏的可执行文件
```
- DX12/DX11启用方法
```text
在官方启动器中勾选DX11选项就是启用DX11，不勾选默认DX12，桥接器会沿用官方启动器设置
```
## 明日方舟终末地（ArknightsEndfield）：
- 需要使用桥接器实现绕过官方启动器启动游戏本体
```text
桥接器仓库，仓库有多个游戏的桥接器，认准对应游戏的的安装包
https://github.com/Clearlove0923/GameLaunchBridges
使用方法：
推荐将游戏相关文件放到一个目录下（启动器文件+游戏本体文件）
将桥接器放到游戏本体的安装目录下作为启动游戏的可执行文件
```
- DX12/DX11启用方法
```text
在官方启动器中勾选DX11选项就是启用DX11，不勾选默认DX12，桥接器会沿用官方启动器设置
```
## 燕云十六声（Where Winds Meet）：
- 需要使用桥接器实现绕过官方启动器启动游戏本体
```text
桥接器仓库，仓库有多个游戏的桥接器，认准对应游戏的的安装包
https://github.com/Clearlove0923/GameLaunchBridges
使用方法：
推荐将游戏相关文件放到一个目录下（启动器文件+游戏本体文件）
将桥接器放到游戏本体的安装目录下作为启动游戏的可执行文件
```
- DX12/DX11启用方法
```text
在官方启动器中勾选DX11选项就是启用DX11，不勾选默认DX12，桥接器会沿用官方启动器设置
```

- 桥接器可选参数（在Steam国服游戏启动器中的特殊启动参数中追加）：

```text
只跟踪已启动或随后由官方启动器启动的游戏，不主动直启本体。未发现游戏时默认等待最多 180 秒。
--attach-only
```
```text
设置 --attach-only 等待游戏出现的最长时间，范围 1–1800 秒。
--startup-timeout-seconds N 
```
```text
游戏进程消失后的延时退出时间，范围 0–300 秒，默认 10 秒。
--exit-delay-seconds N
```
```text
"路径"显式指定安装根目录；默认从桥接 EXE 所在目录向上查找。
--install-root 
```

```text
直启时指定本体目录；默认优先 Win64r，再尝试 Win64rh。
--variant Win64r/ --variant Win64rh 
```
```text
只输出识别到的安装目录、工作目录和将使用的启动命令，不启动游戏。
--dry-run
```
```text
出错时不显示弹窗；诊断信息仍写入日志。
--no-dialog
```

## 无限暖暖（Infinity Nikki）：
- 需要使用桥接器实现绕过官方启动器启动游戏本体
```text
桥接器仓库，仓库有多个游戏的桥接器，认准对应游戏的的安装包
https://github.com/Clearlove0923/GameLaunchBridges
使用方法：
推荐将游戏相关文件放到一个目录下（启动器文件+游戏本体文件）
将桥接器放到游戏本体的安装目录下作为启动游戏的可执行文件
```
- DX12/DX11启用方法
```text
在官方启动器中勾选DX11选项就是启用DX11，不勾选默认DX12，桥接器会沿用官方启动器设置
```

## 卡拉彼丘（Calabiyau）：
- 需要使用桥接器实现绕过官方启动器启动游戏本体
```text
桥接器仓库，仓库有多个游戏的桥接器，认准对应游戏的的安装包
https://github.com/Clearlove0923/GameLaunchBridges
使用方法：
推荐将游戏相关文件放到一个目录下（启动器文件+游戏本体文件）
将桥接器放到游戏本体的安装目录下作为启动游戏的可执行文件
```
- DX12/DX11启用方法
```text
在官方启动器中勾选DX11选项就是启用DX11，不勾选默认DX12，桥接器会沿用官方启动器设置
```



## 参考
- [Wuthering Waves - PCGamingWiki](https://www.pcgamingwiki.com/wiki/Wuthering_Waves)
- [Infinity Nikki Steam 启动配置](https://steamdb.info/app/3164330/config/)
