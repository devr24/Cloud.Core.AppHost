namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using System.Collections.Generic;
    using Cloud.Core.AppHost;

    public static class ServiceCollectionExtensions
    {
        internal readonly static List<Type> ProcessTypes = new List<Type>();

        /// <summary>
        /// Adds the _appHosted process.
        /// </summary>
        /// <param name="serviceBuilder">A collection of services</param>
        /// <param name="process">The _appHosted process.</param>
        /// <returns>Application _appHost with a _appHosted process attached.</returns>
        public static IServiceCollection AddHostedProcess(this IServiceCollection serviceBuilder, IHostedProcess process)
        {
            var type = process.GetType();
            ProcessTypes.Add(type);
            serviceBuilder.AddSingleton(type, process);
            return serviceBuilder;
        }

        /// <summary>
        /// Adds the _appHosted process.
        /// </summary>
        /// <param name="serviceBuilder">A collection of services</param>
        /// <returns>Application _appHost with a _appHosted process attached.</returns>
        public static IServiceCollection AddHostedProcess<T>(this IServiceCollection serviceBuilder) where T : IHostedProcess
        {
            var type = typeof(T);
            ProcessTypes.Add(type);
            serviceBuilder.AddSingleton(type);
            return serviceBuilder;
        }
    }
}
