namespace Cloud.Core.AppHost
{
    using System.Linq;
    using System.Threading;
    using Polly;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Sockets;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Extension methods for application _appHost.
    /// </summary>
    public class AppHostBuilder
    {
        private bool _hasBuilt;
        private readonly bool _outputConfigInfo;
        private readonly IServiceCollection _services = new ServiceCollection();
        internal readonly List<AsyncPolicy> _retryPolicies = new List<AsyncPolicy>();
        internal readonly List<Type> _processTypes = new List<Type>();
        internal IWebHostBuilder _webBuilder = WebHost.CreateDefaultBuilder(null);
        internal IConfigurationBuilder _configBuilder = new ConfigurationBuilder();
        internal IWebHost _webHost;
        internal AppHost _appHost;
        internal bool _enableProcessEndpoints;
        internal string _webPort = "8080";
        internal ServiceProvider _prov;
        internal bool _usesProbe;
        internal string _link = "<a href='{0}'>{0}</a><br/>";
        internal int _backgroundMonitor = -1;
        internal ILogger _internalLogger;
        internal object _startup;

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        public IConfiguration Config { get; internal set; }

        /// <summary>
        /// Gets the base URL.
        /// </summary>
        /// <value>The base URL.</value>
        public string BaseUrl => $"http://0.0.0.0:{_webPort}/";

        /// <summary>
        /// Gets the host system information.
        /// </summary>
        /// <value>The host system information.</value>
        public SystemInfo HostSystemInfo { get; } = new SystemInfo();

        /// <summary>
        /// Initializes a new instance of the <see cref="AppHostBuilder"/> class.
        /// </summary>
        /// <param name="outputConfigInfo">if set to <c>true</c> [output configuration information].</param>
        public AppHostBuilder(bool outputConfigInfo = false)
        {
            _outputConfigInfo = outputConfigInfo;
            _services.AddLogging(loggers => loggers.AddConsole());
            _internalLogger = _services.BuildServiceProvider().GetService<ILogger<AppHostBuilder>>();
        }

        /// <summary>
        /// Configures the logging.
        /// </summary>
        /// <param name="configure">The configure.</param>
        /// <returns>AppHostBuilder.</returns>
        public AppHostBuilder ConfigureLogging(Action<IConfiguration, ILoggingBuilder> configure)
        {
            try
            {
                _services.AddLogging(loggers => configure(Config ?? new ConfigurationBuilder().Build(), loggers));
                _internalLogger = _services.BuildServiceProvider().GetService<ILogger<AppHostBuilder>>();
            }
            catch (Exception e)
            {
                _internalLogger.LogError(e, $"Error during ConfigureLogging: {e.Message}");
                throw;
            }

            return this;
        }

        /// <summary>Configures the application configuration.</summary>
        /// <param name="configure">The configure.</param>
        /// <returns>AppHostBuilder.</returns>
        public AppHostBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
        {
            try
            {
                configure(_configBuilder);

                Config = _configBuilder.Build();

                _services.AddSingleton(Config);

                // Additional output information to the console, before the logger has been built.
                if (_outputConfigInfo)
                {
                    _internalLogger.LogInformation(Config.GetAllSettingsAsString());
                }
            }
            catch (Exception e)
            {
                _internalLogger.LogError(e, $"Error during ConfigureAppConfiguration: {e.Message}");
                throw;
            }

            return this;
        }

        /// <summary>
        /// Configures the services.
        /// </summary>
        /// <param name="configureServices">The configure services.</param>
        /// <returns>Application _appHost with services configured.</returns>
        public AppHostBuilder ConfigureServices(Action<IConfiguration, ILogger, IServiceCollection> configureServices)
        {
            try
            {
                _internalLogger.LogInformation($"SystemInfo: {HostSystemInfo}");
                configureServices(Config ?? new ConfigurationBuilder().Build(), _internalLogger, _services);
            }
            catch (Exception e)
            {
                _internalLogger.LogError(e,$"Error during ConfigureServices: {e.Message}");
                throw;
            }
            return this;
        }

        /// <summary>
        /// Adds the polly retry policy for the given type.
        /// </summary>
        /// <typeparam name="T">Exception type to handle</typeparam>
        /// <param name="waitTimeInSeconds">The wait time in seconds.</param>
        /// <param name="retryAttempts">The retry attempts.</param>
        /// <returns>AppHostBuilder.</returns>
        public AppHostBuilder AddRetryWaitPolicy<T>(int waitTimeInSeconds = 5, int retryAttempts = 3)
            where T : Exception
        {

            // Setup the polly policy that will be added to the executing code.
            var policy = Policy.Handle<T>().WaitAndRetryAsync(
                retryCount: retryAttempts,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(waitTimeInSeconds),

                onRetry: (exception, calculatedWaitDuration) =>
                {
                    _appHost._logger.LogError(exception, $"An exception has been caught by retry/wait policy: {exception.Message}");
                    _appHost._logger.LogWarning($"Retry policy executed for type {typeof(T).Name}");
                });

            _retryPolicies.Add(policy);

            return this;
        }

