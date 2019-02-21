namespace Cloud.Core.AppHost.Extensions
{
    using System;
    using System.Reflection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Class AppHostBuilder extensions.
    /// </summary>
    public static class AppHostBuilderExtensions
    {
        /// <summary>Creates the default builder.</summary>
        /// <param name="hostBuilder">The host builder to extend.</param>
        /// <returns>AppHostBuilder instance with defaults of polly retry policies and memory cache applied.</returns>
        public static AppHostBuilder CreateDefaultBuilder(this AppHostBuilder hostBuilder)
        {
            return hostBuilder.UseDefaultRetryPolicies().AddMemoryCache();
        }

        /// <summary>Specify the startup type to be used by the web host.</summary>
        /// <param name="hostBuilder">The <see cref="T:Microsoft.AspNetCore.Hosting.IWebHostBuilder" /> to configure.</param>
        /// <param name="startupType">The <see cref="T:System.Type" /> to be used.</param>
        /// <returns>The <see cref="T:Microsoft.AspNetCore.Hosting.IWebHostBuilder" />.</returns>
        public static AppHostBuilder UseStartup(this AppHostBuilder hostBuilder, Type startupType)
        {
            var instance = Activator.CreateInstance(startupType);

            try
            {
                MethodInfo configureAppConfiguration = startupType.GetMethod("ConfigureAppConfiguration");

                if (configureAppConfiguration != null)
                    hostBuilder.ConfigureAppConfiguration((configuration) => configureAppConfiguration.Invoke(instance, new object[] { configuration }));
            }
            catch (Exception ex) when (ex is TargetParameterCountException)
            {
                var exMsg = "Could not run ConfigureAppConfiguration method, ensure param (IConfigurationBuilder) has been set in method signature";
                hostBuilder.InternalLogger.LogError(exMsg, ex);
                throw new ArgumentException(exMsg, ex);
            }

            try
            {
                MethodInfo configureLogging = startupType.GetMethod("ConfigureLogging");

                if (configureLogging != null)
                    hostBuilder.ConfigureLogging((configuration, loggingBuilder) => configureLogging.Invoke(instance, new object[] { configuration, loggingBuilder }));
            }
            catch (Exception ex) when (ex is TargetParameterCountException)
            {
                var exMsg = "Could not run ConfigureLogging, ensure params (IConfigurationRoot, ILoggingBuilder) have been set in method signature";
                hostBuilder.InternalLogger.LogError(exMsg);
                throw new ArgumentException(exMsg, ex);
            }

            try
            {
                MethodInfo configureServices = startupType.GetMethod("ConfigureServices");

                if (configureServices != null)
                    hostBuilder.ConfigureServices((configuration, logger, serviceCollection) => configureServices.Invoke(instance, new object[] { configuration, logger, serviceCollection }));
            }
            catch (Exception ex) when (ex is TargetParameterCountException)
            {
                var exMsg = "Could not run ConfigureServices, ensure params (IConfigurationRoot, ILogger, IServiceCollection) have been set in method signature";
                hostBuilder.InternalLogger.LogError(exMsg);
                throw new ArgumentException(exMsg, ex);
            }

            return hostBuilder;
        }

        /// <summary>Specify the startup type to be used by the web host.</summary>
        /// <param name="hostBuilder">The <see cref="T:Microsoft.AspNetCore.Hosting.IWebHostBuilder" /> to configure.</param>
        /// <typeparam name="TStartup">The type containing the startup methods for the application.</typeparam>
        /// <returns>The <see cref="T:Microsoft.AspNetCore.Hosting.IWebHostBuilder" />.</returns>
        public static AppHostBuilder UseStartup<TStartup>(this AppHostBuilder hostBuilder) where TStartup : class
        {
            return hostBuilder.UseStartup(typeof(TStartup));
        }
    }
}
