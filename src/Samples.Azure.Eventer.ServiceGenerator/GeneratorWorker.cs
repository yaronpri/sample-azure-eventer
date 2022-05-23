﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
        private static string SAMPLE_FILE_1MB = "SampleSourceFile1MB.xml";
        protected readonly IConfiguration Configuration;
        protected readonly ILogger<GeneratorWorker> Logger;
        protected readonly TelemetryClient TelemetryClient;
        private BinaryData SampleFileData;
        private BinaryData SampleFileData1MB;

        // Specify the StorageTransferOptions
        private BlobUploadOptions options = new BlobUploadOptions
        {            
            TransferOptions = new StorageTransferOptions
            {
                // Set the maximum number of workers that 
                // may be used in a parallel transfer.
                MaximumConcurrency = 16,

                // Set the maximum length of a transfer to 50MB.
                MaximumTransferSize = 50 * 1024 * 1024,                
            }
        };

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

                var filebytes = await File.ReadAllBytesAsync(SAMPLE_FILE);
                SampleFileData = BinaryData.FromBytes(filebytes);
                var filebytes1MB = await File.ReadAllBytesAsync(SAMPLE_FILE_1MB);
                SampleFileData1MB = BinaryData.FromBytes(filebytes1MB);

                if (queueClient.Exists())
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var msg = await queueClient.ReceiveMessageAsync(); //set visibility timeout to 10 sec
                        if (msg.Value != null)
                        {
                            var request = msg.Value.Body.ToObjectFromJson<GeneratorDetails>();
                            await queueClient.DeleteMessageAsync(msg.Value.MessageId, msg.Value.PopReceipt);

                            await SendFiles(containerClient, request.IsReadFromMemory, request.IsOneMBFile, request.RequestedAmount,
                                request.RequestedSeconds);
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

        private async Task SendFiles(BlobContainerClient containerClient, bool isReadFromMemory, bool isOneMBFile,
            int requestedAmount, int requestedSeconds)
        {
            Logger.LogInformation("SendFiles - Start uploading files at: {Time}", DateTimeOffset.UtcNow);
            var totalTime = new TimeSpan();
            int numOfFiles = requestedAmount * requestedSeconds;
            var generatedNames = GenerateNames(numOfFiles);
            DateTime beforeStart = DateTime.Now;

            for (int i = 0; i < requestedSeconds; i++)
            {
                DateTime before = DateTime.Now;
                var tasks = new List<Task>();
                for (int j = 0; j < requestedAmount; j++)
                {
                    var index = (i * requestedAmount) + j;
                    var filename = "demofile-" + generatedNames[index] + ".xml";

                    using (Logger.BeginScope(new Dictionary<string, object> { ["fileuid"] = generatedNames[index], ["step"]="GeneratorUploadFile" }))
                    {
                        tasks.Add(UploadBlob(containerClient, filename, isReadFromMemory, isOneMBFile));
                        Logger.LogInformation("SendFiles - second " + (i + 1) + " of " + requestedSeconds + " total, upload: " + filename);
                    }
                }
                await Task.WhenAll(tasks);

                var after = DateTime.Now.Subtract(before);
                totalTime = totalTime.Add(after);
                //add the time need to wait for 1 second
                using (Logger.BeginScope(new Dictionary<string, object> { ["reqNumOfFiles"] = requestedAmount, ["reqTotalTime"] = after.TotalMilliseconds }))
                {
                    if (after.TotalMilliseconds < 1000)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1000 - after.TotalMilliseconds));
                        Logger.LogInformation("SendFiles - second " + (i+1) + " of " + requestedSeconds + " total, number of files: " + requestedAmount + " took " + after.TotalMilliseconds + " ms");
                    }
                    else
                    {
                        Logger.LogWarning("SendFiles - second " + (i+1) + " of " + requestedSeconds + " total, number of files: " + requestedAmount + " took more than 1sec: " + after.TotalMilliseconds + " ms");
                    }
                }
            }
            using (Logger.BeginScope(new Dictionary<string, object> { ["totalNumOfFiles"] = requestedAmount * requestedSeconds, ["totalTime"] = totalTime.TotalMilliseconds, ["totalAvg"] = (totalTime.TotalMilliseconds / requestedSeconds) }))
            {
                Logger.LogInformation("SendFiles - end uploading files at: " + DateTimeOffset.UtcNow + " took: " + DateTime.Now.Subtract(beforeStart).TotalMilliseconds + " ms " + " without delays: " + totalTime.TotalMilliseconds + " ms, avg of " + (totalTime.TotalMilliseconds / requestedSeconds) + " ms to upload " + requestedAmount + " files per 1sec");
            }        
        }
    
        private Task UploadBlob(BlobContainerClient containerClient, string filename, bool isFromMemory, bool isOneMBFile)
        {
            if (isFromMemory)
            {
                if (isOneMBFile)
                {
                    return containerClient.UploadBlobAsync(filename, SampleFileData1MB);
                }
                else
                {
                    return containerClient.UploadBlobAsync(filename, SampleFileData);
                }
            }
            else
            {
                BlobClient blobClient = containerClient.GetBlobClient(filename);
                if (isOneMBFile)
                {
                    return blobClient.UploadAsync(SAMPLE_FILE_1MB, options);
                }
                else
                {
                    return blobClient.UploadAsync(SAMPLE_FILE, options);
                }                
            }
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
        public bool IsReadFromMemory { get; set; }
        public bool IsOneMBFile { get; set; }
    }
}
