using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Cloud.Core.AppHost.Tests.Fakes
{
    /// <summary>
    /// Class FakeLoggerProvider.
    /// Implements the <see cref="Microsoft.Extensions.Logging.ILoggerProvider" />
    /// </summary>
    /// <seealso cref="Microsoft.Extensions.Logging.ILoggerProvider" />
    public class FakeLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// The logger
        /// </summary>
        private FakeLogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeLoggerProvider"/> class.
        /// </summary>
        public FakeLoggerProvider()
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="FakeLoggerProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public FakeLoggerProvider(FakeLogger logger)
        {
            _logger = logger;
        }
        
        /// <inheritdoc />
        public ILogger CreateLogger(string name)
        {
            if (_logger == null)
            {
                _logger = new FakeLogger();
            }

            return _logger;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Class FakeLogger.
    /// Implements the <see cref="Microsoft.Extensions.Logging.ILogger" />
    /// </summary>
    /// <seealso cref="Microsoft.Extensions.Logging.ILogger" />
    public class FakeLogger : ILogger
    {
        /// <summary>
        /// Gets the log messages.
        /// </summary>
        /// <value>The log messages.</value>
        public List<LogMessage> LogMessages { get; } = new List<LogMessage>();

        /// <summary>
        /// Begins a logical operation scope.
        /// </summary>
        /// <typeparam name="TState">The type of the state to begin scope for.</typeparam>
        /// <param name="state">The identifier for the scope.</param>
        /// <returns>An <see cref="T:System.IDisposable" /> that ends the logical operation scope on dispose.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Checks if the given <paramref name="logLevel" /> is enabled.
        /// </summary>
        /// <param name="logLevel">level to be checked.</param>
        /// <returns><c>true</c> if enabled.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool IsEnabled(LogLevel logLevel)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes a log entry.
        /// </summary>
        /// <typeparam name="TState">The type of the object to be written.</typeparam>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="eventId">Id of the event.</param>
        /// <param name="state">The entry to be written. Can be also an object.</param>
        /// <param name="exception">The exception related to this entry.</param>
        /// <param name="formatter">Function to create a <see cref="T:System.String" /> message of the <paramref name="state" /> and <paramref name="exception" />.</param>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            var message = formatter != null ? formatter(state, exception) : state.ToString();
            LogMessages.Add(new LogMessage
            {
                LogLevel = logLevel,
                Message = message,
                ExceptionType = exception?.GetType()
            });
        }

        /// <summary>
        /// Gets the type of the messages by exception.
        /// </summary>
        /// <param name="exType">Type of the ex.</param>
        /// <returns>System.Collections.Generic.List&lt;Cloud.Core.AppHost.Tests.Fakes.LogMessage&gt;.</returns>
        public List<LogMessage> GetMessagesByExceptionType(Type exType)
        {
            return LogMessages.FindAll(e => e.ExceptionType != null && e.ExceptionType == exType);
        }
    }


    /// <summary>
    /// Class LogMessage.
    /// </summary>
    public class LogMessage
    {
        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public string Message { get; set; }
        /// <summary>
        /// Gets or sets the log level.
        /// </summary>
        /// <value>The log level.</value>
        public LogLevel LogLevel { get; set; }
        /// <summary>
        /// Gets or sets the type of the exception.
        /// </summary>
        /// <value>The type of the exception.</value>
        public Type ExceptionType { get; set; }
    }
}
