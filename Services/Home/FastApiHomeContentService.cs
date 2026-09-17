using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// Service-layer adapter that turns the Python Worker <see cref="IHomeContentTransport"/>
/// (HTTP) into the same <see cref="IHomeContentService"/> contract that
/// <see cref="PreviewHomeContentService"/> already implements.
///
/// Behaviour:
/// <list type="bullet">
///   <item>Happy path — transport returns an envelope; envelope.Content and
///         envelope.Errors flow into <see cref="HomeContentResult"/> without
///         touching them.</item>
///   <item>Transport raises <see cref="HomeContentTransportException"/> — the
///         service returns an empty <see cref="HomeContent"/> plus a single
///         <see cref="HomeContentError"/> with code <c>transport_*</c> and
///         <see cref="HomeContentError.Recoverable"/> = true, so the caller
///         can keep the previous cached background or fall back to a static
///         placeholder without crashing the UI.</item>
///   <item>Caller-supplied <see cref="OperationCanceledException"/> — rethrown
///         so the cancellation token propagates (UI cancels the request, the
///         service should not eat it).</item>
/// </list>
///
/// Construction is intentionally tiny — the caller (a factory in
/// LauncherHomePage or a future DI container) owns the
/// <see cref="IHomeContentTransport"/> and the HttpClient lifecycle, so the
/// service can be unit-tested with a fake transport and reused against a
/// remote aggregator without modification.
/// </summary>
public sealed class FastApiHomeContentService : IHomeContentService
{
    private readonly IHomeContentTransport _transport;

    public FastApiHomeContentService(IHomeContentTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    public async Task<HomeContentResult> GetAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        HomeContentEnvelope envelope;
        try
        {
            envelope = await _transport
                .FetchAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HomeContentTransportException ex)
        {
            return BuildFailureResult(ex);
        }

        ArgumentNullException.ThrowIfNull(envelope);

        return new HomeContentResult(
            envelope.Content,
            IsStale: false,
            Errors: envelope.Errors.Count > 0 ? envelope.Errors : null);
    }

    private static HomeContentResult BuildFailureResult(HomeContentTransportException ex)
    {
        var error = new HomeContentError
        {
            Code = $"transport_{Classify(ex)}",
            Message = ex.Message,
            Recoverable = true,
        };
        return new HomeContentResult(
            Content: new HomeContent(),
            IsStale: false,
            Errors: new[] { error });
    }

    /// <summary>
    /// Pick the most informative exception name to embed in the error code.
    /// A wrapped <see cref="System.Text.Json.JsonException"/> or
    /// <see cref="HttpRequestException"/> is more useful than a flat
    /// "TransportError", so we unwrap the inner exception when present.
    /// </summary>
    private static string Classify(HomeContentTransportException ex) =>
        ex.InnerException is { } inner ? inner.GetType().Name : nameof(HomeContentTransportException);
}