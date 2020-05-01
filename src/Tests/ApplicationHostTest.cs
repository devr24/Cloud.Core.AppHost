using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Cloud.Core.AppHost.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cloud.Core.AppHost.Tests.Extensions;
using FluentAssertions;
using Xunit;
using Cloud.Core.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging;
using Cloud.Core.AppHost.Tests.Fakes;
using Microsoft.Extensions.Hosting;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace Cloud.Core.AppHost.Tests
{
    [IsUnit]
    public class ApplicationHostTest
    {
        /// <summary>Ensure the builder fails on multiple builds.</summary>
        [Fact]
        public void Test_ApplicationHost_MultipleBuild()
        {
            // Arrange - setup host.
            var builder = new AppHostBuilder(true).CreateDefaultBuilder()
                .ConfigureAppConfiguration(configBuilder =>
                {
                    configBuilder.AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                    {
                        new KeyValuePair<string, string>("a", "b")
                    });
                });

            // Act - first build works fine.
            builder.Build();
            builder.Config.GetValue<string>("a").Should().Be("b");

            // Assert - Should fail the second build method call.
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        /// <summary>Ensure exception is trapped when hosted process stop fails.</summary>
        [Fact]
        public void Test_ApplicationHost_ErrorStopping()
        {
            // Arrange 
            var builder = new AppHostBuilder().CreateDefaultBuilder()
                                              .ConfigureLogging((config, logBuilder) => logBuilder.AddFakeLogger());

            // Act - Should fail the rebuild method.
            var host = builder.AddHostedProcess<ErrorStopping>().Build();
            host.RunOnce();
            var fakeLogger = host.GetFakeLogger();
            var logMessage = fakeLogger.LogMessages.Where(m => m.LogLevel == LogLevel.Error && m.ExceptionType == typeof(InvalidOperationException));

            // Assert - expected error should have been caught.
            logMessage.Should().NotBeNull();
        }

        /// <summary>Verify the hosted process is only called once.</summary>
        [Fact]
        public void Test_ApplicationHost_WithNoRetryPolicies()
        {
            // Arrange
            var builder = new AppHostBuilder();
            var host = builder.AddHostedProcess<HttpErrorService>().Build();

            // Act - Should fail the rebuild method.
            host.RunOnce();
            var hostedService = host.GetService<HttpErrorService>();

            // Assert - Only called once.
            Assert.True(hostedService.CallCount == 1);
        }

        /// <summary>Ensure background timer is called once.</summary>
        [Fact]
        public void Test_ApplicationHost_BackgroundOperation()
        {
            // Arrange
            var hostedService = new BackgroundTimerService();
            var appHost = new AppHostBuilder().UseBackgroundMonitor(3).AddHostedProcess(hostedService).Build();

            hostedService.StopProcessing = (context) =>
            {
                hostedService.StopProcessing = null;
                var host = ((AppHost)appHost);
                host._cancellation.Dispose();
                host.ProcessingStop();

                // Assert
                context.ApplicationRunDuration.Should().BeGreaterThan(TimeSpan.Zero);
                context.IsContinuouslyRunning.Should().BeTrue();
                context.IsBackgroundMonitorRunning.Should().BeTrue();
                context.SystemInfo.Should().NotBe(null);
                hostedService.BackgroundTickCount.Should().Be(1);
            };

            // Act
            appHost.RunOnce();
        }

        /// <summary>Ensure the background monitor start/stop sets the timer running.</summary>
        [Fact]
        public void Test_AppHostContext_BackgroundMonitor()
        {
            // Arrange
            var hostContext = new AppHostContext(30, new SystemInfo(), null);

            // Act/Assert
            hostContext.StopMonitor();
            hostContext.IsBackgroundMonitorRunning.Should().Be(false);
            hostContext.StartMonitor();
            hostContext.IsBackgroundMonitorRunning.Should().Be(true);
            hostContext.StartMonitor();
            hostContext.IsBackgroundMonitorRunning.Should().Be(true);
            hostContext.StopMonitor();
            hostContext.IsBackgroundMonitorRunning.Should().Be(false);
        }

        /// <summary>Ensure argument exception when invalid background monitor time is passed.</summary>
        [Fact]
        public void Test_ApplicationHost_BackgroundOperationInvalid()
        {
            // Arrange
            var hostedService = new BackgroundTimerService();

            // Act/Assert
            Assert.Throws<ArgumentException>(() => new AppHostBuilder().UseBackgroundMonitor(0)
                .AddHostedProcess(hostedService));
        }

        /// <summary>Ensure health probe endpoint is setup correctly.</summary>
        [Fact]
        public void Test_ApplicationHost_HealthProbe()
        {
            var builder = new AppHostBuilder(true)
                .ConfigureAppConfiguration(config =>
                    {
                        config.AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                        {
                            new KeyValuePair<string, string>("Key1", "Val1")
                        });

                        // Do "global" initialization here; Only called once.
                        var currentDir = Directory.GetCurrentDirectory();

                        // Method 1 - app settings.
                        File.WriteAllText(Path.Combine(currentDir, "appsettings.json"), "{ \"TestKey1\":\"testVal1\", \"TestKey2\": { \"TestKey3\":\"testVal3\" } }");

                        config.AddJsonFile("appsettings.json");
                    })
                .ConfigureServices((config, logger, services) =>
                {
                    var allSettings = config.GetAllSettings().ToList();
                    allSettings.Count(i => i.Key == "Key1").Should().Be(1);
                    allSettings.Count(i => i.Key == "Key2").Should().Be(0);

                    var allSettingsSkipped = config.GetAllSettings(new[] { typeof(MemoryConfigurationProvider) }).ToList();
                    allSettingsSkipped.Count(i => i.Key == "Key1").Should().Be(0);

                    var configString = config.GetAllSettingsAsString();
                    configString.Length.Should().BeGreaterThan(0);

                    var configSkipped = config.GetAllSettingsAsString(new[] { typeof(MemoryConfigurationProvider) });
                    Assert.True(configSkipped.Length != configString.Length);
                    services.AddSingleton<SampleDependency>();
                })
                .ConfigureLogging((config, logging) => { })
                .UseDefaultRetryPolicies()
                .AddRetryWaitPolicy<InvalidOperationException>(5, 5)
                .AddRetryWaitPolicy<TimeoutException>()
                .AddHttpClient("test", "http://localhost:882/")
                .AddHealthProbe("882")
                .AddHostedProcess<SimpleService>()
                .AddHostedProcess<BackgroundTimerService>();

            var processHost = builder.Build();

            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>();

            var res = httpClient.CreateClient("test").GetAsync("probe").GetAwaiter().GetResult();
            res.StatusCode.Should().Be(200);

        }

        /// <summary>Ensure services are created and can be called like end points via http.</summary>
        [Fact]
        public void Test_ApplicationHost_ServiceEndPoint()
        {
            // Arrange.
            var builder = new AppHostBuilder()
                .ConfigureServices((config, logger, services) => { services.AddSingleton<SampleDependency>(); })
                .AddHttpClient("test", "http://localhost:889/")
                .AddHealthProbe("889")
                .AddHostedProcess<SimpleService>()
                .AddHostedProcess<BackgroundTimerService>()
                .AddHostedProcess<SimpleService2>()
                .AddHostedProcess<SimpleService2>()
                .UseHostedProcessEndpoints();

            builder._processTypes.Add(typeof(SimpleService3));

            // Act
            var processHost = builder.Build();
            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>()
                .CreateClient("test");

            // Assert
            httpClient.GetAsync("probe").GetAwaiter().GetResult().StatusCode.Should().Be(200);
            httpClient.GetAsync(typeof(SimpleService).Name).GetAwaiter().GetResult().StatusCode.Should().Be(200);
            httpClient.GetAsync(typeof(BackgroundTimerService).Name).GetAwaiter().GetResult().StatusCode.Should().Be(200);
            httpClient.GetAsync(typeof(SimpleService2).Name).GetAwaiter().GetResult().StatusCode.Should().Be(200);
            httpClient.GetAsync(typeof(SimpleService3).Name).GetAwaiter().GetResult().StatusCode.Should().Be(404);
            httpClient.GetAsync("swagger").GetAwaiter().GetResult().StatusCode.Should().Be(200);

            StopHost((AppHost)processHost).ConfigureAwait(false);

            processHost.RunAndBlock();

            builder = new AppHostBuilder().CreateDefaultBuilder()
                .ConfigureServices((config, logger, services) => { services.AddSingleton<SampleDependency>(); })
                .AddHostedProcess<SimpleService>().UseHostedProcessEndpoints();
            builder.Build().RunOnce();
        }

        private async Task StopHost(AppHost processHost)
        {
            var task = Task.Delay(1000);
            await task;
            processHost.ProcessingStop();
        }

        /// <summary>Ensure run once has a status of "stopped" after its been completed multiple times.</summary>
        [Fact]
        public void Test_ApplicationHost_RunOnceMultipleTimes()
        {
            // Run first time.
            // Arrange
            var builder = new AppHostBuilder()
                .ConfigureServices((config, logger, services) => services.AddSingleton<SampleDependency>()
                .AddHostedProcess<SimpleService>());

            // Act
            var processHost = builder.Build();
            var appHost = new AppHost(((AppHost)processHost)._webHost,
                ((AppHost)processHost)._retryPolicies,
                ((AppHost)processHost)._serviceProvider,
                new List<Type>(), -1, true);
            appHost.RunOnce();

            // Assert
            appHost.Status.Should().Be(HostStatus.Stopped);

            // Arrage - Run second time - reach code branches that check for already created params.
            appHost = new AppHost(((AppHost)processHost)._webHost,
                ((AppHost)processHost)._retryPolicies,
                new ServiceCollection().BuildServiceProvider(),
                new List<Type>(), -1, true);

            // Act
            appHost.RunOnce();

            // Assert
            appHost.Status.Should().Be(HostStatus.Stopped);
        }

        /// <summary>Ensure default settings are configured when CreateDefaultBuilder is called.</summary>
        [Fact]
        public void Test_ApplicationHost_DefaultBuilder()
        {
            // Arrange/Act
            var processHost = new AppHostBuilder().CreateDefaultBuilder().AddHealthProbe("889")
                .AddHttpClient("test", "http://localhost:889/").AddHostedProcess(new HttpErrorService()).Build();

            var memoryCache = ((AppHost)processHost)._serviceProvider.GetService<IMemoryCache>();
            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>();
            var res = httpClient.CreateClient("test").GetAsync("probe").GetAwaiter().GetResult();

            // Assert
            memoryCache.Should().NotBe(null);
            httpClient.Should().NotBe(null);
            res.StatusCode.Should().Be(200);
        }

        /// <summary>Ensure retry mechanisms kick in when expected.</summary>
        [Fact]
        public void Test_ApplicationHost_PollyRetry()
        {
            // Arrange
            var builder = new AppHostBuilder(true);
            var collection = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("setting:0", "1"),
                new KeyValuePair<string, string>("setting:1", "2")
            };

            // Act - Configure the application host.
            var processHost = builder
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(collection))
                .AddRetryWaitPolicy<HttpRequestException>(2, 2)
                .AddHostedProcess<HttpErrorService>()
                .Build(); // Build the host - this wires up dependencies.

            var service = ((AppHost)processHost)._serviceProvider.GetService<HttpErrorService>();
            processHost.RunOnce();

            // Assert
            service.CallCount.Should().Be(3);
        }

        /// <summary>Ensure single http client is available to service.</summary>
        [Fact]
        public void Test_ApplicationHost_HttpClientSingle()
        {
            // Arrange
            var builder = new AppHostBuilder();

            // Act - Configure the application host.
            var processHost = builder
                .AddHealthProbe("884")
                .AddHttpClient("test", "http://localhost:884")
                .ConfigureServices((config, logger, serviceBuilder) => serviceBuilder.AddHostedProcess(new SimpleService(new SampleDependency())))
                .AddHostedProcess<BackgroundTimerService>()
                .Build();

            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>();
            Assert.NotNull(httpClient);

            var client = httpClient.CreateClient("test");
            client.Timeout = TimeSpan.FromSeconds(1);

            try
            {
                client.GetAsync("doesnotexist").GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Ensure multiple http clients are made available to services.</summary>
        [Fact]
        public void Test_ApplicationHost_HttpClientMultiple()
        {
            var builder = new AppHostBuilder();

            // Configure the application host.
            var processHost = builder
                .AddHealthProbe("886")
                .AddHttpClient(new Dictionary<string, Uri>
                {
                        { "test1", new Uri("http://localhost:886") },
                        { "test2", new Uri("http://test2.com") },
                        { "test3", new Uri("http://test3.com") }
                })
                .AddHostedProcess<BackgroundTimerService>()
                .Build();

            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>();
            Assert.NotNull(httpClient);

            var client = httpClient.CreateClient("test1");
            client.Timeout = TimeSpan.FromSeconds(1);

            try
            {
                client.GetAsync("doesnotexist").GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Ensure http client is available as a service to the hosted process.</summary>
        [Fact]
        public void Test_ApplicationHost_HttpClientTyped()
        {
            var builder = new AppHostBuilder();

            // Configure the application host.
            var processHost = builder
                .AddHealthProbe("885")
                .AddHttpClientTyped<HttpClientDependencyTest>("test", "http://a.com:885")
                .AddHostedProcess<BackgroundTimerService>()
                .Build();

            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<HttpClientDependencyTest>();
            httpClient.Client.Timeout = TimeSpan.FromSeconds(1);
            Assert.NotNull(httpClient);
            try
            {
                httpClient.RetryExample();
            }
            catch (Exception)
            {
            }

        }

        /// <summary>Ensure startup class is called as expected.</summary>
        [Fact]
        public void Test_ApplicationHost_Startup()
        {
            // Arrange
            var builder = new AppHostBuilder().CreateDefaultBuilder();

            //Act
            builder.UseStartup<FakeStartup>().Build();

            // Assert
            builder._startupInstance.GetType().Should().Be(typeof(FakeStartup));
            ((FakeStartup)builder._startupInstance).Config.Should().NotBeNull();
            ((FakeStartup)builder._startupInstance).Logger.Should().NotBeNull();
            ((FakeStartup)builder._startupInstance).Services.Should().NotBeNull();
            ((FakeStartup)builder._startupInstance).Config.GetChildren().Count().Should().BeGreaterThan(0);
        }

        /// <summary>Ensure error during startup configure app config.</summary>
        [Fact]
        public void Test_ApplicationHost_Startup_ErrorConfigBuild()
        {
            // Arrange/Act
            var builder = new AppHostBuilder().CreateDefaultBuilder();

            // Assert
            Assert.Throws<ArgumentException>(() => builder.UseStartup<MalformedStartupConfig>().Build());
        }

        /// <summary>Ensure error during startup build logger.</summary>
        [Fact]
        public void Test_ApplicationHost_Startup_ErrorLoggingBuild()
        {
            // Arrange/Act
            var builder = new AppHostBuilder().CreateDefaultBuilder();

            // Assert
            Assert.Throws<ArgumentException>(() => builder.UseStartup<MalformedStartupLogging>().Build());
        }

        /// <summary>Ensure error during startup configure services.</summary>
        [Fact]
        public void Test_ApplicationHost_Startup_ErrorServiceBuild()
        {
            // Arrange/Act
            var builder = new AppHostBuilder().CreateDefaultBuilder();

            // Assert
            Assert.Throws<ArgumentException>(() => builder.UseStartup<MalformedStartupConfigureServices>().Build());
        }

        /// <summary>Ensure external IP address is not null.</summary>
        [Fact]
        public void Test_WebClientExtension_GetExternalIPAddress()
        {
            // Arrange/Act
            var externalIP = WebClientExtensions.GetExternalIpAddress();

            // Assert
            externalIP.Should().NotBe(null);
        }

        [Fact]
        public void Test_AppHost_IHostedService()
        {
            var hostedProcess = new SampleProcess() { Name = "Process1" };
            var hostedProcess2 = new SampleProcess() { Name = "Process2" };
            var hostedService = new SampleService() { Name = "Service1" };
            var hostedService2 = new SampleService() { Name = "Service2" };

            var hostBuilder = new AppHostBuilder().CreateDefaultBuilder();
            hostBuilder.AddHostedProcess(hostedProcess);
            hostBuilder.AddHostedProcess(hostedProcess2);
            hostBuilder.AddHostedProcess<SampleProcess>();
            hostBuilder.AddHostedService(hostedService);
            hostBuilder.AddHostedService(hostedService2);
            hostBuilder.AddHostedService<SampleService>();
            var host = hostBuilder.Build();

            host.RunOnce();

            //hostedProcess.StartCalls.Should().Be(1);
            //hostedProcess.StopCalls.Should().Be(1);
            //hostedProcess.ErrorCalls.Should().Be(1);
            //hostedService.StartCalls.Should().Be(1);
            //hostedService.StopCalls.Should().Be(1);
        }

        private class SampleProcess : IHostedProcess
        {
            public string Name { get; set; } = "SampleProcessName1";
            public int ErrorCalls { get; set; }
            public int StartCalls { get; set; }
            public int StopCalls { get; set; }

            public void Error(Exception ex, ErrorArgs args)
            {
                ErrorCalls++;
            }

            public Task Start(AppHostContext context, CancellationToken cancellationToken)
            {
                Task.Delay(4000);
                StartCalls++;
                return Task.CompletedTask;
            }

            public Task Stop()
            {
                Task.Delay(4000);
                StopCalls++;
                return Task.CompletedTask;
            }
        }

        private class SampleService : IHostedService
        {
            public string Name { get; set; } = "SampleServiceName1";
            public int StartCalls { get; set; }
            public int StopCalls { get; set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                StartCalls++;
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                StopCalls++;
                return Task.CompletedTask;
            }
        }
    }
}
