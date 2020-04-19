using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Cloud.Core.AppHost.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Xunit;
using Cloud.Core.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging;

namespace Cloud.Core.AppHost.Tests
{
    [IsUnit]
    public class ApplicationHostTest
    {
        [Fact]
        public void Test_ApplicationHost_MultipleBuild()
        {
            var builder = new AppHostBuilder(true).CreateDefaultBuilder()
                .ConfigureAppConfiguration(configBuilder =>
                {
                    configBuilder.AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                    {
                        new KeyValuePair<string, string>("a", "b")
                    });
                });
            builder.Build();
            builder.Config.GetValue<string>("a").Should().Be("b");

            // Should fail the rebuild method.
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Fact]
        public void Test_ApplicationHost_ErrorStopping()
        {
            var builder = new AppHostBuilder().CreateDefaultBuilder();
            var host = builder.AddHostedProcess<ErrorStopping>().Build();

            // Should fail the rebuild method.
            host.RunOnce();
        }

        [Fact]
        public void Test_ApplicationHost_WithNoRetryPolicies()
        {
            var builder = new AppHostBuilder();
            var host = builder.AddHostedProcess<SimpleService2>().Build();

            // Should fail the rebuild method.
            host.RunOnce();
        }

        [Fact]
        public void Test_ApplicationHost_BackgroundOperation()
        {
            var hostedService = new SimpleService2();

            var appHost = new AppHostBuilder().UseBackgroundMonitor(3).AddHostedProcess(hostedService).Build();

            hostedService.StopProcessing = (context) =>
            {
                hostedService.StopProcessing = null;
                context.ApplicationRunDuration.Should().BeGreaterThan(TimeSpan.Zero);
                context.IsContinuouslyRunning.Should().BeTrue();
                context.IsBackgroundMonitorRunning.Should().BeTrue();
                context.SystemInfo.Should().NotBe(null);

                var host = ((AppHost)appHost);
                host._cancellation.Dispose();
                host.ProcessingStop();
            };

            appHost.RunOnce();
        }

        [Fact]
        public void Test_ApplicationHost_BackgroundMonitor()
        {
            var hostContext = new AppHostContext(30, new SystemInfo(), null);

            hostContext.StopMonitor();
            hostContext.StartMonitor();
            hostContext.StartMonitor();
            hostContext.StopMonitor();

            hostContext.IsBackgroundMonitorRunning.Should().Be(false);
        }

        [Fact]
        public void Test_ApplicationHost_BackgroundOperationInvalid()
        {
            var hostedService = new SimpleService2();

            Assert.Throws<ArgumentException>(() => new AppHostBuilder().UseBackgroundMonitor(0)
                .AddHostedProcess(hostedService));
        }

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
                .AddHostedProcess<SimpleService1>();

            var processHost = builder.Build();

            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>();

