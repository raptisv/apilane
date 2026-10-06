using Microsoft.Extensions.Logging;

namespace Apilane.UnitTests
{
    /// <summary>
    /// An <see cref="ILogger{TCategoryName}"/> that keeps what was logged, so that a test can read back
    /// what a log sink (console, Graylog) would have received.
    /// </summary>
    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception)
    {
        /// <summary>
        /// Everything the entry shows: its message, and the exception with its stack trace.
        /// </summary>
        public string Text => Exception is null ? Message : Message + Environment.NewLine + Exception;
    }
}
