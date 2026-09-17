using Microsoft.Extensions.Logging;
namespace NetworkIntelligence.MonitoringService;

/// <summary>Bounded rolling file logger. The service runs as LocalSystem, so it cannot use a per-user AppData path.</summary>
internal sealed class ServiceLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object sync = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private void Write(string category, LogLevel level, string message)
    {
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "service.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_097_152) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} [{level}] {category}: {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private sealed class FileLogger(ServiceLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null) message += " | " + exception.GetType().Name + ": " + exception.Message;
            owner.Write(category, logLevel, message);
        }
    }
}
