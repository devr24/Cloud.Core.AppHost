namespace Cloud.Core.AppHost
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>Interface IAppStartup, used for defining how startup classes should be structured.</summary>
    internal interface IHostBuilder
    {
        /// <summary>Configures the dependency services.</summary>
        /// <param name="config">The configuration.</param>
        /// <param name="services">The services.</param>
        void ConfigureServices(IConfiguration config, IServiceCollection services);
    }
}
