using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
namespace NetworkIntelligence.Infrastructure;

public sealed record TransferResult(DateTimeOffset Timestamp, string Endpoint, long DownloadBytes, long UploadBytes,
    double DownloadMbps, double UploadMbps, double HttpResponseMilliseconds, double DurationSeconds, string Detail);
public sealed class TransferTest
{
    public async Task<TransferResult> RunAsync(string endpoint, CancellationToken token)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Use an HTTPS provider base URL without credentials, a query or fragment.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkIntelligence/0.2");
        client.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
        string root = endpoint.TrimEnd('/');
        var total = Stopwatch.StartNew();
        var watch = Stopwatch.StartNew();
        using (var response = await client.GetAsync(root + "/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, deadline.Token)) response.EnsureSuccessStatusCode();
        var responseTime = watch.Elapsed.TotalMilliseconds;
        const int downloadSize = 25_000_000, uploadSize = 10_000_000;
        watch.Restart(); long downloaded = 0;
        using (var response = await client.GetAsync(root + $"/__down?bytes={downloadSize}", HttpCompletionOption.ResponseHeadersRead, deadline.Token))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); var buffer = new byte[65536];
            while (downloaded < downloadSize)
            {
                int received = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, downloadSize - downloaded)), deadline.Token);
                if (received == 0) break; downloaded += received;
            }
            if (downloaded != downloadSize) throw new IOException("Provider returned an incomplete download; speed result discarded.");
        }
        double down = downloaded * 8d / watch.Elapsed.TotalSeconds / 1_000_000;
        var payload = GC.AllocateUninitializedArray<byte>(uploadSize); RandomNumberGenerator.Fill(payload);
        using var content = new ByteArrayContent(payload); content.Headers.ContentType = new("application/octet-stream");
        watch.Restart();
        using (var request = new HttpRequestMessage(HttpMethod.Post, root + "/__up") { Content = content })
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)) response.EnsureSuccessStatusCode();
        double up = uploadSize * 8d / watch.Elapsed.TotalSeconds / 1_000_000;
        return new(DateTimeOffset.UtcNow, endpoint, downloaded, uploadSize, down, up, responseTime, total.Elapsed.TotalSeconds,
            "Measured single-request HTTPS transfer throughput, including request overhead. Not a maximum line-speed estimate. HTTP response time includes connection/server overhead; jitter and packet loss unavailable. Provider chooses its serving edge. No results are sent to a results-logging endpoint.");
    }
}
