using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Samples.Azure.Eventer.ServiceGenerator
{
    public class Program
    {       
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {            
            var env = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddEnvironmentVariables();                    
                })
                .ConfigureLogging((hostBuilderContext, loggingBuilder) =>
                {
                    loggingBuilder.AddConsole(consoleLoggerOptions => consoleLoggerOptions.TimestampFormat = "[HH:mm:ss]");
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<GeneratorWorker>();
                    if (string.IsNullOrEmpty(env) == false)
                    {
                        services.AddApplicationInsightsTelemetryWorkerService();
                        services.AddSingleton<ITelemetryInitializer, MyTelemetryInitializer>();

                        var aiOptions = new Microsoft.ApplicationInsights.WorkerService.ApplicationInsightsServiceOptions();
                        aiOptions.EnableAdaptiveSampling = false;
                        aiOptions.EnableDependencyTrackingTelemetryModule = false;
                        aiOptions.EnableQuickPulseMetricStream = false;
                        aiOptions.EnablePerformanceCounterCollectionModule = false;
                        services.AddApplicationInsightsTelemetryWorkerService(aiOptions);
                        
                             
                    }
                });
        }
    }
}