            var res = httpClient.CreateClient("test").GetAsync("probe").GetAwaiter().GetResult();
            res.StatusCode.Should().Be(200);

        }

        [Fact]
        public void Test_ApplicationHost_ServiceEndPoint()
        {
            var builder = new AppHostBuilder()
                .ConfigureServices((config, logger, services) => { services.AddSingleton<SampleDependency>(); })
                .AddHttpClient("test", "http://localhost:889/")
                .AddHealthProbe("889")
                .AddHostedProcess<SimpleService>()
                .AddHostedProcess<SimpleService1>()
                .AddHostedProcess<SimpleService2>()
                .AddHostedProcess<SimpleService2>()
                .UseHostedProcessEndpoints();

            builder._processTypes.Add(typeof(SimpleService3));

            var processHost = builder.Build();
            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>()
                .CreateClient("test");

            httpClient.GetAsync("probe").GetAwaiter().GetResult().StatusCode.Should().Be(200);
            httpClient.GetAsync(typeof(SimpleService).Name).GetAwaiter().GetResult().StatusCode.Should().Be(200);
            httpClient.GetAsync(typeof(SimpleService1).Name).GetAwaiter().GetResult().StatusCode.Should().Be(200);
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

        [Fact]
        public void Test_ApplicationHost_LogSystemInfo()
        {
            var builder = new AppHostBuilder()
                .ConfigureServices((config, logger, services) => services.AddSingleton<SampleDependency>().AddHostedProcess<SimpleService>())
                .UseHostedProcessEndpoints();

            var processHost = builder.Build();

            var appHost = new AppHost(((AppHost)processHost)._webHost,
                ((AppHost)processHost)._retryPolicies,
                ((AppHost)processHost)._serviceProvider,
                new List<Type>(), -1, true);

            appHost.RunOnce();

            appHost.Status.Should().Be(HostStatus.Stopped);

            appHost = new AppHost(((AppHost)processHost)._webHost,
                ((AppHost)processHost)._retryPolicies,
                new ServiceCollection().BuildServiceProvider(),
                new List<Type>(), -1, true);

            appHost.RunOnce();

            appHost.Status.Should().Be(HostStatus.Stopped);
        }

        [Fact]
        public void Test_ApplicationHost_DefaultBuilder()
        {
            var processHost = new AppHostBuilder().CreateDefaultBuilder().AddHealthProbe("889")
                .AddHttpClient("test", "http://localhost:889/").AddHostedProcess(new HttpErrorService()).Build();

            var memoryCache = ((AppHost)processHost)._serviceProvider.GetService<IMemoryCache>();
            var httpClient = ((AppHost)processHost)._serviceProvider.GetService<IHttpClientFactory>();

            memoryCache.Should().NotBe(null);
            httpClient.Should().NotBe(null);

            var res = httpClient.CreateClient("test").GetAsync("probe").GetAwaiter().GetResult();
            res.StatusCode.Should().Be(200);
        }

        [Fact]
        public void Test_ApplicationHost_PollyRetry()
        {
            var builder = new AppHostBuilder(true);

            var collection = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("setting:0", "1"),
                new KeyValuePair<string, string>("setting:1", "2")
            };


            // Configure the application host.
            var processHost = builder
                .ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(collection);
                })
                .AddRetryWaitPolicy<HttpRequestException>(2, 2)
                .AddHostedProcess<HttpErrorService>()
                .Build(); // Build the host - this wires up dependencies.

            var service = ((AppHost)processHost)._serviceProvider.GetService<HttpErrorService>();

            processHost.RunOnce();

            //Thread.Sleep(3000);

            service.CallCount.Should().Be(3);
        }

        [Fact]
        public void Test_ApplicationHost_HttpClientSingle()
        {
            var builder = new AppHostBuilder();

            // Configure the application host.
            var processHost = builder
                .AddHealthProbe("884")
                .AddHttpClient("test", "http://localhost:884")
                .ConfigureServices((config, logger, serviceBuilder) => serviceBuilder.AddHostedProcess(new SimpleService(new SampleDependency())))
                .AddHostedProcess<SimpleService1>()
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
                .AddHostedProcess<SimpleService1>()
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

        [Fact]
        public void Test_ApplicationHost_HttpClientTyped()
        {
            var builder = new AppHostBuilder();

            // Configure the application host.
            var processHost = builder
                .AddHealthProbe("885")
                .AddHttpClientTyped<HttpClientDependencyTest>("test", "http://a.com:885")
                .AddHostedProcess<SimpleService1>()
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

        [Fact]
        public void Test_ApplicationHost_Startup()
        {
            var builder = new AppHostBuilder().CreateDefaultBuilder();
            builder.UseStartup<FakeStartup>().Build();

            var instanceBuilder = new AppHostBuilder().CreateDefaultBuilder();
            instanceBuilder.UseStartup(typeof(FakeStartup)).Build().RunOnce();
        }

        [Fact]
        public void Test_ApplicationHost_Startup_ErrorConfigBuild()
        {
            var builder = new AppHostBuilder().CreateDefaultBuilder();
            Assert.Throws<ArgumentException>(() => builder.UseStartup<MalformedStartupConfig>().Build());
        }

        [Fact]
        public void Test_ApplicationHost_Startup_ErrorLoggingBuild()
        {
            var builder = new AppHostBuilder().CreateDefaultBuilder();
            Assert.Throws<ArgumentException>(() => builder.UseStartup<MalformedStartupLogging>().Build());
        }

        [Fact]
        public void Test_ApplicationHost_Startup_ErrorServiceBuild()
        {
            var builder = new AppHostBuilder().CreateDefaultBuilder();
            Assert.Throws<ArgumentException>(() => builder.UseStartup<MalformedStartupServices>().Build());
        }

        [Fact]
        public void Test_WebClientExtension_GetExternalIPAddress()
        {
            var externalIP = WebClientExtensions.GetExternalIPAddress();
            externalIP.Should().NotBe(null);
        }
    }

    public class MalformedStartupConfig
    {
        public void ConfigureAppConfiguration() { } // will error
    }


    public class MalformedStartupLogging
    {
        public void ConfigureAppConfiguration(IConfigurationBuilder builder) { }
        public void ConfigureLogging() { } // will error
    }


    public class MalformedStartupServices
    {
        public void ConfigureAppConfiguration(IConfigurationBuilder builder) { }
        public void ConfigureLogging(IConfiguration config, ILoggingBuilder builder) { }
        public void ConfigureServices() { } // will error
    }

    public class FakeStartup
    {
        public void ConfigureAppConfiguration(IConfigurationBuilder builder)
        {
            builder.AddInMemoryCollection(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Array:0", "1"),
                new KeyValuePair<string, string>("Array:1", "2"),
                new KeyValuePair<string, string>("Array:2", "3")
            });
        }

        public void ConfigureLogging(IConfiguration config, ILoggingBuilder builder)
        {
            builder.AddConsole();
            config.GetChildren().Count().Should().BeGreaterThan(0);
        }

        public void ConfigureServices(IConfiguration config, ILogger logger, IServiceCollection services)
        {
            config.GetChildren().Count().Should().BeGreaterThan(0);
        }
    }

    internal class SampleDependency
    {
        public bool Test()
        {
            return true;
        }
    }

    internal class SimpleService1 : HttpErrorService
    {
        public Action<AppHostContext> StopProcessing;
        public override void Error(Exception ex, ErrorArgs args) { }

        public override async Task Start(AppHostContext context, CancellationToken cancellationToken)
        {
            context.BackgroundTimerTick = (elapsed) =>
            {
                Debug.Write(context.IsContinuouslyRunning);
                Debug.Write(context.ApplicationRunDuration);
                StopProcessing?.Invoke(context);
            };

            await Task.FromResult(true);
        }
    }

    internal class SimpleService2 : SimpleService1
    {
    }

    internal class SimpleService3 : SimpleService2
    {
    }

    internal class SimpleService : SimpleService1
    {
        public SimpleService(SampleDependency dependency)
        {
            dependency.Test().Should().BeTrue();
        }
    }

    internal class HttpErrorService : IHostedProcess
    {
        public int CallCount = 0;
        public virtual Task Start(AppHostContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException();
        }

        public void Stop() { }

        public virtual void Error(Exception ex, ErrorArgs args)
        {
            throw new Exception();
        }
    }

    internal class ErrorStopping : IHostedProcess
    {
        public void Error(Exception ex, ErrorArgs args) { }
        public async Task Start(AppHostContext context, CancellationToken cancellationToken) { await Task.FromResult(true); }

        public void Stop()
        {
            throw new InvalidOperationException();
        }
    }

    public class HttpClientDependencyTest
    {
        public readonly HttpClient Client = null;
        public HttpClientDependencyTest(HttpClient client)
        {
            Client = client;
        }

        public void RetryExample()
        {
            var response = Client.GetAsync("doesnotexist").GetAwaiter().GetResult();
            response.StatusCode.Should().Be(200);
        }
    }
}
