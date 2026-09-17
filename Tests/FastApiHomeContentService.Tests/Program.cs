using System.Net.Http;
using System.Text.Json;
using SteamCNGameLauncher.Models.Home;
using SteamCNGameLauncher.Services.Home;

// FastApiHomeContentService 单测：用 FakeHomeContentTransport 注入 IHomeContentTransport，
// 不开 HTTP、不开 Python worker。覆盖：构造器入参校验、Happy path 透传、envelope.Errors 透传、
// HomeContentTransportException（无 inner + JsonException inner + HttpRequestException inner）
// 降级为带 transport_* code 的 HomeContentResult.Errors、CancellationToken 不被吞、request
// 字段透传。
//
// Top-level statements 语法要求：所有 var/方法定义集中放在文件最顶部（包括 local function
// helpers），然后是 type declaration，type declaration 之后再不允许出现顶层语句。因此把测试
// 执行代码包到 RunAllTests() local function 里，文件最末调用一次即可。

// ===== 1. local function helpers =====

static HomeContentRequest SampleRequest(string providerId = "kuro-launcher")
{
    return new HomeContentRequest
    {
        SchemaVersion = 1,
        RequestId = "req-1",
        GameId = "wuthering-waves",
        ProviderId = providerId,
        Locale = "zh-CN",
    };
}

static HomeContentEnvelope SampleEnvelope(HomeContent? content = null, params HomeContentError[] errors)
{
    return new HomeContentEnvelope
    {
        SchemaVersion = 1,
        RequestId = "req-1",
        ProviderId = "kuro-launcher",
        FetchedAt = DateTimeOffset.UtcNow,
        Content = content ?? new HomeContent(),
        Errors = errors,
    };
}

// ===== 2. 测试执行入口 =====

var totalChecks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAILED: " + label);
    totalChecks++;
    Console.WriteLine("PASS: " + label);
}

