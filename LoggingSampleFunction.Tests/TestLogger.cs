using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LoggingSampleFunction.Tests
{
    internal sealed class TestLogger : ILogger
    {
        public List<TestLogEvent> Messages { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return default;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(new TestLogEvent()
            {
                Message = formatter(state, exception),
                Level = logLevel
            });
        }
    }

    internal sealed class TestLogEvent
    {
        public string Message { get; set; } = string.Empty;

        public LogLevel Level { get; set; }
    }
}
