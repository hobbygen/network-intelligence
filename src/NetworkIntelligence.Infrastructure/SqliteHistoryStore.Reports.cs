using NetworkIntelligence.Contracts;
using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Infrastructure;

public sealed partial class SqliteHistoryStore
{
    public Task<UsageReport> GetUsageReportAsync(UsageReportRange range, CancellationToken token) =>
        WithConnection(async connection =>
        {
            if (range.Days.Count is < 1 or > 366 || range.ToUtc < range.FromUtc)
                throw new ArgumentException("Invalid report range.", nameof(range));
            // One read snapshot for both data sources, even while another app instance writes to WAL.
            using var transaction = connection.BeginTransaction(deferred: true);
            var adapters = new List<(string Id, string Name)>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT Id,Name FROM Adapters a WHERE EXISTS(SELECT 1 FROM TrafficMinutes t WHERE t.AdapterId=a.Id) ORDER BY Name,Id";
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) adapters.Add((reader.GetString(0), reader.GetString(1)));
            }
            var measured = new Dictionary<(string Id, int Day), DailyAdapterUsage>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                var parameters = new List<string>();
                for (int i = 0; i < range.Days.Count; i++)
                {
                    parameters.Add($"({i},$start{i},$end{i})");
                    command.Parameters.AddWithValue($"$start{i}", range.Days[i].FromUtc.ToUnixTimeSeconds());
                    command.Parameters.AddWithValue($"$end{i}", range.Days[i].ToUtc.ToUnixTimeSeconds());
                }
                command.CommandText = $"""
                    WITH days(DayIndex,StartUtc,EndUtc) AS (VALUES {string.Join(",", parameters)})
                    SELECT t.AdapterId,d.DayIndex,SUM(t.Download),SUM(t.Upload),SUM(t.Samples),
                        SUM(t.CoveredSeconds),SUM(MIN(t.CoveredSeconds,60.0,MAX(0,d.EndUtc-t.Minute))),
                        MAX(t.CoveredSeconds > 60.01)
                    FROM days d JOIN TrafficMinutes t ON t.Minute >= d.StartUtc AND t.Minute < d.EndUtc
                    GROUP BY t.AdapterId,d.DayIndex
                    """;
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    int day = reader.GetInt32(1);
                    measured[(reader.GetString(0), day)] = new(range.Days[day], reader.GetInt64(2), reader.GetInt64(3),
                        reader.GetInt64(4), reader.GetDouble(5), Math.Clamp(reader.GetDouble(6), 0, range.Days[day].ExpectedSeconds), reader.GetInt32(7) != 0);
                }
            }
            var rows = adapters.Select(adapter => new AdapterUsageReport(adapter.Id, adapter.Name,
                range.Days.Select((day, index) => measured.GetValueOrDefault((adapter.Id, index)) ?? new(day, null, null, 0, 0, 0, false)).ToArray()))
                .OrderByDescending(adapter => (decimal)(adapter.DownloadBytes ?? 0) + (adapter.UploadBytes ?? 0)).ThenBy(adapter => adapter.AdapterName).ToArray();
            var applications = new List<ReportApplicationUsage>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                // No top-N SQL limit: exports contain the complete application breakdown for the range.
                command.CommandText = """
                    SELECT MIN(ProcessName),SUM(Received),SUM(Sent) FROM ApplicationTrafficMinutes
                    WHERE Minute >= $from AND Minute < $to
                    GROUP BY ProcessName COLLATE NOCASE ORDER BY SUM(Received)+SUM(Sent) DESC,MIN(ProcessName)
                    """;
                command.Parameters.AddWithValue("$from", range.FromUtc.ToUnixTimeSeconds());
                command.Parameters.AddWithValue("$to", range.ToUtc.ToUnixTimeSeconds());
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) applications.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
            }
            transaction.Commit();
            return new UsageReport(range, rows, applications);
        }, token);
}
