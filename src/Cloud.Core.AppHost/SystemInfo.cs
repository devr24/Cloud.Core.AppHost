namespace Cloud.Core.AppHost
{
    using System.Reflection;
    using System;

    /// <summary>
    /// Holds information about the host system.
    /// </summary>
    public class SystemInfo
    {
        /// <summary>
        /// Gets or sets the application identifier for this instance.
        /// </summary>
        /// <value>
        /// The application identifier for this instance.
        /// </value>
        internal static Guid AppInstanceIdentifier { get; } = Guid.NewGuid();

        /// <summary>
        /// Gets the version of the common language runtime.
        /// </summary>
        /// <value>
        /// The version of the CLR.
        /// </value>
        internal string Version { get; }

        /// <summary>
        /// Gets the operation system platform identifier and version number.
        /// </summary>
        /// <value>
        /// The operation system identifier and version.
        /// </value>
        internal string OperationSystem { get; }

        /// <summary>
        /// Gets the NetBIOS name of the machine the code is ran on.
        /// </summary>
        /// <value>
        /// The name of the host machine.
        /// </value>
        internal string Hostname { get; }

        /// <summary>
        /// Gets the username of the person currently logged on to the machine.
        /// </summary>
        /// <value>
        /// The username.
        /// </value>
        internal string Username { get; }

        /// <summary>
        /// Gets the cpu count.
        /// </summary>
        /// <value>
        /// The cpu count.
        /// </value>
        internal string CpuCount { get; }

        /// <summary>
        /// Gets the name of the application.
        /// </summary>
        /// <value>
        /// The name of the application.
        /// </value>
        internal string AppName { get; }

        /// <summary>Get the version of the application running the App Host.</summary>
        internal string AppVersion { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemInfo"/> class and sets up the property values.
        /// </summary>
        public SystemInfo()
        {
            Version = Environment.Version.ToString();
            OperationSystem = Environment.OSVersion.ToString();
            CpuCount = Environment.ProcessorCount.ToString();
            Hostname = Environment.MachineName;
            Username = Environment.UserName;
            AppName = AppDomain.CurrentDomain.FriendlyName;
            AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"AppInstanceId: {AppInstanceIdentifier.ToString()}, AppName: {AppName}, AppVersion: {AppVersion}, NetVersion: {Version}, OS: {OperationSystem}, CPU: {CpuCount}, Hostname: {Hostname}, Username: {Username}";
        }
    }
}
