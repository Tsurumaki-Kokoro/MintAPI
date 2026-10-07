using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MintAPI.Tests.TestDoubles;

public sealed record RecordedLog(LogLevel Level, string Message, Exception? Exception);

public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<RecordedLog> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Recorder(Entries);
    public void Dispose() { }

    private sealed class Recorder(ConcurrentQueue<RecordedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new(logLevel, formatter(state, exception), exception));
    }
}
