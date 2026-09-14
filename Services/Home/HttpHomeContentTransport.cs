using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// HTTP transport for the unified home-content envelope.
///
/// Talks to the Python Worker (FastAPI / uvicorn) over HTTP. The WinUI
/// client owns the Python process lifecycle; this transport only sends
/// requests and parses responses, and never reads or interprets
/// source-specific DTOs.
///
/// Configuration (endpoint URL, timeout, Python executable, etc.) is
/// injected by the caller so the same transport works against the local
/// Worker, a remote aggregator or a test stub.
/// </summary>
public sealed class HttpHomeContentTransport : IHomeContentTransport
{
    private static readonly MediaTypeWithQualityHeaderValue JsonAccept =
        new("application/json");

    private readonly HttpClient _httpClient;
    private readonly Uri _homeContentEndpoint;

    public HttpHomeContentTransport(HttpClient httpClient, Uri homeContentEndpoint)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(homeContentEndpoint);
        _httpClient = httpClient;
        _homeContentEndpoint = homeContentEndpoint;
    }

    public async Task<HomeContentEnvelope> FetchAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _homeContentEndpoint)
        {
            Content = JsonContent.Create(request, options: HomeContentJson.Options),
        };
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.Add(JsonAccept);
        httpRequest.Headers.UserAgent.ParseAdd("SteamCN-GameLauncher/1.0");

        using var response = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        var payload = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HomeContentTransportException(
                $"Python home-content endpoint returned {(int)response.StatusCode}: {payload}");
        }

        try
        {
            return HomeContentJson.DeserializeEnvelope(payload);
        }
        catch (JsonException ex)
        {
            throw new HomeContentTransportException(
                "Python home-content endpoint returned an invalid envelope.",
                ex);
        }
    }
}

public sealed class HomeContentTransportException : Exception
{
    public HomeContentTransportException(string message) : base(message) { }
    public HomeContentTransportException(string message, Exception inner)
        : base(message, inner) { }
}