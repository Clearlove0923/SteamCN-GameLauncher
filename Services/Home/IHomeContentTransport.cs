using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>一种 Python 传输方式，可由 HTTP 服务、本机 Worker 或测试替身实现。</summary>
public interface IHomeContentTransport
{
    Task<HomeContentEnvelope> FetchAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default);
}
