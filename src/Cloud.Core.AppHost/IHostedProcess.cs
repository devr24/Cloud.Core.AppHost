namespace Cloud.Core.AppHost
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Interface used to govern any processing that will be hosted within a <see cref="AppHost" />
    /// </summary>
    public interface IHostedProcess
    {
        /// <summary>
        /// Initiated when this process instance is starting up.
        /// </summary>
        /// <param name="context">The application host context.</param>
        /// <param name="cancellationToken">Converts to ken.</param>
        /// <returns>Task for action.</returns>
        Task Start(AppHostContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Runs the implementing code.  The cancellation token ensures the implementing code can "ThrowIfCancelled".
        /// </summary>
        void Stop();

        /// <summary>
        /// Error occurred with the specified exception.
        /// </summary>
        /// <param name="ex">The exception that has occurred.</param>
        /// <param name="args">The arguments.</param>
        void Error(Exception ex, ErrorArgs args);
    }

    /// <summary>
    /// Arguments giving the hosted process a chance to stop the app from closing.
    /// </summary>
    public class ErrorArgs
    {
        /// <summary>
        /// Gets or sets a value indicating whether [continue close].
        /// </summary>
        /// <value><c>true</c> if [continue close]; otherwise, <c>false</c>.</value>
        public bool ContinueClose { get; set; } = true;
    }
}