        /// <summary>
        /// Adds three default polly retry policies for the following types:
        /// <see cref="SocketException" /> Socket Exception
        /// <see cref="HttpRequestException" /> Http Request Exception
        /// <see cref="TimeoutException" /> Timeout Exception
        /// </summary>
        /// <param name="waitTimeInSeconds">The wait time in seconds.</param>
        /// <param name="retryAttempts">The retry attempts.</param>
        /// <returns>Application _appHost with a _appHosted process attached.</returns>
        public AppHostBuilder UseDefaultRetryPolicies(int waitTimeInSeconds = 5, int retryAttempts = 3)
        {
            return AddRetryWaitPolicy<SocketException>(waitTimeInSeconds, retryAttempts)
                .AddRetryWaitPolicy<HttpRequestException>(waitTimeInSeconds, retryAttempts)
                .AddRetryWaitPolicy<TimeoutException>(waitTimeInSeconds, retryAttempts);
        }

        /// <summary>
        /// Use the background monitor to log whether the application is alive or not.
        /// </summary>
        /// <param name="tickTimeInSeconds">The tick time in seconds.</param>
        /// <returns>AppHostBuilder.</returns>
        /// <exception cref="ArgumentException">Tick time must be greater than zero</exception>
        public AppHostBuilder UseBackgroundMonitor(int tickTimeInSeconds)
        {
            if (tickTimeInSeconds < 1)
            {
                throw new ArgumentException("Tick time must be greater than zero");
            }

            _backgroundMonitor = tickTimeInSeconds;

            return this;
        }

        /// <summary>
        /// Adds the memory cache.
        /// </summary>
        /// <returns>Application _appHost with memory cache attached to the services.</returns>
        public AppHostBuilder AddMemoryCache()
        {
            _services.AddMemoryCache();
            return this;
        }

        /// <summary>
        /// Adds the _appHosted process.
        /// </summary>
        /// <param name="process">The _appHosted process.</param>
        /// <returns>Application _appHost with a _appHosted process attached.</returns>
        public AppHostBuilder AddHostedProcess(IHostedProcess process)
        {
            var type = process.GetType();

            _services.AddSingleton(type, process);
            _processTypes.Add(type);
            return this;
        }

        /// <summary>
        /// Adds the _appHosted process.
        /// </summary>
        /// <returns>Application _appHost with a _appHosted process attached.</returns>
        public AppHostBuilder AddHostedProcess<T>() where T : IHostedProcess
        {
            var type = typeof(T);
            _services.AddSingleton(type);
            _processTypes.Add(type);
            return this;
        }

        /// <summary>
        /// Adds the health probe.
        /// </summary>
        /// <param name="port">The port for the health probe to run on.</param>
        /// <returns>Application _appHost with a _appHosted process attached.</returns>
        public AppHostBuilder AddHealthProbe(string port = "8080")
        {
            _webPort = port;
            _usesProbe = true;
            return this;
        }

        /// <summary>
        /// Adds an HTTP client factory for the requested address.
        /// </summary>
        /// <param name="name">The name of the http context.</param>
        /// <param name="baseUrlAddress">The base URL address for the http client.</param>
        /// <returns>ApplicationHost with the http client factory added to services.</returns>
        public AppHostBuilder AddHttpClient(string name, string baseUrlAddress)
        {
            _services.AddHttpClient(name, client => client.BaseAddress = new Uri(baseUrlAddress));
            return this;
        }

        /// <summary>
        /// Adds an HTTP client factory for each name/address requested.
        /// </summary>
        /// <param name="baseAddresses">The name/base addresses, to add.</param>
        /// <returns>ApplicationHost with new web clients added.</returns>
        public AppHostBuilder AddHttpClient(Dictionary<string, Uri> baseAddresses)
        {
            foreach (var baseAddress in baseAddresses)
            {
                _services.AddHttpClient(baseAddress.Key, client => client.BaseAddress = baseAddress.Value);
            }

            return this;
        }

        /// <summary>
        /// Adds a typed HTTP client factory.
        /// </summary>
        /// <typeparam name="T">Type of class to add.</typeparam>
        /// <param name="name">The name of this context.</param>
        /// <param name="baseUrlAddress">The base URL address.</param>
        /// <returns>ApplicationHost with typed web client added.</returns>
        public AppHostBuilder AddHttpClientTyped<T>(string name, string baseUrlAddress)
            where T : class
        {
            _services.AddHttpClient(name, client => client.BaseAddress = new Uri(baseUrlAddress))
                .AddTypedClient<T>();
            return this;
        }

