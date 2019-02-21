namespace Cloud.Core.AppHost
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Application context the hosted process runs under.
    /// </summary>
    public class AppHostContext
    {
        private int _monitorFrequencySeconds;
        private readonly ILogger _logger;
        private readonly Stopwatch _elapsedTime;
        private Timer _monitor;

        public AppHostContext(int monitorFrequencySeconds, SystemInfo systemInfo, ILogger logger)
        {
            _logger = logger;
            _monitorFrequencySeconds = monitorFrequencySeconds;
            SystemInfo = systemInfo;
            _elapsedTime = new Stopwatch();
        }

        /// <summary>
        /// Gets a value indicating whether the background monitor is running.
        /// </summary>
        /// <value><c>true</c> if this instance is background monitor running; otherwise, <c>false</c>.</value>
        public bool IsBackgroundMonitorRunning => _monitor != null;

        /// <summary>
        /// Gets a value indicating whether the application context instance is continuously running.
        /// </summary>
        /// <value><c>true</c> if this instance is continuously running; otherwise, <c>false</c>.</value>
        public bool IsContinuouslyRunning { get; internal set; }

        /// <summary>
        /// Gets the system information the application context is running under.
        /// </summary>
        /// <value>The system information.</value>
        public SystemInfo SystemInfo { get; }

        /// <summary>
        /// Gets or sets the background timer tick action event - used to hook into the background timer tick to allow custom logs to be written.
        /// </summary>
        /// <value>The background timer tick.</value>
        public Action<TimeSpan> BackgroundTimerTick { get; set; } = null;

        /// <summary>
        /// Gets the length of time the application context has been running.
        /// </summary>
        /// <value>The elapsed time.</value>
        public TimeSpan ApplicationRunDuration => _elapsedTime.Elapsed;

        /// <summary>
        /// Starts the monitoring logger. Default frequency is 60 seconds (can only be set during construction by the AppHostBuilder).
        /// </summary>
        public void StartMonitor()
        {
            _elapsedTime.Start();

            _monitor?.Dispose();
            _monitor = new Timer(
                _ =>
                {
                    var timespan = _elapsedTime.Elapsed;
                    _logger.LogDebug($"{SystemInfo.AppName} running time: {timespan.ToString("dd")} day(s) {timespan.ToString("hh")}:{timespan.ToString("mm")}:{timespan.ToString("ss")}.{timespan.ToString("fff")}");

                    BackgroundTimerTick?.Invoke(timespan);
                },
                null,
                TimeSpan.FromSeconds(_monitorFrequencySeconds),
                TimeSpan.FromSeconds(_monitorFrequencySeconds));
        }

        /// <summary>
        /// Stops the monitoring from taking place.
        /// </summary>
        public void StopMonitor()
        {
            _elapsedTime?.Stop();
            _monitor?.Dispose();
            _monitor = null;
        }
    }
}
