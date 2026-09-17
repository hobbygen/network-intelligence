using Microsoft.Extensions.Logging;
namespace NetworkIntelligence.App;

internal sealed class LocalLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object sync = new();
    public ILogger CreateLogger(string categoryName) => new LocalLogger(this, categoryName);
    public void Dispose() { }
    private void Write(string category, LogLevel level, string message)
    {
        if (level < LogLevel.Warning) return;
        lock (sync)
        {
            try
            {
                var path = Path.Combine(directory, "application.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} [{level}] {category}: {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private sealed class LocalLogger(LocalLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(logLevel)) owner.Write(category, logLevel, formatter(state, null)); }
    }
}
