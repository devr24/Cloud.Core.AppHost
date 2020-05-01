using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;

namespace Cloud.Core.AppHost.Tests.Fakes
{

    /// <summary>
    /// Class BackgroundTimerService.
    /// Implements the <see cref="Cloud.Core.AppHost.Tests.Fakes.HttpErrorService" />
    /// </summary>
    /// <seealso cref="Cloud.Core.AppHost.Tests.Fakes.HttpErrorService" />
    internal class BackgroundTimerService : HttpErrorService
    {
        private readonly int _targetTicks;

        public int BackgroundTickCount { get; set; }

        public BackgroundTimerService(int maxTickCount = 1)
        {
            _targetTicks = maxTickCount;
        }

        /// <summary>
        /// The stop processing
        /// </summary>
        public Action<AppHostContext> StopProcessing;

        /// <summary>
        /// Error occurred with the specified exception.
        /// </summary>
        /// <param name="ex">The exception that has occurred.</param>
        /// <param name="args">The arguments.</param>
        public override void Error(Exception ex, ErrorArgs args) { }

        /// <summary>
        /// Initiated when this process instance is starting up.
        /// </summary>
        /// <param name="context">The application host context.</param>
        /// <param name="cancellationToken">Converts to ken.</param>
        /// <returns>Task for action.</returns>
        public override async Task Start(AppHostContext context, CancellationToken cancellationToken)
        {
            context.BackgroundTimerTick = (elapsed) =>
            {
                Debug.Write(context.IsContinuouslyRunning);
                Debug.Write(context.ApplicationRunDuration);
                BackgroundTickCount++;
                if (BackgroundTickCount >= _targetTicks)
                {
                    StopProcessing?.Invoke(context);
                }
            };

            await Task.FromResult(true);
        }
    }

    /// <summary>
    /// Class SimpleService2.
    /// Implements the <see cref="BackgroundTimerService" />
    /// </summary>
    /// <seealso cref="BackgroundTimerService" />
    internal class SimpleService2 : BackgroundTimerService
    {
    }

    /// <summary>
    /// Class SimpleService3.
    /// Implements the <see cref="Cloud.Core.AppHost.Tests.Fakes.SimpleService2" />
    /// </summary>
    /// <seealso cref="Cloud.Core.AppHost.Tests.Fakes.SimpleService2" />
    internal class SimpleService3 : SimpleService2
    {
    }

    /// <summary>
    /// Class SimpleService.
    /// Implements the <see cref="BackgroundTimerService" />
    /// </summary>
    /// <seealso cref="BackgroundTimerService" />
    internal class SimpleService : BackgroundTimerService
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleService"/> class.
        /// </summary>
        /// <param name="dependency">The dependency.</param>
        public SimpleService(SampleDependency dependency)
        {
            dependency.Test().Should().BeTrue();
        }
    }

    /// <summary>
    /// Class HttpErrorService.
    /// Implements the <see cref="Cloud.Core.AppHost.IHostedProcess" />
    /// </summary>
    /// <seealso cref="Cloud.Core.AppHost.IHostedProcess" />
    internal class HttpErrorService : IHostedProcess
    {
        /// <summary>
        /// The call count
        /// </summary>
        public int CallCount = 0;

        /// <summary>
        /// Initiated when this process instance is starting up.
        /// </summary>
        /// <param name="context">The application host context.</param>
        /// <param name="cancellationToken">Converts to ken.</param>
        /// <returns>Task for action.</returns>
        /// <exception cref="HttpRequestException"></exception>
        public virtual Task Start(AppHostContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException();
        }

        /// <summary>
        /// Runs the implementing code.  The cancellation token ensures the implementing code can "ThrowIfCancelled".
        /// </summary>
        public Task Stop()
        {
            return Task.FromResult(false);
        }

        /// <summary>
        /// Error occurred with the specified exception.
        /// </summary>
        /// <param name="ex">The exception that has occurred.</param>
        /// <param name="args">The arguments.</param>
        /// <exception cref="Exception"></exception>
        public virtual void Error(Exception ex, ErrorArgs args)
        {
            throw new Exception();
        }
    }

    /// <summary>
    /// Class ErrorStopping.
    /// Implements the <see cref="Cloud.Core.AppHost.IHostedProcess" />
    /// </summary>
    /// <seealso cref="Cloud.Core.AppHost.IHostedProcess" />
    internal class ErrorStopping : IHostedProcess
    {
        /// <summary>
        /// Error occurred with the specified exception.
        /// </summary>
        /// <param name="ex">The exception that has occurred.</param>
        /// <param name="args">The arguments.</param>
        public void Error(Exception ex, ErrorArgs args) { }
        /// <summary>
        /// Initiated when this process instance is starting up.
        /// </summary>
        /// <param name="context">The application host context.</param>
        /// <param name="cancellationToken">Converts to ken.</param>
        /// <returns>Task for action.</returns>
        public async Task Start(AppHostContext context, CancellationToken cancellationToken) { await Task.FromResult(true); }

        /// <summary>
        /// Runs the implementing code.  The cancellation token ensures the implementing code can "ThrowIfCancelled".
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public Task Stop()
        {
            throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Class SampleDependency.
    /// </summary>
    internal class SampleDependency
    {
        /// <summary>
        /// Tests this instance.
        /// </summary>
        /// <returns><c>true</c>.</returns>
        public bool Test()
        {
            return true;
        }
    }

    /// <summary>
    /// Class HttpClientDependencyTest.
    /// </summary>
    public class HttpClientDependencyTest
    {
        /// <summary>
        /// The client
        /// </summary>
        public readonly HttpClient Client = null;
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientDependencyTest"/> class.
        /// </summary>
        /// <param name="client">The client.</param>
        public HttpClientDependencyTest(HttpClient client)
        {
            Client = client;
        }

        /// <summary>
        /// Retries the example.
        /// </summary>
        public void RetryExample()
        {
            var response = Client.GetAsync("doesnotexist").GetAwaiter().GetResult();
            response.StatusCode.Should().Be(200);
        }
    }
}
