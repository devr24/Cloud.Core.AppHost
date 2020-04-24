using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cloud.Core.AppHost.Tests.Fakes
{
    /// <summary>
    /// Class MalformedStartupConfig has a badly constructed ConfigureAppConfiguration method.
    /// </summary>
    public class MalformedStartupConfig
    {
        /// <summary>
        /// Configures the application configuration.
        /// Will error.
        /// </summary>
        public void ConfigureAppConfiguration() { }
    }

    /// <summary>
    /// Class MalformedStartupLogging has a badly constructed ConfigureLogging method.
    /// </summary>
    public class MalformedStartupLogging
    {
        /// <summary>
        /// Configures the application configuration.
        /// </summary>
        /// <param name="builder">The builder.</param>
        public void ConfigureAppConfiguration(IConfigurationBuilder builder) { }

        /// <summary>
        /// Configures the logging.
        /// Will error.
        /// </summary>
        public void ConfigureLogging() { }
    }

    /// <summary>
    /// Class MalformedStartupConfigureServices has a badly constructed ConfigureServices method.
    /// </summary>
    public class MalformedStartupConfigureServices
    {
        /// <summary>
        /// Configures the application configuration.
        /// </summary>
        /// <param name="builder">The builder.</param>
        public void ConfigureAppConfiguration(IConfigurationBuilder builder) { }

        /// <summary>
        /// Configures the logging.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <param name="builder">The builder.</param>
        public void ConfigureLogging(IConfiguration config, ILoggingBuilder builder) { }

        /// <summary>
        /// Configures the services.
        /// Will error.
        /// </summary>
        public void ConfigureServices() { }
    }

    /// <summary>
    /// Class FakeStartup.
    /// </summary>
    public class FakeStartup
    {
        public IConfiguration Config { get; private set; }
        public ILogger Logger { get; private set; }
        public IServiceCollection Services { get; private set; }

        /// <summary>
        /// Configures the application configuration.
        /// </summary>
        /// <param name="builder">The builder.</param>
        public void ConfigureAppConfiguration(IConfigurationBuilder builder)
        {
            builder.AddInMemoryCollection(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Array:0", "1"),
                new KeyValuePair<string, string>("Array:1", "2"),
                new KeyValuePair<string, string>("Array:2", "3")
            });
        }

        /// <summary>
        /// Configures the logging.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <param name="builder">The builder.</param>
        public void ConfigureLogging(IConfiguration config, ILoggingBuilder builder)
        {
            builder.AddConsole();
        }

        /// <summary>
        /// Configures the services.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="services">The services.</param>
        public void ConfigureServices(IConfiguration config, ILogger logger, IServiceCollection services)
        {
            Config = config;
            Logger = logger;
            Services = services;
        }
    }
}
