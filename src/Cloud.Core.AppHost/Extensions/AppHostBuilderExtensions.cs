namespace Cloud.Core.AppHost.Extensions
{
    using System;

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
            hostBuilder.StartupClass = startupType;
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
