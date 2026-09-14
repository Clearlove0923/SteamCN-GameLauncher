# Python 后端接入指南

本文面向首页内容 Python 后端开发者，说明 C# 客户端已经固定的边界、源码位置和验证方式。完整字段说明见 [HOMEPAGE_CONTENT_ARCHITECTURE.md](HOMEPAGE_CONTENT_ARCHITECTURE.md)。

## 当前状态

- C# DTO、服务接口、传输接口和 JSON 解析入口已经建立。
- Python Pydantic 模型、Provider 基类、七个 Provider 类和注册表已经建立。
- Provider 类目前是接口骨架，尚未实现厂商请求与解析。
- 首页资讯栏已通过 `HomeBannerAndNews` 消费统一 DTO，并由 `IHomeContentService` 提供数据。当前使用
  `PreviewHomeContentService` 显示接入前的占位内容；接入 Python 后替换服务实现即可，无需修改页面控件。
- 截图目录使用独立接口，不属于 `HomeContent`。

## 固定首页模型

所有 Provider 必须返回同一个 `HomeContent`：

```text
HomeContent
├─ Background
│  ├─ VideoUrl
│  ├─ ImageUrl
│  └─ LocalPath
├─ Banners[]
├─ News[]
└─ UpdateInfo
```

对应源码：

- C#：`Models/Home/HomeContentContracts.cs`
- Python：`python/home_content/models.py`
- JSON Schema：`contracts/home-content-v1.schema.json`
- 固定样本：`contracts/samples/home-content-v1.json`

JSON 必须为 UTF-8，属性使用 camelCase，时间使用 ISO 8601 UTC。响应信封必须包含 `schemaVersion`、`requestId`、`providerId`、`fetchedAt`、`content` 和 `errors`。

## C# 调用边界

页面和 ViewModel 只调用：

```csharp
public interface IHomeContentService
{
    Task<HomeContentResult> GetAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default);
}
```

缓存服务再调用：

```csharp
public interface IHomeContentTransport
{
    Task<HomeContentEnvelope> FetchAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default);
}
```

后续可以分别实现 HTTP 聚合服务传输和本机 Python Worker 传输。Python 执行方式、地址和超时不能写进页面 code-behind。

## Provider 结构

Provider 注册表位于 `python/home_content/provider_registry.py`，当前稳定 ID 为：

| Provider 类 | providerId |
|---|---|
| `HoYoPlayJsonProvider` | `hoyoplay-json` |
| `KuroLauncherProvider` | `kuro-launcher` |
| `HypergryphBatchProvider` | `hypergryph-batch` |
| `PerfectWorldHybridProvider` | `perfect-world-hybrid` |
| `NextJsDataProvider` | `nextjs-data` |
| `NetEaseStaticCmsProvider` | `netease-static-cms` |
| `LocalLauncherAssetProvider` | `local-launcher-asset` |

实现 Provider 时继承 `HomeContentProvider` 并实现：

```python
async def fetch(self, request: HomeContentRequest) -> HomeContent:
    ...
```

Provider 负责把来源数据转换为统一模型。C# 不接收厂商原始响应，也不感知 Provider 类名。

## 动画与资讯链接

- 动画地址写入 `content.background.videoUrl`。
- 视频首帧或静态回退图写入 `content.background.imageUrl`。
- 必须读取本地启动器资源时写入 `content.background.localPath`。
- Banner 点击地址写入 `banners[].targetUrl`。
- 资讯点击地址写入 `news[].targetUrl`。
- 无链接的 Banner 或资讯返回 `null`，不得编造地址。
- Python 负责 URL 归一化；交给客户端的远程链接应为经过验证的 `https` 绝对地址。

## 游戏截图路径

截图路径由 `python/home_content/screenshot_paths.py` 集中写死，通过独立的 `GameScreenshotPathEnvelope` 返回。不要把 `screenshotPath` 加入 `HomeContent`，也不要把每个游戏的路径写到 C# 页面。

当前路径模板使用：

```text
{steamInstallPath}\userdata\{steamId}\760\remote\{appId}\screenshots
```

Python 只返回固定模板；C# 使用本机 Steam 设置展开 `{steamInstallPath}` 和 `{steamId}`，随后规范化并验证路径。未知 `gameId` 返回 `null`。

对应源码：

- Python 注册表：`python/home_content/screenshot_paths.py`
- C# 契约：`Models/Home/GameScreenshotPathContracts.cs`
- C# 服务：`Services/Home/IGameScreenshotPathService.cs`
- JSON Schema：`contracts/game-screenshot-path-v1.schema.json`
- 固定样本：`contracts/samples/game-screenshot-path-v1.json`

## 本地验证

在仓库根目录执行：

```powershell
dotnet build Tests\HomeContentContract.Tests\HomeContentContract.Tests.csproj --configuration Debug -p:OutDir=..\..\bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\HomeContentContract\
dotnet bin\x64\Debug\net8.0-windows10.0.19041.0\Tests\HomeContentContract\HomeContentContract.Tests.dll
python -m compileall -q python\home_content
```

契约字段发生变化时，必须同步更新 C# DTO、Pydantic 模型、JSON Schema、固定样本和契约测试。
