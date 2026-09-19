using System.Net;
using NetworkIntelligence.Contracts;
using NetworkIntelligence.Infrastructure;
namespace NetworkIntelligence.Domain.Tests;

public sealed class TransferTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyOverrideSelectsBuiltInProvider(string? endpoint) =>
        Assert.Equal("https://speed.cloudflare.com", SpeedTestProvider.Resolve(endpoint));

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://example.com?key=secret")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("not a url")]
    public async Task InvalidEndpointNeverSendsTraffic(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => (new AppSettings { SpeedTestEndpoint = endpoint }).Validate());
        var transfer = new TransferTest(() => throw new InvalidOperationException("Must not create transport"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentException>(() => transfer.RunAsync(endpoint, default));
    }

    [Fact]
    public async Task AutomaticTestUsesDocumentedRequestsAndReportsCompleteMeasurements()
    {
        var calls = new List<string>();
        var progress = new CaptureProgress();
        var transfer = Create(async (request, token) =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.AbsoluteUri);
            Assert.True(request.Headers.CacheControl!.NoStore);
            Assert.Contains(request.Headers.AcceptEncoding, h => h.Value == "identity");
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("application/octet-stream", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal(TransferTest.UploadSize, (await request.Content.ReadAsByteArrayAsync(token)).Length);
                return new(HttpStatusCode.OK);
            }
            return DownloadResponse(request);
        });
        var result = await transfer.RunAsync(null, default, progress);
        Assert.Equal(8, calls.Count); // warm-up + five latency samples + download + upload
        Assert.All(calls, call => Assert.Contains("https://speed.cloudflare.com/", call));
        Assert.Equal(TransferTest.DownloadSize, result.DownloadBytes);
        Assert.Equal(TransferTest.UploadSize, result.UploadBytes);
        Assert.Equal(5, result.LatencySamples);
        Assert.True(double.IsFinite(result.DownloadMbps) && result.DownloadMbps > 0);
        Assert.True(double.IsFinite(result.UploadMbps) && result.UploadMbps > 0);
        Assert.True(result.HttpResponseMilliseconds >= 0 && result.HttpJitterMilliseconds >= 0);
        Assert.Equal(100, progress.Values.Last().Percent);
        Assert.Equal(progress.Values.Select(p => p.Percent).Order(), progress.Values.Select(p => p.Percent));
    }

    [Fact]
    public async Task CustomBasePathIsUsedWithoutContactingDefaultProvider()
    {
        var transfer = Create((request, _) =>
        {
            Assert.StartsWith("https://example.com/test/", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(request.Method == HttpMethod.Post ? new(HttpStatusCode.OK) : DownloadResponse(request));
        });
        var result = await transfer.RunAsync(" https://example.com/test/ ", default);
        Assert.Equal("https://example.com/test", result.Endpoint);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ProviderErrorsAreNotRetriedOrReportedAsSuccess(int status)
    {
        int calls = 0;
        var progress = new CaptureProgress();
        var transfer = Create((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => transfer.RunAsync(null, default, progress));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal(1, calls);
        Assert.DoesNotContain(progress.Values, p => p.Percent == 100);
    }

    [Fact]
    public async Task TruncatedDownloadIsDiscardedBeforeUpload()
    {
        var transfer = Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var response = DownloadResponse(request);
            if (request.RequestUri!.Query != "?bytes=0")
            {
                response.Content = new ByteArrayContent(new byte[100]);
                response.Content.Headers.ContentLength = TransferTest.DownloadSize;
            }
            return Task.FromResult(response);
        });
        var error = await Assert.ThrowsAsync<IOException>(() => transfer.RunAsync(null, default));
        Assert.Contains("incomplete", error.Message);
    }

    [Fact]
    public async Task HtmlResponseCannotBeMeasuredAsTraffic()
    {
        var transfer = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("<html>Sign in</html>", System.Text.Encoding.UTF8, "text/html") }));
        await Assert.ThrowsAsync<IOException>(() => transfer.RunAsync(null, default));
    }

    [Fact]
    public async Task UploadFailureDiscardsDownloadResult()
    {
        var progress = new CaptureProgress();
        var transfer = Create((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? new(HttpStatusCode.ServiceUnavailable) : DownloadResponse(request)));
        await Assert.ThrowsAsync<HttpRequestException>(() => transfer.RunAsync(null, default, progress));
        Assert.DoesNotContain(progress.Values, p => p.Percent == 100);
    }

    [Fact]
    public async Task UserCancellationInterruptsOutstandingRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var transfer = Create(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.RunAsync(null, cancellation.Token));
    }

    [Fact]
    public async Task DeadlineIsReportedSeparatelyFromUserCancellation()
    {
        var transfer = new TransferTest(() => new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        }), TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAsync<TimeoutException>(() => transfer.RunAsync(null, default));
    }

    [Fact]
    public async Task CancellationInterruptsDownloadBodyAfterHeadersArrive()
    {
        using var cancellation = new CancellationTokenSource();
        var transfer = Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.Query == "?bytes=0") return Task.FromResult(DownloadResponse(request));
            var content = new StreamContent(new CancelOnReadStream(cancellation));
            content.Headers.ContentLength = TransferTest.DownloadSize;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.RunAsync(null, cancellation.Token));
    }

    [Fact]
    public async Task CancellationDuringUploadNeverReportsCompletion()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CaptureProgress();
        var transfer = Create(async (request, token) =>
        {
            if (request.Method != HttpMethod.Post) return DownloadResponse(request);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.RunAsync(null, cancellation.Token, progress));
        Assert.DoesNotContain(progress.Values, p => p.Percent == 100);
    }

    [Fact]
    public async Task PreCancelledTestDoesNotCreateTransport()
    {
        var transfer = new TransferTest(() => throw new InvalidOperationException("Must not create transport"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.RunAsync(null, new CancellationToken(true)));
    }

    private static TransferTest Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(() => new Handler(send), TimeSpan.FromSeconds(10));
    private static HttpResponseMessage DownloadResponse(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(new byte[request.RequestUri!.Query == "?bytes=0" ? 0 : TransferTest.DownloadSize])
    };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class CaptureProgress : IProgress<TransferProgress>
    {
        public List<TransferProgress> Values { get; } = [];
        public void Report(TransferProgress value) => Values.Add(value);
    }
    private sealed class CancelOnReadStream(CancellationTokenSource source) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            source.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
