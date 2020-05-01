namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using System.Collections.Generic;
    using Cloud.Core.AppHost;
    using Hosting;

    /// <summary>
    /// Class ServiceCollection extensions.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        internal static readonly List<Type> ProcessTypes = new List<Type>();

        /// <summary>
        /// Adds the IHostedProcess to the service collection.
        /// </summary>
        /// <param name="serviceBuilder">A collection of services</param>
        /// <param name="process">The IHostedProcess to add to the service collection.</param>
        /// <returns>List of services with the IHostedProcess attached.</returns>
        public static IServiceCollection AddHostedProcess(this IServiceCollection serviceBuilder, IHostedProcess process)
        {
            var type = process.GetType();
            ProcessTypes.Add(type);
            serviceBuilder.AddSingleton(type, process);
            return serviceBuilder;
        }

        /// <summary>
        /// Adds the IHostedService to the service collection.
        /// </summary>
        /// <param name="serviceBuilder">A collection of services</param>
        /// <param name="service">The IHostedService to add to the service collection.</param>
        /// <returns>List of services with the IHostedService attached.</returns>
        public static IServiceCollection AddHostedService(this IServiceCollection serviceBuilder, IHostedService service)
        {
            var type = service.GetType();
            ProcessTypes.Add(type);
            serviceBuilder.AddSingleton(type, service);
            return serviceBuilder;
        }

        /// <summary>
        /// Adds the IHostedProcess of type T to the service collection.
        /// </summary>
        /// <param name="serviceBuilder">A collection of services</param>
        /// <returns>Service collection with the type T (IHostedProcess) injected.</returns>
        public static IServiceCollection AddHostedProcess<T>(this IServiceCollection serviceBuilder) where T : IHostedProcess
        {
            var type = typeof(T);
            ProcessTypes.Add(type);
            serviceBuilder.AddSingleton(type);
            return serviceBuilder;
        }

        /// <summary>
        /// Adds the IHostedProcess of type T to the service collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serviceBuilder">A collection of services</param>
        /// <returns>Service collection with the type T (IHostedService) injected.</returns>
        public static IServiceCollection AddHostedService<T>(this IServiceCollection serviceBuilder) where T : IHostedService
        {
            var type = typeof(T);
            ProcessTypes.Add(type);
            serviceBuilder.AddSingleton(type);
            return serviceBuilder;
        }
    }
}
