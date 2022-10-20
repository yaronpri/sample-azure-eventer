using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Samples.Azure.Eventer.ServiceGenerator
{
    public class GeneratorWorker : BackgroundService
    {
        private static string SAMPLE_FILE = "SampleSourceFile.xml";
        private static string SAMPLE_FILE_1MB = "SampleSourceFile1MB.xml";
        private static string SAMPLES_FILES_1MB_PLACEHOLDER = "samplesfiles" + Path.DirectorySeparatorChar + "SampleSourceFile{0}.xml";
        private static string PREFIX_STRING = "<File><Name>{0}</Name>";
        protected readonly IConfiguration Configuration;
        protected readonly ILogger<GeneratorWorker> Logger;
        protected readonly TelemetryClient TelemetryClient;
        protected readonly Random Rand = new Random();
        private BinaryData SampleFileData;
        private BinaryData SampleFileData1MB;

        private static Dictionary<string,string> INITIAL_METADATA =
            new Dictionary<string, string> { { "meta1", "val1" }, { "meta2", DateTime.Now.ToShortDateString() } };
        //private static Dictionary<string, string> INITIAL_TAGS =
        //    new Dictionary<string, string> { { "tag1", "val1" }, { "tag2", DateTime.Now.ToShortDateString() } };
        // Specify the StorageTransferOptions
        private BlobUploadOptions options = new BlobUploadOptions
        {
            Metadata = INITIAL_METADATA,
            //Tags = INITIAL_TAGS,
            TransferOptions = new StorageTransferOptions
            {
                // Set the maximum number of workers that 
                // may be used in a parallel transfer.
                MaximumConcurrency = 16,

                // Set the maximum length of a transfer to 50MB.
                MaximumTransferSize = 50 * 1024 * 1024,
                
            },
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

                            await SendFiles(containerClient, request.IsReadFromMemory, request.SendFileMode, request.RequestedAmount,
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

        private async Task SendFiles(BlobContainerClient containerClient, bool isReadFromMemory, int sendFileMode,
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

                    using (Logger.BeginScope(new Dictionary<string, object> { ["fileuid"] = generatedNames[index], ["step"]="GeneratorUploadFile" }))
                    {
                        tasks.Add(UploadBlob(containerClient, generatedNames[index], isReadFromMemory, sendFileMode));
                        Logger.LogInformation("SendFiles - second " + (i + 1) + " of " + requestedSeconds + " total, upload: " + generatedNames[index]);
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
    
        private Task UploadBlob(BlobContainerClient containerClient, string filename, bool isFromMemory, int sendFileMode)
        {
            var fullFilename = "demofile-" + filename + ".xml";
            if (isFromMemory)
            {
                if (sendFileMode == (int)eSendMode.OneMB)
                {
                    return containerClient.UploadBlobAsync(fullFilename, SampleFileData1MB);
                }
                else
                {
                    return containerClient.UploadBlobAsync(fullFilename, SampleFileData);
                }
            }
            else
            {
                BlobClient blobClient = containerClient.GetBlobClient(fullFilename);
                if (sendFileMode == (int)eSendMode.OneMB)
                {
                    

                    return blobClient.UploadAsync(SAMPLE_FILE_1MB, options);
                }
                else
                {
                    if (sendFileMode == (int)eSendMode.ListOfOneMB)
                    {
                        int randomFileNumeber = Rand.Next(1, 100);
                        string fileToUpload = string.Format(SAMPLES_FILES_1MB_PLACEHOLDER, randomFileNumeber);

                        var firstPart = Encoding.ASCII.GetBytes(string.Format(PREFIX_STRING, filename));
                        var secondPart = File.ReadAllBytes(fileToUpload);
                        var combineFile = Combine(firstPart, secondPart);

                        var newFileToUpload = BinaryData.FromBytes(combineFile);

                        return blobClient.UploadAsync(newFileToUpload, options);
                    }
                    else
                    {
                        return blobClient.UploadAsync(SAMPLE_FILE, options);
                    }
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

        private static byte[] Combine(byte[] first, byte[] second)
        {
            return first.Concat(second).ToArray();
        }
    }

    public class GeneratorDetails
    {
        public int RequestedAmount { get; set; }
        public int RequestedSeconds { get; set; }
        public bool IsReadFromMemory { get; set; }
        public int SendFileMode { get; set; }        //1 - 1MB, 2 - 3MB, 3 - list of 200 files of 1MB
    }

    public enum eSendMode
    {
        OneMB = 1,
        ThreeMB = 2,
        ListOfOneMB = 3
    }
}
