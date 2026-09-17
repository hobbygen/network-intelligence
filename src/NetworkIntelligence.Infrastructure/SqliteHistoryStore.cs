using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Infrastructure;

public sealed class SqliteHistoryStore(string path) : IHistoryStore
{
    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5 }.ToString();
    private readonly SemaphoreSlim gate = new(1, 1);
    private async Task<T> WithConnection<T>(Func<SqliteConnection, Task<T>> action, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(token);
            return await action(connection);
        }
        finally { gate.Release(); }
    }
    private static async Task Execute(SqliteConnection connection, string sql, CancellationToken token, params (string Key, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }
    public Task InitializeAsync(CancellationToken token) => WithConnection(async connection =>
    {
        await Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;", token);
        await Execute(connection, "CREATE TABLE IF NOT EXISTS SchemaMigrations(Version INTEGER PRIMARY KEY, AppliedUtc INTEGER NOT NULL);", token);
        await using var check = connection.CreateCommand(); check.CommandText = "SELECT COALESCE(MAX(Version),0) FROM SchemaMigrations";
        var version = Convert.ToInt32(await check.ExecuteScalarAsync(token));
        if (version > 3) throw new InvalidOperationException("Database version is newer than this application. Use a newer application build.");
        if (version < 1)
        {
            using var transaction = connection.BeginTransaction();
            await using var migration = connection.CreateCommand(); migration.Transaction = transaction;
            migration.CommandText = """
                CREATE TABLE Settings(Key TEXT PRIMARY KEY, Json TEXT NOT NULL);
                CREATE TABLE Adapters(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Type TEXT NOT NULL, LastSeen INTEGER NOT NULL);
                CREATE TABLE TrafficMinutes(AdapterId TEXT NOT NULL, Minute INTEGER NOT NULL, Download INTEGER NOT NULL, Upload INTEGER NOT NULL,
                    CoveredSeconds REAL NOT NULL, Samples INTEGER NOT NULL, PRIMARY KEY(AdapterId,Minute));
                CREATE INDEX IX_TrafficMinutes_Time ON TrafficMinutes(Minute);
                CREATE TABLE ConnectionEvents(Id INTEGER PRIMARY KEY, Time INTEGER NOT NULL, AdapterId TEXT NOT NULL, AdapterName TEXT NOT NULL, Previous TEXT NOT NULL, State TEXT NOT NULL);
                CREATE INDEX IX_ConnectionEvents_Time ON ConnectionEvents(Time);
                CREATE TABLE Diagnostics(Id INTEGER PRIMARY KEY, Time INTEGER NOT NULL, Json TEXT NOT NULL);
                CREATE INDEX IX_Diagnostics_Time ON Diagnostics(Time);
                INSERT INTO SchemaMigrations VALUES(1,unixepoch());
                """;
            await migration.ExecuteNonQueryAsync(token); transaction.Commit();
        }
        if (version < 2)
        {
            using var transaction = connection.BeginTransaction();
            await using var migration = connection.CreateCommand(); migration.Transaction = transaction;
            migration.CommandText = """
                CREATE TABLE ApplicationTrafficMinutes(Pid INTEGER NOT NULL, ProcessName TEXT NOT NULL, Minute INTEGER NOT NULL,
                    Received INTEGER NOT NULL, Sent INTEGER NOT NULL, Events INTEGER NOT NULL, Windows INTEGER NOT NULL,
                    PRIMARY KEY(Pid,ProcessName,Minute));
                CREATE INDEX IX_ApplicationTrafficMinutes_Time ON ApplicationTrafficMinutes(Minute);
                INSERT INTO SchemaMigrations VALUES(2,unixepoch());
                """;
            await migration.ExecuteNonQueryAsync(token); transaction.Commit();
        }
        if (version < 3)
        {
            using var transaction = connection.BeginTransaction();
            await using var migration = connection.CreateCommand(); migration.Transaction = transaction;
            migration.CommandText = """
                CREATE TABLE AnomalyEvents(Id INTEGER PRIMARY KEY, Time INTEGER NOT NULL, ProcessName TEXT NOT NULL,
                    Direction TEXT NOT NULL, Severity TEXT NOT NULL, CurrentRate REAL NOT NULL, BaselineMean REAL NOT NULL,
                    Deviation REAL NOT NULL, Explanation TEXT NOT NULL, Notified INTEGER NOT NULL);
                CREATE INDEX IX_AnomalyEvents_Time ON AnomalyEvents(Time);
                INSERT INTO SchemaMigrations VALUES(3,unixepoch());
                """;
            await migration.ExecuteNonQueryAsync(token); transaction.Commit();
        }
        return 0;
    }, token);
    public Task<AppSettings> LoadSettingsAsync(CancellationToken token) => WithConnection(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Json FROM Settings WHERE Key='app'";
        var json = await command.ExecuteScalarAsync(token) as string;
        var settings = json is null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        settings.Validate(); return settings;
    }, token);
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken token)
    {
        settings.Validate();
        return WithConnection(async connection => { await Execute(connection, "INSERT INTO Settings VALUES('app',$json) ON CONFLICT(Key) DO UPDATE SET Json=excluded.Json", token, ("$json", JsonSerializer.Serialize(settings))); return 0; }, token);
    }
    public Task SaveAsync(MonitoringSnapshot snapshot, IReadOnlyList<ConnectionEvent> events, CancellationToken token) => WithConnection(async connection =>
    {
        using var transaction = connection.BeginTransaction();
        foreach (var adapter in snapshot.Adapters)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO Adapters VALUES($id,$name,$type,$time) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Type=excluded.Type, LastSeen=excluded.LastSeen";
            command.Parameters.AddWithValue("$id", adapter.Id); command.Parameters.AddWithValue("$name", adapter.Name);
            command.Parameters.AddWithValue("$type", adapter.Type); command.Parameters.AddWithValue("$time", snapshot.Timestamp.ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(token);
            // Store only complete measured intervals. Gaps remain absent; never substitute zero.
            if (adapter.State != "Up" || adapter.DownloadDelta is not { } down || adapter.UploadDelta is not { } up || adapter.IntervalSeconds is <= 0 or > 10) continue;
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO TrafficMinutes VALUES($id,$minute,$down,$up,$seconds,1)
                ON CONFLICT(AdapterId,Minute) DO UPDATE SET Download=Download+excluded.Download,Upload=Upload+excluded.Upload,
                CoveredSeconds=CoveredSeconds+excluded.CoveredSeconds,Samples=Samples+1
                """;
            command.Parameters.AddWithValue("$id", adapter.Id); command.Parameters.AddWithValue("$minute", snapshot.Timestamp.ToUnixTimeSeconds() / 60 * 60);
            command.Parameters.AddWithValue("$down", down); command.Parameters.AddWithValue("$up", up); command.Parameters.AddWithValue("$seconds", adapter.IntervalSeconds);
            await command.ExecuteNonQueryAsync(token);
        }
        foreach (var item in events)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO ConnectionEvents(Time,AdapterId,AdapterName,Previous,State) VALUES($time,$id,$name,$previous,$state)";
            command.Parameters.AddWithValue("$time", item.Timestamp.ToUnixTimeSeconds()); command.Parameters.AddWithValue("$id", item.AdapterId);
            command.Parameters.AddWithValue("$name", item.AdapterName); command.Parameters.AddWithValue("$previous", item.PreviousState); command.Parameters.AddWithValue("$state", item.State);
            await command.ExecuteNonQueryAsync(token);
        }
        transaction.Commit(); return 0;
    }, token);
    public Task<IReadOnlyList<UsageSummary>> GetUsageAsync(DateTimeOffset from, CancellationToken token) => WithConnection<IReadOnlyList<UsageSummary>>(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT t.AdapterId,a.Name,SUM(Download),SUM(Upload),SUM(Samples),SUM(CoveredSeconds),MIN(Minute),MAX(Minute) FROM TrafficMinutes t JOIN Adapters a ON a.Id=t.AdapterId WHERE Minute >= $from GROUP BY t.AdapterId ORDER BY SUM(Download)+SUM(Upload) DESC";
        command.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        var rows = new List<UsageSummary>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetDouble(5), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(6)), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(7))));
        return rows;
    }, token);
    public Task<IReadOnlyList<HistoryPoint>> GetHistoryAsync(string adapterId, DateTimeOffset from, CancellationToken token) => WithConnection<IReadOnlyList<HistoryPoint>>(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Minute,Download/CoveredSeconds,Upload/CoveredSeconds FROM TrafficMinutes WHERE AdapterId=$id AND Minute >= $from ORDER BY Minute DESC LIMIT 720";
        command.Parameters.AddWithValue("$id", adapterId); command.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        var rows = new List<HistoryPoint>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        rows.Reverse(); return rows;
    }, token);
    public Task<IReadOnlyList<ConnectionEvent>> GetEventsAsync(CancellationToken token) => WithConnection<IReadOnlyList<ConnectionEvent>>(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Time,AdapterId,AdapterName,Previous,State FROM ConnectionEvents ORDER BY Time DESC,Id DESC LIMIT 200";
        var rows = new List<ConnectionEvent>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }, token);
    public Task SaveApplicationTrafficAsync(ServiceSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.Availability != "Measured" || snapshot.Applications.Count == 0) return Task.CompletedTask;
        return WithConnection(async connection =>
        {
            using var transaction = connection.BeginTransaction();
            long minute = snapshot.Timestamp.ToUnixTimeSeconds() / 60 * 60;
            foreach (var sample in snapshot.Applications)
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO ApplicationTrafficMinutes VALUES($pid,$name,$minute,$received,$sent,$events,1)
                    ON CONFLICT(Pid,ProcessName,Minute) DO UPDATE SET Received=Received+excluded.Received,
                    Sent=Sent+excluded.Sent, Events=Events+excluded.Events, Windows=Windows+1
                    """;
                command.Parameters.AddWithValue("$pid", sample.Pid); command.Parameters.AddWithValue("$name", sample.ProcessName);
                command.Parameters.AddWithValue("$minute", minute); command.Parameters.AddWithValue("$received", sample.ReceivedBytesTotal);
                command.Parameters.AddWithValue("$sent", sample.SentBytesTotal); command.Parameters.AddWithValue("$events", sample.Events);
                await command.ExecuteNonQueryAsync(token);
            }
            transaction.Commit(); return 0;
        }, token);
    }
    public Task<IReadOnlyList<ApplicationUsageSummary>> GetApplicationUsageAsync(DateTimeOffset from, CancellationToken token) => WithConnection<IReadOnlyList<ApplicationUsageSummary>>(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Pid,ProcessName,SUM(Received),SUM(Sent),SUM(Windows),MIN(Minute),MAX(Minute) FROM ApplicationTrafficMinutes WHERE Minute >= $from GROUP BY Pid,ProcessName ORDER BY SUM(Received)+SUM(Sent) DESC LIMIT 200";
        command.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        var rows = new List<ApplicationUsageSummary>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(6))));
        return rows;
    }, token);
    public Task<IReadOnlyList<ApplicationMinutePoint>> GetApplicationMinuteSeriesAsync(string processName, DateTimeOffset from, CancellationToken token) => WithConnection<IReadOnlyList<ApplicationMinutePoint>>(async connection =>
    {
        // Grouped by Minute alone (dropping Pid) so multiple concurrent processes sharing one name — e.g.
        // several chrome.exe helpers — contribute to one learned baseline, matching "multiple processes
        // belonging to one application" (requirements section 10.1). Bytes are summed across PIDs (combined
        // throughput), but covered seconds uses MAX(Windows), not SUM — concurrent PIDs' 5-second windows
        // overlap in wall-clock time, so summing them would double-count coverage and understate the rate.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Minute,SUM(Received),SUM(Sent),MAX(Windows)*5.0 FROM ApplicationTrafficMinutes WHERE ProcessName=$name AND Minute >= $from GROUP BY Minute ORDER BY Minute";
        command.Parameters.AddWithValue("$name", processName); command.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        var rows = new List<ApplicationMinutePoint>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)), reader.GetInt64(1), reader.GetInt64(2), reader.GetDouble(3)));
        return rows;
    }, token);
    public Task<long> SaveAnomalyEventAsync(AnomalyEvent anomaly, CancellationToken token) => WithConnection(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AnomalyEvents(Time,ProcessName,Direction,Severity,CurrentRate,BaselineMean,Deviation,Explanation,Notified) VALUES($time,$name,$direction,$severity,$current,$mean,$deviation,$explanation,$notified); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$time", anomaly.Timestamp.ToUnixTimeSeconds()); command.Parameters.AddWithValue("$name", anomaly.ProcessName);
        command.Parameters.AddWithValue("$direction", anomaly.Direction); command.Parameters.AddWithValue("$severity", anomaly.Severity);
        command.Parameters.AddWithValue("$current", anomaly.CurrentBytesPerSecond); command.Parameters.AddWithValue("$mean", anomaly.BaselineMeanBytesPerSecond);
        command.Parameters.AddWithValue("$deviation", anomaly.DeviationMultiple); command.Parameters.AddWithValue("$explanation", anomaly.Explanation);
        command.Parameters.AddWithValue("$notified", anomaly.Notified ? 1 : 0);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }, token);
    public Task<IReadOnlyList<AnomalyEvent>> GetAnomalyEventsAsync(int limit, CancellationToken token) => WithConnection<IReadOnlyList<AnomalyEvent>>(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Time,ProcessName,Direction,Severity,CurrentRate,BaselineMean,Deviation,Explanation,Notified FROM AnomalyEvents ORDER BY Time DESC,Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        var rows = new List<AnomalyEvent>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt64(0), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetString(8), reader.GetInt32(9) != 0));
        return rows;
    }, token);
    public Task SaveDiagnosticAsync(DiagnosticResult result, CancellationToken token) => WithConnection(async connection =>
    {
        await Execute(connection, "INSERT INTO Diagnostics(Time,Json) VALUES($time,$json)", token, ("$time", result.Timestamp.ToUnixTimeSeconds()), ("$json", JsonSerializer.Serialize(result))); return 0;
    }, token);
    public Task<int> CleanupAsync(int retentionDays, CancellationToken token)
    {
        if (retentionDays is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        return WithConnection(async connection =>
        {
            int count = 0; var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeSeconds();
            foreach (var (table, column) in new[] { ("TrafficMinutes", "Minute"), ("ConnectionEvents", "Time"), ("Diagnostics", "Time"), ("ApplicationTrafficMinutes", "Minute"), ("AnomalyEvents", "Time") })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DELETE FROM {table} WHERE rowid IN (SELECT rowid FROM {table} WHERE {column} < $cutoff LIMIT 2000)";
                command.Parameters.AddWithValue("$cutoff", cutoff); count += await command.ExecuteNonQueryAsync(token);
            }
            return count;
        }, token);
    }
    public Task DeleteHistoryAsync(CancellationToken token) => WithConnection(async connection =>
    {
        await Execute(connection, "BEGIN; DELETE FROM TrafficMinutes; DELETE FROM ConnectionEvents; DELETE FROM Diagnostics; DELETE FROM Adapters; DELETE FROM ApplicationTrafficMinutes; DELETE FROM AnomalyEvents; COMMIT; PRAGMA wal_checkpoint(TRUNCATE);", token); return 0;
    }, token);
    public Task<string> CheckIntegrityAsync(CancellationToken token) => WithConnection(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA quick_check";
        return (string?)await command.ExecuteScalarAsync(token) ?? "Unavailable";
    }, token);
}
