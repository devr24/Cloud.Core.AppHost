namespace Cloud.Core.AppHost
{
    /// <summary>
    /// Enum Host Status.
    /// </summary>
    public enum HostStatus
    {
        /// <summary>
        /// Host is starting.
        /// </summary>
        Starting,
        /// <summary>
        /// Host is running.
        /// </summary>
        Running,
        /// <summary>
        /// Host is faulted.
        /// </summary>
        Faulted,
        /// <summary>
        /// Host is stopping.
        /// </summary>
        Stopping,
        /// <summary>
        /// Host is stopped.
        /// </summary>
        Stopped
    }
}
