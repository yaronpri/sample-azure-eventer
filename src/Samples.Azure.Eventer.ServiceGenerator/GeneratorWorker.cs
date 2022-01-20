using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Samples.Azure.Eventer.ServiceGenerator
{
    public class GeneratorWorker : BackgroundService
    {
        private static string SAMPLE_FILE = "SampleSourceFile.xml";
        protected readonly IConfiguration Configuration;
        protected readonly ILogger<GeneratorWorker> Logger;
        protected readonly TelemetryClient TelemetryClient;

        public GeneratorWorker(IConfiguration configuration, ILogger<GeneratorWorker> logger)
        {
            Configuration = configuration;
            Logger = logger;
        }

        public GeneratorWorker(IConfiguration configuration, ILogger<GeneratorWorker> logger, TelemetryClient tc) :
            this(configuration, logger)
        {
            TelemetryClient = tc;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("GeneratorWorker started to queue at: {time}", DateTimeOffset.UtcNow);

            try
            {
                //getting the queue details which we should listen to
                var storageConnectionString = Configuration.GetValue<string>("BLOB_CONNECTIONSTRING");
                var queueName = Configuration.GetValue<string>("QUEUE_NAME");
                var queueClient = new QueueClient(storageConnectionString, queueName);

                //getting the blob container details which we should upload the generated files
                var blobUploadConnectionString = Configuration.GetValue<string>("BLOB_UPLOAD_CONNECTIONSTRING");
                var containerName = Configuration.GetValue<string>("CONTAINER_NAME");
                var blobServiceClient = new BlobServiceClient(blobUploadConnectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                if (queueClient.Exists())
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var msg = await queueClient.ReceiveMessageAsync(); //set visibility timeout to 10 sec
                        if (msg.Value != null)
                        {
                            var request = msg.Value.Body.ToObjectFromJson<GeneratorDetails>();
                            await queueClient.DeleteMessageAsync(msg.Value.MessageId, msg.Value.PopReceipt);

                            await SendFiles(containerClient, request.RequestedAmount, request.RequestedSeconds);
                        }
                        await Task.Delay(1000); //delay the next in 1sec
                    }
                }
                else
                {
                    Logger.LogError("GeneratorWorker Error - queue not exist - " + queueName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("GeneratorWorker Error - " + ex.ToString());
            }
            Logger.LogInformation("GeneratorWorker stop queuing at: {Time}", DateTimeOffset.UtcNow);
        }

        private async Task SendFiles(BlobContainerClient containerClient, int requestedAmount, int requestedSeconds)
        {
            Logger.LogInformation("SendFiles - Start uploading files at: {Time}", DateTimeOffset.UtcNow);

            //using (var operation = TelemetryClient.StartOperation<RequestTelemetry>("SendFiles Operation requestAmount=" +  requestedAmount + " requestSeconds=" + requestedSeconds))
            //{
                int numOfFiles = requestedAmount * requestedSeconds;
                var generatedNames = GenerateNames(numOfFiles);

                DateTime beforeStart = DateTime.Now;

                for (int i = 0; i < requestedSeconds; i++)
                {
                    //using (var inneroperation = TelemetryClient.StartOperation<RequestTelemetry>("SendFiles Operation requestAmount=" + requestedAmount + " requestSeconds=" + requestedSeconds,))
                    //{
                        DateTime before = DateTime.Now;
                        var tasks = new List<Task>();
                        for (int j = 0; j < requestedAmount; j++)
                        {
                            var index = (i * requestedAmount) + j;
                            var filename = "demofile-" + generatedNames[index] + ".xml";
                            if (TelemetryClient != null)
                            {
                                tasks.Add(UploadBlob(containerClient, filename));
                            }
                            else
                            {
                                tasks.Add(UploadBlob(containerClient, filename));
                            }
                        }
                        await Task.WhenAll(tasks);

                        var after = DateTime.Now.Subtract(before);

                        //add the time need to wait for 1 second                        
                        if (after.TotalMilliseconds < 1000)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(1000 - after.TotalMilliseconds));
                            Logger.LogInformation("SendFiles - second " + i + " of " + requestedSeconds + " total, number of files: " + requestedAmount + " took " + after.TotalMilliseconds + " ms");
                        }
                        else
                            Logger.LogWarning("SendFiles - second " + i + " of " + requestedSeconds + " total, number of files: " + requestedAmount + " took more than 1sec: " + after.TotalMilliseconds + " ms");
                            //}
                        }                
            //} 
            Logger.LogInformation("SendFiles - end uploading files at: " + DateTimeOffset.UtcNow + " took: " + DateTime.Now.Subtract(beforeStart).TotalMilliseconds + " ms");
        }

        private Task UploadBlob(BlobContainerClient containerClient, string filename)
        {            
            BlobClient blobClient = containerClient.GetBlobClient(filename);            
            return blobClient.UploadAsync(SAMPLE_FILE, true);            
        }

        private static List<string> GenerateNames(int count)
        {
            List<string> retVal = new List<string>();

            for (int i = 0; i < count; i++)
            {
                retVal.Add(Guid.NewGuid().ToString().Replace("-",""));
            }

            return retVal;
        }
    }

    public class GeneratorDetails
    {
        public int RequestedAmount { get; set; }
        public int RequestedSeconds { get; set; }
    }
}
