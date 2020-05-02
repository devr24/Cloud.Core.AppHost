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
    using Microsoft.Extensions.Hosting;
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
        internal readonly AppHostContext _context;
        internal readonly object _lock = new object();

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

            _context = new AppHostContext(_monitorFrequency, HostSystemInfo, _logger);

            // Attach to domain wide exceptions.
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => _logger?.LogError(eventArgs.ExceptionObject as Exception, $"Unhandled exception captured: {eventArgs.ExceptionObject}");
            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => { _logger?.LogWarning("Application exit captured"); ProcessingStop(); };
            Console.CancelKeyPress += (sender, eventArgs) => { _logger?.LogWarning("Application cancel keys triggered"); eventArgs.Cancel = true; ProcessingStop(); };
        }

        /// <summary>
        /// Runs the specified process.
        /// </summary>
        /// <param name="runOnce">if set to <c>true</c> [run once].</param>
        internal void Run(bool runOnce = false)
        {
            _context.IsContinuouslyRunning = !runOnce;
            if (_monitorFrequency > 0)
                _context.StartMonitor();

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
                {
                    _logger?.LogWarning("Running as service endpoints, processes can only be triggered over http");
                }

                if (!runOnce)
                {
                     _keepAliveEvent.Wait();
                }
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
            foreach (var processType in _hostedProcessTypes)
            {
                _logger?.LogInformation($"Starting hosted process {processType.Name}");

                var hostedProcess = _serviceProvider.GetService(processType);

                Task t = null;

                if (hostedProcess is IHostedProcess process)
                {
                    t = StartHostedProcess(runOnce, process, processType.Name);
                }
                else if (hostedProcess is IHostedService service)
                {
                    t = StartHostedService(runOnce, service, processType.Name);
                }

                if (runOnce)
                {
                    t?.GetAwaiter().GetResult();
                }
            }
        }

        private Task StartHostedProcess(bool runOnce, [NotNull]IHostedProcess process, string name)
        {
            // Run the hosted process, wrapped in polly retry logic.
            return BuildWrappedPolicy().ExecuteAsync(token => process.Start(_context, token), _cancellation.Token)
                .ContinueWith(p =>
                {
                    if (p.IsFaulted)
                    {
                        _logger?.LogError(p.Exception, $"Error running hosted process {name} execution {p.Exception?.Message}");
                        ProcessError(process, p.Exception, !runOnce);
                    }

                    if (p.Status == TaskStatus.Canceled)
                    {
                        _logger?.LogWarning("Hosted process cancelled");
                    }
                });
        }

        private Task StartHostedService(bool runOnce, [NotNull]IHostedService process, string name)
        {
            // Run the hosted process, wrapped in polly retry logic.
            return BuildWrappedPolicy().ExecuteAndCaptureAsync(process.StartAsync, _cancellation.Token)
                .ContinueWith(p =>
                {
                    if (p.IsFaulted)
                    {
                        _logger?.LogError(p.Exception, $"Error running hosted process {name} execution {p.Exception?.Message}");
                        ProcessError(null, p.Exception, !runOnce);
                    }

                    if (p.Status == TaskStatus.Canceled)
                    {
                        _logger?.LogWarning("Hosted process cancelled");
                    }
                });
        }

        /// <summary>
        /// Processes stop will call the hosted process's Stop method and waiting for the result.
        /// </summary>
        internal void ProcessingStop()
        {
            lock (_lock)
            {
                if (_stopped)
                    return;

                _stopped = true;

                if (Status != HostStatus.Faulted)
                    Status = HostStatus.Stopping;

                try
                {
                    _cancellation.Cancel(true);

                    if (!_usingServiceEndpoints && 
                        _hostedProcessTypes != null && _hostedProcessTypes.Any())
                    {
                        foreach (Type processType in _hostedProcessTypes.Reverse())
                        {
                            var hostedProcess = _serviceProvider.GetService(processType);
                            try
                            {
                                _logger?.LogWarning($"Stopping {processType.Name}");

                                Task t = null;

                                if (hostedProcess is IHostedProcess process)
                                {
                                    t = process.Stop();
                                }
                                else if (hostedProcess is IHostedService service)
                                {
                                    t = service.StopAsync(_cancellation.Token);
                                }

                                t?.GetAwaiter().GetResult();
                            }
                            catch (Exception e)
                            {
                                _logger?.LogError(e, $"Error during \"Stop\" execution for {processType.Name}: {e.Message}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger?.LogError(e, $"Error stopping process host: {e.Message}");
                }
                finally
                {
                    _logger?.LogWarning("Releasing KeepAliveEvent main app thread will exit");
                    _keepAliveEvent.Set();

                    _webHost?.Dispose();
                    _context.StopMonitor();

                    if (Status != HostStatus.Faulted)
                        Status = HostStatus.Stopped;

                    _logger?.LogInformation("Exiting app host");
                }
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
                catch (Exception ex)
                {
                    _logger?.LogError(ex,$"Error caught in process error handling for {hostedProcess?.GetType().Name}: {ex.Message}");
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
