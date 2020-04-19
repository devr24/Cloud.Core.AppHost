namespace Cloud.Core.AppHost
{
    using Polly;
    using Polly.Wrap;
    using Polly.Timeout;
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.DependencyInjection;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Interface IAppHost
    /// </summary>
    public interface IAppHost
    {
        /// <summary>
        /// Runs the IHostedService processes then stays alive.
        /// </summary>
        void RunAndBlock();

        /// <summary>
        /// Runs the IHostedService processes once then stops.
        /// </summary>
        void RunOnce();
    }


    /// <summary>
    /// Process host service.
    /// Allows a common approach to hosting applications within containers.
    /// Will eventually include logging, verifying connection and application self-healing can be included.
    /// Implements the <see cref="Cloud.Core.AppHost.IAppHost" />
    /// Implements the <see cref="System.IDisposable" />
    /// </summary>
    /// <seealso cref="Cloud.Core.AppHost.IAppHost" />
    /// <seealso cref="System.IDisposable" />
    internal class AppHost : IAppHost
    {
        private readonly bool _showSystemInfo;
        internal readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        internal readonly ManualResetEventSlim _keepAliveEvent = new ManualResetEventSlim();
        internal readonly object _lockGate = new object();
        internal bool _stopped;
        private bool _usingServiceEndpoints;
        internal readonly int _monitorFrequency;
        internal readonly List<AsyncPolicy> _retryPolicies;
        internal readonly IWebHost _webHost;
        internal readonly ServiceProvider _serviceProvider;
        internal readonly ILogger _logger;
        internal readonly IEnumerable<Type> _hostedProcessTypes;
        internal readonly AppHostContext Context;

        /// <summary>
        /// Gets the status.
        /// </summary>
        /// <value>The status.</value>
        public HostStatus Status { get; internal set; } = HostStatus.Starting;

        /// <summary>
        /// Gets the system information.
        /// </summary>
        /// <value>The system information.</value>
        public SystemInfo HostSystemInfo { get; } = new SystemInfo();

        /// <summary>
        /// Initializes a new instance of the AppHost class.
        /// </summary>
        /// <param name="webHost">The web host.</param>
        /// <param name="retryPolicies">The retry policies.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="hostedProcessTypes">The hosted process types.</param>
        /// <param name="monitorFrequency">The monitor frequency.</param>
        /// <param name="showSystemInfo">if set to <c>true</c> [show system information].  Typically set to false when web host builder is used.</param>
        /// <param name="usingServiceEndpoints">if set to <c>true</c> [using service endpoints].</param>
        [ExcludeFromCodeCoverage]
        internal AppHost(IWebHost webHost, List<AsyncPolicy> retryPolicies, ServiceProvider serviceProvider, IEnumerable<Type> hostedProcessTypes, int monitorFrequency = -1, bool showSystemInfo = false, bool usingServiceEndpoints = false)
        {
            _webHost = webHost;
            _retryPolicies = retryPolicies;
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetService<ILogger<AppHost>>();
            _hostedProcessTypes = hostedProcessTypes;
            _monitorFrequency = monitorFrequency;
            _showSystemInfo = showSystemInfo;
            _usingServiceEndpoints = usingServiceEndpoints;

            Context = new AppHostContext(_monitorFrequency, HostSystemInfo, _logger);

            // Attach to domain wide exceptions.
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => _logger?.LogError($"Unhandled exception captured: {eventArgs.ExceptionObject}");
            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => { _logger?.LogWarning("Application exit captured"); ProcessingStop(); };
            Console.CancelKeyPress += (sender, eventArgs) => { _logger?.LogWarning("Application cancel keys triggered"); eventArgs.Cancel = true; ProcessingStop(); };
        }

        /// <summary>
        /// Runs the specified process.
        /// </summary>
        /// <param name="runOnce">if set to <c>true</c> [run once].</param>
        internal void Run(bool runOnce = false)
        {
            Context.IsContinuouslyRunning = !runOnce;
            if (_monitorFrequency > 0)
                Context.StartMonitor();

            _logger?.LogInformation("Running app host");

            if (_showSystemInfo)
                _logger?.LogInformation($"SystemInfo: {HostSystemInfo}");

            Status = HostStatus.Running;

            if (_hostedProcessTypes != null && _hostedProcessTypes.Any())
            {
                if (!_usingServiceEndpoints)
                {
                    RunHostedProcesses(runOnce);
                }
                else
                    _logger?.LogWarning("Running as service endpoints, processes can only be trigged over http");


                if (!runOnce)
                    _keepAliveEvent.Wait();
            }
            else
            {
                _logger?.LogWarning("No hosted processes found" +
                    (runOnce ? " - cannot run and block without a hosted process to execute" : string.Empty));
            }

            // Stop all processing when the code reaches here.
            ProcessingStop();
        }

        /// <summary>
        /// Run each of the hosted processes one at a time.
        /// </summary>
        /// <param name="runOnce">if set to <c>true</c> [run once].</param>
        private void RunHostedProcesses(bool runOnce)
        {
            var finishedProcesses = 0;
            var processTypeCount = _hostedProcessTypes.Count();

            foreach (var processType in _hostedProcessTypes)
            {
                var hostedProcess = _serviceProvider.GetService(processType) as IHostedProcess;

                _logger?.LogInformation($"Starting hosted process {processType.Name}");

                // Run the hosted process, wrapped in polly retry logic.
                BuildWrappedPolicy().ExecuteAsync(token => hostedProcess?.Start(Context, _cancellation.Token), _cancellation.Token)
                    .ContinueWith(p =>
                    {
                        if (p.IsFaulted || p.Status == TaskStatus.Canceled)
                        {
                            _logger?.LogError($"Error running hosted process {processType.Name} execution {p.Exception?.Message}");
                            ProcessError(hostedProcess, p.Exception, !runOnce);
                        }

                        finishedProcesses++;
                    })
                    .ConfigureAwait(runOnce);

            }
            // During run once - we want to ensure all polly retry mechanisms have been executed.  This method will ensure this happens.
            while (runOnce && finishedProcesses != processTypeCount)
                Thread.Sleep(500); // wait for retry policies to finish.
        }


        /// <summary>
        /// Processes stop will call the hosted process's Stop method and waiting for the result.
        /// </summary>
        internal void ProcessingStop()
        {
            if (_stopped)
                return;

            _stopped = true;

            if (Status != HostStatus.Faulted)
                Status = HostStatus.Stopping;

            try
            {
                _cancellation.Cancel(true);

                _logger?.LogWarning("Stopping process host");

                if (_hostedProcessTypes != null && _hostedProcessTypes.Any())
                {
                    foreach (Type processType in _hostedProcessTypes.Reverse())
                    {
                        var hostedProcess = _serviceProvider.GetService(processType) as IHostedProcess;
                        try
                        {
                            hostedProcess?.Stop();
                        }
                        catch (Exception e)
                        {
                            _logger?.LogError($"Error during \"Stop\" execution for {processType.Name}: {e.Message}");
                            ProcessError(hostedProcess, e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger?.LogError($"Error stopping process host: {e.Message}");
            }
            finally
            {
                _logger?.LogWarning("Releasing KeepAliveEvent main app thread will exit");
                _keepAliveEvent.Set();

                _webHost?.Dispose();
                Context.StopMonitor();

                if (Status != HostStatus.Faulted)
                    Status = HostStatus.Stopped;
            }
        }

        /// <summary>
        /// Processes the error by logging it and then running the hosted Error method and waiting for the result.
        /// </summary>
        /// <param name="hostedProcess">The hosted service.</param>
        /// <param name="e">The exception to handle.</param>
        /// <param name="forceStop">if set to <c>true</c> [force stop].</param>
        internal void ProcessError(IHostedProcess hostedProcess, Exception e, bool forceStop = true)
        {
            Status = HostStatus.Faulted;

            // Lock statement as this method can be accessed from both threads.
            lock (_lockGate)
            {
                _logger?.LogError($"Application host has caught an error {hostedProcess?.GetType().Name}: {e?.Message}");

                var args = new ErrorArgs { ContinueClose = forceStop };

                try
                {
                    hostedProcess?.Error(e, args);
                    _logger?.LogDebug($"Ran {hostedProcess?.GetType().Name}'s Error method successfully");
                }
                catch (Exception exception)
                {
                    _logger?.LogError($"Error caught in process error handling for {hostedProcess?.GetType().Name}: {exception.Message}");
                }


                // Don't stop the app if the hosted process decides it does not want that and changes the Continue close arg.
                if (args.ContinueClose)
                    // As an error occurred, stop processing now and shut down.
                    ProcessingStop();
            }
        }

        /// <summary>
        /// Builds the wrapped retry polly policy.
        /// </summary>
        /// <returns><see cref="PolicyWrap" /> wrapped policy that has been built.</returns>
        internal AsyncPolicy BuildWrappedPolicy()
        {
            // If we have more than one policy, then wrap them together.
            switch (_retryPolicies.Count)
            {
                case 0:
                    return Policy.TimeoutAsync(TimeSpan.FromSeconds(60), TimeoutStrategy.Optimistic);
                case 1:
                    return _retryPolicies[0];
                default:
                    var polWrap = _retryPolicies[0].WrapAsync(_retryPolicies[1]);

                    for (int i = 2; i < _retryPolicies.Count; i++)
                    {
                        polWrap = _retryPolicies[i].WrapAsync(polWrap);
                    }

                    return polWrap;
            }
        }

        /// <summary>
        /// Runs the _appHosted process and blocks the application from exiting by waiting.
        /// </summary>
        public void RunAndBlock()
        {
            Run();
        }

        /// <summary>
        /// Runs the _appHosted process once then stops.
        /// </summary>
        public void RunOnce()
        {
            if (_usingServiceEndpoints)
            {
                _logger.LogWarning("Indicated intended as service endpoints (deferred execution) but executed with RunOnce. Disabled endpoints, processes will run one time at startup instead");
                _usingServiceEndpoints = false;
            }
            Run(true);
        }
    }
}
