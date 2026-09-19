using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Infrastructure;

public sealed record TransferResult(DateTimeOffset Timestamp, string Endpoint, long DownloadBytes, long UploadBytes,
    double DownloadMbps, double UploadMbps, double HttpResponseMilliseconds, double DurationSeconds, string Detail,
    double HttpJitterMilliseconds, int LatencySamples);
public sealed record TransferProgress(string Stage, double Percent);

public sealed class TransferTest
{
    public const int DownloadSize = 25_000_000, UploadSize = 10_000_000, LatencySampleCount = 5;
    private readonly Func<HttpMessageHandler> handlerFactory;
    private readonly TimeSpan timeout;

    public TransferTest() : this(() => new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None
    }, TimeSpan.FromSeconds(60)) { }

    // Each run owns its handler/client. The factory also allows deterministic offline transport tests.
    public TransferTest(Func<HttpMessageHandler> handlerFactory, TimeSpan timeout)
    {
        this.handlerFactory = handlerFactory;
        this.timeout = timeout;
    }

    public async Task<TransferResult> RunAsync(string? endpoint, CancellationToken token, IProgress<TransferProgress>? progress = null)
    {
        string root = SpeedTestProvider.Resolve(endpoint);
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        using var client = new HttpClient(handlerFactory()) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkIntelligence/0.2");
        client.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
        var total = Stopwatch.StartNew();
        var watch = new Stopwatch();
        try
        {
            progress?.Report(new($"Connecting to {new Uri(root).Host}…", 0));
            // Warm the connection before sampling HTTP response latency; no payload is requested.
            await ProbeAsync();
            var latencies = new double[LatencySampleCount];
            for (int i = 0; i < latencies.Length; i++)
            {
                progress?.Report(new($"Measuring HTTP latency · {i + 1}/{latencies.Length}", 5 + i * 3));
                watch.Restart();
                await ProbeAsync();
                latencies[i] = watch.Elapsed.TotalMilliseconds;
            }
            double latency = latencies.Average();
            double jitter = latencies.Zip(latencies.Skip(1), (a, b) => Math.Abs(b - a)).Average();

            progress?.Report(new("Downloading · 0/25 MB", 20));
            watch.Restart();
            long downloaded = 0;
            using (var response = await client.GetAsync(root + $"/__down?bytes={DownloadSize}", HttpCompletionOption.ResponseHeadersRead, deadline.Token))
            {
                ValidateDownload(response, DownloadSize);
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                var buffer = new byte[65536];
                long nextReport = 0;
                while (downloaded < DownloadSize)
                {
                    int received = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, DownloadSize - downloaded)), deadline.Token);
                    if (received == 0) break;
                    downloaded += received;
                    if (downloaded >= nextReport)
                    {
                        progress?.Report(new($"Downloading · {downloaded / 1_000_000d:0.0}/25 MB", 20 + 55d * downloaded / DownloadSize));
                        nextReport = downloaded + 500_000;
                    }
                }
                if (downloaded != DownloadSize) throw new IOException("Provider returned an incomplete download; speed result discarded.");
            }
            double down = downloaded * 8d / Math.Max(watch.Elapsed.TotalSeconds, 0.000001) / 1_000_000;
            deadline.Token.ThrowIfCancellationRequested();
            progress?.Report(new("Uploading · 10 MB test payload; waiting for provider acknowledgement…", 75));
            var payload = GC.AllocateUninitializedArray<byte>(UploadSize);
            RandomNumberGenerator.Fill(payload);
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new("application/octet-stream");
            watch.Restart();
            using (var request = new HttpRequestMessage(HttpMethod.Post, root + "/__up") { Content = content })
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
                response.EnsureSuccessStatusCode();
            double up = UploadSize * 8d / Math.Max(watch.Elapsed.TotalSeconds, 0.000001) / 1_000_000;
            deadline.Token.ThrowIfCancellationRequested();
            progress?.Report(new("Complete", 100));
            return new(DateTimeOffset.UtcNow, root, downloaded, UploadSize, down, up, latency, total.Elapsed.TotalSeconds,
                "Single-request HTTPS throughput including request overhead; not a maximum line-speed estimate. HTTP latency and jitter include server overhead. Packet loss is unavailable. The system route (including any VPN/proxy) determines the connection, not the selected monitoring adapter. No results-logging request is sent.",
                jitter, latencies.Length);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("The speed test exceeded its time limit. Incomplete results were discarded. Try again or choose a custom provider in Settings.", ex);
        }

        async Task ProbeAsync()
        {
            using var response = await client.GetAsync(root + "/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            ValidateDownload(response, 0);
            // Finish the empty response so the connection can be reused, but never buffer a provider's error page.
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            if (await stream.ReadAsync(new byte[1], deadline.Token) != 0)
                throw new IOException("Provider did not return an empty latency response. Check the provider in Settings.");
        }
    }

    private static void ValidateDownload(HttpResponseMessage response, int expectedBytes)
    {
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
            (response.Content.Headers.ContentLength is long length && length != expectedBytes) ||
            response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json")
            throw new IOException("Provider returned an incompatible response. Choose a compatible HTTPS provider in Settings; no speed result was accepted.");
    }
}
