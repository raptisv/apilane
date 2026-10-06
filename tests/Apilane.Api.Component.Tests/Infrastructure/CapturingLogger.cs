using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Api.Component.Tests.Infrastructure
{
    /// <summary>
    /// An <see cref="ILogger{TCategoryName}"/> that keeps what was logged, with the scopes that were open at
    /// the time, so that a test can read back what a log sink (console, Graylog) would have received: a sink
    /// gets the values of the open scopes as properties of the entry.
    /// </summary>
    public sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<object> _scopes = new();

        public List<CapturedLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            _scopes.Add(state);

            return new ScopeEnd(() => _scopes.Remove(state));
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
            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception, _scopes.Select(Describe).ToList()));
        }

        private static string Describe(object scope)
        {
            return scope is IEnumerable<KeyValuePair<string, object>> values
                ? string.Join(Environment.NewLine, values.Select(x => $"{x.Key}={x.Value}"))
                : scope.ToString() ?? string.Empty;
        }

        private sealed class ScopeEnd : IDisposable
        {
            private readonly Action _end;

            public ScopeEnd(Action end)
            {
                _end = end;
            }

            public void Dispose()
            {
                _end();
            }
        }
    }

    public sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception, List<string> Scopes)
    {
        /// <summary>
        /// Everything the entry shows: its message, its exception and the values of its scopes.
        /// </summary>
        public string Text => string.Join(Environment.NewLine, new[] { Message, Exception?.ToString() ?? string.Empty }.Concat(Scopes));
    }
}
