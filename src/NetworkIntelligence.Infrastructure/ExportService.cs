using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Infrastructure;

public static partial class ExportService
{
    public static async Task WriteUsageAsync(string path, IReadOnlyList<UsageSummary> rows, bool json, CancellationToken token)
    {
        if (json)
        {
            var payload = new { SchemaVersion = 1, ExportedUtc = DateTimeOffset.UtcNow, Source = "NetworkInterface.GetIPStatistics deltas; minute aggregates", Availability = "Measured intervals only", Unit = "bytes", Rows = rows };
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }, token);
        }
        else
        {
            var buffer = new StringBuilder("AdapterId,AdapterName,DownloadBytes,UploadBytes,Samples,CoveredSeconds,FirstUtc,LastUtc,Source,Availability\r\n");
            foreach (var row in rows)
                buffer.AppendLine(string.Join(",", new[] { Csv(row.AdapterId), Csv(row.AdapterName), row.DownloadBytes.ToString(CultureInfo.InvariantCulture), row.UploadBytes.ToString(CultureInfo.InvariantCulture), row.Samples.ToString(CultureInfo.InvariantCulture), row.CoveredSeconds.ToString(CultureInfo.InvariantCulture), row.First.ToString("O"), row.Last.ToString("O"), "NetworkInterface counter deltas", "Measured intervals only" }));
            await File.WriteAllTextAsync(path, buffer.ToString(), new UTF8Encoding(true), token);
        }
    }
    public static async Task WriteApplicationUsageAsync(string path, IReadOnlyList<ApplicationUsageSummary> rows, bool json, CancellationToken token)
    {
        if (json)
        {
            var payload = new { SchemaVersion = 1, ExportedUtc = DateTimeOffset.UtcNow, Source = "MonitoringService kernel-ETW minute aggregates (docs/DECISIONS.md ADR-007/009)",
                Availability = "Measured windows only; grouped by PID+process name, not a stable app identity; not accuracy-certified beyond a narrow loopback test (docs/ETW_ACCURACY.md)", Unit = "bytes", Rows = rows };
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }, token);
        }
        else
        {
            var buffer = new StringBuilder("Pid,ProcessName,ReceivedBytes,SentBytes,Windows,FirstUtc,LastUtc,Source,Availability\r\n");
            foreach (var row in rows)
                buffer.AppendLine(string.Join(",", new[] { row.Pid.ToString(CultureInfo.InvariantCulture), Csv(row.ProcessName), row.ReceivedBytes.ToString(CultureInfo.InvariantCulture), row.SentBytes.ToString(CultureInfo.InvariantCulture), row.Windows.ToString(CultureInfo.InvariantCulture), row.First.ToString("O"), row.Last.ToString("O"), "MonitoringService kernel-ETW minute aggregates", "Measured windows only; not a stable app identity" }));
            await File.WriteAllTextAsync(path, buffer.ToString(), new UTF8Encoding(true), token);
        }
    }
    public static string Csv(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
