using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
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

        public GeneratorWorker(IConfiguration configuration, ILogger<GeneratorWorker> logger)
        {            
            Configuration = configuration;
            Logger = logger;            
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("GeneratorWorker started to queue at: {time}", DateTimeOffset.UtcNow);

            try
            {
                //getting the queue details which we should listen to
                var blobConnectionString = Configuration.GetValue<string>("BLOB_CONNECTIONSTRING");
                var queueName = Configuration.GetValue<string>("QUEUE_NAME");                
                var queueClient = new QueueClient(blobConnectionString, queueName);

                //getting the blob container details which we should upload the generated files
                var blobUploadConnectionString = Configuration.GetValue<string>("BLOB_UPLOAD_CONNECTIONSTRING");
                var containerName = Configuration.GetValue<string>("CONTAINER_NAME");
                var blobServiceClient = new BlobServiceClient(blobConnectionString);
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

            int numOfFiles = requestedAmount * requestedSeconds;
            var generatedNames = GenerateFileNames(numOfFiles);

            DateTime before = DateTime.Now;

            for (int i = 0; i < requestedSeconds; i++)
            {
                List<Task> tasks = new List<Task>();
                for (int j = 0; j < requestedAmount; j++)
                {
                    var index = (i * requestedAmount) + j;
                    var filename = generatedNames[index];
                    BlobClient blobClient = containerClient.GetBlobClient(filename);
                    Logger.LogInformation("Second {0} id {1} - operation num. {2} - uploading - {3}", i + 1, j + 1, index + 1, blobClient.Uri);
                    tasks.Add(blobClient.UploadAsync(SAMPLE_FILE, true));
                }
                //Task.WaitAll(tasks.ToArray());
                await Task.WhenAll(tasks);

                var after = DateTime.Now.Subtract(before);

                //add the time need to wait for 1 second 
                if (1000 - after.Milliseconds > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(1000 - after.Milliseconds));
            }

            Logger.LogInformation("SendFiles - end uploading files at: {Time}", DateTimeOffset.UtcNow);
        }

        private static List<string> GenerateFileNames(int count)
        {
            List<string> retVal = new List<string>();

            for (int i = 0; i < count; i++)
            {
                retVal.Add("demofile-" + Guid.NewGuid().ToString() + ".xml");
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