async Task RunAllTests()
{
    // 构造器契约：null transport 必须抛 ArgumentNullException
    try
    {
        _ = new FastApiHomeContentService(null!);
        throw new Exception("FAILED: 构造器未拒绝 null transport");
    }
    catch (ArgumentNullException)
    {
        Check(true, "构造器拒绝 null transport");
    }

    // GetAsync 入参校验：null request 必须抛 ArgumentNullException
    var fakeEmpty = new FakeHomeContentTransport();
    var serviceEmpty = new FastApiHomeContentService(fakeEmpty);
    try
    {
        await serviceEmpty.GetAsync(null!);
        throw new Exception("FAILED: GetAsync 未拒绝 null request");
    }
    catch (ArgumentNullException)
    {
        Check(true, "GetAsync 拒绝 null request");
    }

    // Happy path：envelope.Content 透传；envelope.Errors 为空时不塞入 HomeContentResult.Errors
    var sampleContent = new HomeContent
    {
        Background = new HomeBackground { VideoUrl = "https://cdn.example/bg.mp4" },
        Banners = new[] { new HomeBanner { Id = "b1", Title = "banner-1" } },
        News = Array.Empty<HomeNewsItem>(),
    };
    var fakeHappy = new FakeHomeContentTransport { NextEnvelope = SampleEnvelope(sampleContent) };
    var serviceHappy = new FastApiHomeContentService(fakeHappy);

    var resultHappy = await serviceHappy.GetAsync(SampleRequest());
    Check(ReferenceEquals(resultHappy.Content, sampleContent), "Content 透传引用相等（无 clone）");
    Check(resultHappy.IsStale == false, "IsStale 默认 false");
    Check(resultHappy.Errors is null, "errors 为空时 HomeContentResult.Errors 保持 null（不浪费内存）");
    Check(fakeHappy.LastRequest is not null, "transport 收到 request");
    Check(fakeHappy.LastRequest!.ProviderId == "kuro-launcher", "request.providerId 透传");

    // Envelope.Errors 非空 → 透传到 HomeContentResult.Errors
    var errorA = new HomeContentError { Code = "provider_HTTPError", Message = "503" };
    var errorB = new HomeContentError { Code = "provider_ValidationError", Message = "bad json" };
    var fakeWithErrors = new FakeHomeContentTransport { NextEnvelope = SampleEnvelope(new HomeContent(), errorA, errorB) };
    var serviceWithErrors = new FastApiHomeContentService(fakeWithErrors);

    var resultWithErrors = await serviceWithErrors.GetAsync(SampleRequest());
    Check(resultWithErrors.Errors is not null, "errors 非空时 HomeContentResult.Errors 不为 null");
    Check(resultWithErrors.Errors!.Count == 2, "errors 数量等于 envelope 数量");
    Check(resultWithErrors.Errors![0].Code == "provider_HTTPError", "errors[0].Code 透传");
    Check(resultWithErrors.Errors![1].Recoverable == true, "errors[1].Recoverable 默认 true（从 envelope 继承）");

    // TransportException 无 inner → 返空 Content + code = transport_HomeContentTransportException
    var fakeNoH = new FakeHomeContentTransport
    {
        NextException = new HomeContentTransportException("python worker returned 500"),
    };
    var serviceNoH = new FastApiHomeContentService(fakeNoH);

    var resultNoH = await serviceNoH.GetAsync(SampleRequest());
    Check(resultNoH.Content.Background is null, "transport exception 时 Content.Background 为 null");
    Check(resultNoH.Content.Banners.Count == 0, "transport exception 时 Banners 空");
    Check(resultNoH.Errors is not null && resultNoH.Errors.Count == 1, "transport exception 时返一个 structured error");
    Check(resultNoH.Errors![0].Code == "transport_HomeContentTransportException", "无 inner 时 code 是 transport_HomeContentTransportException");
    Check(resultNoH.Errors![0].Recoverable == true, "transport exception 默认 recoverable=true");
    Check(resultNoH.Errors![0].Message.Contains("500"), "error message 包含原始异常 message");

    // TransportException with JsonException inner → code 是 transport_JsonException
    var fakeJson = new FakeHomeContentTransport
    {
        NextException = new HomeContentTransportException("bad envelope", new JsonException("trailing comma")),
    };
    var serviceJson = new FastApiHomeContentService(fakeJson);
    var resultJson = await serviceJson.GetAsync(SampleRequest());
    Check(resultJson.Errors![0].Code == "transport_JsonException", "unwrap inner 后 code 是 transport_JsonException");

    // TransportException with HttpRequestException inner → code 是 transport_HttpRequestException
    var fakeHttp = new FakeHomeContentTransport
    {
        NextException = new HomeContentTransportException("connect refused", new HttpRequestException("nope")),
    };
    var serviceHttp = new FastApiHomeContentService(fakeHttp);
    var resultHttp = await serviceHttp.GetAsync(SampleRequest());
    Check(resultHttp.Errors![0].Code == "transport_HttpRequestException", "unwrap inner 后 code 是 transport_HttpRequestException");

    // Cancellation：已 cancel 的 token → service 抛 OperationCanceledException，不吞
    var fakeSlow = new FakeHomeContentTransport
    {
        NextException = new OperationCanceledException(),
    };
    var serviceSlow = new FastApiHomeContentService(fakeSlow);

    using var alreadyCancelled = new CancellationTokenSource();
    alreadyCancelled.Cancel();

    try
    {
        await serviceSlow.GetAsync(SampleRequest(), alreadyCancelled.Token);
        throw new Exception("FAILED: cancelled token 未抛 OperationCanceledException");
    }
    catch (OperationCanceledException)
    {
        Check(true, "已 cancel 的 token 让 service 抛 OperationCanceledException");
    }

    // Cancellation propagation：transport 自己抛 OCE 时 service 不吞
    var fakeCancel = new FakeHomeContentTransport
    {
        NextException = new OperationCanceledException(),
    };
    var serviceCancel = new FastApiHomeContentService(fakeCancel);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    try
    {
        await serviceCancel.GetAsync(SampleRequest(), cts.Token);
        throw new Exception("FAILED: token cancel 后 transport 抛 OCE 时 service 不应吞");
    }
    catch (OperationCanceledException)
    {
        Check(true, "token cancel 后 transport 抛 OperationCanceledException 时 service 不吞");
    }

    // request 字段透传：service 不修改 game_id / provider_id / locale
    var fakePass = new FakeHomeContentTransport { NextEnvelope = SampleEnvelope(new HomeContent()) };
    var servicePass = new FastApiHomeContentService(fakePass);
    await servicePass.GetAsync(SampleRequest("hoyoplay-json"));

    Check(fakePass.LastRequest!.GameId == "wuthering-waves", "request.GameId 透传");
    Check(fakePass.LastRequest!.ProviderId == "hoyoplay-json", "request.ProviderId 透传");
    Check(fakePass.LastRequest!.Locale == "zh-CN", "request.Locale 透传");
}

await RunAllTests();
Console.WriteLine($"All {totalChecks} checks passed.");

// ===== 3. 测试夹具 type declaration（放在顶层语句之后）=====

sealed class FakeHomeContentTransport : IHomeContentTransport
{
    public HomeContentEnvelope? NextEnvelope { get; set; }
    public Exception? NextException { get; set; }
    public HomeContentRequest? LastRequest { get; private set; }

    public Task<HomeContentEnvelope> FetchAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (NextException is { } ex)
        {
            throw ex;
        }
        return Task.FromResult(NextEnvelope ?? new HomeContentEnvelope());
    }
}