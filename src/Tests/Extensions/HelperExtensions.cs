using Cloud.Core.AppHost.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cloud.Core.AppHost.Tests.Extensions
{
    /// <summary>
    /// Class HelperExtensions.
    /// </summary>
    public static class HelperExtensions
    {
        /// <summary>
        /// Adds the fake logger.
        /// </summary>
        /// <param name="logBuilder">The log builder.</param>
        /// <returns>ILoggingBuilder.</returns>
        public static ILoggingBuilder AddFakeLogger(this ILoggingBuilder logBuilder)
        {
            var logger = new FakeLogger();
            logBuilder.Services.AddSingleton(logger);
            logBuilder.Services.AddSingleton<ILoggerProvider>(a => new FakeLoggerProvider(logger));
            return logBuilder;
        }

        /// <summary>
        /// Gets the fake logger.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>FakeLogger.</returns>
        public static FakeLogger GetFakeLogger(this ServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<FakeLogger>();
        }

        /// <summary>
        /// Gets the fake logger.
        /// </summary>
        /// <param name="appHost">The application host.</param>
        /// <returns>FakeLogger.</returns>
        public static FakeLogger GetFakeLogger(this IAppHost appHost)
        {
            return (appHost as AppHost)._serviceProvider.GetFakeLogger();
        }

        /// <summary>
        /// Gets the service.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="appHost">The application host.</param>
        /// <returns>T.</returns>
        public static T GetService<T>(this IAppHost appHost)
        {
            return (appHost as AppHost)._serviceProvider.GetService<T>();
        }
    }
}