        /// <summary>
        /// Enables an endpoint for each of the hosted processes, as well as a "swagger" endpoint for listing all services available.
        /// If you enable this - the hosted processes WILL NOT execute on startup, instead, they will wait to be triggered.
        /// </summary>
        public AppHostBuilder UseHostedProcessEndpoints()
        {
            _enableProcessEndpoints = true;
            return this;
        }

        /// <summary>
        /// Builds the specified _appHost.
        /// </summary>
        /// <returns>Application _appHost with service provider built.</returns>
        /// <exception cref="InvalidOperationException">Builder can only build once</exception>
        public IAppHost Build()
        {
            if (_hasBuilt)
            {
                throw new InvalidOperationException("ApplicationHost has already been built and can only be built once");
            }

            _hasBuilt = true;
            _prov = _services.BuildServiceProvider();
            _processTypes.AddRange(ServiceCollectionExtensions.ProcessTypes);

            // Setup service endpoints for hosted processes (if configured).
            ConfigureWebHost();

            _appHost = new AppHost(_webHost, _retryPolicies, _prov, _processTypes, _backgroundMonitor, false, _enableProcessEndpoints);

            return _appHost;
        }

        /// <summary>
        /// Configures the web host.
        /// </summary>
        private void ConfigureWebHost()
        {
            if (_enableProcessEndpoints || _usesProbe)
            {
                SetupServiceEndpoints();
                _webHost = _webBuilder.UseUrls(BaseUrl).Build();
                _webHost.RunAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Setups the service endpoints.
        /// </summary>
        private void SetupServiceEndpoints()
        {
            _webBuilder.Configure(app =>
            {
                // Add a probe endpoint that can be used to check the application is still alive.
                EnableProbeEndpoint(app);

                // Add an endpoint for each hosted process.
                EnableProcessEndpoints(app);

                // Add swagger like documentation.
                EnableSwaggerEndpoint(app);
            });
        }

        /// <summary>
        /// Add an endpoint called "probe" which can be called to return the status of the component.
        /// </summary>
        /// <param name="app">App to add the endpoint.</param>
        private void EnableProbeEndpoint(IApplicationBuilder app)
        {
            if (!_usesProbe)
            {
                return;
            }

            // Add probe controller endpoint.
            app.Map("/probe", mapRun =>
                mapRun.Run(async context =>
                {
                    context.Response.StatusCode = _appHost.Status != HostStatus.Faulted ? 200 : 500;
                    await context.Response.WriteAsync(_appHost.Status.ToString());
                }));
        }

        /// <summary>
        /// Add an endpoint for each of our hosted processes.
        /// </summary>
        /// <param name="app">App to add each endpoint.</param>
        private void EnableProcessEndpoints(IApplicationBuilder app)
        {
            if (!_enableProcessEndpoints)
            {
                return;
            }

            var logger = app.ApplicationServices.GetService<ILogger<AppHostBuilder>>();

            foreach (var processType in _processTypes.Distinct())
            {
                app.Map($"/{processType.Name}", mapRun =>
                    mapRun.Run(async context =>
                    {
                        logger.LogInformation($"Endpoint called, executing process {processType.Name}");

                        try
                        {
                            if (!(_prov.GetService(processType) is IHostedProcess process))
                            {
                                throw new InvalidOperationException($"Could not find process type {processType.Name}");
                            }

                            await process.Start(_appHost._context, default);
                            process.Stop();

                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync($"Start {processType.Name} ran successfully");
                        }
                        catch (InvalidOperationException n)
                        {
                            logger.LogError(n, $"ServiceEndpoint: Process type {processType.Name} not found");
                            context.Response.StatusCode = 404;
                            await context.Response.WriteAsync($"Could not find process with endpoint {n.Message}");
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e,$"ServiceEndpoint: Problem during process start for process type {processType.Name}");
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync($"Problem executing process via endpoint {e.Message}");
                        }
                    }));
            }
        }

        /// <summary>
        /// Add a swagger endpoint that can document this app's endpoints.  Only added if probe or process endpoints were added.
        /// </summary>
        /// <param name="app">App to add the endpoint to.</param>
        private void EnableSwaggerEndpoint(IApplicationBuilder app)
        {
            // Write out a swagger like document for each process.
            app.Map("/swagger", map =>
                map.Run(async context =>
                {
                    context.Response.StatusCode = 200;

                    var url = BaseUrl.Replace("0.0.0.0", "localhost");

                    if (_usesProbe)
                    {
                        await context.Response.WriteAsync(string.Format(_link, url + "probe"));
                    }

                    foreach (var service in _processTypes)
                        await context.Response.WriteAsync(string.Format(_link, url + service.Name));

                }));
        }
    }
}
